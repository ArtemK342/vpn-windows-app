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

        _single = new Mutex(true, @"Local\FsocietyNet.Single", out var first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\FsocietyNet.Show");
        if (!first && !args.Contains("-render"))
        {
            _showSignal.Set();   // bring the running copy to front
            Shutdown();
            return;
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

        // Second start → show this window
        var signal = _showSignal;
        _ = Task.Run(() =>
        {
            while (!_quitting) { if (signal.WaitOne(1000)) Dispatcher.BeginInvoke(ShowMain); }
        });
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
