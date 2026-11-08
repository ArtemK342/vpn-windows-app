using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FsocietyNet.Services;

namespace FsocietyNet.Views;

/// <summary>One row of the server list.</summary>
public sealed class ServerRow : INotifyPropertyChanged
{
    public ServerResponse S { get; }
    public ServerRow(ServerResponse s) => S = s;
    public int Ping { get; set; }   // -1 failed, 0 measuring

    public string Name => S.DisplayName;
    public string Country => S.country;
    public string Protocol => S.server_type switch
    {
        "wireguard" => "AmneziaWG", "vless" => "VLESS", "trojan" => "Trojan", "hysteria" => "Hysteria2",
        "openvpn_cloak" => "OpenVPN + Cloak", "bypass" => Loc.Get("home.proto_bypass"), var t => t
    };
    public bool IsFavorite => AppSettings.Current.Favorites.Contains(S.id);
    public bool IsCurrent => Vpn.Server?.id == S.id && Vpn.Status != VpnStatus.Off;
    public Visibility LogsVisibility => S.logs_enabled ? Visibility.Visible : Visibility.Collapsed;
    public double RowOpacity => S.CanConnect && !S.IsUnavailable ? 1 : 0.5;
    public Brush StarBrush => Ui.Res(IsFavorite ? "AccentTextBrush" : "FaintBrush");
    public string StarHint => Loc.Get(IsFavorite ? "home.menu_unfavorite" : "home.menu_favorite");

    static readonly Dictionary<string, string> Templates = new()
    {
        ["unstable"] = "status.unstable", ["high_load"] = "status.high_load", ["packet_loss"] = "status.packet_loss",
        ["ddos"] = "status.ddos", ["isp_blocking"] = "status.isp_blocking", ["maintenance"] = "status.maintenance",
        ["config_update"] = "status.config_update",
    };

    public string Message
    {
        get
        {
            if (!S.SupportedOnWindows) return Loc.Get("home.only_mobile");
            if (S.access_block?.Text() is { } b) return b;
            var m = S.status_message;
            if (string.IsNullOrWhiteSpace(m)) return "";
            var text = Templates.TryGetValue(m, out var k) ? Loc.Get(k) : m;
            return string.IsNullOrWhiteSpace(S.status_message_extra) ? text : $"{text} · {S.status_message_extra}";
        }
    }
    public Brush MessageBrush => Ui.Res(S.HasWarning ? "WarnBrush" : "MutedBrush");

    public string StatusText
    {
        get
        {
            if (IsCurrent && Vpn.Status == VpnStatus.On) return Loc.Get("home.status_connected");
            if (IsCurrent && Vpn.Status is VpnStatus.Connecting or VpnStatus.Reconnecting) return "…";
            if (!S.SupportedOnWindows || S.IsUnavailable || !S.is_active && S.status == "active") return Loc.Get("home.status_soon");
            if (S.access_block != null) return Loc.Get("home.status_blocked");
            if (S.subscription_required) return Loc.Get("home.status_subscription");
            if (S.status == "maintenance") return Loc.Get("home.status_maintenance");
            if (S.status is "testing" or "beta" or "hidden") return Loc.Get("home.status_test");
            if (S.IsBypass) return Loc.Get("home.status_bypass");
            return Ping switch { 0 => "…", < 0 => "—", var p => $"{p} {Loc.Get("unit.ms")}" };
        }
    }

    public Brush StatusBrush
    {
        get
        {
            if (IsCurrent && Vpn.Status == VpnStatus.On) return Ui.Res("AccentTextBrush");
            if (S.access_block != null) return Ui.Res("DangerBrush");
            if (S.subscription_required || S.status is "maintenance" or "testing" or "beta" or "hidden") return Ui.Res("WarnBrush");
            if (!S.CanConnect || S.IsUnavailable || Ping <= 0 || S.IsBypass) return Ui.Res(S.IsBypass ? "AccentTextBrush" : "MutedBrush");
            return Ui.Res(Ping < 100 ? "AccentTextBrush" : Ping < 200 ? "WarnBrush" : "DangerBrush");
        }
    }

    public string? Hint => !S.SupportedOnWindows ? Loc.Get("home.not_on_windows")
        : S.subscription_required ? Loc.Get("home.error_subscription_only") : null;

    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

public partial class HomeView : UserControl
{
    readonly ObservableCollection<ServerRow> _rows = [];
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _reload = new() { Interval = TimeSpan.FromMinutes(3) };
    (long rx, long tx) _last;
    DateTime _lastAt;
    bool _autoConnectTried;
    ExternalOverview? _ext;
    Storyboard? _spin;

