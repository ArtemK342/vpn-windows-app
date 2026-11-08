using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FsocietyNet.Services;

namespace FsocietyNet.Views;

/// <summary>
/// Settings → Bypass: «Режим РФ» engine (zapret / ByeDPI) and every item of zapret's service.bat menu as buttons:
/// SERVICE (install, remove, status), SETTINGS (game filter, IPSet, auto-update check, active fakes),
/// UPDATES (IPSet list, hosts, new version), TOOLS (diagnostics, tests) — plus the strategy choice.
/// </summary>
public sealed partial class SettingsView
{
    FrameworkElement Bypass()
    {
        var p = Page(Loc.Get("bypass.title"), Loc.Get("bypass.desc"));
        var zapretPanel = new StackPanel();
        var zapret = St.BypassEngine != "byedpi";
        p.Children.Add(Options("engine", new[]
        {
            ("zapret", Loc.Get("bypass.engine_zapret"), Loc.Get("bypass.engine_zapret_desc")),
            ("byedpi", Loc.Get("bypass.engine_byedpi"), Loc.Get("bypass.engine_byedpi_desc")),
        }, zapret ? "zapret" : "byedpi", v =>
        {
            St.BypassEngine = v;
            AppSettings.Save();
            zapretPanel.Visibility = v == "zapret" ? Visibility.Visible : Visibility.Collapsed;
        }));
        p.Children.Add(Note(Loc.Get("bypass.apply_note"), 2));
        zapretPanel.Visibility = zapret ? Visibility.Visible : Visibility.Collapsed;
        p.Children.Add(zapretPanel);

        if (!Zapret.Embedded) { zapretPanel.Children.Add(Note(Loc.Get("bypass.zapret_missing"), 16)); return p; }
        if (!Api.DemoMode)
        {
            try { Zapret.Ensure(); }
            catch (Exception e) { Log.E("zapret ensure", e); zapretPanel.Children.Add(Note(Loc.Get("bypass.zapret_broken") + " " + e.Message, 16)); return p; }
        }
        BuildZapret(zapretPanel);
        return p;
    }

    void BuildZapret(StackPanel panel)
    {
        Button Btn(string key, string style, Func<Button, Task> run)
        {
            var b = Dialogs.Btn(Loc.Get(key), style, async (s, _) =>
            {
                var btn = (Button)s;
                btn.IsEnabled = false;
                try { await run(btn); }
                catch (Exception e) { Log.E("zapret " + key, e); await Dialogs.Info(Loc.Get(key), Errors.Text(e) + "\n" + e.Message); }
                finally { btn.IsEnabled = true; }
            });
            b.Margin = new Thickness(0, 0, 8, 8);
            return b;
        }
        WrapPanel Buttons(params Button[] bs) { var w = new WrapPanel { Margin = new Thickness(0, 14, 0, 6) }; foreach (var b in bs) w.Children.Add(b); return w; }
        Border Box(params UIElement[] items)
        {
            var s = new StackPanel();
            foreach (var i in items) s.Children.Add(i);
            return new Border { Style = S("Card"), Padding = new Thickness(20, 16, 20, 12), Margin = new Thickness(0, 10, 0, 0), Child = s };
        }
        TextBlock Title(string key) => new() { Text = Loc.Get(key), FontWeight = FontWeights.SemiBold };
        TextBlock Desc(string text) => new() { Text = text, Style = S("T.Muted"), Margin = new Thickness(0, 3, 0, 0) };

        // ── Strategy ──
        panel.Children.Add(Label(Loc.F("bypass.zapret_label", Zapret.Version), 26));
        var strategyText = new TextBlock { FontFamily = (FontFamily)FindResource("MonoFont"), FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) };
        void ShowStrategy() => strategyText.Text = St.ZapretStrategy;
        ShowStrategy();
        var serviceStrategy = new TextBlock { Style = S("T.Note"), Margin = new Thickness(0, 4, 0, 0) };
        void ShowServiceStrategy()
        {
            var s = Zapret.InstalledStrategy();
            serviceStrategy.Text = s == null ? "" : Loc.F("bypass.service_uses", s);
            serviceStrategy.Visibility = s == null ? Visibility.Collapsed : Visibility.Visible;
        }
        ShowServiceStrategy();
        panel.Children.Add(Box(Title("bypass.strategy"), Desc(Loc.Get("bypass.strategy_desc")), strategyText, serviceStrategy,
            Buttons(
                Btn("bypass.strategy_pick", "Btn.Secondary", async _ => { if (await PickStrategy()) ShowStrategy(); }),
                Btn("bypass.tests", "Btn.Primary", async _ => { if (await RunTests()) ShowStrategy(); }))));

