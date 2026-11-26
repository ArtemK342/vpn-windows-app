using System.Buffers.Binary;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace FsocietyNet.Services;

/// <summary>Persistent outbound WFP policy independent of the lifetime of the tunnel or UI.</summary>
internal static class TunnelGuard
{
    static readonly Guid[] Layers = [new("c38d57d1-05a7-4c33-904f-7fbceee60e82"), new("4a72393b-319f-44bc-84c3-badcb3b6b484")];
    static readonly Guid Interface = new("4cd62a49-59c3-4969-b7f3-bda5d32890a4");
    static readonly Guid Flags = new("632ce23b-5167-435c-86d7-e903684aa80c");
    static readonly Guid RemoteAddress = new("b235ae9a-1d64-49b8-a44c-5ff3d9095045");
    static readonly Guid RemotePort = new("c35a604d-d22b-4e1a-91b4-68f674ee674b");
    static readonly Guid Protocol = new("3971ef2b-623e-4f9a-8cb1-6e79b806b9a7");
    static readonly Guid AppId = new("d78e1e87-8644-4ea5-9437-d809ecefc971");
    static Guid Key(int slot) => new(SHA256.HashData(Encoding.UTF8.GetBytes(Brand.Id + "/wfp/" + slot)).AsSpan(0, 16));
    static readonly Guid SubLayerKey = Key(-1);
    const uint NotFound = 0x80320003;
    const uint AlreadyExists = 0x80320009;
    const uint BlockAction = 0x1001;
    const uint PermitAction = 0x1002;
    const uint Persistent = 1;
    const int BlockSlot = 0, LoopbackSlot = 2, TunnelSlot = 4, PeerSlot = 6, KernelPeerSlot = 7;
    public static bool Armed { get; private set; }

    public static void Detect()
    {
        WithEngine(handle =>
        {
            var key = Key(BlockSlot);
            var result = FwpmFilterGetByKey0(handle, ref key, out var data);
            if (result == NotFound) { Armed = false; return; }
            Check(result);
            FwpmFreeMemory0(ref data);
            Armed = true;
        });
    }

    public static async Task<IPAddress> ResolvePeer(string host)
    {
        var addresses = IPAddress.TryParse(host, out var ip) ? [ip] :
            await Dns.GetHostAddressesAsync(host, System.Net.Sockets.AddressFamily.InterNetwork);
        return addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            ?? throw new EngineException("home.error_server_unreachable");
    }

