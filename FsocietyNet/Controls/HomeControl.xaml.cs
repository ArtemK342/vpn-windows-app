using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FsocietyNet.Models;
using FsocietyNet.Services;

namespace FsocietyNet.Controls;

public class ServerItem : INotifyPropertyChanged
{
    public ServerResponse Server { get; init; } = null!;
    public string Name => Server.name;
    public string Flag => Server.country switch
    {
        "Finland"     => "🇫🇮",
        "Germany"     => "🇩🇪",
        "Switzerland" => "🇨🇭",
        "Russia"      => "🇷🇺",
        "Netherlands" => "🇳🇱",
        _             => "🌍"
    };

    private int  _ping        = -1;
    private bool _isConnected = false;
    private bool _isFavorite  = false;

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

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            _isFavorite = value;
            OnChanged(nameof(IsFavorite));
            OnChanged(nameof(FavoriteIcon));
            OnChanged(nameof(FavoriteColor));
        }
    }

    public string FavoriteIcon  => _isFavorite ? "★" : "☆";
    public Brush  FavoriteColor => _isFavorite
        ? new SolidColorBrush(Color.FromRgb(0xC8, 0xF1, 0x35))
        : new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));

    public string PingText => IsConnected ? "● ПОДКЛЮЧЁН" : !Server.is_active ? "СКОРО" :
        _ping < 0 ? "● ..." : _ping >= 999 ? "● —" : $"● {_ping}мс";

    public Brush PingColor
    {
        get
        {
            if (IsConnected)       return B(0xC8, 0xF1, 0x35);
            if (!Server.is_active) return B(0x88, 0x88, 0x92);
            if (_ping < 0)         return B(0x88, 0x88, 0x92);
            if (_ping >= 999)      return B(0x88, 0x88, 0x92);
            if (_ping < 100)       return B(0xC8, 0xF1, 0x35);
            if (_ping < 200)       return B(0xFF, 0xAA, 0x00);
            return                        B(0xFF, 0x44, 0x44);
        }
    }

    private static SolidColorBrush B(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public partial class HomeControl : UserControl
{
    private readonly string     _token;
    private readonly ApiService _api  = new();
    private readonly VpnService _vpn  = new();
    private readonly ObservableCollection<ServerItem> _items = new();

    private bool        _isConnected;
    private bool        _isBusy;
    private bool        _suppressSelection;
    private ServerItem? _connectedItem;

    // Auto-reconnect watchdog
    private readonly DispatcherTimer _watchdog = new() { Interval = TimeSpan.FromSeconds(30) };
    private int _reconnectAttempts = 0;

    private Brush AccentBrush => (Brush)Application.Current.Resources["AccentBrush"]!;
    private Brush MutedBrush  => (Brush)Application.Current.Resources["TextMutedBrush"]!;

    public HomeControl(string token)
    {
        _token = token;
        InitializeComponent();
        ServerList.ItemsSource = _items;

        // Wire VpnState events
        VpnState.ConnectBestRequested += OnConnectBestRequested;
        VpnState.DisconnectRequested  += OnDisconnectRequested;

        // Sync button/status when VPN state changes from outside (e.g. tray)
        VpnState.StateChanged += OnVpnStateChanged;

        // Watchdog
        _watchdog.Tick += WatchdogTick;
        _watchdog.Start();

        _ = LoadServersAsync();
        _ = LoadUsageAsync();
    }

    private void OnVpnStateChanged(bool connected, string? serverName)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (connected && !_isConnected)
            {
                // Tray connected — update button state without knowing the server item
                ConnectBtn.Style   = (Style)Application.Current.Resources["DisconnectButton"]!;
                ConnectBtn.Content = "● Отключиться";
                ConnectBtn.IsEnabled = true;
                ConnectedText.Text       = serverName != null ? $"●  {serverName}" : "●  Подключён";
                ConnectedText.Foreground = AccentBrush;
                _isConnected = true;
            }
            else if (!connected && _isConnected)
            {
                // Tray disconnected
                ConnectedText.Text       = "○  Отключён";
                ConnectedText.Foreground = MutedBrush;
                SetBtn("Подключиться", true, primary: true);
                _isConnected = false;
                if (_connectedItem != null) { _connectedItem.IsConnected = false; _connectedItem = null; }
            }
        });
    }

    // ── VpnState callbacks ───────────────────────────────────────────────

    private void OnConnectBestRequested() =>
        Dispatcher.InvokeAsync(() => ConnectBtn_Click(this, new RoutedEventArgs()));

    private void OnDisconnectRequested() =>
        Dispatcher.InvokeAsync(() => { if (_isConnected) _ = DisconnectCurrentAsync(); });

    // ── Watchdog ─────────────────────────────────────────────────────────

    private async void WatchdogTick(object? sender, EventArgs e)
    {
        if (!_isConnected || _isBusy) return;
        var cfg = AppConfig.Load();
        if (!cfg.AutoReconnect) return;
        if (VpnService.TunnelServiceExists()) { _reconnectAttempts = 0; return; }

        _reconnectAttempts++;
        if (_reconnectAttempts > 3)
        {
            _isConnected = false;
            VpnState.IsConnected = false;
            if (_connectedItem != null) _connectedItem.IsConnected = false;
            _connectedItem = null;
            ConnectedText.Text       = "○  Отключён";
            ConnectedText.Foreground = MutedBrush;
            SetBtn("Подключиться", true, primary: true);
            MainWindow.Tray?.ShowNotification("[f]society VPN",
                "VPN отключился. Не удалось переподключиться.", WinForms.ToolTipIcon.Warning);
            _reconnectAttempts = 0;
            return;
        }

        MainWindow.Tray?.ShowNotification("[f]society VPN",
            $"VPN отключился — переподключение ({_reconnectAttempts}/3)...");
        if (_connectedItem != null) await ConnectToServerAsync(_connectedItem);
    }

    // ───────────────────────── Загрузка ─────────────────────────

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824L => $"{bytes / 1_073_741_824.0:F1} ГБ",
        >= 1_048_576L     => $"{bytes / 1_048_576.0:F1} МБ",
        >= 1024L          => $"{bytes / 1024.0:F0} КБ",
        _                 => $"{bytes} Б"
    };

    private async Task LoadUsageAsync()
    {
        try
        {
            var usage = await _api.GetUsageAsync(_token);
            if (usage == null || !usage.is_limited) return;

            var remaining = Math.Max(0L, usage.limit_bytes - usage.bytes_used);
            var fraction  = Math.Clamp((double)usage.bytes_used / usage.limit_bytes, 0, 1);

            var barColor = fraction switch
            {
                >= 1.0 => new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44)),
                >= 0.8 => new SolidColorBrush(Color.FromRgb(0xFF, 0xAA, 0x00)),
                _      => (Brush)Application.Current.Resources["AccentBrush"]!
            };

            Dispatcher.InvokeAsync(() =>
            {
                UsageText.Text = $"Осталось {FormatBytes(remaining)} из {FormatBytes(usage.limit_bytes)}";
                UsageText.Foreground = barColor;
                UsageBar.Width  = 160 * fraction;
                UsageBar.Fill   = barColor;
                UsageBanner.Visibility = Visibility.Visible;
            });
        }
        catch { }
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
            _ = PingAllAsync();

            // Auto-connect to last server
            var cfg = AppConfig.Load();
            if (cfg.AutoConnect && !string.IsNullOrEmpty(cfg.LastServerId))
            {
                var target = _items.FirstOrDefault(i =>
                    i.Server.id == cfg.LastServerId && i.Server.allow_auto_connect);
                if (target != null) await ConnectToServerAsync(target);
            }
        }
        catch
        {
            LoadingText.Text = "Ошибка загрузки серверов";
        }
    }

    private async Task PingAllAsync()
    {
        RefreshBtn.IsEnabled = false;
        var tasks = _items.Where(i => i.Server.is_active).Select(async item =>
        {
            var parts = item.Server.endpoint?.Split(':');
            if (parts is not { Length: > 0 }) return;
            var host = parts[0];
            if (string.IsNullOrEmpty(host)) return;
            var ping = await VpnService.MeasurePingAsync(host);
            if (ping >= 999) ping = await VpnService.MeasureTcpAsync(host, 443);
            item.Ping = ping;
        });
        await Task.WhenAll(tasks);
        RefreshBtn.IsEnabled = true;
    }

    // ───────────────────────── Вкладки ─────────────────────────

    private void AllTab_Click(object sender, MouseButtonEventArgs e)
    {
        var view = CollectionViewSource.GetDefaultView(_items);
        view.Filter = null;
        AllTabText.Foreground = AccentBrush;
        AllTabText.FontWeight = FontWeights.Bold;
        FavTabText.Foreground = MutedBrush;
        FavTabText.FontWeight = FontWeights.Normal;
    }

    private void FavTab_Click(object sender, MouseButtonEventArgs e)
    {
        var view = CollectionViewSource.GetDefaultView(_items);
        view.Filter = o => o is ServerItem item && item.IsFavorite;
        FavTabText.Foreground = AccentBrush;
        FavTabText.FontWeight = FontWeights.Bold;
        AllTabText.Foreground = MutedBrush;
        AllTabText.FontWeight = FontWeights.Normal;
    }

    private void Star_Down(object sender, MouseButtonEventArgs e)
    {
        _suppressSelection = true;
    }

    private void Star_Up(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ServerItem item)
            item.IsFavorite = !item.IsFavorite;

        CollectionViewSource.GetDefaultView(_items).Refresh();
        e.Handled = true;

        Dispatcher.InvokeAsync(
            () => _suppressSelection = false,
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items)
            if (!item.IsConnected) item.Ping = -1;
        await PingAllAsync();
    }

    // ───────────────── Клик по серверу = подключиться ───────────

    private void ServerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || _isBusy) return;
        if (ServerList.SelectedItem is not ServerItem selected) return;
        if (!selected.Server.is_active) return;
        if (_isConnected && _connectedItem == selected) return;
        _ = ConnectToServerAsync(selected);
    }

    private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnected) { await DisconnectCurrentAsync(); return; }

        var best = _items
            .Where(i => i.Server.is_active && i.Server.allow_auto_connect && i.Ping > 0 && i.Ping < 999)
            .OrderBy(i => i.Ping)
            .FirstOrDefault()
            ?? _items.FirstOrDefault(i => i.Server.is_active && i.Server.allow_auto_connect);

        if (best == null) return;

        _suppressSelection = true;
        ServerList.SelectedItem = best;
        _suppressSelection = false;

        await ConnectToServerAsync(best);
    }

    // ───────────────────── Логика подключения ─────────────────────

    private async Task ConnectToServerAsync(ServerItem target)
    {
        if (_isBusy) return;
        _isBusy = true;
        try
        {
            if (_isConnected && _connectedItem != target)
            {
                SetBtn("Переключение...", false);
                await DisconnectCurrentAsync(silent: true);
            }

            SetBtn("Получение конфига...", false);
            SetStatus($"Подключение к {target.Name}...");

            var resp = await _api.GetVpnConfigAsync(_token, target.Server.id);
            if (resp.config == null)
            {
                SetStatus(resp.message ?? "Ошибка конфига");
                SetBtn("Подключиться", true, primary: true);
                return;
            }

            SetBtn("Подключение...", false);
            // Run off UI thread — WireGuard service install can take 5-10s and cause ANR
            var ok = await Task.Run(async () => await _vpn.ConnectAsync(resp.config!));

            if (ok)
            {
                _isConnected   = true;
                _connectedItem = target;
                target.IsConnected = true;
                ConnectBtn.Style   = (Style)Application.Current.Resources["DisconnectButton"]!;
                ConnectBtn.Content = "● Отключиться";
                ConnectBtn.IsEnabled = true;
                ConnectedText.Text       = $"●  {target.Name}";
                ConnectedText.Foreground = AccentBrush;
                SetStatus("");

                // Update global state
                VpnState.ConnectedServerName = target.Name;
                VpnState.IsConnected = true;

                // Save last server
                var cfg2 = AppConfig.Load();
                cfg2.LastServerId = target.Server.id;
                cfg2.Save();

                MainWindow.Tray?.ShowNotification("[f]society VPN", $"Подключён: {target.Name}");
            }
            else
            {
                SetStatus("Ошибка подключения");
                SetBtn("Подключиться", true, primary: true);
                MainWindow.Tray?.ShowNotification("[f]society VPN", "Ошибка подключения", WinForms.ToolTipIcon.Error);
            }
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            SetStatus(msg.Contains("AmneziaWG") || msg.Contains("WireGuard") ? "AmneziaWG не установлен"
                    : msg.Contains("401")                                     ? "Сессия истекла"
                    : msg.Contains("403")                                     ? "Нет подписки"
                    : "Нет соединения с сервером");
            SetBtn("Подключиться", true, primary: true);
            MainWindow.Tray?.ShowNotification("[f]society VPN", "Ошибка подключения", WinForms.ToolTipIcon.Error);
        }
        finally { _isBusy = false; }
    }

    private async Task DisconnectCurrentAsync(bool silent = false)
    {
        try { await _vpn.DisconnectAsync(); } catch { }
        _isConnected = false;
        VpnState.IsConnected = false;
        VpnState.ConnectedServerName = null;
        if (_connectedItem != null) _connectedItem.IsConnected = false;
        _connectedItem = null;
        if (!silent)
        {
            ConnectedText.Text       = "○  Отключён";
            ConnectedText.Foreground = MutedBrush;
            SetStatus("");
            SetBtn("Подключиться", true, primary: true);
            MainWindow.Tray?.ShowNotification("[f]society VPN", "Отключён");
        }
    }

    // ───────────────────────── Хелперы ─────────────────────────

    private void SetBtn(string text, bool enabled, bool primary = false)
    {
        ConnectBtn.Content  = text;
        ConnectBtn.IsEnabled = enabled;
        if (primary) ConnectBtn.Style = (Style)Application.Current.Resources["PrimaryButton"]!;
    }

    private void SetStatus(string msg)
    {
        StatusText.Text       = msg;
        StatusText.Visibility = string.IsNullOrEmpty(msg) ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Foreground = MutedBrush;
    }
}