        // ── SERVICE ──
        panel.Children.Add(Label(Loc.Get("bypass.section_service"), 26));
        var status = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        void ShowStatus() { status.Children.Clear(); foreach (var d in Zapret.Status()) status.Children.Add(DiagRow(d)); ShowServiceStrategy(); }
        ShowStatus();
        panel.Children.Add(Box(Title("bypass.service"), Desc(Loc.Get("bypass.service_desc")), status,
            Buttons(
                Btn("bypass.install", "Btn.Primary", async _ =>
                {
                    if (!await Dialogs.Confirm(Loc.Get("bypass.install"), Loc.F("bypass.install_confirm", St.ZapretStrategy), Loc.Get("bypass.install"))) return;
                    if (Vpn.Active && Vpn.EngineKind == "bypass") await Vpn.Disconnect(false);
                    var log = await Zapret.InstallService(St.ZapretStrategy);
                    ShowStatus();
                    await ShowLog(Loc.Get("bypass.install"), log, Zapret.Status());
                }),
                Btn("bypass.remove", "Btn.Danger", async _ =>
                {
                    if (!await Dialogs.Confirm(Loc.Get("bypass.remove"), Loc.Get("bypass.remove_confirm"), Loc.Get("bypass.remove"), danger: true)) return;
                    if (Vpn.Active && Vpn.EngineKind == "bypass") await Vpn.Disconnect(false);
                    var log = await Zapret.RemoveServices();
                    ShowStatus();
                    await ShowLog(Loc.Get("bypass.remove"), log, Zapret.Status());
                }),
                Btn("bypass.status", "Btn.Secondary", async _ => { ShowStatus(); await ShowLog(Loc.Get("bypass.status"), null, Zapret.Status()); }))));

        // ── SETTINGS ──
        panel.Children.Add(Label(Loc.Get("bypass.section_settings"), 26));

