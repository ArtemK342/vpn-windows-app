using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FsocietyNet.Services;

namespace FsocietyNet.Views;

/// <summary>Settings: sub-menu on the left, section on the right (Android: Settings → Account / Advanced / …).</summary>
public sealed partial class SettingsView : UserControl
{
    static AppSettings St => AppSettings.Current;
    static Style S(string k) => (Style)Application.Current.FindResource(k);

    readonly StackPanel _menu = new();
    readonly ContentControl _section = new() { Focusable = false };
    readonly Dictionary<string, RadioButton> _items = [];

    static readonly (string id, string icon, string key)[] Sections =
    {
        ("account", "I.User", "settings.account"),
        ("connection", "I.ShieldCheck", "settings.connection"),
        ("protocols", "I.Gauge", "settings.protocols"),
        ("bypass", "I.Bolt", "settings.bypass"),
        ("appearance", "I.Moon", "settings.appearance"),
        ("journal", "I.Doc", "settings.journal"),
        ("update", "I.Download", "settings.update"),
        ("about", "I.Info", "settings.about"),
    };

    public SettingsView(string? section = null)
    {
        var root = new Grid { Margin = new Thickness(32, 44, 32, 24) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = Loc.Get("settings.section"), Style = S("T.Label") });
        left.Children.Add(new TextBlock { Text = Loc.Get("settings.title"), Style = S("T.H1"), Margin = new Thickness(0, 8, 0, 18) });
        foreach (var (id, icon, key) in Sections)
        {
            var rb = new RadioButton { Style = S("NavItem"), GroupName = "settings", Content = Loc.Get(key), Margin = new Thickness(-10, 0, 0, 0), Tag = null };
            Ui.SetIcon(rb, Ui.Geo(icon));
            rb.Checked += (_, _) => Open(id);
            _items[id] = rb;
            _menu.Children.Add(rb);
        }
        left.Children.Add(_menu);
        root.Children.Add(left);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _section };
        Grid.SetColumn(scroll, 2);
        root.Children.Add(scroll);
        Content = root;

        var start = section != null && _items.ContainsKey(section) ? section : "account";
        _items[start].IsChecked = true;
    }

    void Open(string id)
    {
        _section.Content = id switch
        {
            "connection" => Connection(),
            "protocols" => Protocols(),
            "bypass" => Bypass(),
            "appearance" => Appearance(),
            "journal" => Journal(),
            "update" => Update(),
            "about" => About(),
            _ => Account(),
        };
    }

    // ── Building blocks ──

    static StackPanel Page(string title, string? desc = null)
    {
        var p = new StackPanel { MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 52, 0, 20) };
        p.Children.Add(new TextBlock { Text = title, Style = S("T.H2") });
        if (desc != null) p.Children.Add(new TextBlock { Text = desc, Style = S("T.Muted"), Margin = new Thickness(0, 6, 0, 0) });
        return p;
    }

    static Border Card(UIElement child, double top = 14) =>
        new() { Style = S("Card"), Padding = new Thickness(18, 4, 18, 4), Margin = new Thickness(0, top, 0, 0), Child = child };

    static StackPanel Rows(params UIElement[] rows)
    {
        var p = new StackPanel();
        for (int i = 0; i < rows.Length; i++)
        {
            if (i > 0) p.Children.Add(new Border { Height = 1, Background = Ui.Res("LineBrush") });
            p.Children.Add(rows[i]);
        }
        return p;
    }

    static Grid Toggle(string title, string desc, bool value, Func<bool, Task> changed)
    {
        var g = new Grid { Margin = new Thickness(0, 14, 0, 14) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        t.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        t.Children.Add(new TextBlock { Text = desc, Style = S("T.Muted"), Margin = new Thickness(0, 3, 0, 0) });
        g.Children.Add(t);
        var sw = new CheckBox { Style = S("Switch"), IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
        sw.Click += async (_, _) => { sw.IsEnabled = false; await changed(sw.IsChecked == true); sw.IsEnabled = true; };
        Grid.SetColumn(sw, 1);
        g.Children.Add(sw);
        return g;
    }

    static Grid Info(string label, string value, Button? action = null)
    {
        var g = new Grid { Margin = new Thickness(0, 13, 0, 13) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock { Text = label, Style = S("T.Muted"), VerticalAlignment = VerticalAlignment.Center });
        var v = new TextBlock { Text = value, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeights.SemiBold };
        Grid.SetColumn(v, 1);
        g.Children.Add(v);
        if (action != null) { Grid.SetColumn(action, 2); action.Height = 30; action.Padding = new Thickness(12, 0, 12, 0); g.Children.Add(action); }
        return g;
    }

    static StackPanel Options(string group, (string id, string title, string desc)[] opts, string current, Action<string> changed)
    {
        var p = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var (id, title, desc) in opts)
        {
            var c = new StackPanel();
            c.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
            if (desc.Length > 0) c.Children.Add(new TextBlock { Text = desc, Style = S("T.Muted"), Margin = new Thickness(0, 2, 0, 0) });
            var rb = new RadioButton { Style = S("OptionCard"), GroupName = group, Content = c, IsChecked = id == current };
            rb.Checked += (_, _) => changed(id);
            p.Children.Add(rb);
        }
        return p;
    }

    static TextBlock Note(string text, double top = 12) => new() { Text = text, Style = S("T.Note"), Margin = new Thickness(0, top, 0, 0) };
    static TextBlock Label(string text, double top = 22) => new() { Text = text, Style = S("T.Label"), Margin = new Thickness(0, top, 0, 0) };

    // ── Account ──

    FrameworkElement Account()
    {
        var p = Page(Loc.Get("account.title"));
        var me = Session.Me;
        var anon = Session.IsAnonymous;
        var subRow = Info(Loc.Get("account.subscription"), "");
        var subText = (TextBlock)subRow.Children[1];
        var idLabel = anon ? Loc.Get("account.code") : me?.account_type == "login" ? Loc.Get("account.login") : "Email";
        Button? copy = null;
        if (anon && me?.email != null) copy = Dialogs.Btn(Loc.Get("ext.copy"), "Btn.Secondary", (_, _) => Dialogs.Copy(me.email!), 70);
        p.Children.Add(Card(Rows(
            Info(idLabel, me?.email ?? "—", copy),
            subRow,
            Info(Loc.Get("account.role"), RoleName(me?.role)))));
        // Cached value at once (no «Загрузка…» flash), fresh one replaces it when it arrives
        if (Session.SubKnown) ShowSub(subText, Session.Sub); else subText.Text = Loc.Get("common.loading");
        _ = LoadSub(subText);

        if (!anon)
        {
            p.Children.Add(Label(Loc.Get("account.change_password"), 26));
            var form = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            PasswordField Field(string label, string placeholder)
            {
                form.Children.Add(new TextBlock { Text = label, Style = S("T.Caps"), Margin = new Thickness(0, 12, 0, 6) });
                var b = new PasswordField { Placeholder = placeholder };
                form.Children.Add(b);
                return b;
            }
            var oldP = Field(Loc.Get("account.old_password"), Loc.Get("field.password_ph"));
            var newP = Field(Loc.Get("account.new_password"), Loc.Get("field.new_password_ph"));
            var repP = Field(Loc.Get("account.repeat_password"), Loc.Get("field.repeat_password_ph"));
            var msg = new TextBlock { FontSize = 12.5, Margin = new Thickness(14, 0, 0, 0), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var btn = Dialogs.Btn(Loc.Get("account.btn_change"), "Btn.Primary", async (s, _) =>
            {
                void Err(string t) { msg.Text = t; msg.Foreground = Ui.Res("DangerBrush"); }
                if (oldP.Password.Length == 0 || newP.Password.Length == 0) { Err(Loc.Get("login.error_empty")); return; }
                if (newP.Password.Length < 8) { Err(Loc.Get("register.error_length")); return; }
                if (newP.Password != repP.Password) { Err(Loc.Get("register.error_mismatch")); return; }
                ((Button)s).IsEnabled = false;
                try
                {
                    await Api.ChangePassword(oldP.Password, newP.Password);
                    oldP.Clear(); newP.Clear(); repP.Clear();
                    msg.Text = Loc.Get("account.password_changed"); msg.Foreground = Ui.Res("AccentTextBrush");
                }
                catch (ApiException e) when (e.Status == 400 && (e.Detail ?? "").Contains("текущ")) { Err(Loc.Get("account.wrong_password")); }
                catch (Exception e) { Err(Errors.Text(e)); }
                finally { ((Button)s).IsEnabled = true; }
            });
            var row = new Grid { Margin = new Thickness(0, 18, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(btn);
            Grid.SetColumn(msg, 1);
            row.Children.Add(msg);
            form.Children.Add(row);
            p.Children.Add(new Border { Style = S("Card"), Padding = new Thickness(20, 8, 20, 16), Margin = new Thickness(0, 10, 0, 0), Child = form });
        }

        // Sign out
        var logout = Dialogs.Btn(Loc.Get("account.logout"), "Btn.Secondary", async (_, _) =>
        {
            if (!await Dialogs.Confirm(Loc.Get("account.logout_title"), Loc.Get(anon ? "account.logout_anon" : "account.logout_text"),
                    Loc.Get("account.logout"), danger: true)) return;
            await Vpn.Disconnect(false);
            Session.Clear();
            MainWindow.Instance?.ShowLogin();
        });
        p.Children.Add(ActionRow(Loc.Get("account.logout_row"), Loc.Get("account.logout_row_desc"), logout, 26));

        // Danger zone: far from «sign out», own frame
        p.Children.Add(new TextBlock { Text = Loc.Get("account.danger"), Style = S("T.Label"), Foreground = Ui.Res("DangerBrush"), Margin = new Thickness(0, 40, 0, 0) });
        var delete = Dialogs.Btn(Loc.Get("account.delete"), "Btn.Danger", async (_, _) =>
        {
            if (!await Dialogs.Confirm(Loc.Get("account.delete_title"), Loc.Get("account.delete_text"), Loc.Get("account.delete_confirm"), danger: true)) return;
            try
            {
                await Vpn.Disconnect(false);
                await Api.DeleteMe();
                Session.Clear();
                MainWindow.Instance?.ShowLogin();
            }
            catch (Exception e) { await Dialogs.Info(Loc.Get("account.delete_title"), Errors.Text(e)); }
        });
        var danger = ActionRow(Loc.Get("account.delete"), Loc.Get("account.delete_row_desc"), delete, 10);
        danger.SetResourceReference(Border.BorderBrushProperty, "DangerSoftBrush");
        p.Children.Add(danger);
        return p;
    }

    /// <summary>Card with a title + description on the left and one button on the right.</summary>
    static Border ActionRow(string title, string desc, Button button, double top)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = new StackPanel { Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        t.Children.Add(new TextBlock { Text = desc, Style = S("T.Muted"), Margin = new Thickness(0, 3, 0, 0) });
        g.Children.Add(t);
        button.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(button, 1);
        g.Children.Add(button);
        return new Border { Style = S("Card"), Padding = new Thickness(20, 16, 16, 16), Margin = new Thickness(0, top, 0, 0), Child = g };
    }

    static void ShowSub(TextBlock target, SubscriptionResponse? s)
    {
        if (s is { is_active: true } && s.plan is "month" or "year")
        {
            var plan = Loc.Get(s.plan == "year" ? "plan.year" : "plan.month");
            var until = DateTime.TryParse(s.expires_at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
                ? d.ToLocalTime().ToString("dd.MM.yyyy") : "—";
            target.Text = Loc.F("account.sub_active", plan, until);
            target.Foreground = Ui.Res("AccentTextBrush");
        }
        else
        {
            target.Text = Loc.Get("account.sub_none");
            target.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        }
    }

    static async Task LoadSub(TextBlock target)
    {
        try { ShowSub(target, await Api.Subscription()); }
        catch (Exception e) { if (!Session.SubKnown) target.Text = Errors.Text(e); }
    }

    static string RoleName(string? role) => role switch
    {
        "admin" or "superadmin" => Loc.Get("role.admin"), "developer" => Loc.Get("role.developer"),
        "tester" => Loc.Get("role.tester"), "support" => Loc.Get("role.support"), _ => Loc.Get("role.user")
    };

    // ── Connection ──

    FrameworkElement Connection()
    {
        var p = Page(Loc.Get("settings.connection"), Loc.Get("settings.connection_desc"));
        p.Children.Add(Card(Rows(
            Toggle(Loc.Get("ks.title"), Loc.Get("ks.desc"), St.KillSwitch, async v => { St.KillSwitch = v; AppSettings.Save(); await Task.CompletedTask; }),
            Toggle(Loc.Get("reconnect.title"), Loc.Get("reconnect.desc"), St.AutoReconnect, async v => { St.AutoReconnect = v; AppSettings.Save(); await Task.CompletedTask; }),
            Toggle(Loc.Get("autostart.title"), Loc.Get("autostart.desc"), St.Autostart, async v =>
            {
                if (await Autostart.Set(v)) { St.Autostart = v; AppSettings.Save(); }
                else Dialogs.Toast(Loc.Get("autostart.error"));
            }),
            Toggle(Loc.Get("connect_start.title"), Loc.Get("connect_start.desc"), St.ConnectOnStart, async v => { St.ConnectOnStart = v; AppSettings.Save(); await Task.CompletedTask; }),
            Toggle(Loc.Get("tray.title"), Loc.Get("tray.desc"), St.CloseToTray, async v => { St.CloseToTray = v; AppSettings.Save(); await Task.CompletedTask; }),
            Toggle(Loc.Get("lan.title"), Loc.Get("lan.desc"), St.AllowLan, async v => { St.AllowLan = v; AppSettings.Save(); await Task.CompletedTask; }))));
        p.Children.Add(Note(Loc.Get("ks.note")));

        p.Children.Add(Label(Loc.Get("dns.title")));
        p.Children.Add(new TextBlock { Text = Loc.Get("dns.desc"), Style = S("T.Muted"), Margin = new Thickness(0, 6, 0, 0) });
        p.Children.Add(Options("dns", new[]
        {
            ("cloudflare", "Cloudflare · 1.1.1.1", Loc.Get("dns.cloudflare")),
            ("google", "Google · 8.8.8.8", Loc.Get("dns.google")),
            ("quad9", "Quad9 · 9.9.9.9", Loc.Get("dns.quad9")),
            ("adguard", "AdGuard · 94.140.14.14", Loc.Get("dns.adguard")),
        }, St.Dns, v => { St.Dns = v; AppSettings.Save(); }));
        p.Children.Add(Note(Loc.Get("settings.apply_next"), 4));
        return p;
    }

    // ── Protocols ──

    FrameworkElement Protocols()
    {
        var p = Page(Loc.Get("proto.title"), Loc.Get("proto.cc_desc"));
        var speeds = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        p.Children.Add(Options("cc", new[]
        {
            ("bbr", "BBR", Loc.Get("proto.bbr")),
            ("brutal", "Brutal", Loc.Get("proto.brutal")),
        }, St.HysteriaMode, v => { St.HysteriaMode = v; AppSettings.Save(); speeds.Visibility = v == "brutal" ? Visibility.Visible : Visibility.Collapsed; }));

        var g = new Grid { MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Left };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        TextBox Speed(int col, string label, double value, Action<double> set)
        {
            var c = new StackPanel();
            c.Children.Add(new TextBlock { Text = label, Style = S("T.Caps"), Margin = new Thickness(0, 0, 0, 6) });
            var box = new TextBox { Style = S("Input"), Text = value.ToString("0.#", CultureInfo.InvariantCulture) };
            box.LostFocus += (_, _) =>
            {
                if (double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 && v < 1000)
                { set(v); AppSettings.Save(); }
                else box.Text = value.ToString("0.#", CultureInfo.InvariantCulture);
            };
            c.Children.Add(box);
            Grid.SetColumn(c, col);
            g.Children.Add(c);
            return box;
        }
        Speed(0, Loc.Get("proto.down"), St.HysteriaDownMBs, v => St.HysteriaDownMBs = v);
        Speed(2, Loc.Get("proto.up"), St.HysteriaUpMBs, v => St.HysteriaUpMBs = v);
        speeds.Children.Add(g);
        speeds.Children.Add(Note(Loc.Get("proto.speed_hint")));
        speeds.Visibility = St.HysteriaMode == "brutal" ? Visibility.Visible : Visibility.Collapsed;
        p.Children.Add(speeds);
        p.Children.Add(Note(Loc.Get("proto.apply_note"), 16));
        return p;
    }

    // ── Appearance ──

    FrameworkElement Appearance()
    {
        var p = Page(Loc.Get("settings.appearance"));
        p.Children.Add(Label(Loc.Get("theme.title"), 14));
        p.Children.Add(Options("theme", new[]
        {
            ("dark", Loc.Get("theme.dark"), Loc.Get("theme.dark_desc")),
            ("light", Loc.Get("theme.light"), Loc.Get("theme.light_desc")),
            ("system", Loc.Get("theme.system"), Loc.Get("theme.system_desc")),
        }, St.Theme, v => { St.Theme = v; AppSettings.Save(); Theme.Apply(); }));

        p.Children.Add(Label(Loc.Get("lang.title")));
        p.Children.Add(Options("lang", new[] { ("ru", "Русский", ""), ("en", "English", "") }, Loc.Lang, v =>
        {
            if (v == Loc.Lang) return;
            St.Language = v;
            AppSettings.Save();
            MainWindow.Instance?.Dispatcher.BeginInvoke(() => MainWindow.Instance?.Reload());
        }));
        return p;
    }

    // ── Journal ──

    FrameworkElement Journal()
    {
        var p = Page(Loc.Get("log.title"), Loc.Get("log.desc"));
        var size = new TextBlock { Style = S("T.Muted"), Margin = new Thickness(0, 4, 0, 0) };
        void UpdateSize() => size.Text = Loc.F("log.size", Loc.Bytes(Log.Size));
        p.Children.Add(Options("log", new[]
        {
            ("off", Loc.Get("log.off"), Loc.Get("log.off_desc")),
            ("min", Loc.Get("log.min"), Loc.Get("log.min_desc")),
            ("max", Loc.Get("log.max"), Loc.Get("log.max_desc")),
        }, St.LogLevel, v =>
        {
            St.LogLevel = v;
            AppSettings.Save();
            if (v == "off") Log.Clear(); else Log.I(Log.Header());
            UpdateSize();
        }));
        UpdateSize();
        p.Children.Add(size);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        row.Children.Add(Dialogs.Btn(Loc.Get("log.save"), "Btn.Primary", (_, _) =>
        {
            if (!File.Exists(Log.FilePath)) { Dialogs.Toast(Loc.Get("log.empty")); return; }
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = $"fsociety-journal-{DateTime.Now:yyyyMMdd-HHmm}.txt", Filter = "Text|*.txt" };
            if (dlg.ShowDialog() != true) return;
            File.WriteAllText(dlg.FileName, Log.Header() + Environment.NewLine + File.ReadAllText(Log.FilePath));
            Dialogs.Toast(Loc.Get("common.saved"));
        }));
        var clear = Dialogs.Btn(Loc.Get("log.clear"), "Btn.Ghost", (_, _) => { Log.Clear(); UpdateSize(); });
        clear.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(clear);
        p.Children.Add(row);
        p.Children.Add(Note(Loc.Get("log.note"), 16));
        return p;
    }

    // ── Update ──

    FrameworkElement Update()
    {
        var p = Page(Loc.Get("update.title"));
        var state = new TextBlock { Style = S("T.Muted") };
        var notes = new TextBlock { Style = S("T.Body"), Margin = new Thickness(0, 8, 0, 0) };
        var get = Dialogs.Btn("", "Btn.Primary", (_, _) => { if (Updates.Available is { } u) Updates.Open(u.AssetUrl ?? u.PageUrl); });
        get.Visibility = Visibility.Collapsed;
        get.HorizontalAlignment = HorizontalAlignment.Left;
        get.Margin = new Thickness(0, 14, 0, 0);
        p.Children.Add(Card(Rows(Info(Loc.Get("update.current"), App.Version), new Border { Padding = new Thickness(0, 12, 0, 14), Child = state })));
        p.Children.Add(get);
        var notesLabel = Label(Loc.Get("update.whats_new"));
        notesLabel.Visibility = Visibility.Collapsed;
        p.Children.Add(notesLabel);
        p.Children.Add(notes);
        var retry = Dialogs.Btn(Loc.Get("update.check"), "Btn.Secondary", async (_, _) => await Check());
        retry.HorizontalAlignment = HorizontalAlignment.Left;
        retry.Margin = new Thickness(0, 14, 0, 0);
        p.Children.Add(retry);
        p.Children.Add(Note(Loc.Get("update.note"), 14));

        async Task Check()
        {
            state.Text = Loc.Get("update.checking");
            try
            {
                var info = await Updates.Check();
                if (Updates.Available is { } u)
                {
                    state.Text = Loc.F("update.new", u.Version);
                    state.Foreground = Ui.Res("AccentTextBrush");
                    get.Content = Loc.F("update.btn", u.Version);
                    get.Visibility = Visibility.Visible;
                    notes.Text = u.Notes;
                    notesLabel.Visibility = string.IsNullOrWhiteSpace(u.Notes) ? Visibility.Collapsed : Visibility.Visible;
                }
                else state.Text = info == null ? Loc.Get("update.up_to_date") : Loc.Get("update.up_to_date");
            }
            catch { state.Text = Loc.Get("update.error"); }
        }
        _ = Check();
        return p;
    }

    // ── About ──

    FrameworkElement About()
    {
        var p = Page(Loc.Get("settings.about"));
        Button Link(string text, string url)
        {
            var b = new Button { Style = S("Btn.Link"), Content = text, Margin = new Thickness(0, 0, 18, 0) };
            b.Click += (_, _) => Updates.Open(url);
            return b;
        }
        p.Children.Add(Card(Rows(
            Info(Loc.Get("update.current"), $"FsocietyNet {App.Version}"),
            Info("AmneziaWG", "2.0.0"),
            Info("sing-box", Binaries.Embedded(Binaries.SingBox) ? "1.14.4" : Loc.Get("about.missing")),
            Info("ByeDPI", Binaries.Embedded(Binaries.CiaDpi) ? "17.3" : Loc.Get("about.missing")))));
        var links = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        links.Children.Add(Link(Loc.Get("privacy.policy_title"), LoginView.LegalUrl("privacy")));
        links.Children.Add(Link(Loc.Get("privacy.terms_title"), LoginView.LegalUrl("terms")));
        links.Children.Add(Link("fsociety-vpn.org", Api.Site));
        links.Children.Add(Link("Telegram", "https://t.me/reich_kanzlei"));
        p.Children.Add(links);
        p.Children.Add(Note(Loc.Get("about.note"), 18));
        return p;
    }
}
