using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FsocietyNet.Models;
using FsocietyNet.Services;
using Microsoft.Win32;

namespace FsocietyNet.Controls;

public partial class SettingsControl : UserControl
{
    private readonly ApiService  _api;
    private readonly VpnService  _vpn = new();
    private readonly string      _token;
    private AppConfig            _cfg  = AppConfig.Load();
    private SubscriptionResponse? _sub;

    private static readonly SolidColorBrush AccentBrushColor = new(Color.FromRgb(0xC8, 0xF1, 0x35));
    private static readonly SolidColorBrush MutedBrushColor  = new(Color.FromRgb(0x88, 0x88, 0x92));
    private static readonly SolidColorBrush DimBrushColor    = new(Color.FromRgb(0x22, 0x22, 0x22));

    public SettingsControl(string token)
    {
        _token = token;
        _api   = new ApiService();
        InitializeComponent();
        _ = LoadAsync();

        UpdateToggleUi(KsToggle, KsThumb, _cfg.KillSwitch);
        UpdateToggleUi(AutoStartToggle, AutoStartThumb, _cfg.AutoStart);
        UpdateToggleUi(AutoConnectToggle, AutoConnectThumb, _cfg.AutoConnect);
        UpdateToggleUi(AutoReconnectToggle, AutoReconnectThumb, _cfg.AutoReconnect);

        // Show real WG version
        var wgVer = VpnService.GetWireGuardVersion();
        WgText.Text = _vpn.IsWireGuardInstalled
            ? $"{(_vpn.IsAmneziaWG ? "AmneziaWG" : "WireGuard")} v{wgVer}"
            : "✗ Не найден — скачайте AmneziaWG";
        WgText.Foreground = _vpn.IsWireGuardInstalled
            ? (Brush)Application.Current.Resources["AccentBrush"]!
            : (Brush)Application.Current.Resources["ErrorRedBrush"]!;
    }

    private async Task LoadAsync()
    {
        try
        {
            var user = await _api.GetMeAsync(_token);
            EmailText.Text = user.email;

            _sub = await _api.GetSubscriptionAsync(_token);
            if (_sub?.is_active == true)
            {
                var expires = _sub.expires_at?.Length >= 10 ? _sub.expires_at[..10] : _sub.expires_at ?? "";
                SubText.Text = $"{_sub.plan} · до {expires}";
                BuySubBtn.Visibility = Visibility.Collapsed;
            }
            else
            {
                SubText.Text = "Нет активной подписки";
                BuySubBtn.Visibility = Visibility.Visible;
            }
        }
        catch
        {
            EmailText.Text = "Ошибка загрузки";
        }
    }

    // ──────────────────── Toggle helpers ────────────────────

    private void UpdateToggleUi(Border toggle, System.Windows.Shapes.Ellipse thumb, bool on)
    {
        toggle.Background          = on ? AccentBrushColor : DimBrushColor;
        thumb.Fill                 = on ? new SolidColorBrush(Colors.Black) : MutedBrushColor;
        thumb.HorizontalAlignment  = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        thumb.Margin               = on ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
    }

    // ──────────────────── Kill Switch UI ────────────────────

    private void UpdateKsToggleUi()
    {
        UpdateToggleUi(KsToggle, KsThumb, _cfg.KillSwitch);
        KsDescText.Text      = _cfg.KillSwitch
            ? "● Активен — трафик заблокирован при отключённом VPN."
            : "Весь трафик будет заблокирован, пока VPN не подключён.";
        KsDescText.Foreground = _cfg.KillSwitch ? AccentBrushColor : MutedBrushColor;
    }

