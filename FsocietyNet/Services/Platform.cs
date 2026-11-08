using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using FsocietyNet.Views;

namespace FsocietyNet.Services;

/// <summary>Tray icon (vector brand mark rendered at the shell's icon size) and its menu.</summary>
public static class Tray
{
    static WinForms.NotifyIcon? _icon;
    static readonly Dictionary<BrandState, Drawing.Icon> Icons = [];
    static WinForms.ToolStripMenuItem? _open, _connect, _disconnect, _unblock, _exit;

    public static event Action? OpenRequested;
    public static event Action? ExitRequested;
    public static Func<bool>? WindowVisible;

    public static void Init()
    {
        var menu = new WinForms.ContextMenuStrip { ShowImageMargin = false, Font = new Drawing.Font("Segoe UI", 9.5f) };
        _open = new("", null, (_, _) => OpenRequested?.Invoke());
        _connect = new("", null, async (_, _) =>
        {
            var s = Vpn.BestServer();
            if (s == null) { OpenRequested?.Invoke(); return; }
            await Vpn.Connect(s);
        });
        _disconnect = new("", null, async (_, _) => await Vpn.Disconnect());
        _unblock = new("", null, async (_, _) => await Vpn.Unblock());
        _exit = new("", null, (_, _) => ExitRequested?.Invoke());
        menu.Items.AddRange(new WinForms.ToolStripItem[]
        {
            _open, new WinForms.ToolStripSeparator(), _connect, _disconnect, _unblock, new WinForms.ToolStripSeparator(), _exit
        });
        menu.Opening += (_, _) => { Style(menu); Update(); };

        _icon = new WinForms.NotifyIcon { ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) OpenRequested?.Invoke(); };
        Vpn.Changed += Update;
        Update();
    }

    static void Style(WinForms.ContextMenuStrip m)
    {
        var dark = Theme.IsDark;
        m.Renderer = new WinForms.ToolStripProfessionalRenderer(new MenuColors(dark)) { RoundedEdges = false };
        m.BackColor = dark ? Drawing.Color.FromArgb(0x1A, 0x1A, 0x1D) : Drawing.Color.White;
        m.ForeColor = dark ? Drawing.Color.FromArgb(0xED, 0xED, 0xEF) : Drawing.Color.FromArgb(0x14, 0x14, 0x16);
        foreach (WinForms.ToolStripItem i in m.Items) { i.ForeColor = m.ForeColor; i.Padding = new WinForms.Padding(4, 5, 4, 5); }
    }

    sealed class MenuColors : WinForms.ProfessionalColorTable
    {
        readonly Drawing.Color _bg, _sel, _line;
        public MenuColors(bool dark)
        {
            _bg = dark ? Drawing.Color.FromArgb(0x1A, 0x1A, 0x1D) : Drawing.Color.White;
            _sel = dark ? Drawing.Color.FromArgb(0x2A, 0x2A, 0x30) : Drawing.Color.FromArgb(0xEE, 0xEE, 0xE8);
            _line = dark ? Drawing.Color.FromArgb(0x33, 0x33, 0x3A) : Drawing.Color.FromArgb(0xDE, 0xDE, 0xD7);
        }
        public override Drawing.Color ToolStripDropDownBackground => _bg;
        public override Drawing.Color MenuItemSelected => _sel;
        public override Drawing.Color MenuItemBorder => _sel;
        public override Drawing.Color MenuBorder => _line;
        public override Drawing.Color SeparatorDark => _line;
        public override Drawing.Color SeparatorLight => _bg;
        public override Drawing.Color ImageMarginGradientBegin => _bg;
        public override Drawing.Color ImageMarginGradientMiddle => _bg;
        public override Drawing.Color ImageMarginGradientEnd => _bg;
    }

    static Drawing.Icon IconFor(BrandState st)
    {
        if (Icons.TryGetValue(st, out var i)) return i;
        var px = Math.Max(16, WinForms.SystemInformation.SmallIconSize.Width);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BrandMark.Render(px, st)));
        using var ms = new MemoryStream();
        enc.Save(ms);
        ms.Position = 0;
        using var bmp = new Drawing.Bitmap(ms);
        var icon = Drawing.Icon.FromHandle(bmp.GetHicon());
        Icons[st] = icon;
        return icon;
    }

    public static void Update()
    {
        if (_icon == null) return;
        var st = Vpn.Status switch
        {
            VpnStatus.On => BrandState.Connected,
            VpnStatus.Connecting or VpnStatus.Reconnecting or VpnStatus.Disconnecting => BrandState.Busy,
            VpnStatus.Blocked => BrandState.Blocked,
            _ => BrandState.Off
        };
        _icon.Icon = IconFor(st);
        var text = Vpn.Status switch
        {
            VpnStatus.On => Loc.F("tray.connected", Vpn.Server?.DisplayName ?? ""),
            VpnStatus.Connecting => Loc.Get("home.connecting"),
            VpnStatus.Reconnecting => Loc.Get("home.reconnecting"),
            VpnStatus.Blocked => Loc.Get("tray.blocked"),
            _ => Loc.Get("tray.disconnected")
        };
        _icon.Text = ("FsocietyNet · " + text) is var t && t.Length > 63 ? t[..63] : t;
        if (_open == null) return;
        _open.Text = Loc.Get("tray.open");
        _connect!.Text = Loc.Get("tray.connect");
        _disconnect!.Text = Loc.Get("tray.disconnect");
        _unblock!.Text = Loc.Get("home.unblock");
        _exit!.Text = Loc.Get("tray.exit");
        _connect.Visible = Vpn.Status == VpnStatus.Off;
        _disconnect.Visible = Vpn.Status is VpnStatus.On or VpnStatus.Reconnecting or VpnStatus.Connecting;
        _unblock.Visible = Vpn.Status == VpnStatus.Blocked;
    }

    /// <summary>Balloon only when the window is hidden (otherwise the app shows the state itself).</summary>
    public static void Notify(string text)
    {
        if (_icon == null || WindowVisible?.Invoke() == true) return;
        try { _icon.ShowBalloonTip(3500, "FsocietyNet", text, WinForms.ToolTipIcon.None); } catch { }
    }

    public static void Dispose()
    {
        if (_icon == null) return;
        _icon.Visible = false;
        _icon.Dispose();
        _icon = null;
    }
}