    public HomeView()
    {
        InitializeComponent();
        List.ItemsSource = _rows;
        Vpn.Changed += OnVpn;
        _tick.Tick += async (_, _) => await Tick();
        _reload.Tick += async (_, _) => await LoadServers(quiet: true);
        _tick.Start();
        _reload.Start();
        OnVpn();
        _ = LoadAll();
    }

    /// <summary>The view is dropped (sign-out, language change).</summary>
    public void Detach()
    {
        Vpn.Changed -= OnVpn;
        _tick.Stop();
        _reload.Stop();
    }

    async Task LoadAll()
    {
        await Task.WhenAll(LoadServers(), LoadUsage(), LoadAppConfig(), LoadExternal());
    }

    async Task LoadServers(bool quiet = false)
    {
        if (!quiet && _rows.Count == 0) ShowEmpty(Loc.Get("common.loading"), false);
        try
        {
            var list = await Api.Servers();
            Vpn.Servers = list;
            var pings = _rows.ToDictionary(r => r.S.id, r => r.Ping);
            _rows.Clear();
            foreach (var s in list.Where(s => s.status != "hidden" || s.connectable))
                _rows.Add(new ServerRow(s) { Ping = pings.GetValueOrDefault(s.id) });
            ApplyFilter();
            _ = PingAll();
            await TryAutoConnect();
        }
        catch (Exception e)
        {
            if (_rows.Count == 0) ShowEmpty(Errors.Text(e), true);
        }
    }

    void ShowEmpty(string text, bool retry)
    {
        ListEmptyText.Text = text;
        RetryBtn.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
        ListEmpty.Visibility = Visibility.Visible;
        List.Visibility = Visibility.Collapsed;
    }

    void ApplyFilter()
    {
        var fav = TabFav.IsChecked == true;
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_rows);
        view.Filter = fav ? o => ((ServerRow)o).IsFavorite : null;
        var any = !view.IsEmpty;
        if (any) { ListEmpty.Visibility = Visibility.Collapsed; List.Visibility = Visibility.Visible; }
        else ShowEmpty(Loc.Get(fav ? "home.fav_empty" : "home.no_servers"), false);
    }

    async Task PingAll()
    {
        RefreshBtn.IsEnabled = false;
        foreach (var r in _rows.Where(r => string.IsNullOrEmpty(r.S.Host) && r.Ping == 0)) { r.Ping = -1; r.Refresh(); }
        var tasks = _rows.Where(r => r.S.CanConnect && !r.S.IsBypass && !string.IsNullOrEmpty(r.S.Host)).Select(async r =>
        {
            int p = Api.DemoMode ? Demo.Pings.GetValueOrDefault(r.S.id, -1) : await Net.Ping(r.S.Host);
            r.Ping = p;
            Vpn.Pings[r.S.id] = p;
            r.Refresh();
        });
        await Task.WhenAll(tasks);
        RefreshBtn.IsEnabled = true;
    }

    async Task LoadUsage()
    {
        try
        {
            var u = await Api.Usage();
            if (!u.is_limited || u.limit_bytes <= 0) { UsageBox.Visibility = Visibility.Collapsed; return; }
            var left = Math.Max(0, u.limit_bytes - u.bytes_used);
            var frac = Math.Clamp((double)u.bytes_used / u.limit_bytes, 0, 1);
            UsageText.Text = frac >= 1 ? Loc.Get("home.limit_reached") : Loc.F("home.traffic_left", Loc.Bytes(left), Loc.Bytes(u.limit_bytes));
            UsageSpeed.Text = Session.IsAnonymous ? "" : Loc.Get("home.free_speed");
            UsageBar.Background = Ui.Res(frac >= 1 ? "DangerBrush" : frac >= 0.8 ? "WarnBrush" : "AccentBrush");
            UsageBox.Visibility = Visibility.Visible;
            UsageBox.Dispatcher.BeginInvoke(() =>
                UsageBar.Width = ((FrameworkElement)UsageBar.Parent).ActualWidth * frac, DispatcherPriority.Loaded);
        }
        catch { }
    }

    async Task LoadAppConfig()
    {
        try
        {
            var cfg = await Api.AppConfig();
            Vpn.AppCfg = cfg;
            var m = cfg.access?.maintenance;
            if (m is { enabled: true })
            {
                MaintText.Text = m.Text() ?? Loc.Get("home.maintenance_default");
                MaintBanner.Visibility = Visibility.Visible;
            }
            else MaintBanner.Visibility = Visibility.Collapsed;
        }
        catch { }
    }

    async Task LoadExternal()
    {
        try { _ext = await Api.External(); } catch { _ext = null; }
    }