        // 4. Game filter
        var g = Zapret.ReadGameFilter();
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var modeHost = new Border { Background = Ui.Res("BgBrush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Left, Child = modes };
        foreach (var (id, key) in new[] { ("disabled", "bypass.gf_off"), ("all", "bypass.gf_all"), ("tcp", "bypass.gf_tcp"), ("udp", "bypass.gf_udp") })
        {
            var rb = new RadioButton { Style = S("Segment"), GroupName = "gf", Content = Loc.Get(key), IsChecked = g.Mode == id };
            rb.Checked += (_, _) => { g = g with { Mode = id }; Zapret.SaveGameFilter(g); Dialogs.Toast(Loc.Get("bypass.restart_hint")); };
            modes.Children.Add(rb);
        }
        var ports = new Grid { Margin = new Thickness(0, 12, 0, 0), MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left };
        ports.ColumnDefinitions.Add(new ColumnDefinition());
        ports.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        ports.ColumnDefinitions.Add(new ColumnDefinition());
        ports.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBox PortBox(int col, string label, string value)
        {
            var c = new StackPanel();
            c.Children.Add(new TextBlock { Text = label, Style = S("T.Caps"), Margin = new Thickness(0, 0, 0, 6) });
            var box = new TextBox { Style = S("Input"), Text = value, FontFamily = (FontFamily)FindResource("MonoFont"), FontSize = 12.5 };
            c.Children.Add(box);
            Grid.SetColumn(c, col);
            ports.Children.Add(c);
            return box;
        }
        var tcpBox = PortBox(0, "TCP", g.Tcp);
        var udpBox = PortBox(2, "UDP", g.Udp);
        var savePorts = Dialogs.Btn(Loc.Get("bypass.gf_save"), "Btn.Secondary", (_, _) =>
        {
            if (!Zapret.ValidPorts(tcpBox.Text) || !Zapret.ValidPorts(udpBox.Text)) { Dialogs.Toast(Loc.Get("bypass.gf_invalid")); return; }
            g = g with { Tcp = tcpBox.Text.Replace(" ", ""), Udp = udpBox.Text.Replace(" ", "") };
            Zapret.SaveGameFilter(g);
            Dialogs.Toast(Loc.Get("bypass.restart_hint"));
        }, 80);
        savePorts.Margin = new Thickness(10, 0, 0, 0);
        savePorts.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(savePorts, 3);
        ports.Children.Add(savePorts);

        // 5. IPSet
        var ipsetText = new TextBlock { Style = S("T.Muted"), Margin = new Thickness(0, 3, 0, 0) };
        void ShowIpset() => ipsetText.Text = Loc.Get("bypass.ipset_" + Zapret.IpsetStatus());
        ShowIpset();
        var ipsetBtn = Btn("bypass.ipset_switch", "Btn.Secondary", _ =>
        {
            if (!Zapret.CycleIpset()) Dialogs.Toast(Loc.Get("bypass.ipset_no_backup"));
            else Dialogs.Toast(Loc.Get("bypass.restart_hint"));
            ShowIpset();
            return Task.CompletedTask;
        });

        // 6. Auto-update check
        var autoUpdate = Toggle(Loc.Get("bypass.autoupdate"), Loc.Get("bypass.autoupdate_desc"), Zapret.UpdateCheckEnabled,
            v => { Zapret.SetUpdateCheck(v); return Task.CompletedTask; });

        // 7. Active fakes
        var fakesBtn = Btn("bypass.fakes", "Btn.Secondary", async _ => await ReplaceFakes());

        panel.Children.Add(new Border
        {
            Style = S("Card"), Padding = new Thickness(20, 4, 20, 4), Margin = new Thickness(0, 10, 0, 0),
            Child = Rows(
                Wrap(Title("bypass.gf"), Desc(Loc.Get("bypass.gf_desc")), modeHost, ports),
                Wrap(Title("bypass.ipset"), ipsetText, Desc(Loc.Get("bypass.ipset_desc")), Buttons(ipsetBtn)),
                autoUpdate,
                Wrap(Title("bypass.fakes"), Desc(Loc.Get("bypass.fakes_desc")), Buttons(fakesBtn)))
        });
        panel.Children.Add(Note(Loc.Get("bypass.restart_note"), 10));

        // ── UPDATES ──
        panel.Children.Add(Label(Loc.Get("bypass.section_updates"), 26));
        panel.Children.Add(Box(Desc(Loc.Get("bypass.updates_desc")), Buttons(
            Btn("bypass.ipset_update", "Btn.Secondary", async _ =>
            {
                await Zapret.UpdateIpset();
                ShowIpset();
                Dialogs.Toast(Loc.Get("bypass.ipset_updated"));
            }),
            Btn("bypass.hosts", "Btn.Secondary", async _ => await HostsCheck()),
            Btn("bypass.check_updates", "Btn.Secondary", async _ =>
            {
                var latest = await Zapret.LatestVersion();
                if (latest == null) { await Dialogs.Info(Loc.Get("bypass.check_updates"), Loc.Get("bypass.updates_failed")); return; }
                if (latest == Zapret.Version) { await Dialogs.Info(Loc.Get("bypass.check_updates"), Loc.F("bypass.updates_latest", Zapret.Version)); return; }
                if (await Dialogs.Confirm(Loc.Get("bypass.check_updates"), Loc.F("bypass.updates_new", latest, Zapret.Version), Loc.Get("bypass.open_releases")))
                    Updates.Open(Zapret.ReleasesUrl);
            }))));

        // ── TOOLS ──
        panel.Children.Add(Label(Loc.Get("bypass.section_tools"), 26));
        panel.Children.Add(Box(Desc(Loc.Get("bypass.tools_desc")), Buttons(
            Btn("bypass.diagnostics", "Btn.Secondary", async _ => await Diagnostics()),
            Btn("bypass.tests", "Btn.Secondary", async _ => { if (await RunTests()) ShowStrategy(); }),
            Btn("bypass.user_list", "Btn.Ghost", _ =>
            {
                try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Zapret.UserListPath("list-general-user.txt")}\"") { UseShellExecute = false }); }
                catch (Exception e) { Dialogs.Toast(e.Message); }
                return Task.CompletedTask;
            }))));
        panel.Children.Add(Note(Loc.Get("bypass.credits"), 14));
    }

    static StackPanel Wrap(params UIElement[] items)
    {
        var s = new StackPanel { Margin = new Thickness(0, 14, 0, 12) };
        foreach (var i in items) s.Children.Add(i);
        return s;
    }

    static FrameworkElement DiagRow(DiagItem d)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var (geo, brush) = d.Level switch { "ok" => ("I.Check", "AccentTextBrush"), "warn" => ("I.Warn", "WarnBrush"), _ => ("I.Close", "DangerBrush") };
        g.Children.Add(new Icon { Data = Ui.Geo(geo), Size = 15, Stroke = 2, Brush = Ui.Res(brush), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0) });
        var t = new TextBlock { Text = d.Text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        return g;
    }

    static Task ShowLog(string title, string? log, IEnumerable<DiagItem>? items, params Button[] extra)
    {
        var p = Dialogs.Panel(title);
        var body = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        if (items != null) foreach (var d in items) body.Children.Add(DiagRow(d));
        if (!string.IsNullOrWhiteSpace(log))
            body.Children.Add(new TextBox
            {
                Style = S("Input.Multi"), Text = log.Trim(), IsReadOnly = true, MaxHeight = 160, Margin = new Thickness(0, 10, 0, 0),
                FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"), FontSize = 11,
            });
        p.Children.Add(new ScrollViewer { Content = body, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var buttons = extra.Append(Dialogs.Btn(Loc.Get("common.close"), "Btn.Primary", (_, _) => MainWindow.Instance!.CloseDialog(true))).ToArray();
        p.Children.Add(Dialogs.Buttons(buttons));
        return MainWindow.Instance!.ShowDialog(p, true, 560);
    }

    static async Task<bool> PickStrategy()
    {
        var list = Zapret.Strategies();
        var p = Dialogs.Panel(Loc.Get("bypass.strategy"), Loc.Get("bypass.strategy_desc"));
        var items = new StackPanel { Margin = new Thickness(0, 12, 6, 0) };
        string chosen = St.ZapretStrategy;
        foreach (var s in list)
        {
            var rb = new RadioButton { Style = S("OptionCard"), GroupName = "strategy", IsChecked = s == chosen, Margin = new Thickness(0, 0, 0, 6),
                Content = new TextBlock { Text = s, FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"), FontSize = 12.5 } };
            rb.Checked += (_, _) => chosen = s;
            items.Children.Add(rb);
        }
        p.Children.Add(new ScrollViewer { Content = items, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        p.Children.Add(Dialogs.Buttons(
            Dialogs.Btn(Loc.Get("common.cancel"), "Btn.Ghost", (_, _) => MainWindow.Instance!.CloseDialog(false)),
            Dialogs.Btn(Loc.Get("bypass.use"), "Btn.Primary", (_, _) => MainWindow.Instance!.CloseDialog(true))));
        if (!await MainWindow.Instance!.ShowDialog(p, true, 480)) return false;
        St.ZapretStrategy = chosen;
        AppSettings.Save();
        Dialogs.Toast(Loc.Get("bypass.restart_hint"));
        return true;
    }

    /// <summary>12. Run tests: all strategies in turn; the best one can be used at once.</summary>
    static async Task<bool> RunTests()
    {
        var w = MainWindow.Instance!;
        if (Vpn.Active || Vpn.Busy)
        {
            if (!await Dialogs.Confirm(Loc.Get("bypass.tests"), Loc.Get("bypass.tests_vpn"), Loc.Get("bypass.tests_disconnect"))) return false;
            await Vpn.Disconnect(false);
        }
        var strategies = Zapret.Strategies();
        if (!await Dialogs.Confirm(Loc.Get("bypass.tests"), Loc.F("bypass.tests_confirm", strategies.Count), Loc.Get("bypass.tests_start"))) return false;

        var cts = new CancellationTokenSource();
        var p = Dialogs.Panel(Loc.Get("bypass.tests"));
        var line = new TextBlock { Style = S("T.Muted"), Margin = new Thickness(0, 12, 0, 8) };
        var track = new Grid { Height = 6 };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Ui.Res("LineBrush") });
        var bar = new Border { CornerRadius = new CornerRadius(3), Background = Ui.Res("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        track.Children.Add(bar);
        p.Children.Add(line);
        p.Children.Add(track);
        p.Children.Add(Dialogs.Buttons(Dialogs.Btn(Loc.Get("common.cancel"), "Btn.Ghost", (_, _) => cts.Cancel())));
        _ = w.ShowDialog(p, false, 480);

        List<ZapretTestResult> results;
        try
        {
            results = await Zapret.RunTests(strategies, (s, i, n) => w.Dispatcher.BeginInvoke(() =>
            {
                line.Text = Loc.F("bypass.tests_progress", i + 1, n, s);
                bar.Width = Math.Max(0, (track.ActualWidth > 0 ? track.ActualWidth : 420) * i / Math.Max(1, n));
            }), cts.Token);
        }
        catch (Exception e) { w.CloseDialog(false); await Dialogs.Info(Loc.Get("bypass.tests"), Errors.Text(e) + "\n" + e.Message); return false; }
        w.CloseDialog(false);
        if (results.Count == 0) return false;

        // Results: best first, every line can be used
        var rp = Dialogs.Panel(Loc.Get("bypass.tests_done"), Loc.Get("bypass.tests_done_desc"));
        var list = new StackPanel { Margin = new Thickness(0, 12, 6, 0) };
        string? use = null;
        foreach (var (r, i) in results.Select((r, i) => (r, i)))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = (i == 0 ? "★ " : "") + r.Strategy, FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"), FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center, ToolTip = r.Details, FontWeight = i == 0 ? FontWeights.Bold : FontWeights.Normal };
            row.Children.Add(name);
            var score = new TextBlock { Text = r.Total == 0 ? Loc.Get("bypass.tests_failed") : $"{r.Ok}/{r.Total}", FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"),
                FontSize = 12, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
                Foreground = Ui.Res(r.Total > 0 && r.Ok == r.Total ? "AccentTextBrush" : r.Ok * 2 >= r.Total && r.Total > 0 ? "WarnBrush" : "DangerBrush") };
            Grid.SetColumn(score, 1);
            row.Children.Add(score);
            var b = Dialogs.Btn(Loc.Get("bypass.use"), i == 0 ? "Btn.Primary" : "Btn.Secondary", (_, _) => { use = r.Strategy; w.CloseDialog(true); }, 70);
            b.Height = 30;
            b.Padding = new Thickness(10, 0, 10, 0);
            Grid.SetColumn(b, 2);
            row.Children.Add(b);
            list.Children.Add(row);
        }
        rp.Children.Add(new ScrollViewer { Content = list, MaxHeight = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        rp.Children.Add(Dialogs.Buttons(Dialogs.Btn(Loc.Get("common.close"), "Btn.Ghost", (_, _) => w.CloseDialog(false))));
        if (!await w.ShowDialog(rp, true, 560) || use == null) return false;
        St.ZapretStrategy = use;
        AppSettings.Save();
        Dialogs.Toast(Loc.F("bypass.strategy_set", use));
        return true;
    }

    /// <summary>7. Replace active fakes: Discord UDP / game filter UDP ← one of bin/*.bin.</summary>
    static async Task ReplaceFakes()
    {
        var (fakes, discord, game) = Zapret.Fakes();
        var p = Dialogs.Panel(Loc.Get("bypass.fakes"), Loc.Get("bypass.fakes_desc"));
        string? newDiscord = discord, newGame = game;
        StackPanel Group(string title, string? current, Action<string> set, string group)
        {
            var s = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            s.Children.Add(new TextBlock { Text = Loc.F(title, current ?? "—"), Style = S("T.Label") });
            var wrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var f in fakes)
            {
                var rb = new RadioButton { Style = S("Segment"), GroupName = group, Content = f, IsChecked = f == current, Margin = new Thickness(0, 0, 4, 4) };
                rb.Checked += (_, _) => set(f);
                wrap.Children.Add(rb);
            }
            s.Children.Add(wrap);
            return s;
        }
        var body = new StackPanel();
        body.Children.Add(Group("bypass.fake_discord", discord, f => newDiscord = f, "fd"));
        body.Children.Add(Group("bypass.fake_game", game, f => newGame = f, "fg"));
        p.Children.Add(new ScrollViewer { Content = body, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        p.Children.Add(Dialogs.Buttons(
            Dialogs.Btn(Loc.Get("common.cancel"), "Btn.Ghost", (_, _) => MainWindow.Instance!.CloseDialog(false)),
            Dialogs.Btn(Loc.Get("bypass.fakes_apply"), "Btn.Primary", (_, _) => MainWindow.Instance!.CloseDialog(true))));
        if (!await MainWindow.Instance!.ShowDialog(p, true, 600)) return;
        if (newDiscord != null && newDiscord != discord) Zapret.ReplaceFake("discord", newDiscord);
        if (newGame != null && newGame != game) Zapret.ReplaceFake("game", newGame);
        Dialogs.Toast(Loc.Get("bypass.restart_hint"));
    }

    /// <summary>9. Update hosts file: like service.bat — the file is only checked, the user copies the block himself.</summary>
    static async Task HostsCheck()
    {
        var (needs, content) = await Zapret.CheckHosts();
        if (!needs) { await Dialogs.Info(Loc.Get("bypass.hosts"), Loc.Get("bypass.hosts_ok")); return; }
        var copy = Dialogs.Btn(Loc.Get("ext.copy"), "Btn.Secondary", (_, _) => Dialogs.Copy(content));
        var open = Dialogs.Btn(Loc.Get("bypass.hosts_open"), "Btn.Secondary", (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Zapret.HostsPath}\"") { UseShellExecute = false }); } catch { }
        });
        await ShowLog(Loc.Get("bypass.hosts"), content, new[] { new DiagItem("warn", Loc.Get("bypass.hosts_needs")) }, copy, open);
    }

    /// <summary>11. Run diagnostics; conflicts and the Discord cache are fixed by buttons in the result.</summary>
    static async Task Diagnostics()
    {
        var items = await Zapret.Diagnostics();
        var extra = new List<Button>();
        if (Zapret.FoundConflicts().Count > 0)
            extra.Add(Dialogs.Btn(Loc.Get("bypass.remove_conflicts"), "Btn.Danger", async (_, _) =>
            {
                var log = await Zapret.RemoveConflicts();
                MainWindow.Instance!.CloseDialog(true);
                await ShowLog(Loc.Get("bypass.remove_conflicts"), log, null);
            }));
        extra.Add(Dialogs.Btn(Loc.Get("bypass.discord_cache"), "Btn.Secondary", async (_, _) =>
        {
            if (!await Dialogs.Confirm(Loc.Get("bypass.discord_cache"), Loc.Get("bypass.discord_cache_confirm"), Loc.Get("bypass.discord_cache"))) return;
            await ShowLog(Loc.Get("bypass.discord_cache"), null, Zapret.ClearDiscordCache());
        }));
        await ShowLog(Loc.Get("bypass.diagnostics"), null, items, extra.ToArray());
    }
}
