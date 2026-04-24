using System.Windows;
using System.Windows.Controls;

namespace FsocietyNet.Controls;

public partial class AppShell : UserControl
{
    private readonly string _token;
    private HomeControl? _home;

    public AppShell(string token)
    {
        _token = token;
        InitializeComponent();
        ShowHome();
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
        TabContent.Content = new ComingSoonControl("Туннелирование");
        SetActiveTab(TunnelTabBtn);
    }

    private void SupportTab_Click(object sender, RoutedEventArgs e)
    {
        TabContent.Content = new ComingSoonControl("Поддержка");
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
        HomeTabBtn.Style    = inactive;
        TunnelTabBtn.Style  = inactive;
        SupportTabBtn.Style = inactive;
        SettingsTabBtn.Style = inactive;
        active.Style = accent!;
    }
}
