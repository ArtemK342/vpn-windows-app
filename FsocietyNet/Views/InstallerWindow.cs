using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using FsocietyNet.Services;

namespace FsocietyNet.Views;

/// <summary>Install / update / uninstall window (Services/Installer.cs), in the app's own style.</summary>
public sealed class InstallerWindow : Window
{
    static Style S(string k) => (Style)Application.Current.FindResource(k);
    static Brush B(string k) => (Brush)Application.Current.FindResource(k);

    readonly bool _uninstall;
    readonly StackPanel _body = new();
    readonly TextBlock _error = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
    readonly Button _ok;
    readonly Button _cancel;
    public bool Done { get; private set; }

    InstallerWindow(bool uninstall)
    {
        _uninstall = uninstall;
        Title = uninstall ? Loc.Get("setup.uninstall_title") : Loc.Get("setup.title");
        Width = 600;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        UseLayoutRounding = true;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        FontSize = 13.5;
        SetResourceReference(BackgroundProperty, "BgBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/app.ico"));
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 48, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0, 0, 0, 1), UseAeroCaptionButtons = false });
        SourceInitialized += (_, _) => Theme.StyleFrame(new WindowInteropHelper(this).Handle);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");

        var root = new StackPanel { Margin = new Thickness(32, 22, 32, 26) };
        var top = new Grid();
        var brand = new TextBlock { FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"), FontSize = 17, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        brand.Inlines.Add(new Run("[") { Foreground = B("MutedBrush") });
        brand.Inlines.Add(new Run("f") { Foreground = B("AccentTextBrush") });
        brand.Inlines.Add(new Run("]society"));
        top.Children.Add(brand);
        var close = new Button { Style = S("Btn.Chrome"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, -10, -16, 0), Content = new Icon { Data = Ui.Geo("I.Close"), Size = 15, Stroke = 1.5 } };
        close.Click += (_, _) => Close();
        top.Children.Add(close);
        root.Children.Add(top);

        root.Children.Add(new TextBlock { Text = Loc.Get(uninstall ? "setup.uninstall_label" : "setup.label"), Style = S("T.Label"), Margin = new Thickness(0, 26, 0, 0) });
        root.Children.Add(new TextBlock { Text = uninstall ? Loc.Get("setup.uninstall_h1") : $"FsocietyNet {App.Version}", Style = S("T.H1"), Margin = new Thickness(0, 8, 0, 0) });
        root.Children.Add(_body);
        root.Children.Add(_error);

        _cancel = Dialogs.Btn(Loc.Get("common.cancel"), "Btn.Ghost", (_, _) => Close());
        _ok = Dialogs.Btn("", uninstall ? "Btn.Danger" : "Btn.Primary", async (_, _) => await Run());
        _ok.MinWidth = 150;
        var buttons = Dialogs.Buttons(_cancel, _ok);
        buttons.Margin = new Thickness(0, 26, 0, 0);
        root.Children.Add(buttons);
        Content = new Border { BorderThickness = new Thickness(1), Child = root };
        ((Border)Content).SetResourceReference(Border.BorderBrushProperty, "LineBrush");

        if (uninstall) BuildUninstall(); else BuildInstall();
    }

    // ── Install ──
    TextBox? _dir;
    CheckBox? _accept, _desktop, _autostart, _launch;

    static (Grid row, CheckBox box) Switch(UIElement label, bool on)
    {
        var g = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var cb = new CheckBox { Style = S("Switch"), IsChecked = on, VerticalAlignment = VerticalAlignment.Top };
        g.Children.Add(cb);
        if (label is FrameworkElement fe) { fe.Margin = new Thickness(12, 2, 0, 0); fe.VerticalAlignment = VerticalAlignment.Center; }
        Grid.SetColumn(label, 1);
        g.Children.Add(label);
        return (g, cb);
    }

    void BuildInstall()
    {
        var existing = Installer.Existing();
        _body.Children.Add(new TextBlock
        {
            Text = existing is { } e ? Loc.F("setup.update_desc", e.version, e.dir) : Loc.Get("setup.desc"),
            Style = S("T.Muted"), Margin = new Thickness(0, 8, 0, 0)
        });

        _body.Children.Add(new TextBlock { Text = Loc.Get("setup.folder"), Style = S("T.Caps"), Margin = new Thickness(0, 22, 0, 6) });
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _dir = new TextBox { Style = S("Input"), Text = existing?.dir ?? Installer.DefaultDir, FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"), FontSize = 12.5,
            IsEnabled = existing == null };
        _dir.SetValue(Ui.IconProperty, Ui.Geo("I.Download"));
        _dir.Padding = new Thickness(10, 0, 10, 0);
        row.Children.Add(_dir);
        var browse = Dialogs.Btn(Loc.Get("setup.browse"), "Btn.Secondary", (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.Get("setup.folder"), InitialDirectory = Path.GetDirectoryName(_dir.Text.TrimEnd('\\')) ?? "" };
            if (dlg.ShowDialog(this) == true) _dir.Text = Installer.Normalize(dlg.FolderName);
        }, 90);
        browse.Margin = new Thickness(8, 0, 0, 0);
        browse.Height = 42;
        browse.IsEnabled = existing == null;
        Grid.SetColumn(browse, 1);
        row.Children.Add(browse);
        _body.Children.Add(row);

        // Consents: the policy and terms are required, the rest is up to the user
        var legal = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        legal.Inlines.Add(new Run(Loc.Get("privacy.accept") + " "));
        var pl = new Hyperlink(new Run(Loc.Get("privacy.policy"))) { Foreground = B("AccentTextBrush") };
        pl.Click += (_, _) => Updates.Open(LoginView.LegalUrl("privacy"));
        legal.Inlines.Add(pl);
        legal.Inlines.Add(new Run(" " + Loc.Get("privacy.and") + " "));
        var tl = new Hyperlink(new Run(Loc.Get("privacy.terms"))) { Foreground = B("AccentTextBrush") };
        tl.Click += (_, _) => Updates.Open(LoginView.LegalUrl("terms"));
        legal.Inlines.Add(tl);

        var space = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        (var r1, _accept) = Switch(legal, false);
        (var r2, _desktop) = Switch(new TextBlock { Text = Loc.Get("setup.desktop"), TextWrapping = TextWrapping.Wrap }, true);
        (var r3, _autostart) = Switch(new TextBlock { Text = Loc.Get("setup.autostart"), TextWrapping = TextWrapping.Wrap }, AppSettings.Current.Autostart);
        (var r4, _launch) = Switch(new TextBlock { Text = Loc.Get("setup.launch"), TextWrapping = TextWrapping.Wrap }, true);
        foreach (var r in new[] { r1, r2, r3, r4 }) space.Children.Add(r);
        _body.Children.Add(space);
        _body.Children.Add(new TextBlock { Text = Loc.Get("setup.note"), Style = S("T.Note"), Margin = new Thickness(0, 18, 0, 0) });

        _ok.Content = Loc.Get(existing == null ? "setup.install" : "setup.update");
        _ok.IsEnabled = false;
        _accept.Checked += (_, _) => _ok.IsEnabled = true;
        _accept.Unchecked += (_, _) => _ok.IsEnabled = false;
    }

    // ── Uninstall ──
    CheckBox? _removeData;

    void BuildUninstall()
    {
        _body.Children.Add(new TextBlock { Text = Loc.Get("setup.uninstall_desc"), Style = S("T.Muted"), Margin = new Thickness(0, 8, 0, 0) });
        (var r, _removeData) = Switch(new TextBlock { Text = Loc.Get("setup.uninstall_data"), TextWrapping = TextWrapping.Wrap }, true);
        r.Margin = new Thickness(0, 18, 0, 0);
        _body.Children.Add(r);
        _ok.Content = Loc.Get("setup.uninstall_btn");
    }

    async Task Run()
    {
        _ok.IsEnabled = _cancel.IsEnabled = false;
        _error.Visibility = Visibility.Collapsed;
        var label = _ok.Content;
        _ok.Content = Loc.Get(_uninstall ? "setup.removing" : "setup.installing");
        try
        {
            if (_uninstall)
            {
                await Installer.Uninstall(_removeData!.IsChecked == true);
                Done = true;
                await Dialogs0.Info(this, Loc.Get("setup.uninstall_done"));
                Close();
                return;
            }
            var dir = Installer.Existing()?.dir ?? Installer.Normalize(_dir!.Text);
            var exe = await Installer.Install(dir, _desktop!.IsChecked == true, _autostart!.IsChecked == true);
            Done = true;
            if (_launch!.IsChecked == true)
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = dir });
            Close();
        }
        catch (Exception e)
        {
            Log.E(_uninstall ? "uninstall" : "install", e);
            _error.Text = Loc.Get(_uninstall ? "setup.uninstall_failed" : "setup.failed") + " " + e.Message;
            _error.Visibility = Visibility.Visible;
            _ok.Content = label;
            _ok.IsEnabled = _cancel.IsEnabled = true;
        }
    }

    /// <summary>`-render`: the window without showing it modally.</summary>
    internal static Window Preview(bool uninstall) => new InstallerWindow(uninstall);

    public static bool RunInstall() { var w = new InstallerWindow(false); w.ShowDialog(); return w.Done; }
    public static bool RunUninstall() { var w = new InstallerWindow(true); w.ShowDialog(); return w.Done; }

    /// <summary>Simple message inside the installer (no MainWindow to host the app's dialogs).</summary>
    static class Dialogs0
    {
        public static Task Info(Window owner, string text)
        {
            MessageBox.Show(owner, text, "FsocietyNet", MessageBoxButton.OK, MessageBoxImage.Information);
            return Task.CompletedTask;
        }
    }
}
