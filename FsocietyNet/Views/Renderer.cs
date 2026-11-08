using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FsocietyNet.Services;

namespace FsocietyNet.Views;

/// <summary>
/// `FsocietyNet.exe -render &lt;dir&gt;` (preview build): every screen with demo data → PNG at 125 % scale, so the UI can
/// be checked without an account, admin rights or touching the network / VPN. Nothing is saved to disk.
/// </summary>
public static class Renderer
{
    public static async Task RenderAll(string dir)
    {
        Directory.CreateDirectory(dir);
        AppSettings.ReadOnly = true;
        var lang = Environment.GetEnvironmentVariable("FSOCIETY_RENDER_LANG");
        if (lang is "ru" or "en") AppSettings.Current.Language = lang;

        // Login screen
        Session.SetDemo(false);
        var w = new MainWindow { Left = -20000, Top = 0, ShowActivated = false, ShowInTaskbar = false, Width = 1120, Height = 720 };
        w.Show();
        await Settle();
        Save(w, dir, "01-login");
        // Same field with text typed in: its first letter must start where the placeholder starts (caret position)
        if (w.Content is FrameworkElement r0 && FindChild<System.Windows.Controls.TextBox>(r0, "Email") is { } email)
        {
            email.Text = "you@example.com";
            await Settle(200);
            Save(w, dir, "01b-login-typed");
            email.Text = "";
        }

        Session.SetDemo(true);
        w.ShowApp();
        await Settle();
        Save(w, dir, "02-home");

        Vpn.DemoSet(VpnStatus.On, Demo.Servers()[0]);
        await Settle();
        Save(w, dir, "03-home-connected");
        Vpn.DemoSet(VpnStatus.Connecting, Demo.Servers()[3]);
        await Settle();
        Save(w, dir, "04-home-connecting");
        Vpn.DemoSet(VpnStatus.Off, null);

        // Settings → Bypass → «always»: no «Обход» row, status line under the title
        AppSettings.Current.BypassMode = "system"; AppSettings.Current.BypassEngine = "zapret";
        Zapret.NotifyModeChanged();
        await Settle(700);
        Save(w, dir, "04b-home-bypass-always");
        AppSettings.Current.BypassMode = "server";
        Zapret.NotifyModeChanged();
        await Settle(300);

        foreach (var (page, name) in new[] { ("rules", "05-rules"), ("news", "06-news"), ("support", "07-support"),
                     ("settings", "08-settings-account"), ("settings_connection", "09-settings-connection"),
                     ("settings_protocols", "10-settings-protocols"), ("settings_bypass", "10b-settings-bypass"), ("settings_appearance", "11-settings-appearance"),
                     ("settings_journal", "12-settings-journal"), ("settings_about", "13-settings-about") })
        {
            w.Go(page);
            await Settle(page == "support" ? 900 : 500);
            if (page == "support" && w.Page.Content is SupportView sv) { await sv.OpenFirstForPreview(); await Settle(300); }
            Save(w, dir, name);
            if (page.StartsWith("settings") && FindScroll(w.Page) is { } sc)
            {
                sc.ScrollToEnd();
                await Settle(200);
                Save(w, dir, name + "-bottom");
            }
        }

        w.Go("home");
        _ = ExternalDialog.Show(Demo.Servers()[3], Demo.External());
        await Settle();
        Save(w, dir, "14-dialog-other-app");
        w.CloseDialog(false);

        AppSettings.Current.Theme = "light";
        Theme.Apply();
        Vpn.DemoSet(VpnStatus.On, Demo.Servers()[0]);
        w.Go("home");
        await Settle();
        Save(w, dir, "15-light-home");
        w.Go("rules");
        await Settle();
        Save(w, dir, "16-light-rules");
        Vpn.DemoSet(VpnStatus.Off, null);
        Session.SetDemo(false);
        w.ShowLogin();
        await Settle();
        Save(w, dir, "17-light-login");

        // Tray menu (drawn in place: a popup is not part of the window bitmap)
        foreach (var (st, n) in new[] { (VpnStatus.Off, "18-tray-menu-off"), (VpnStatus.On, "19-tray-menu-on") })
        {
            Vpn.DemoSet(st, Demo.Servers()[0]);
            var menu = Tray.BuildMenu();
            var items = menu.Items.Cast<object>().ToList();
            menu.Items.Clear();
            var stack = new System.Windows.Controls.StackPanel();
            foreach (var it in items) stack.Children.Add((UIElement)it);
            var card = new System.Windows.Controls.Border
            {
                Background = (Brush)Application.Current.FindResource("Surface2Brush"), BorderBrush = (Brush)Application.Current.FindResource("Line2Brush"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(4), Width = 280,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = stack,
                Margin = new Thickness(40),
            };
            var holder = new System.Windows.Controls.Grid { Background = (Brush)Application.Current.FindResource("BgBrush") };
            holder.Children.Add(card);
            var old = w.Content;
            w.Content = holder;
            await Settle(300);
            Save(w, dir, n);
            w.Content = old;
        }
        Vpn.DemoSet(VpnStatus.Off, null);

        // Icons as the shell shows them
        foreach (var (st, n) in new[] { (BrandState.Normal, "app"), (BrandState.Connected, "tray-on"), (BrandState.Off, "tray-off"),
                     (BrandState.Busy, "tray-busy"), (BrandState.Blocked, "tray-blocked") })
            foreach (var px in new[] { 16, 24, 32, 256 })
                SavePng(BrandMark.Render(px, st), Path.Combine(dir, $"icon-{n}-{px}.png"));

#if PREVIEW
        File.WriteAllLines(Path.Combine(dir, "missing-strings.txt"), Loc.Missing.OrderBy(x => x));
#endif
        File.WriteAllText(Path.Combine(dir, "x25519-selftest.txt"), X25519.SelfTest() ? "ok" : "FAIL");

        // zapret: every strategy → winws.exe arguments (parsing check only, winws is not started)
        var zsb = new System.Text.StringBuilder();
        try
        {
            Zapret.RootOverride = Path.Combine(dir, "zapret-test");
            Zapret.Ensure();
            foreach (var s in Zapret.Strategies()) zsb.AppendLine($"{s}: [{Zapret.Args(s).Count}] " + string.Join(" ¦ ", Zapret.Args(s)));
            zsb.AppendLine("ipset=" + Zapret.IpsetStatus() + " gamefilter=" + Zapret.ReadGameFilter() + " targets=" + Zapret.Targets().Count);
            var (fakes, d, g) = Zapret.Fakes();
            zsb.AppendLine($"fakes={fakes.Count} discord={d} game={g}");
        }
        catch (Exception e) { zsb.AppendLine("FAIL " + e); }
        finally { Zapret.RootOverride = null; }
        File.WriteAllText(Path.Combine(dir, "zapret-args.txt"), zsb.ToString());
        w.Close();
    }

    static System.Windows.Controls.ScrollViewer? FindScroll(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is System.Windows.Controls.ScrollViewer sv && sv.ScrollableHeight > 0) return sv;
            if (FindScroll(c) is { } f) return f;
        }
        return null;
    }

    static T? FindChild<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t && t.Name == name) return t;
            if (FindChild<T>(c, name) is { } found) return found;
        }
        return null;
    }

    static async Task Settle(int ms = 500)
    {
        await Task.Delay(ms);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    static void Save(Window w, string dir, string name)
    {
        var root = (FrameworkElement)w.Content;
        const double scale = 1.25;
        var bmp = new RenderTargetBitmap((int)(root.ActualWidth * scale), (int)(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle((Brush)Application.Current.FindResource("BgBrush"), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }
        bmp.Render(dv);
        SavePng(bmp, Path.Combine(dir, name + ".png"));
    }

    static void SavePng(BitmapSource bmp, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(path);
        enc.Save(f);
    }
}
