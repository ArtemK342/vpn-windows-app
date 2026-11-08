using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FsocietyNet.Services;

namespace FsocietyNet.Views;

public partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }
    HomeView? _home;
    readonly Dictionary<RadioButton, FrameworkElement> _views = [];
    bool _reloading;
    static bool _trayHintShown;
    bool _dialogDismissable;
    TaskCompletionSource<bool>? _dialogTcs;
    readonly DispatcherTimer _unreadTimer = new() { Interval = TimeSpan.FromSeconds(90) };
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.6) };

    public MainWindow()
    {
        Instance = this;
        InitializeComponent();
        SourceInitialized += (_, _) => Theme.StyleFrame(new WindowInteropHelper(this).Handle);
        Theme.Changed += OnTheme;
        StateChanged += (_, _) =>
        {
            // WindowChrome: a maximised window hangs over the screen edge by the resize border
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaxIcon.Data = Ui.Geo(WindowState == WindowState.Maximized ? "I.Restore" : "I.Max");
        };
        Closing += (_, e) =>
        {
            if (_reloading) return;
            if (AppSettings.Current.CloseToTray)
            {
                e.Cancel = true;
                Hide();
                if (!_trayHintShown) { _trayHintShown = true; Tray.Notify(Loc.Get("tray.hidden")); }
                return;
            }
            e.Cancel = true;
            App.Quit();
        };
        Closed += (_, _) => { Vpn.Changed -= UpdateState; Session.Expired -= ShowLogin; Theme.Changed -= OnTheme; _unreadTimer.Stop(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && Overlay.IsVisible && _dialogDismissable) CloseDialog(false); };
        Vpn.Changed += UpdateState;
        Session.Expired += ShowLogin;
        _unreadTimer.Tick += async (_, _) => await RefreshUnread();
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast.Visibility = Visibility.Collapsed; };

        if (Session.SignedIn) ShowApp(); else ShowLogin();
        UpdateState();
        _ = CheckUpdate();
    }

    void OnTheme() => Theme.StyleFrame(new WindowInteropHelper(this).Handle);

    // ── Screens ──

    public void ShowLogin()
    {
        _unreadTimer.Stop();
        _home?.Detach();
        _home = null;
        _views.Clear();
        Sidebar.Visibility = Visibility.Collapsed;
        SideCol.Width = new GridLength(0);
        Page.Content = new LoginView();
        if (Vpn.Active) _ = Vpn.Disconnect(false);
    }

    public void ShowApp()
    {
        Sidebar.Visibility = Visibility.Visible;
        SideCol.Width = new GridLength(236);
        _home?.Detach();
        _views.Clear();
        _home = new HomeView();
        NavHome.IsChecked = null;
        NavHome.IsChecked = true;
        Page.Content = _home;
        UpdateAccount();
        _unreadTimer.Start();
        _ = RefreshUnread();
        _ = LoadMe();
    }

    async Task LoadMe()
    {
        try { await Api.Me(); } catch { }
        UpdateAccount();
        try { await Api.Subscription(); } catch { }   // cached for Settings → Account
    }

    public void UpdateAccount()
    {
        var me = Session.Me;
        AccountText.Text = me == null ? "" : Session.IsAnonymous ? Loc.Get("account.anon_short") : me.email ?? "";
    }

    string? _pendingSection;

    /// <summary>home | rules | news | support | settings | settings_&lt;section&gt; (e.g. settings_update).</summary>
    public void Go(string page)
    {
        if (page.StartsWith("settings_")) { _pendingSection = page["settings_".Length..]; page = "settings"; }
        var target = page switch
        {
            "rules" => NavRules, "news" => NavNews, "support" => NavSupport, "settings" => NavSettings, _ => NavHome
        };
        if (target.IsChecked == true) Nav_Checked(target, new RoutedEventArgs());
        else target.IsChecked = true;
    }

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (Page == null) return;
        // One instance per tab for the whole session: coming back shows the same section and scroll position
        var rb = (RadioButton)sender;
        if (rb == NavHome) _views[rb] = _home ??= new HomeView();
        if (!_views.TryGetValue(rb, out var view))
        {
            view = rb == NavRules ? new RulesView()
                : rb == NavNews ? new NewsView()
                : rb == NavSupport ? new SupportView()
                : new SettingsView(_pendingSection);
            _views[rb] = view;
        }
        else if (rb == NavSettings && _pendingSection != null) ((SettingsView)view).ShowSection(_pendingSection);
        _pendingSection = null;
        Page.Content = view;
        view.Opacity = 0;
        view.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
    }

    /// <summary>Language changed: rebuild the window (texts are resolved when views are created).</summary>
    public void Reload()
    {
        var nw = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = Left, Top = Top, Width = Width, Height = Height };
        nw.Show();
        if (WindowState == WindowState.Maximized) nw.WindowState = WindowState.Maximized;
        Application.Current.MainWindow = nw;
        if (Session.SignedIn) nw.Go("settings");
        _reloading = true;
        _home?.Detach();
        Close();
        Tray.Update();
    }

    // ── Sidebar state ──

    void UpdateState()
    {
        var (dot, text) = Vpn.Status switch
        {
            VpnStatus.On => ("AccentBrush", Loc.Get("home.state_on")),
            VpnStatus.Connecting => ("WarnBrush", Loc.Get("home.connecting")),
            VpnStatus.Reconnecting => ("WarnBrush", Loc.Get("home.reconnecting")),
            VpnStatus.Disconnecting => ("WarnBrush", Loc.Get("home.disconnecting")),
            VpnStatus.Blocked => ("DangerBrush", Loc.Get("home.state_blocked")),
            _ => ("FaintBrush", Loc.Get("home.state_off"))
        };
        StateDot.Fill = Ui.Res(dot);
        StateText.Text = text;
        StateSub.Text = Vpn.Active || Vpn.Busy ? Vpn.Server?.DisplayName ?? "" : Loc.Get("home.state_off_sub");
    }

    void StatePill_Click(object sender, MouseButtonEventArgs e) => Go("home");

    public async Task RefreshUnread()
    {
        if (!Session.SignedIn) return;
        try
        {
            var n = (await Api.Tickets()).Sum(t => t.unread);
            NavSupport.Tag = n > 0 ? n.ToString() : null;
        }
        catch { }
    }

    // ── Update banner ──

    async Task CheckUpdate()
    {
        await Task.Delay(4000);
        try
        {
            await Updates.Check();
            if (Updates.Available is { } u)
            {
                UpdateText.Text = Loc.F("update.banner", u.Version);
                UpdateBanner.Visibility = Visibility.Visible;
            }
        }
        catch { }
    }

    void UpdateOpen_Click(object sender, RoutedEventArgs e) => Go("settings_update");
    void UpdateHide_Click(object sender, RoutedEventArgs e) => UpdateBanner.Visibility = Visibility.Collapsed;

    // ── Window buttons ──

    void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ── Dialogs / toasts (Views/Dialogs.cs builds the content) ──

    public Task<bool> ShowDialog(FrameworkElement content, bool dismissable = true, double width = 440)
    {
        _dialogTcs?.TrySetResult(false);
        _dialogTcs = new TaskCompletionSource<bool>();
        _dialogDismissable = dismissable;
        DialogCard.Width = width;
        DialogContent.Content = content;
        Overlay.Visibility = Visibility.Visible;
        Overlay.Opacity = 0;
        Overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        var st = new ScaleTransform(0.97, 0.97);
        DialogCard.RenderTransformOrigin = new Point(0.5, 0.5);
        DialogCard.RenderTransform = st;
        st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
        st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
        return _dialogTcs.Task;
    }

    public void CloseDialog(bool result)
    {
        Overlay.Visibility = Visibility.Collapsed;
        DialogContent.Content = null;
        var t = _dialogTcs;
        _dialogTcs = null;
        t?.TrySetResult(result);
    }

    void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == Overlay && _dialogDismissable) CloseDialog(false);
    }

    public void ShowToast(string text)
    {
        ToastText.Text = text;
        Toast.Visibility = Visibility.Visible;
        Toast.Opacity = 0;
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}
