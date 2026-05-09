using System.IO;
using System.Windows;
using FsocietyNet.Controls;
using FsocietyNet.Services;

namespace FsocietyNet;

public partial class MainWindow : Window
{
    private static MainWindow _instance = null!;
    private readonly VpnService _vpn = new();

    public static void Navigate(UIElement control) =>
        _instance.RootContent.Content = control;

    public MainWindow()
    {
        _instance = this;
        InitializeComponent();
        Closing += MainWindow_Closing;

        // Извлекаем amneziawg.exe + wintun.dll из embedded resources
        ResourceExtractor.EnsureExtracted();

        // При старте всегда чистим старый туннель (если остался после крэша/пересборки)
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
        // Синхронно отключаем туннель при закрытии окна
        try
        {
            _vpn.DisconnectAsync().GetAwaiter().GetResult();
        }
        catch { }
    }

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
