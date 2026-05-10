using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using FsocietyNet.Services;

namespace FsocietyNet.Controls;

public partial class AppShell : UserControl
{
    private readonly string _token;
    private HomeControl? _home;
    private string? _updateUrl;

    public AppShell(string token)
    {
        _token = token;
        InitializeComponent();
        ShowHome();
        _ = CheckUpdateAsync();
    }

    private async Task CheckUpdateAsync()
    {
        var result = await UpdateService.CheckAsync();
        if (!result.HasUpdate) return;

        _updateUrl        = result.ReleaseUrl;
        UpdateText.Text   = $"Доступна новая версия {result.LatestVersion} →";
        UpdateBanner.Visibility = Visibility.Visible;
    }

    private void UpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_updateUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo(_updateUrl) { UseShellExecute = true });
        }
        catch { }
    }

    private void ShowHome()
    {
        _home ??= new HomeControl(_token);
        TabContent.Content = _home;
        SetActiveTab(HomeTabBtn);
    }

    private void HomeTab_Click(object sender, RoutedEventArgs e) => ShowHome();

    private void TunnelTab_Click(object sender, RoutedEventArgs e)
    {
        TabContent.Content = new TunnelingControl();
        SetActiveTab(TunnelTabBtn);
    }

    private void StatsTab_Click(object sender, RoutedEventArgs e)
    {
        TabContent.Content = new StatsControl();
        SetActiveTab(StatsTabBtn);
    }

    private void SupportTab_Click(object sender, RoutedEventArgs e)
    {
        TabContent.Content = new SupportControl(_token);
        SetActiveTab(SupportTabBtn);
    }

    private void SettingsTab_Click(object sender, RoutedEventArgs e)
    {
        TabContent.Content = new SettingsControl(_token);
        SetActiveTab(SettingsTabBtn);
    }

    private void SetActiveTab(Button active)
    {
        var accent   = Application.Current.Resources["TabButtonActive"] as Style;
        var inactive = Application.Current.Resources["TabButton"]       as Style;
        HomeTabBtn.Style     = inactive;
        TunnelTabBtn.Style   = inactive;
        StatsTabBtn.Style    = inactive;
        SupportTabBtn.Style  = inactive;
        SettingsTabBtn.Style = inactive;
        active.Style = accent!;
    }
}
