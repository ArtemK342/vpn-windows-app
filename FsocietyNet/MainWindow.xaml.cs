using System.IO;
using System.Windows;
using FsocietyNet.Controls;
using FsocietyNet.Services;

namespace FsocietyNet;

public partial class MainWindow : Window
{
    private static MainWindow _instance = null!;
    private readonly VpnService _vpn = new();
    private bool _reallyClosing = false;

    public static TrayService? Tray { get; private set; }

    public static void Navigate(UIElement control) =>
        _instance.RootContent.Content = control;

    public MainWindow()
    {
        _instance = this;
        InitializeComponent();

        // Create tray icon
        Tray = new TrayService(
            onOpen:       ShowFromTray,
            onConnect:    VpnState.RequestConnectBest,
            onDisconnect: VpnState.RequestDisconnect,
            onExit:       () => { _reallyClosing = true; Close(); }
        );

        // Subscribe to VPN state changes → update tray
        VpnState.StateChanged += (connected, serverName) =>
        {
            Dispatcher.InvokeAsync(() => Tray?.SetConnected(connected, serverName));
        };

        Closing += MainWindow_Closing;

        // Extract amneziawg.exe + wintun.dll from embedded resources
        ResourceExtractor.EnsureExtracted();

        // Clean up any leftover tunnel from previous run
        _ = CleanupOldTunnelAsync();

        var token = LoadToken();
        if (!string.IsNullOrEmpty(token))
            Navigate(new AppShell(token));
        else
            Navigate(new LoginControl());
    }

    private async Task CleanupOldTunnelAsync()
    {
        try { await _vpn.DisconnectAsync(); } catch { }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_reallyClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        // Real close — dispose tray and disconnect tunnel
        Tray?.Dispose();
        Tray = null;

        try
        {
            _vpn.DisconnectAsync().GetAwaiter().GetResult();
        }
        catch { }
    }

    private void ShowFromTray()
    {
        Visibility     = Visibility.Visible;
        ShowInTaskbar  = true;
        WindowState    = WindowState.Normal;
        Activate();
        Focus();
    }

    private void HideToTray()
    {
        Visibility    = Visibility.Hidden;
        ShowInTaskbar = false;
    }

    // ── Token helpers ──────────────────────────────────────────────────────

    public static string? LoadToken()
    {
        var path = TokenPath();
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    public static void SaveToken(string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TokenPath())!);
        File.WriteAllText(TokenPath(), token);
    }

    public static void ClearToken()
    {
        var path = TokenPath();
        if (File.Exists(path)) File.Delete(path);
    }

    private static string TokenPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FsocietyNet", "token.txt");
}
