using System.Windows;
using System.Windows.Threading;

namespace FsocietyNet.Services;

public enum VpnStatus { Off, Connecting, On, Reconnecting, Blocked, Disconnecting }

/// <summary>Message already meant for the user.</summary>
public class UserError : Exception { public UserError(string m) : base(m) { } }

/// <summary>
/// Connection state machine for all protocols: access check → config from the API → engine → verification.
/// Watchdog every 5 s: a dead link is restarted from the local config (up to 3 times); with the kill switch on
/// WFP keeps the block in place until the user unblocks or reconnects.
/// </summary>
public static class Vpn
{
    public static VpnStatus Status { get; private set; } = VpnStatus.Off;
    public static ServerResponse? Server { get; private set; }
    public static DateTime? Since { get; private set; }
    public static string? LastError { get; private set; }
    /// <summary>Rules the running connection was built with — the Rules screen offers to reconnect when they change.</summary>
    public static string? RulesAtConnect { get; private set; }
    public static event Action? Changed;

    /// <summary>Filled by the home screen: used by «connect» from the tray and by auto-connect.</summary>
    public static List<ServerResponse> Servers { get; set; } = [];
    public static Dictionary<string, int> Pings { get; } = [];
    public static AppConfigResponse? AppCfg { get; set; }

    public static bool Busy => Status is VpnStatus.Connecting or VpnStatus.Disconnecting;
    public static bool Active => Status is VpnStatus.On or VpnStatus.Reconnecting or VpnStatus.Blocked;
    public static string? EngineKind => _engine?.Kind;

    static IEngine? _engine;
    static readonly SemaphoreSlim Gate = new(1, 1);
    static DispatcherTimer? _watchdog;
    static int _badChecks;
    static int _connectionRequest;

    static void Set(VpnStatus st, ServerResponse? s = null)
    {
        Status = st;
        if (s != null) Server = s;
        Application.Current?.Dispatcher.BeginInvoke(() => Changed?.Invoke());
    }

    public static void StartWatchdog()
    {
        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _watchdog.Tick += async (_, _) => await Check();
        _watchdog.Start();
    }

    public static ServerResponse? BestServer()
    {
        // Reserve servers only when picked by hand (user 2026-11-09)
        var auto = Servers.Where(s => s.CanConnect && s.allow_auto_connect && !s.IsBypass && !s.IsUnavailable && !s.IsReserve).ToList();
        var last = Servers.FirstOrDefault(s => s.id == AppSettings.Current.LastServerId && s.CanConnect);
        return auto.Where(s => Pings.TryGetValue(s.id, out var p) && p > 0).OrderBy(s => Pings[s.id]).FirstOrDefault()
               ?? last ?? auto.FirstOrDefault();
    }

