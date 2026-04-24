using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FsocietyNet.Models;
using FsocietyNet.Services;

namespace FsocietyNet.Controls;

public class ServerItem : INotifyPropertyChanged
{
    public ServerResponse Server { get; init; } = null!;
    public string Name    => Server.name;
    public string Flag    => Server.country switch
    {
        "Finland"     => "🇫🇮",
        "Germany"     => "🇩🇪",
        "Switzerland" => "🇨🇭",
        "Russia"      => "🇷🇺",
        "Netherlands" => "🇳🇱",
        _             => "🌍"
    };

    private int _ping = -1;
    private bool _isConnected;

    public int Ping
    {
        get => _ping;
        set { _ping = value; OnChanged(nameof(PingText)); OnChanged(nameof(PingColor)); }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set { _isConnected = value; OnChanged(nameof(PingText)); OnChanged(nameof(PingColor)); }
    }

    public string PingText => IsConnected ? "● ПОДКЛЮЧЁН" : !Server.is_active ? "СКОРО" :
        _ping < 0 ? "● ..." : _ping >= 999 ? "● —" : $"● {_ping}мс";

    public Brush PingColor
    {
        get
        {
            if (IsConnected)        return new SolidColorBrush(Color.FromRgb(0xC8, 0xF1, 0x35));
            if (!Server.is_active)  return new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x92));
            if (_ping < 0)          return new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x92));
            if (_ping >= 999)       return new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x92));
            if (_ping < 100)        return new SolidColorBrush(Color.FromRgb(0xC8, 0xF1, 0x35));
            if (_ping < 200)        return new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x00));
            return new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class HomeControl : UserControl
{
    private readonly string _token;
    private readonly ApiService _api   = new();
    private readonly VpnService _vpn   = new();
    private readonly ObservableCollection<ServerItem> _items = new();
    private bool _isConnected;
    private ServerItem? _connectedItem;

    public HomeControl(string token)
    {
        _token = token;
        InitializeComponent();
        ServerList.ItemsSource = _items;
        _ = LoadServersAsync();
    }

    private async Task LoadServersAsync()
    {
        try
        {
            var servers = await _api.GetServersAsync(_token);
            LoadingText.Visibility = Visibility.Collapsed;
            ServerList.Visibility  = Visibility.Visible;

            foreach (var s in servers)
                _items.Add(new ServerItem { Server = s });

            // Выбираем первый активный
            var first = _items.FirstOrDefault(s => s.Server.is_active);
            if (first != null)
            {
                ServerList.SelectedItem = first;
                ConnectBtn.IsEnabled    = true;
            }

            // Пингуем в фоне
            _ = PingAllAsync();
        }
        catch
        {
            LoadingText.Text = "Ошибка загрузки серверов";
        }
    }

    private async Task PingAllAsync()
    {
        var ping = await VpnService.MeasurePingAsync();
        foreach (var item in _items)
            if (item.Server.is_active)
                item.Ping = ping;
    }

    private void ServerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isConnected)
            ConnectBtn.IsEnabled = ServerList.SelectedItem is ServerItem { Server.is_active: true };
    }

    private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnected)
        {
            // Отключаемся
            ConnectBtn.IsEnabled  = false;
            ConnectBtn.Content    = "Отключение...";
            SetStatus("");
            try { await _vpn.DisconnectAsync(); } catch { }
            _isConnected = false;
            if (_connectedItem != null) _connectedItem.IsConnected = false;
            _connectedItem = null;
            ConnectBtn.Style   = Application.Current.Resources["PrimaryButton"] as Style;
            ConnectBtn.Content = "○ ПОДКЛЮЧИТЬСЯ";
            ConnectedText.Text = "○ Отключён";
            ConnectedText.Foreground = Application.Current.Resources["TextMutedBrush"] as Brush;
            ConnectBtn.IsEnabled = true;
            return;
        }

        if (ServerList.SelectedItem is not ServerItem selected) return;

        ConnectBtn.IsEnabled = false;
        ConnectBtn.Content   = "Получение конфига...";
        SetStatus("Получение конфигурации...");

        try
        {
            var resp = await _api.GetVpnConfigAsync(_token, selected.Server.id);
            if (resp.config == null)
            {
                SetStatus(resp.message ?? "Ошибка получения конфига");
                ConnectBtn.IsEnabled = true;
                ConnectBtn.Content   = "○ ПОДКЛЮЧИТЬСЯ";
                return;
            }

            SetStatus("Подключение...");
            ConnectBtn.Content = "Подключение...";
            var ok = await _vpn.ConnectAsync(resp.config);

            if (ok)
            {
                _isConnected = true;
                _connectedItem = selected;
                selected.IsConnected = true;
                ConnectBtn.Style   = Application.Current.Resources["DisconnectButton"] as Style;
                ConnectBtn.Content = "● ОТКЛЮЧИТЬСЯ";
                ConnectedText.Text = $"● {selected.Name}";
                ConnectedText.Foreground = Application.Current.Resources["AccentBrush"] as Brush;
                SetStatus($"● {selected.Name}");
                StatusText.Foreground = Application.Current.Resources["AccentBrush"] as Brush;
            }
            else
            {
                SetStatus("Ошибка подключения");
                ConnectBtn.Content = "○ ПОДКЛЮЧИТЬСЯ";
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message.Contains("WireGuard") ? ex.Message : "Ошибка сети");
            ConnectBtn.Content = "○ ПОДКЛЮЧИТЬСЯ";
        }
        finally
        {
            ConnectBtn.IsEnabled = true;
        }
    }

    private void AllTab_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { }
    private void FavTab_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { }

    private void SetStatus(string msg)
    {
        StatusText.Text       = msg;
        StatusText.Visibility = string.IsNullOrEmpty(msg) ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Foreground = Application.Current.Resources["TextMutedBrush"] as Brush;
    }
}
