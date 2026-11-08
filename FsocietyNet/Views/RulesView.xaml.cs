using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FsocietyNet.Services;

namespace FsocietyNet.Views;

public record SiteItem(string Entry, string Display);

public sealed class AppItem : INotifyPropertyChanged
{
    public string Name { get; init; } = "";
    public string Exe { get; init; } = "";
    public string Path { get; init; } = "";
    bool _selected;
    public bool Selected { get => _selected; set { _selected = value; Changed(nameof(Selected)); } }
    ImageSource? _icon;
    public ImageSource? IconImage { get => _icon; set { _icon = value; Changed(nameof(IconImage)); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public partial class RulesView : UserControl
{
    static AppSettings S => AppSettings.Current;
    readonly List<AppItem> _apps = [];
    bool _loading = true;

    public RulesView()
    {
        InitializeComponent();
        SitesOn.IsChecked = S.SitesEnabled;
        (S.SitesMode == "include" ? SitesInclude : SitesExclude).IsChecked = true;
        AppsOn.IsChecked = S.AppsEnabled;
        (S.AppsMode == "exclude" ? AppsExclude : AppsInclude).IsChecked = true;
        _loading = false;
        RefreshSites();
        RefreshAppsHeader();
        // The view is kept between tab switches: subscribe while it is shown
        Loaded += (_, _) => { Vpn.Changed += UpdateApplyBar; UpdateApplyBar(); };
        Unloaded += (_, _) => Vpn.Changed -= UpdateApplyBar;
        _ = LoadApps();
    }

    void Save()
    {
        AppSettings.Save();
        UpdateApplyBar();
    }

    void UpdateApplyBar() =>
        ApplyBar.Visibility = Vpn.Status == VpnStatus.On && Vpn.RulesAtConnect != null && Vpn.RulesAtConnect != S.RulesFingerprint()
            ? Visibility.Visible : Visibility.Collapsed;

    async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (Vpn.Server is { } srv) await Vpn.Connect(srv);
        UpdateApplyBar();
    }

    // ═══ Sites ═══

    void RefreshSites()
    {
        var list = S.Sites;
        SitesList.ItemsSource = list.Select(x => new SiteItem(x, Rules.Display(x))).ToList();
        SitesEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SitesCount.Text = Loc.F("rules.list_title", list.Count);
        SitesSummary.Text = !S.SitesEnabled ? Loc.Get("rules.off")
            : Loc.F(S.SitesMode == "include" ? "rules.sites_include_n" : "rules.sites_exclude_n", list.Count);

        Presets.Children.Clear();
        foreach (var p in Rules.Presets)
        {
            var t = new ToggleButton
            {
                Content = Loc.Get(p.TitleKey), Style = (Style)FindResource("PresetChip"), Tag = p,
                IsChecked = p.Entries.All(list.Contains)
            };
            t.Click += Preset_Click;
            Presets.Children.Add(t);
        }
    }

    void Preset_Click(object sender, RoutedEventArgs e)
    {
        var t = (ToggleButton)sender;
        var p = (SitePreset)t.Tag;
        var list = S.Sites;
        if (t.IsChecked == true) { foreach (var x in p.Entries) if (!list.Contains(x)) list.Add(x); }
        else list.RemoveAll(p.Entries.Contains);
        if (t.IsChecked == true && !S.SitesEnabled) { S.SitesEnabled = true; SitesOn.IsChecked = true; }
        Save();
        RefreshSites();
    }

    void SitesOn_Click(object sender, RoutedEventArgs e) { S.SitesEnabled = SitesOn.IsChecked == true; Save(); RefreshSites(); }

    void SitesMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.SitesMode = SitesInclude.IsChecked == true ? "include" : "exclude";
        Save();
        RefreshSites();
    }

    void AddSite_Click(object sender, RoutedEventArgs e)
    {
        SiteError.Visibility = Visibility.Collapsed;
        var entry = Rules.Normalize(SiteInput.Text);
        if (entry == null) { ShowSiteError(Loc.Get("rules.sites_invalid")); return; }
        if (S.Sites.Contains(entry)) { ShowSiteError(Loc.Get("rules.sites_duplicate")); return; }
        S.Sites.Insert(0, entry);
        if (!S.SitesEnabled) { S.SitesEnabled = true; SitesOn.IsChecked = true; }
        SiteInput.Text = "";
        Save();
        RefreshSites();
    }

    void ShowSiteError(string t) { SiteError.Text = t; SiteError.Visibility = Visibility.Visible; }

    void SiteInput_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) AddSite_Click(sender, e); }

    void RemoveSite_Click(object sender, RoutedEventArgs e)
    {
        S.Sites.Remove((string)((Button)sender).Tag);
        Save();
        RefreshSites();
    }

    async void ClearSites_Click(object sender, RoutedEventArgs e)
    {
        if (S.Sites.Count == 0) return;
        if (!await Dialogs.Confirm(Loc.Get("rules.clear_title"), Loc.F("rules.list_title", S.Sites.Count), Loc.Get("rules.clear_btn"), danger: true)) return;
        S.Sites.Clear();
        Save();
        RefreshSites();
    }

    // ═══ Apps ═══

    async Task LoadApps()
    {
        // Already chosen programs at once, the Start menu scan adds the rest
        var selected = S.Apps;
        _apps.AddRange(selected.Select(a => new AppItem { Name = a.Name, Exe = a.Exe, Path = a.Path, Selected = true }));
        AppsList.ItemsSource = _apps;
        if (_apps.Count > 0) AppsLoading.Visibility = Visibility.Collapsed;
        var installed = await AppScan.InstalledAsync();
        foreach (var a in selected)
            if (!installed.Any(i => i.Exe.Equals(a.Exe, StringComparison.OrdinalIgnoreCase))) installed.Add(a);
        _apps.Clear();
        _apps.AddRange(installed.Select(a => new AppItem
        {
            Name = a.Name, Exe = a.Exe, Path = a.Path,
            Selected = selected.Any(x => x.Exe.Equals(a.Exe, StringComparison.OrdinalIgnoreCase))
        }).OrderByDescending(a => a.Selected).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase));
        CollectionViewSource.GetDefaultView(_apps).Refresh();
        AppsLoading.Text = Loc.Get("rules.apps_none");
        AppsLoading.Visibility = _apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshAppsHeader();
        _ = Task.Run(() =>
        {
            foreach (var a in _apps.ToList())
            {
                var img = ExeIcon(a.Path);
                if (img != null) Dispatcher.BeginInvoke(() => a.IconImage = img);
            }
        });
    }

    static ImageSource? ExeIcon(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var ic = Drawing.Icon.ExtractAssociatedIcon(path);
            if (ic == null) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
    }

    void RefreshAppsHeader()
    {
        var n = S.Apps.Count;
        AppsCount.Text = Loc.F("rules.list_title", n);
        AppsSummary.Text = !S.AppsEnabled ? Loc.Get("rules.off")
            : Loc.F(S.AppsMode == "include" ? "rules.apps_include_n" : "rules.apps_exclude_n", n);
        BlockedPreset.Visibility = S.AppsMode == "include" ? Visibility.Visible : Visibility.Collapsed;
        var installed = BlockedInstalled().Count;
        BlockedDesc.Text = installed == 0 && _apps.Count > 0 ? Loc.Get("rules.preset_none") : Loc.F("rules.preset_blocked_desc", installed);
        BlockedBtn.IsEnabled = installed > 0;
        foreach (var a in _apps) a.Selected = S.Apps.Any(x => x.Exe.Equals(a.Exe, StringComparison.OrdinalIgnoreCase));
    }

    List<AppItem> BlockedInstalled() =>
        _apps.Where(a => Rules.BlockedApps.Any(p => p.Exes.Contains(a.Exe, StringComparer.OrdinalIgnoreCase))).ToList();

    void AppsOn_Click(object sender, RoutedEventArgs e) { S.AppsEnabled = AppsOn.IsChecked == true; Save(); RefreshAppsHeader(); }

    void AppsMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.AppsMode = AppsInclude.IsChecked == true ? "include" : "exclude";
        Save();
        RefreshAppsHeader();
    }

    void AppToggle_Click(object sender, RoutedEventArgs e)
    {
        var cb = (CheckBox)sender;
        var a = (AppItem)cb.Tag;
        SetApp(a, cb.IsChecked == true);
    }

    void SetApp(AppItem a, bool on)
    {
        S.Apps.RemoveAll(x => x.Exe.Equals(a.Exe, StringComparison.OrdinalIgnoreCase));
        if (on)
        {
            S.Apps.Add(new TunnelApp { Name = a.Name, Exe = a.Exe, Path = a.Path });
            if (!S.AppsEnabled) { S.AppsEnabled = true; AppsOn.IsChecked = true; }
        }
        Save();
        RefreshAppsHeader();
    }

    void BlockedPreset_Click(object sender, RoutedEventArgs e)
    {
        var found = BlockedInstalled();
        if (found.Count == 0) { Dialogs.Toast(Loc.Get("rules.preset_none")); return; }
        foreach (var a in found) if (!a.Selected) SetApp(a, true);
        Dialogs.Toast(Loc.F("rules.added_n", found.Count));
    }

    void PickExe_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Programs|*.exe", Title = Loc.Get("rules.pick_exe") };
        if (dlg.ShowDialog() != true) return;
        var exe = System.IO.Path.GetFileName(dlg.FileName);
        var item = _apps.FirstOrDefault(a => a.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase));
        if (item == null)
        {
            item = new AppItem { Name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName), Exe = exe, Path = dlg.FileName, IconImage = ExeIcon(dlg.FileName) };
            _apps.Insert(0, item);
            CollectionViewSource.GetDefaultView(_apps).Refresh();
        }
        SetApp(item, true);
    }

    void AppSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (AppsList.ItemsSource == null) return;
        var q = AppSearch.Text.Trim();
        CollectionViewSource.GetDefaultView(_apps).Filter = q.Length == 0 ? null
            : o => o is AppItem a && (a.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || a.Exe.Contains(q, StringComparison.OrdinalIgnoreCase));
    }
}
