using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using FsocietyNet.Services;
using FsocietyNet.Views;

namespace FsocietyNet;

public partial class App : Application
{
    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.1.0";

    static Mutex? _single;
    static EventWaitHandle? _showSignal;
    static bool _quitting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        string? Arg(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

        // Build tool: tools/make-icon → Assets/app.ico from the vector mark
        if (Arg("-makeicon") is { } icoPath)
        {
            BrandMark.WriteIco(icoPath, BrandState.Normal, 16, 20, 24, 32, 40, 48, 64, 96, 128, 256);
            Shutdown();
            return;
        }

        // Read-only check of the live API (preview build): news and servers parse without errors
        if (Arg("-apitest") is { } report)
        {
            await ApiTest(report);
            Shutdown();
            return;
        }

        _single = new Mutex(true, @"Local\FsocietyNet.Single", out var first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\FsocietyNet.Show");
        _quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\FsocietyNet.Quit");
        if (!first && !args.Contains("-render"))
        {
            // Another build is running (e.g. an older one hidden in the tray): it quits and this one starts —
            // otherwise a new exe only brought the old window to front. Same exe: just show it.
            if (!await TakeOver())
            {
                _showSignal.Set();
                Shutdown();
                return;
            }
        }

        Paths.EnsureUserDir();
        AppSettings.Load();
        Theme.Apply();
        Theme.WatchSystem();
        DispatcherUnhandledException += (_, ex) => { Log.E("ui", ex.Exception); ex.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, ex) => { Log.E("task", ex.Exception); ex.SetObserved(); };

        if (Arg("-render") is { } dir)
        {
            Api.DemoMode = true;
            await Renderer.RenderAll(dir);
            Shutdown();
            return;
        }
#if PREVIEW
        Api.DemoMode = true;
#else
        Api.DemoMode = args.Contains("-demo");
#endif
        Session.Load();
        Log.I(Log.Header());
        if (!Api.DemoMode)
        {
            Paths.SecureMachineDirs();
            await Vpn.CleanupAtStart();
        }

        Tray.Init();
        Tray.OpenRequested += ShowMain;
        Tray.ExitRequested += Quit;
        Vpn.StartWatchdog();
        SessionEnding += (_, _) => { Vpn.ShutdownBlocking(); Tray.Dispose(); };

        var w = new MainWindow();
        MainWindow = w;
        // Current.MainWindow, not `w`: a language change replaces the window
        Tray.WindowVisible = () => Current.MainWindow is Window mw && mw.IsVisible && mw.WindowState != WindowState.Minimized;
        if (!args.Contains("--tray")) w.Show();

        // Second start → show this window; another build starting → quit and let it run
        var signals = new WaitHandle[] { _showSignal, _quitSignal };
        _ = Task.Run(() =>
        {
            while (!_quitting)
            {
                var i = WaitHandle.WaitAny(signals, 1000);
                if (i == 0) Dispatcher.BeginInvoke(ShowMain);
                else if (i == 1) { Dispatcher.BeginInvoke(Quit); return; }
            }
        });
    }

    static EventWaitHandle? _quitSignal;

    /// <summary>
    /// The running copy is a different exe (other path or other build): ask it to quit (older builds without the
    /// quit signal are closed after 8 s), then take the single-instance mutex. false = same exe, just show it.
    /// </summary>
    static async Task<bool> TakeOver()
    {
        var self = Environment.ProcessPath ?? "";
        var selfInfo = new FileInfo(self);
        var others = System.Diagnostics.Process.GetProcessesByName("FsocietyNet").Where(p => p.Id != Environment.ProcessId).ToList();
        bool Same(System.Diagnostics.Process p)
        {
            try
            {
                var path = p.MainModule?.FileName ?? "";
                var fi = new FileInfo(path);
                return path.Equals(self, StringComparison.OrdinalIgnoreCase) && fi.Length == selfInfo.Length
                       && fi.LastWriteTimeUtc == selfInfo.LastWriteTimeUtc;
            }
            catch { return true; }   // no access: do not touch it
        }
        if (others.Count == 0 || others.All(Same)) return false;

        _quitSignal!.Set();
        for (int i = 0; i < 16; i++)
        {
            await Task.Delay(500);
            if (others.All(p => { try { return p.HasExited; } catch { return true; } })) break;
        }
        foreach (var p in others) { try { if (!p.HasExited) p.Kill(true); } catch { } }
        try { _single!.WaitOne(TimeSpan.FromSeconds(5)); }
        catch (AbandonedMutexException) { }   // the old copy was killed: the mutex is ours anyway
        return true;
    }

    static async Task ApiTest(string path)
    {
        var sb = new System.Text.StringBuilder();
        Api.DemoMode = false;
        try
        {
            var news = await Api.News(5, 0);
            sb.AppendLine($"news ok: {news.items.Count} of {news.total}");
            foreach (var n in news.items) sb.AppendLine($"  {n.published_at} | {n.Title} | image={n.image_id}.{n.image_ext}");
            if (news.items.FirstOrDefault(n => n.image_id != null) is { } withImg)
                sb.AppendLine($"image bytes: {(await Api.NewsImage(withImg.image_id!, withImg.image_ext)).Length}");
        }
        catch (Exception e) { sb.AppendLine("news FAIL: " + e); }
        try
        {
            var servers = await Api.Servers();
            sb.AppendLine($"servers ok: {servers.Count}");
            foreach (var s in servers) sb.AppendLine($"  {s.server_type} {s.status} {s.DisplayName} host={s.Host} ping={await Net.Ping(s.Host)}");
        }
        catch (Exception e) { sb.AppendLine("servers FAIL: " + e); }
        try { var cfg = await Api.AppConfig(); sb.AppendLine($"app-config ok: maintenance={cfg.access?.maintenance?.enabled}"); }
        catch (Exception e) { sb.AppendLine("app-config FAIL: " + e); }
        File.WriteAllText(path, sb.ToString());
    }

    public static void ShowMain()
    {
        if (Current.MainWindow is not Window w) return;
        if (!w.IsVisible) w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
        w.Topmost = true; w.Topmost = false;
        w.Focus();
    }

    public static void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        Vpn.ShutdownBlocking();
        Tray.Dispose();
        Current.Shutdown();
    }
}
