using System.Windows;
using System.Windows.Controls;
using FsocietyNet.Services;

namespace FsocietyNet.Controls;

public partial class SettingsControl : UserControl
{
    private readonly ApiService _api;
    private readonly VpnService _vpn = new();
    private readonly string _token;

    public SettingsControl(string token)
    {
        _token = token;
        _api   = new ApiService();
        InitializeComponent();
        _ = LoadAsync();
        WgText.Text = _vpn.IsWireGuardInstalled
            ? "✓ Установлен"
            : "✗ Не найден — скачайте wireguard.com";
        WgText.Foreground = _vpn.IsWireGuardInstalled
            ? Application.Current.Resources["AccentBrush"] as System.Windows.Media.Brush
            : Application.Current.Resources["ErrorRedBrush"] as System.Windows.Media.Brush;
    }

    private async Task LoadAsync()
    {
        try
        {
            var user = await _api.GetMeAsync(_token);
            EmailText.Text = user.email;
            var sub = await _api.GetSubscriptionAsync(_token);
            if (sub?.is_active == true)
            {
                var expires = sub.expires_at?.Length >= 10 ? sub.expires_at[..10] : sub.expires_at ?? "";
                SubText.Text = $"{sub.plan} · до {expires}";
            }
            else
            {
                SubText.Text = "Нет активной подписки";
            }
        }
        catch
        {
            EmailText.Text = "Ошибка загрузки";
        }
    }

    private void LogoutBtn_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.ClearToken();
        MainWindow.Navigate(new LoginControl());
    }
}
