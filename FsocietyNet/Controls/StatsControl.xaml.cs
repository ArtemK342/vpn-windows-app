using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FsocietyNet.Controls;

public partial class StatsControl : UserControl
{
    private const string TunnelName = "FsocietyNet";

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    private long _prevRx;
    private long _prevTx;
    private DateTime _sessionStart;
    private bool _wasConnected;

    private Brush AccentBrush => (Brush)Application.Current.Resources["AccentBrush"]!;
    private Brush MutedBrush  => (Brush)Application.Current.Resources["TextMutedBrush"]!;
    private Brush PrimaryBrush => (Brush)Application.Current.Resources["TextPrimaryBrush"]!;

    public StatsControl()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => UpdateStats();
        _timer.Start();
        UpdateStats();
    }

    private void UpdateStats()
    {
        var iface = GetTunnelInterface();

        if (iface == null)
        {
            if (_wasConnected)
            {
                _wasConnected = false;
                _prevRx = _prevTx = 0;
            }

            SessionStatusText.Text       = "○  Нет подключения";
            SessionStatusText.Foreground = MutedBrush;
            SessionTimeText.Visibility   = Visibility.Collapsed;

            RxText.Text = TxText.Text = "—";
            RxUnitText.Text = TxUnitText.Text = "";
            RxSpeedText.Text = TxSpeedText.Text = "—";
            IfaceText.Text = "—";
            IfaceIpText.Text = "";
            HintText.Visibility = Visibility.Visible;
            return;
        }

        HintText.Visibility = Visibility.Collapsed;

        if (!_wasConnected)
        {
            _wasConnected  = true;
            _sessionStart  = DateTime.Now;
            var stats0     = iface.GetIPStatistics();
            _prevRx        = stats0.BytesReceived;
            _prevTx        = stats0.BytesSent;
        }

        var st = iface.GetIPStatistics();
        var rx = st.BytesReceived;
        var tx = st.BytesSent;

        var rxDelta = Math.Max(0, rx - _prevRx);
        var txDelta = Math.Max(0, tx - _prevTx);
        _prevRx = rx;
        _prevTx = tx;

        // Статус
        var elapsed = DateTime.Now - _sessionStart;
        SessionStatusText.Text       = $"●  {TunnelName}";
        SessionStatusText.Foreground = AccentBrush;
        SessionTimeText.Text         = $"Сессия: {FormatDuration(elapsed)}";
        SessionTimeText.Visibility   = Visibility.Visible;

        // Суммарный трафик
        (RxText.Text, RxUnitText.Text) = FormatBytes(rx);
        (TxText.Text, TxUnitText.Text) = FormatBytes(tx);

        // Скорость (байт за 2 сек → в секунду)
        RxSpeedText.Text = FormatSpeed(rxDelta / 2);
        TxSpeedText.Text = FormatSpeed(txDelta / 2);

        // Интерфейс
        IfaceText.Text = $"{iface.Name}  ·  {iface.Description}";

        var ip4 = iface.GetIPProperties().UnicastAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        IfaceIpText.Text = ip4 != null ? $"IP: {ip4.Address}" : "";
    }

    private static NetworkInterface? GetTunnelInterface() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(i =>
                i.Name.Equals(TunnelName, StringComparison.OrdinalIgnoreCase) &&
                i.OperationalStatus == OperationalStatus.Up);

    private static (string value, string unit) FormatBytes(long bytes)
    {
        if (bytes < 1024)        return ($"{bytes}", "байт");
        if (bytes < 1024 * 1024) return ($"{bytes / 1024.0:F1}", "КБ");
        if (bytes < 1024L * 1024 * 1024) return ($"{bytes / (1024.0 * 1024):F2}", "МБ");
        return ($"{bytes / (1024.0 * 1024 * 1024):F2}", "ГБ");
    }

    private static string FormatSpeed(long bytesPerSec)
    {
        if (bytesPerSec < 1024)        return $"{bytesPerSec} Б/с";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024.0:F1} КБ/с";
        return $"{bytesPerSec / (1024.0 * 1024):F2} МБ/с";
    }

    private static string FormatDuration(TimeSpan t)
    {
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}ч {t.Minutes:D2}м {t.Seconds:D2}с";
        if (t.TotalMinutes >= 1)
            return $"{t.Minutes}м {t.Seconds:D2}с";
        return $"{t.Seconds}с";
    }

    private void RefreshStatsBtn_Click(object sender, RoutedEventArgs e) => UpdateStats();

    // Останавливаем таймер, когда контрол выгружается
    private void UserControl_Unloaded(object sender, RoutedEventArgs e) => _timer.Stop();
}
