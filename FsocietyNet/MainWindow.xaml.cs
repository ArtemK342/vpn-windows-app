using System.IO;
using System.Windows;
using FsocietyNet.Controls;

namespace FsocietyNet;

public partial class MainWindow : Window
{
    private static MainWindow _instance = null!;

    public static void Navigate(UIElement control) =>
        _instance.RootContent.Content = control;

    public MainWindow()
    {
        _instance = this;
        InitializeComponent();

        var token = LoadToken();
        if (!string.IsNullOrEmpty(token))
            Navigate(new AppShell(token));
        else
            Navigate(new LoginControl());
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
