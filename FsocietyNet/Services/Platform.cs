using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using FsocietyNet.Views;

namespace FsocietyNet.Services;

/// <summary>
/// Tray icon (the brand picture at the shell's icon size) and its menu. The menu is a WPF ContextMenu with the app's
/// own styles (rounded, theme colours, icons): version, open, connect / disconnect, restart, quit.
/// </summary>
public static class Tray
{
    static WinForms.NotifyIcon? _icon;
    static readonly Dictionary<BrandState, Drawing.Icon> Icons = [];
    // Invisible owner window: a tray menu closes on a click outside only while the app is the foreground window
    static Window? _host;

    public static event Action? OpenRequested;
    public static event Action? ExitRequested;
    public static event Action? RestartRequested;
    public static Func<bool>? WindowVisible;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hwnd);

    public static void Init()
    {
        _icon = new WinForms.NotifyIcon { Visible = true };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) OpenRequested?.Invoke();
            else if (e.Button == WinForms.MouseButtons.Right) ShowMenu();
        };
        Vpn.Changed += Update;
        Update();
    }

    static System.Windows.Controls.MenuItem Item(string text, string icon, Action click, string? brush = null, bool enabled = true)
    {
        var mi = new System.Windows.Controls.MenuItem { Header = text, IsEnabled = enabled };
        var ic = new Icon { Data = Ui.Geo(icon), Size = 16 };
        if (brush != null) ic.Brush = Ui.Res(brush);
        mi.Icon = ic;
        mi.Click += (_, _) => click();
        return mi;
    }

    static void ShowMenu()
    {
        _host ??= new Window
        {
            WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false, ShowActivated = true, ResizeMode = ResizeMode.NoResize,
            Width = 1, Height = 1, Left = -20000, Top = -20000, Topmost = true, Title = "",
        };
        _host.Show();
        _host.Activate();
        SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(_host).Handle);

        var m = BuildMenu();
        m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        m.PlacementTarget = _host;
        m.Closed += (_, _) => _host?.Hide();
        m.IsOpen = true;
    }

    internal static System.Windows.Controls.ContextMenu BuildMenu()
    {
        var m = new System.Windows.Controls.ContextMenu();
        // Header: name and version, greyed like in other tray apps
        m.Items.Add(new System.Windows.Controls.MenuItem { Header = $"FsocietyNet {App.Version}", IsEnabled = false });
        m.Items.Add(new System.Windows.Controls.Separator());
        m.Items.Add(Item(Loc.Get("tray.open"), "I.Home", () => OpenRequested?.Invoke()));
        m.Items.Add(Vpn.Status switch
        {
            VpnStatus.Blocked => Item(Loc.Get("home.unblock"), "I.Shield", async () => await Vpn.Unblock(), "DangerBrush"),
            VpnStatus.On or VpnStatus.Reconnecting or VpnStatus.Connecting =>
                Item(Loc.F("tray.disconnect_from", Vpn.Server?.DisplayName ?? ""), "I.Power", async () => await Vpn.Disconnect(), "AccentTextBrush"),
            VpnStatus.Disconnecting => Item(Loc.Get("home.disconnecting"), "I.Power", () => { }, null, false),
            _ => Item(Loc.Get("tray.connect"), "I.Power", async () =>
            {
                var s = Vpn.BestServer();
                if (s == null) { OpenRequested?.Invoke(); return; }
                await Vpn.Connect(s);
            }),
        });
        m.Items.Add(new System.Windows.Controls.Separator());
        m.Items.Add(Item(Loc.Get("tray.restart"), "I.Refresh", () => RestartRequested?.Invoke()));
        m.Items.Add(Item(Loc.Get("tray.exit"), "I.Logout", () => ExitRequested?.Invoke()));
        return m;
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
        _host?.Close();
        _host = null;
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
