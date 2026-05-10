using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FsocietyNet.Services;
using Microsoft.Win32;

namespace FsocietyNet.Controls;

public partial class TunnelingControl : UserControl
{
    private AppConfig _cfg = AppConfig.Load();

    private Brush AccentBrush  => (Brush)Application.Current.Resources["AccentBrush"]!;
    private Brush MutedBrush   => (Brush)Application.Current.Resources["TextMutedBrush"]!;
    private Brush PrimaryBrush => (Brush)Application.Current.Resources["TextPrimaryBrush"]!;
    private Brush Bg2Brush     => (Brush)Application.Current.Resources["Bg2Brush"]!;
    private Brush BorderBrush2 => (Brush)Application.Current.Resources["BorderBrush2"]!;
    private Brush SurfaceBrush => (Brush)Application.Current.Resources["SurfaceBrush"]!;
    private Brush ErrorBrush   => (Brush)Application.Current.Resources["ErrorRedBrush"]!;

    public TunnelingControl()
    {
        InitializeComponent();
        RefreshUI();
    }

    // ──────────────────── Вкладки Приложения / Сайты ────────────────────

    private void AppsTab_Click(object sender, MouseButtonEventArgs e)
    {
        AppsPanel.Visibility   = Visibility.Visible;
        SitesPanel.Visibility  = Visibility.Collapsed;
        AppsTabText.Foreground = AccentBrush;
        AppsTabText.FontWeight = FontWeights.Bold;
        SitesTabText.Foreground = MutedBrush;
        SitesTabText.FontWeight = FontWeights.Normal;
    }

    private void SitesTab_Click(object sender, MouseButtonEventArgs e)
    {
        AppsPanel.Visibility   = Visibility.Collapsed;
        SitesPanel.Visibility  = Visibility.Visible;
        SitesTabText.Foreground = AccentBrush;
        SitesTabText.FontWeight = FontWeights.Bold;
        AppsTabText.Foreground = MutedBrush;
        AppsTabText.FontWeight = FontWeights.Normal;
    }

    // ──────────────────── Режим — Приложения ────────────────────