    public static async Task<bool> Connect(ServerResponse s)
    {
        Interlocked.Increment(ref _connectionRequest);
        await Gate.WaitAsync();
        try
        {
            if (TunnelGuard.Armed)
            {
                if (_engine == null || Server?.id != s.id || s.IsBypass)
                {
                    LastError = Loc.Get("home.reconnect_failed_killswitch");
                    if (_engine == null) Set(VpnStatus.Blocked);
                    return false;
                }
                Set(VpnStatus.Reconnecting);
                TunnelGuard.CloseTunnel();
                if (!await _engine.Restart()) throw new EngineException("home.error_server_unreachable");
                _badChecks = 0;
                Since = DateTime.Now;
                LastError = null;
                Set(VpnStatus.On);
                return true;
            }
            if (_engine != null) await StopEngine();
            if (s.IsBypass && AppSettings.Current.KillSwitch) throw new UserError(Loc.Get("err.killswitch_bypass"));
            LastError = null;
            Set(VpnStatus.Connecting, s);
            Log.I($"connect {s.server_type} {s.id}");

            if (!s.SupportedOnWindows) throw new UserError(Loc.Get("home.not_on_windows"));
            var access = await Api.Access(s.server_type);
            if (!access.allowed)
                throw new UserError(access.Text() ?? Loc.Get(access.code == "limit_reached" ? "home.limit_reached" : "err.forbidden"));

            IEngine engine;
            switch (s.server_type)
            {
                case "wireguard":
                {
                    var (priv, pub) = X25519.NewKeyPair();
                    VpnConfigResponse r;
                    try { r = await Api.WireGuardConfig(s.id, pub); }
                    catch (ApiException e) when (e.Status == 409)
                    {
                        (priv, pub) = X25519.NewKeyPair();
                        r = await Api.WireGuardConfig(s.id, pub);
                    }
                    if (string.IsNullOrEmpty(r.config)) throw new UserError(r.message ?? Loc.Get("err.server"));
                    var conf = await AwgEngine.BuildConfig(r.config, r.client_key ? priv : null);
                    var awg = new AwgEngine();
                    _engine = awg;
                    await awg.Start(conf);
                    engine = awg;
                    break;
                }
                case "vless" or "hysteria" or "trojan":
                {
                    var r = await Api.SingboxConfig(s.server_type, s.id);
                    var sb = new SingBoxEngine(s.server_type);
                    _engine = sb;
                    await sb.Start(r.config);
                    engine = sb;
                    break;
                }
                case "bypass" when Zapret.BypassOff:
                    throw new UserError(Loc.Get("home.bypass_off"));   // обход выключен в Настройках (дев)
                case "bypass" when Zapret.UsesZapret && Zapret.Embedded:
                {
                    var z = new ZapretEngine();
                    await z.Start(AppSettings.Current.ZapretStrategy);
                    engine = z;
                    break;
                }
                case "bypass":
                {
                    try { AppCfg = await Api.AppConfig(); } catch { }
                    var b = new ByeDpiEngine();
                    await b.Start(AppCfg?.access?.bypass_strategy);
                    engine = b;
                    break;
                }
                default: throw new UserError(Loc.Get("home.not_on_windows"));
            }

            _engine = engine;
            _badChecks = 0;
            Since = DateTime.Now;
            RulesAtConnect = AppSettings.Current.RulesFingerprint();
            AppSettings.Current.LastServerId = s.id;
            AppSettings.Current.UserDisconnected = false;
            AppSettings.Save();
            Set(VpnStatus.On, s);
            Tray.Notify(Loc.F("tray.connected", s.DisplayName));
            Log.I("connected");
            return true;
        }
        catch (Exception e)
        {
            LastError = Message(e);
            Log.E("connect", e);
            if (TunnelGuard.Armed)
            {
                CloseTunnelSafely();
                Set(VpnStatus.Blocked);
            }
            else
            {
                await StopEngine();
                Set(VpnStatus.Off);
            }
            Since = null;
            return false;
        }
        finally { Gate.Release(); }
    }

    public static string Message(Exception e)
    {
        switch (e)
        {
            case UserError u: return u.Message;
            case EngineException en: return Loc.Get(en.Key);
            case ApiException a when a.Status == 403:
                var d = a.Detail ?? "";
                if (d.Contains("limit", StringComparison.OrdinalIgnoreCase) || d.Contains("лимит", StringComparison.OrdinalIgnoreCase))
                    return Loc.Get("home.limit_reached");
                if (d.Contains("подписк", StringComparison.OrdinalIgnoreCase) || d.Contains("subscri", StringComparison.OrdinalIgnoreCase))
                    return Loc.Get("home.error_subscription_only");
                return a.UserMessage();
            case ApiException a: return a.UserMessage();
            default: return Errors.Text(e);
        }
    }

    public static async Task Disconnect(bool byUser = true)
    {
        Interlocked.Increment(ref _connectionRequest);
        await Gate.WaitAsync();
        try
        {
            if (_engine == null && Status == VpnStatus.Off) return;
            Set(VpnStatus.Disconnecting);
            CloseTunnelSafely();
            await StopEngine();
            if (byUser) TunnelGuard.Disarm();
            Since = null;
            LastError = null;
            if (byUser) { AppSettings.Current.UserDisconnected = true; AppSettings.Save(); }
            Set(TunnelGuard.Armed ? VpnStatus.Blocked : VpnStatus.Off);
            if (byUser) Tray.Notify(Loc.Get("tray.disconnected"));
            Log.I("disconnected" + (byUser ? " by user" : ""));
        }
        catch (Exception e) { Log.E("disconnect", e); Set(TunnelGuard.Armed ? VpnStatus.Blocked : VpnStatus.Off); }
        finally { Gate.Release(); }
    }