    private async void KsToggle_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _cfg.KillSwitch = !_cfg.KillSwitch;
        _cfg.Save();
        UpdateKsToggleUi();
        try
        {
            if (_cfg.KillSwitch) await VpnService.EnableKillSwitchAsync();
            else                 await VpnService.DisableKillSwitchAsync();
        }
        catch { }
    }

    // ──────────────────── AutoStart ────────────────────

    private void AutoStartToggle_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _cfg.AutoStart = !_cfg.AutoStart;
        _cfg.Save();
        UpdateToggleUi(AutoStartToggle, AutoStartThumb, _cfg.AutoStart);

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return;
            if (_cfg.AutoStart)
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue("FsocietyNet", $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue("FsocietyNet", throwOnMissingValue: false);
            }
        }
        catch { }
    }

    // ──────────────────── AutoConnect ────────────────────

    private void AutoConnectToggle_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _cfg.AutoConnect = !_cfg.AutoConnect;
        _cfg.Save();
        UpdateToggleUi(AutoConnectToggle, AutoConnectThumb, _cfg.AutoConnect);
    }

    // ──────────────────── AutoReconnect ────────────────────

    private void AutoReconnectToggle_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _cfg.AutoReconnect = !_cfg.AutoReconnect;
        _cfg.Save();
        UpdateToggleUi(AutoReconnectToggle, AutoReconnectThumb, _cfg.AutoReconnect);
    }

    // ──────────────────── Buy subscription ────────────────────

    private void BuySubBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://fsociety-vpn.org/#pricing")
                { UseShellExecute = true });
        }
        catch { }
    }

    // ──────────────────── Change password ────────────────────

    private void ShowChangePassBtn_Click(object sender, RoutedEventArgs e)
    {
        ChangePasswordPanel.Visibility = Visibility.Visible;
        ShowChangePassBtn.Visibility   = Visibility.Collapsed;
        PwErrorText.Visibility         = Visibility.Collapsed;
    }

    private void CancelPassBtn_Click(object sender, RoutedEventArgs e)
    {
        ChangePasswordPanel.Visibility = Visibility.Collapsed;
        ShowChangePassBtn.Visibility   = Visibility.Visible;
        OldPassBox.Password     = "";
        NewPassBox.Password     = "";
        ConfirmPassBox.Password = "";
        PwErrorText.Visibility  = Visibility.Collapsed;
    }

    private async void SavePassBtn_Click(object sender, RoutedEventArgs e)
    {
        PwErrorText.Visibility = Visibility.Collapsed;

        var oldPass     = OldPassBox.Password;
        var newPass     = NewPassBox.Password;
        var confirmPass = ConfirmPassBox.Password;

        if (string.IsNullOrEmpty(oldPass) || string.IsNullOrEmpty(newPass) || string.IsNullOrEmpty(confirmPass))
        {
            PwErrorText.Text       = "Заполните все поля";
            PwErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (newPass != confirmPass)
        {
            PwErrorText.Text       = "Новые пароли не совпадают";
            PwErrorText.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            await _api.ChangePasswordAsync(_token, oldPass, newPass);
            PwErrorText.Foreground = AccentBrushColor;
            PwErrorText.Text       = "Пароль изменён";
            PwErrorText.Visibility = Visibility.Visible;
            OldPassBox.Password     = "";
            NewPassBox.Password     = "";
            ConfirmPassBox.Password = "";

            // Hide panel after a moment
            await Task.Delay(2000);
            CancelPassBtn_Click(sender, e);
            PwErrorText.Foreground = (Brush)Application.Current.Resources["ErrorRedBrush"]!;
        }
        catch (Exception ex)
        {
            PwErrorText.Foreground = (Brush)Application.Current.Resources["ErrorRedBrush"]!;
            PwErrorText.Text = ex.Message.Contains("400") || ex.Message.Contains("401")
                ? "Неверный текущий пароль"
                : "Ошибка при смене пароля";
            PwErrorText.Visibility = Visibility.Visible;
        }
    }

    // ──────────────────── Выход ────────────────────

    private async void LogoutBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_cfg.KillSwitch)
        {
            _cfg.KillSwitch = false;
            _cfg.Save();
            try { await VpnService.DisableKillSwitchAsync(); } catch { }
        }

        MainWindow.ClearToken();
        MainWindow.Navigate(new LoginControl());
    }
}