    private void AppModeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        _cfg.AppTunnelMode = Enum.Parse<TunnelMode>((string)btn.Tag);
        _cfg.Save();
        UpdateAppModeButtons();
        UpdateAppModeDesc();
    }

    private void UpdateAppModeButtons()
    {
        var active   = Application.Current.Resources["PrimaryButton"]   as Style;
        var inactive = Application.Current.Resources["SecondaryButton"] as Style;
        AppModeAllBtn.Style = _cfg.AppTunnelMode == TunnelMode.All     ? active : inactive;
        AppModeIncBtn.Style = _cfg.AppTunnelMode == TunnelMode.Include  ? active : inactive;
        AppModeExcBtn.Style = _cfg.AppTunnelMode == TunnelMode.Exclude  ? active : inactive;
    }

    private void UpdateAppModeDesc()
    {
        AppModeDesc.Text = _cfg.AppTunnelMode switch
        {
            TunnelMode.All     => "Весь трафик идёт через VPN. Список приложений не применяется.",
            TunnelMode.Include => "Только выбранные приложения используют VPN. Остальные — прямое подключение.\n⚠ Реализуется блокировкой физических интерфейсов — может не работать для всех приложений.",
            TunnelMode.Exclude => "Выбранные приложения обходят VPN, остальные идут через туннель.\n⚠ Реализуется блокировкой VPN-интерфейса — может не работать для всех приложений.",
            _                  => ""
        };
    }

    // ──────────────────── Режим — Сайты ────────────────────

    private void SiteModeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        _cfg.SiteTunnelMode = Enum.Parse<TunnelMode>((string)btn.Tag);
        _cfg.Save();
        UpdateSiteModeButtons();
        UpdateSiteModeDesc();
    }

    private void UpdateSiteModeButtons()
    {
        var active   = Application.Current.Resources["PrimaryButton"]   as Style;
        var inactive = Application.Current.Resources["SecondaryButton"] as Style;
        SiteModeAllBtn.Style = _cfg.SiteTunnelMode == TunnelMode.All     ? active : inactive;
        SiteModeIncBtn.Style = _cfg.SiteTunnelMode == TunnelMode.Include  ? active : inactive;
        SiteModeExcBtn.Style = _cfg.SiteTunnelMode == TunnelMode.Exclude  ? active : inactive;
    }

    private void UpdateSiteModeDesc()
    {
        SiteModeDesc.Text = _cfg.SiteTunnelMode switch
        {
            TunnelMode.All     => "Весь трафик идёт через VPN. Список IP/CIDR не применяется.",
            TunnelMode.Include => "Только указанные IP/CIDR идут через VPN. Остальное — прямое подключение.\nПрименяется при следующем подключении.",
            TunnelMode.Exclude => "Указанные IP/CIDR обходят VPN, весь остальной трафик — через туннель.\nПрименяется при следующем подключении.",
            _                  => ""
        };
    }

    // ──────────────────── Список приложений ────────────────────

    private void RebuildAppList()
    {
        AppList.Children.Clear();
        foreach (var app in _cfg.TunnelApps)
            AppList.Children.Add(BuildAppRow(app));

        if (_cfg.TunnelApps.Count == 0)
            AppList.Children.Add(BuildEmptyHint("Список пуст — добавьте приложения ниже."));
    }

    private Border BuildAppRow(TunnelApp app)
    {
        var row = new Border
        {
            BorderBrush     = BorderBrush2,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background      = Brushes.Transparent
        };

        var grid = new Grid { Margin = new Thickness(0, 10, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        left.Children.Add(new TextBlock
        {
            Text       = app.Name,
            Foreground = PrimaryBrush,
            FontSize   = 13,
            FontWeight = FontWeights.Bold
        });
        left.Children.Add(new TextBlock
        {
            Text       = app.ExePath,
            Foreground = MutedBrush,
            FontFamily = new FontFamily("Consolas"),
            FontSize   = 10,
            Margin     = new Thickness(0, 3, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var removeBtn = new Button
        {
            Content  = "✕",
            Style    = (Style)Application.Current.Resources["SecondaryButton"]!,
            Width    = 32,
            Height   = 32,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        var captured = app;
        removeBtn.Click += (_, _) =>
        {
            _cfg.TunnelApps.Remove(captured);
            _cfg.Save();
            RebuildAppList();
        };

        Grid.SetColumn(left,      0);
        Grid.SetColumn(removeBtn, 1);
        grid.Children.Add(left);
        grid.Children.Add(removeBtn);
        row.Child = grid;
        return row;
    }

    private void AddFromProcessesBtn_Click(object sender, RoutedEventArgs e)
    {
        // Показываем всплывающий список запущенных процессов
        var popup = new Window
        {
            Title           = "Выберите приложение",
            Width           = 480,
            Height          = 500,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background      = (Brush)Application.Current.Resources["BgDarkBrush"]!,
            ResizeMode      = ResizeMode.NoResize
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var search = new TextBox
        {
            Style  = (Style)Application.Current.Resources["DarkTextBox"]!,
            Margin = new Thickness(12, 6, 12, 6),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(search, 0);

        var listBox = new ListBox
        {
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin          = new Thickness(0)
        };
        Grid.SetRow(listBox, 1);

        var procs = Process.GetProcesses()
            .Where(p => { try { return !string.IsNullOrEmpty(p.MainWindowTitle) && p.Id > 4; } catch { return false; } })
            .DistinctBy(p => { try { return p.MainModule?.FileName ?? ""; } catch { return p.ProcessName; } })
            .OrderBy(p => p.ProcessName)
            .ToList();

        void FillList(string filter)
        {
            listBox.Items.Clear();
            foreach (var proc in procs)
            {
                string? path;
                try { path = proc.MainModule?.FileName; } catch { continue; }
                if (string.IsNullOrEmpty(path)) continue;
                var name = proc.ProcessName;
                if (!string.IsNullOrEmpty(filter) &&
                    !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;

                var item = new ListBoxItem
                {
                    Content    = $"{name}",
                    Tag        = (name, path),
                    Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"]!,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize   = 12,
                    Padding    = new Thickness(12, 8, 12, 8)
                };
                listBox.Items.Add(item);
            }
        }

        FillList("");
        search.TextChanged += (_, _) => FillList(search.Text);

        listBox.MouseDoubleClick += (_, _) =>
        {
            if (listBox.SelectedItem is ListBoxItem { Tag: (string n, string p) })
            {
                AddApp(n, p);
                popup.Close();
            }
        };

        grid.Children.Add(search);
        grid.Children.Add(listBox);
        popup.Content = grid;
        popup.ShowDialog();
    }

    private void AddExeBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Выберите исполняемый файл",
            Filter = "Приложения (*.exe)|*.exe"
        };
        if (dlg.ShowDialog() != true) return;
        var name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
        AddApp(name, dlg.FileName);
    }

    private void AddApp(string name, string path)
    {
        if (_cfg.TunnelApps.Any(a => a.ExePath.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
        _cfg.TunnelApps.Add(new TunnelApp { Name = name, ExePath = path });
        _cfg.Save();
        RebuildAppList();
    }

    // ──────────────────── Список сайтов / IP ────────────────────

    private void RebuildSiteList()
    {
        SiteList.Children.Clear();
        foreach (var site in _cfg.TunnelSites)
            SiteList.Children.Add(BuildSiteRow(site));

        if (_cfg.TunnelSites.Count == 0)
            SiteList.Children.Add(BuildEmptyHint(
                "Список пуст — добавьте домен, IP или CIDR.\n" +
                "Примеры:  vk.com  ·  8.8.8.8  ·  77.88.0.0/18\n" +
                "Или нажмите «+ RU Essential» чтобы добавить популярные российские сайты."));
    }

    // Пресет RU Essential
    private void RuPresetBtn_Click(object sender, RoutedEventArgs e)
    {
        var preset = IpRouteHelper.RuEssentialPreset;
        var allActive = preset.All(d => _cfg.TunnelSites.Contains(d));

        if (allActive)
        {
            // Убираем пресет
            foreach (var d in preset) _cfg.TunnelSites.Remove(d);
            RuPresetBtn.Content = "+ RU Essential";
            RuPresetBtn.Style   = (Style)Application.Current.Resources["SecondaryButton"]!;
        }
        else
        {
            // Добавляем пресет
            foreach (var d in preset)
                if (!_cfg.TunnelSites.Contains(d))
                    _cfg.TunnelSites.Add(d);
            RuPresetBtn.Content = "✓ RU Essential";
            RuPresetBtn.Style   = (Style)Application.Current.Resources["PrimaryButton"]!;
        }

        _cfg.Save();
        RebuildSiteList();
        UpdateRuPresetButton();
    }

    private void UpdateRuPresetButton()
    {
        var allActive = IpRouteHelper.RuEssentialPreset.All(d => _cfg.TunnelSites.Contains(d));
        RuPresetBtn.Content = allActive ? "✓ RU Essential" : "+ RU Essential";
        RuPresetBtn.Style   = allActive
            ? (Style)Application.Current.Resources["PrimaryButton"]!
            : (Style)Application.Current.Resources["SecondaryButton"]!;
    }

    private Border BuildSiteRow(string cidr)
    {
        var row = new Border
        {
            BorderBrush     = BorderBrush2,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background      = Brushes.Transparent
        };

        var grid = new Grid { Margin = new Thickness(0, 10, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new TextBlock
        {
            Text       = cidr,
            Foreground = PrimaryBrush,
            FontFamily = new FontFamily("Consolas"),
            FontSize   = 13,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var removeBtn = new Button
        {
            Content  = "✕",
            Style    = (Style)Application.Current.Resources["SecondaryButton"]!,
            Width    = 32,
            Height   = 32,
            FontSize = 11
        };
        var captured = cidr;
        removeBtn.Click += (_, _) =>
        {
            _cfg.TunnelSites.Remove(captured);
            _cfg.Save();
            RebuildSiteList();
        };

        Grid.SetColumn(text,      0);
        Grid.SetColumn(removeBtn, 1);
        grid.Children.Add(text);
        grid.Children.Add(removeBtn);
        row.Child = grid;
        return row;
    }

    private void AddSiteBtn_Click(object sender, RoutedEventArgs e) => TryAddSite();

    private void SiteInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryAddSite();
    }

    private void TryAddSite()
    {
        var raw = SiteInputBox.Text.Trim();
        if (string.IsNullOrEmpty(raw)) return;

        // Принимаем: домен, IP или CIDR
        // Нормализуем — убираем схему и путь если вставили URL
        var entry = raw.ToLowerInvariant()
            .TrimStart("https://".ToCharArray())
            .TrimStart("http://".ToCharArray())
            .TrimStart("www.".ToCharArray())
            .Split('/')[0].Split('?')[0].Split('#')[0];

        // Валидация: IP, CIDR или домен (минимум одна точка)
        var cidr = IpRouteHelper.NormalizeCidr(entry);
        var isDomain = entry.Contains('.') && !entry.Contains(' ');

        if (cidr == null && !isDomain)
        {
            SiteInputBox.BorderBrush = ErrorBrush;
            return;
        }

        var toAdd = cidr ?? entry; // CIDR-форму для IP, домен как есть
        SiteInputBox.BorderBrush = BorderBrush2;

        if (_cfg.TunnelSites.Contains(toAdd)) { SiteInputBox.Text = ""; return; }
        _cfg.TunnelSites.Add(toAdd);
        _cfg.Save();
        SiteInputBox.Text = "";
        RebuildSiteList();
        UpdateRuPresetButton();
    }

    // ──────────────────── Общий refresh ────────────────────

    private void RefreshUI()
    {
        UpdateAppModeButtons();
        UpdateAppModeDesc();
        UpdateSiteModeButtons();
        UpdateSiteModeDesc();
        RebuildAppList();
        RebuildSiteList();
        UpdateRuPresetButton();
    }

    // ──────────────────── Хелперы UI ────────────────────

    private TextBlock BuildEmptyHint(string text) => new()
    {
        Text       = text,
        Foreground = MutedBrush,
        FontFamily = new FontFamily("Consolas"),
        FontSize   = 11,
        Margin     = new Thickness(0, 8, 0, 8),
        TextWrapping = TextWrapping.Wrap
    };
}