    static async Task StopEngine()
    {
        var e = _engine;
        _engine = null;
        if (e != null) { try { await e.Stop(); } catch (Exception ex) { Log.E("stop", ex); } }
    }

    public static async Task<(long rx, long tx)> Traffic() =>
        _engine == null ? (0, 0) : await _engine.Traffic();

    static void CloseTunnelSafely()
    {
        try { TunnelGuard.CloseTunnel(); }
        catch (Exception e) { Log.E("remove tunnel exception", e); }
    }

    static async Task Check()
    {
        if (!await Gate.WaitAsync(0)) return;
        try
        {
            var engine = _engine;
            if (engine == null || Status != VpnStatus.On) return;
            var request = Volatile.Read(ref _connectionRequest);
            bool alive = engine.Alive();
            bool ok;
            try { ok = alive && await engine.Healthy(); } catch { ok = false; }
            if (request != Volatile.Read(ref _connectionRequest)) return;
            if (ok) { _badChecks = 0; return; }
            if (alive && ++_badChecks < 2) return;

            Log.I($"link lost: {engine.Kind}, process/service alive: {alive}");
            TunnelGuard.CloseTunnel();
            var reconnect = AppSettings.Current.AutoReconnect;
            if (reconnect)
            {
                Set(VpnStatus.Reconnecting);
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    if (request != Volatile.Read(ref _connectionRequest))
                    {
                        if (TunnelGuard.Armed) BlockAfterFailure();
                        return;
                    }
                    Tray.Notify(Loc.F("tray.reconnecting", attempt));
                    try
                    {
                        if (await engine.Restart())
                        {
                            _badChecks = 0;
                            Set(VpnStatus.On);
                            Log.I("reconnected");
                            return;
                        }
                    }
                    catch (Exception e) { Log.E("restart tunnel", e); }
                    TunnelGuard.CloseTunnel();
                    if (attempt < 3) await Task.Delay(TimeSpan.FromSeconds(attempt * 4));
                }
            }
            if (TunnelGuard.Armed)
            {
                BlockAfterFailure();
                return;
            }
            await StopEngine();
            Since = null;
            LastError = Loc.Get(reconnect ? "home.reconnect_failed" : "home.connection_lost");
            Set(VpnStatus.Off);
            Tray.Notify(LastError);
        }
        catch (Exception e)
        {
            Log.E("watchdog", e);
            if (TunnelGuard.Armed) BlockAfterFailure();
        }
        finally { Gate.Release(); }
    }

    static void BlockAfterFailure()
    {
        Since = null;
        LastError = Loc.Get("home.reconnect_failed_killswitch");
        Set(VpnStatus.Blocked);
        Tray.Notify(Loc.Get("tray.blocked"));
    }

    public static Task Unblock() => Disconnect();

    /// <summary>Leftovers of a crashed run (tunnel service, sing-box, ciadpi, system proxy).</summary>
    public static async Task CleanupAtStart()
    {
        TunnelGuard.Detect();
        if (TunnelGuard.Armed)
        {
            CloseTunnelSafely();
            Set(VpnStatus.Blocked);
        }
        try { await AwgEngine.Cleanup(); } catch { }
        Proc.KillOurs("sing-box.exe");
        Proc.KillOurs("ciadpi.exe");
        try { Zapret.KillOrphans(); } catch { }
        SystemProxy.Restore();
    }

    /// <summary>`-render`: show a state without a real connection.</summary>
    public static void DemoSet(VpnStatus st, ServerResponse? s)
    {
        if (!Api.DemoMode) return;
        Server = s ?? Server;
        Since = st == VpnStatus.On ? DateTime.Now.AddMinutes(-23).AddSeconds(-17) : null;
        Status = st;
        Changed?.Invoke();
    }

    public static void ShutdownBlocking()
    {
        try { Task.Run(() => Disconnect(false)).Wait(TimeSpan.FromSeconds(12)); } catch { }
    }
}
