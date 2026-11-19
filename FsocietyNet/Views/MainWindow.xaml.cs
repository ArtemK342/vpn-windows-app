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
        Title = Brand.Title;
        // Размер окна (юзер 2026-11-19): тянется мышкой только пропорционально (как 1120×720), от MinW до MaxW;
        // содержимое слегка масштабируется вместе с окном (ApplyScale). Стартовый размер — от экрана: ~55% ширины
        // рабочей области, не больше 1120 (Full HD → 960×617, 2K → 1120×720). «Развернуть» — на весь экран и обратно.
        var wa = SystemParameters.WorkArea;
        Width = Math.Min(Math.Clamp(wa.Width * 0.5, MinW, 1120), wa.Width - 16);
        Height = Math.Min(Width / Aspect, wa.Height - 16);
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            Theme.StyleFrame(h);
            HwndSource.FromHwnd(h)?.AddHook(SizingHook);
        };
        SizeChanged += (_, _) => ApplyScale();
        Theme.Changed += OnTheme;
        StateChanged += (_, _) =>
        {
            // WindowChrome: a maximised window hangs over the screen edge by the resize border
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            ApplyScale();
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
        // Список программ для «Правил» обновляется в фоне заранее (кэш на диске) — вкладка открывается сразу
        _ = Task.Run(async () => { await Task.Delay(6000); await AppScan.RefreshAsync(); });
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

    // ── Proportional size ──

    const double DesignW = 1120, DesignH = 720, Aspect = DesignW / DesignH, MinW = 800, MaxW = 1320;   // юзер: меньше и не так крупно

    /// <summary>
    /// Чуть крупнее/мельче вместе с окном: половина изменения ширины относительно 1120 (не больше 1.12); в узком окне —
    /// сколько нужно, чтобы вёрстке оставалось ≥ 960 (две колонки главного экрана), но не меньше 0.8.
    /// </summary>
    void ApplyScale()
    {
        var w = ActualWidth - Root.Margin.Left - Root.Margin.Right;
        if (w <= 0) return;
        var k = Math.Clamp(Math.Min(1 + (w / DesignW - 1) * 0.5, w / 960), 0.8, 1.12);
        if (Root.LayoutTransform is ScaleTransform st && Math.Abs(st.ScaleX - k) < 0.005) return;
        Root.LayoutTransform = Math.Abs(k - 1) < 0.005 ? Transform.Identity : new ScaleTransform(k, k);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>WM_SIZING: держит пропорции окна и пределы MinW…MaxW (и не больше рабочей области).</summary>
    IntPtr SizingHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_SIZING = 0x0214;
        if (msg != WM_SIZING) return IntPtr.Zero;
        var r = System.Runtime.InteropServices.Marshal.PtrToStructure<RECT>(lParam);
        var dpi = VisualTreeHelper.GetDpi(this);
        var edge = wParam.ToInt32();   // 1 L, 2 R, 3 T, 4 TL, 5 TR, 6 B, 7 BL, 8 BR
        double w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (edge is 3 or 6) w = h * Aspect;
        var wa = SystemParameters.WorkArea;
        var max = Math.Min(MaxW * dpi.DpiScaleX, Math.Min(wa.Width * dpi.DpiScaleX, wa.Height * dpi.DpiScaleY * Aspect));
        w = Math.Clamp(w, MinW * dpi.DpiScaleX, Math.Max(max, MinW * dpi.DpiScaleX));
        h = w / Aspect;
        if (edge is 1 or 4 or 7) r.Left = r.Right - (int)Math.Round(w); else r.Right = r.Left + (int)Math.Round(w);
        if (edge is 3 or 4 or 5) r.Top = r.Bottom - (int)Math.Round(h); else r.Bottom = r.Top + (int)Math.Round(h);
        System.Runtime.InteropServices.Marshal.StructureToPtr(r, lParam, false);
        handled = true;
        return (IntPtr)1;
    }

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