/// <summary>Start with Windows: a scheduled task «at logon, highest privileges» — no UAC prompt at every login.</summary>
public static class Autostart
{
    const string TaskName = "FsocietyNet";

    public static async Task<bool> Set(bool on)
    {
        var exe = Environment.ProcessPath ?? "";
        var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe") { UseShellExecute = false, CreateNoWindow = true };
        if (on)
        {
            foreach (var a in new[] { "/Create", "/F", "/TN", TaskName, "/SC", "ONLOGON", "/RL", "HIGHEST", "/DELAY", "0000:15", "/TR", $"\"{exe}\" --tray" })
                psi.ArgumentList.Add(a);
        }
        else foreach (var a in new[] { "/Delete", "/F", "/TN", TaskName }) psi.ArgumentList.Add(a);
        psi.RedirectStandardOutput = psi.RedirectStandardError = true;
        try
        {
            using var p = System.Diagnostics.Process.Start(psi)!;
            await p.WaitForExitAsync();
            Log.I($"autostart {(on ? "on" : "off")}: {p.ExitCode}");
            return p.ExitCode == 0 || !on;
        }
        catch (Exception e) { Log.E("autostart", e); return false; }
    }
}

/// <summary>New Windows builds: GitHub releases of ArtemK342/vpn-windows-app.</summary>
public static class Updates
{
    public record Info(string Version, string Notes, string PageUrl, string? AssetUrl);
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    static Updates() => Http.DefaultRequestHeaders.UserAgent.ParseAdd($"FsocietyNet-Windows/{App.Version}");

    public static Info? Available { get; private set; }

    public static async Task<Info?> Check()
    {
        if (Api.DemoMode) return null;
        var json = await Http.GetStringAsync("https://api.github.com/repos/ArtemK342/vpn-windows-app/releases/latest");
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var ver = (r.GetProperty("tag_name").GetString() ?? "").TrimStart('v', 'V');
        string? asset = null;
        if (r.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                { asset = a.GetProperty("browser_download_url").GetString(); break; }
            }
        var info = new Info(ver, r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
            r.GetProperty("html_url").GetString() ?? "https://fsociety-vpn.org/download", asset);
        Available = IsNewer(ver) ? info : null;
        return info;
    }

    public static bool IsNewer(string v) =>
        Version.TryParse(v, out var a) && Version.TryParse(App.Version, out var b) && a > b;

    public static void Open(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) { Log.E("open url", e); }
    }
}