    /// <summary>Settings → «connect on start»: once per run, unless the user disconnected himself last time.</summary>
    async Task TryAutoConnect()
    {
        if (_autoConnectTried) return;
        _autoConnectTried = true;
        var s = AppSettings.Current;
        if (!s.ConnectOnStart || s.UserDisconnected || Vpn.Active || Vpn.Busy || Api.DemoMode) return;
        var target = Vpn.Servers.FirstOrDefault(x => x.id == s.LastServerId && x.CanConnect);
        if (target != null) await Vpn.Connect(target);
    }

    // ── State ──

    void OnVpn()
    {
        var st = Vpn.Status;
        var srv = Vpn.Server;
        var on = st == VpnStatus.On;
        var busy = st is VpnStatus.Connecting or VpnStatus.Reconnecting or VpnStatus.Disconnecting;

        StateTitle.Text = st switch
        {
            VpnStatus.On => Loc.Get("home.title_on"),
            VpnStatus.Connecting => Loc.Get("home.connecting"),
            VpnStatus.Reconnecting => Loc.Get("home.reconnecting"),
            VpnStatus.Disconnecting => Loc.Get("home.disconnecting"),
            VpnStatus.Blocked => Loc.Get("home.title_blocked"),
            _ => Loc.Get("home.title_off")
        };
        StateSub.Text = st == VpnStatus.Off
            ? Loc.Get("home.sub_off")
            : srv == null ? "" : $"{srv.DisplayName} · {new ServerRow(srv).Protocol}";

        PowerBtn.Background = Ui.Res(on ? "AccentBrush" : "Surface2Brush");
        PowerBtn.BorderBrush = Ui.Res(on ? "AccentBrush" : "Line2Brush");
        PowerIcon.Brush = Ui.Res(on ? "OnAccentBrush" : busy ? "AccentTextBrush" : st == VpnStatus.Blocked ? "DangerBrush" : "MutedBrush");
        PowerText.Foreground = PowerIcon.Brush;
        PowerText.Text = (on || st == VpnStatus.Reconnecting ? Loc.Get("home.btn_disconnect")
            : st == VpnStatus.Connecting ? Loc.Get("common.cancel")
            : st == VpnStatus.Blocked ? Loc.Get("home.btn_disconnect") : Loc.Get("home.btn_connect")).ToUpperInvariant();
        PowerBtn.IsEnabled = st != VpnStatus.Disconnecting;
        Ring.Stroke = Ui.Res(on ? "AccentBrush" : st == VpnStatus.Blocked ? "DangerBrush" : "LineBrush");
        Glow.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(400)));
        SetSpinner(busy);

        StatsRow.Visibility = on || st == VpnStatus.Reconnecting ? Visibility.Visible : Visibility.Collapsed;
        UnblockBtn.Visibility = st == VpnStatus.Blocked ? Visibility.Visible : Visibility.Collapsed;
        var err = Vpn.LastError;
        ErrorText.Text = err ?? "";
        ErrorText.Visibility = string.IsNullOrEmpty(err) || on ? Visibility.Collapsed : Visibility.Visible;

        // Rules that do not apply to this protocol on Windows
        string? note = null;
        var s = AppSettings.Current;
        if (srv != null && st != VpnStatus.Off && (s.SitesActive || s.AppsActive))
        {
            if (srv.IsBypass) note = Loc.Get("home.rules_not_applied");
            else if (srv.server_type == "wireguard" && s.AppsActive) note = Loc.Get("home.rules_apps_awg");
            else if (srv.server_type == "wireguard" && s.SitesActive && s.Sites.Any(Rules.IsZone)) note = Loc.Get("home.rules_zone_awg");
        }
        if (srv?.IsBypass == true && on) note = (note == null ? "" : note + "\n") + Loc.Get("home.bypass_note");
        NoteText.Text = note ?? "";
        NoteText.Visibility = note == null ? Visibility.Collapsed : Visibility.Visible;

        if (!on) { _last = (0, 0); }
        foreach (var r in _rows) r.Refresh();
        if (st == VpnStatus.Off && err == null) _ = LoadUsage();
    }

    void SetSpinner(bool on)
    {
        if (on)
        {
            Spinner.Visibility = Visibility.Visible;
            if (_spin == null)
            {
                var a = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever };
                _spin = new Storyboard();
                _spin.Children.Add(a);
                Storyboard.SetTarget(a, Spinner);
                Storyboard.SetTargetProperty(a, new PropertyPath("RenderTransform.Angle"));
                _spin.Begin(this, true);
            }
        }
        else
        {
            _spin?.Stop(this);
            _spin = null;
            Spinner.Visibility = Visibility.Collapsed;
        }
    }

    async Task Tick()
    {
        if (Vpn.Status is not (VpnStatus.On or VpnStatus.Reconnecting) || Vpn.Since == null) return;
        var t = DateTime.Now - Vpn.Since.Value;
        TimeText.Text = $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
        if (Vpn.EngineKind == "bypass")
        {
            DownText.Text = UpText.Text = "—";
            DownTotal.Text = UpTotal.Text = Loc.Get("home.local_mode");
            return;
        }
        var now = DateTime.Now;
        var cur = await Vpn.Traffic();
        if (_last != (0, 0) && cur.rx >= _last.rx)
        {
            var sec = Math.Max(0.5, (now - _lastAt).TotalSeconds);
            DownText.Text = Loc.Speed((cur.rx - _last.rx) / sec);
            UpText.Text = Loc.Speed((cur.tx - _last.tx) / sec);
        }
        else if (DownText.Text.Length == 0) { DownText.Text = UpText.Text = "…"; }
        DownTotal.Text = Loc.Bytes(cur.rx);
        UpTotal.Text = Loc.Bytes(cur.tx);
        _last = cur;
        _lastAt = now;
    }

    // ── Actions ──

    async void Power_Click(object sender, RoutedEventArgs e)
    {
        switch (Vpn.Status)
        {
            case VpnStatus.On or VpnStatus.Reconnecting or VpnStatus.Connecting:
                await Vpn.Disconnect();
                break;
            case VpnStatus.Blocked:
                await Vpn.Unblock();
                break;
            default:
                var target = Vpn.Servers.FirstOrDefault(s => s.id == AppSettings.Current.LastServerId && s.CanConnect)
                             ?? Vpn.BestServer();
                if (target == null) { Dialogs.Toast(Loc.Get("home.no_servers")); return; }
                await Connect(target);
                break;
        }
    }

    async Task Connect(ServerResponse s)
    {
        if (!s.SupportedOnWindows) { await Dialogs.Info(s.DisplayName, Loc.Get("home.not_on_windows")); return; }
        if (s.subscription_required) { await Dialogs.Info(s.DisplayName, Loc.Get("home.error_subscription_only")); return; }
        if (s.access_block?.Text() is { } block) { await Dialogs.Info(s.DisplayName, block); return; }
        if (!s.CanConnect || s.IsUnavailable) { Dialogs.Toast(Loc.Get("home.unavailable")); return; }
        await Vpn.Connect(s);
    }

    async void Unblock_Click(object sender, RoutedEventArgs e) => await Vpn.Unblock();

    async void Row_Click(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not ServerRow r) return;
        if (r.IsCurrent && Vpn.Active) return;
        if (Vpn.Busy) return;
        await Connect(r.S);
    }

    void Row_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not ServerRow r) return;
        var menu = new ContextMenu();
        var fav = new MenuItem
        {
            Header = Loc.Get(r.IsFavorite ? "home.menu_unfavorite" : "home.menu_favorite"),
            Icon = new Icon { Data = Ui.Geo("I.Star"), Size = 16, Filled = !r.IsFavorite, Brush = Ui.Res("AccentTextBrush") }
        };
        fav.Click += (_, _) => ToggleFavorite(r);
        menu.Items.Add(fav);
        if (_ext != null && (_ext.IsAwg(r.S.id) || _ext.IsVless(r.S.id)))
        {
            menu.Items.Add(new Separator());
            var other = new MenuItem
            {
                Header = Loc.Get("home.menu_other_app") + (_ext.entitled ? "" : " · " + Loc.Get("ext.by_sub")),
                Icon = new Icon { Data = Ui.Geo("I.Link"), Size = 16 }
            };
            other.Click += async (_, _) => _ext = await ExternalDialog.Show(r.S, _ext) ?? _ext;
            menu.Items.Add(other);
        }
        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
        e.Handled = true;
    }

    void Star_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is ServerRow r) ToggleFavorite(r);
        e.Handled = true;
    }

    void ToggleFavorite(ServerRow r)
    {
        var f = AppSettings.Current.Favorites;
        if (!f.Remove(r.S.id)) f.Add(r.S.id);
        AppSettings.Save();
        r.Refresh();
        ApplyFilter();
    }

    void Tab_Checked(object sender, RoutedEventArgs e) { if (IsInitialized && List != null) ApplyFilter(); }

    async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rows) { r.Ping = 0; r.Refresh(); }
        await LoadServers();
        _ = LoadUsage();
        _ = LoadAppConfig();
    }
}