    public static void Arm(IPAddress address, ushort port, byte protocol, string exe, bool kernelPeer = false)
    {
        WithEngine(handle => Transaction(handle, () =>
        {
            var layer = new Sublayer { Key = SubLayerKey, Name = Brand.Id + " Kill Switch", Flags = Persistent, Weight = 0xffff };
            var result = FwpmSubLayerAdd0(handle, ref layer, IntPtr.Zero);
            if (result != AlreadyExists) Check(result);
            DeleteFilters(handle);
            for (int i = 0; i < Layers.Length; i++)
            {
                Add(handle, BlockSlot + i, i, 0, BlockAction);
                Add(handle, LoopbackSlot + i, i, 10, PermitAction, new Condition(Flags, 6, 3, 1));
            }
            var peer = new Condition[]
            {
                new(RemoteAddress, 0, 3, BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes())),
                new(RemotePort, 0, 2, port),
                new(Protocol, 0, 1, protocol),
            };
            AddPeer(handle, PeerSlot, exe, peer);
            if (kernelPeer) AddPeer(handle, KernelPeerSlot, "System", peer);
        }));
        Armed = true;
    }

    static void AddPeer(IntPtr handle, int slot, string exe, Condition[] peer)
    {
        Check(FwpmGetAppIdFromFileName0(exe, out var appId));
        try { Add(handle, slot, 0, 12, PermitAction, [.. peer, new(AppId, 0, 12, (nuint)appId)]); }
        finally { FwpmFreeMemory0(ref appId); }
    }

    public static async Task AllowTunnel()
    {
        if (!Armed) return;
        ulong luid = 0;
        for (int i = 0; i < 20; i++)
        {
            if (ConvertInterfaceAliasToLuid(Brand.Id, out luid) == 0 && luid != 0) break;
            await Task.Delay(250);
        }
        if (luid == 0) throw new EngineException("err.killswitch");
        var ptr = Marshal.AllocHGlobal(sizeof(ulong));
        try
        {
            Marshal.WriteInt64(ptr, unchecked((long)luid));
            WithEngine(handle => Transaction(handle, () =>
            {
                for (int i = 0; i < Layers.Length; i++)
                {
                    Delete(handle, TunnelSlot + i);
                    Add(handle, TunnelSlot + i, i, 11, PermitAction, new Condition(Interface, 0, 4, (nuint)ptr));
                }
            }));
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    public static void CloseTunnel()
    {
        if (!Armed) return;
        WithEngine(handle => Transaction(handle, () =>
        {
            for (int i = 0; i < Layers.Length; i++) Delete(handle, TunnelSlot + i);
        }));
    }

    public static void Disarm()
    {
        if (!Armed) return;
        WithEngine(handle => Transaction(handle, () => DeleteFilters(handle)));
        Armed = false;
    }

    static void DeleteFilters(IntPtr handle)
    {
        for (int i = 0; i <= KernelPeerSlot; i++) Delete(handle, i);
    }

    static void Delete(IntPtr handle, int slot)
    {
        var key = Key(slot);
        var result = FwpmFilterDeleteByKey0(handle, ref key);
        if (result != NotFound) Check(result);
    }

    static void Add(IntPtr handle, int slot, int family, byte weight, uint action, params Condition[] conditions)
    {
        var data = conditions.Length == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(Marshal.SizeOf<Condition>() * conditions.Length);
        try
        {
            for (int i = 0; i < conditions.Length; i++)
                Marshal.StructureToPtr(conditions[i], IntPtr.Add(data, i * Marshal.SizeOf<Condition>()), false);
            var filter = new Filter
            {
                Key = Key(slot), Name = Brand.Id + " Kill Switch", Flags = Persistent, Layer = Layers[family],
                SubLayer = SubLayerKey, Weight = new Value(1, weight), Count = (uint)conditions.Length,
                Conditions = data, Action = action,
            };
            Check(FwpmFilterAdd0(handle, ref filter, IntPtr.Zero, out _));
        }
        finally { if (data != IntPtr.Zero) Marshal.FreeHGlobal(data); }
    }

    static void WithEngine(Action<IntPtr> use)
    {
        Check(FwpmEngineOpen0(IntPtr.Zero, 10, IntPtr.Zero, IntPtr.Zero, out var handle));
        try { use(handle); }
        finally { FwpmEngineClose0(handle); }
    }

    static void Transaction(IntPtr handle, Action action)
    {
        Check(FwpmTransactionBegin0(handle, 0));
        try { action(); Check(FwpmTransactionCommit0(handle)); }
        catch { FwpmTransactionAbort0(handle); throw; }
    }

    static void Check(uint code)
    {
        if (code != 0) throw new EngineException("err.killswitch", $"Windows Filtering Platform error 0x{code:x8}");
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Value(uint type, nuint data) { public uint Type = type; public nuint Data = data; }

    [StructLayout(LayoutKind.Sequential)]
    struct Condition(Guid key, uint match, uint type, nuint data)
    {
        public Guid Key = key;
        public uint Match = match;
        public Value Value = new(type, data);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Blob { public uint Size; public IntPtr Data; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Sublayer
    {
        public Guid Key;
        public string Name;
        public string? Description;
        public uint Flags;
        public IntPtr Provider;
        public Blob ProviderData;
        public ushort Weight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Filter
    {
        public Guid Key;
        public string Name;
        public string? Description;
        public uint Flags;
        public IntPtr Provider;
        public Blob ProviderData;
        public Guid Layer;
        public Guid SubLayer;
        public Value Weight;
        public uint Count;
        public IntPtr Conditions;
        public uint Action;
        public Guid ActionKey;
        public ulong ContextLow;
        public ulong ContextHigh;
        public IntPtr Reserved;
        public ulong Id;
        public Value EffectiveWeight;
    }

    [DllImport("fwpuclnt.dll")] static extern uint FwpmEngineOpen0(IntPtr server, uint auth, IntPtr identity, IntPtr session, out IntPtr handle);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmEngineClose0(IntPtr handle);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmTransactionBegin0(IntPtr handle, uint flags);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmTransactionCommit0(IntPtr handle);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmTransactionAbort0(IntPtr handle);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmSubLayerAdd0(IntPtr handle, ref Sublayer layer, IntPtr security);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmFilterAdd0(IntPtr handle, ref Filter filter, IntPtr security, out ulong id);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmFilterDeleteByKey0(IntPtr handle, ref Guid key);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmFilterGetByKey0(IntPtr handle, ref Guid key, out IntPtr filter);
    [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)] static extern uint FwpmGetAppIdFromFileName0(string path, out IntPtr appId);
    [DllImport("fwpuclnt.dll")] static extern void FwpmFreeMemory0(ref IntPtr memory);
    [DllImport("iphlpapi.dll", CharSet = CharSet.Unicode)] static extern uint ConvertInterfaceAliasToLuid(string alias, out ulong luid);
}
