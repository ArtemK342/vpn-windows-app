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

        foreach (var (page, name) in new[] { ("rules", "05-rules"), ("news", "06-news"), ("support", "07-support"),
                     ("settings", "08-settings-account"), ("settings_connection", "09-settings-connection"),
                     ("settings_protocols", "10-settings-protocols"), ("settings_appearance", "11-settings-appearance"),
                     ("settings_journal", "12-settings-journal"), ("settings_about", "13-settings-about") })
        {
            w.Go(page);
            await Settle(page == "support" ? 900 : 500);
            Save(w, dir, name);
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

        // Icons as the shell shows them
        foreach (var (st, n) in new[] { (BrandState.Normal, "app"), (BrandState.Connected, "tray-on"), (BrandState.Off, "tray-off"),
                     (BrandState.Busy, "tray-busy"), (BrandState.Blocked, "tray-blocked") })
            foreach (var px in new[] { 16, 24, 32, 256 })
                SavePng(BrandMark.Render(px, st), Path.Combine(dir, $"icon-{n}-{px}.png"));

#if PREVIEW
        File.WriteAllLines(Path.Combine(dir, "missing-strings.txt"), Loc.Missing.OrderBy(x => x));
#endif
        File.WriteAllText(Path.Combine(dir, "x25519-selftest.txt"), X25519.SelfTest() ? "ok" : "FAIL");
        w.Close();
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
