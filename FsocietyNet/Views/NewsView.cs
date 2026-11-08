using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FsocietyNet.Services;

namespace FsocietyNet.Views;

/// <summary>
/// News (GET /news): the 5 latest, shown at once from the cache in %AppData%\FsocietyNet (text + pictures),
/// then refreshed from the server in the background.
/// </summary>
public sealed class NewsView : UserControl
{
    const int Limit = 5;
    static string CacheFile => Path.Combine(Paths.UserDir, "news.json");
    static string ImageDir => Path.Combine(Paths.UserDir, "news-images");

    readonly StackPanel _list = new();
    readonly Button _retry;
    readonly TextBlock _state;
    string? _shownJson;

    public NewsView()
    {
        var root = new StackPanel { Margin = new Thickness(32, 44, 32, 28), MaxWidth = 780, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(new TextBlock { Text = Loc.Get("news.section"), Style = Dialogs.S<TextBlock>("T.Label") });
        root.Children.Add(new TextBlock { Text = Loc.Get("news.title"), Style = Dialogs.S<TextBlock>("T.H1"), Margin = new Thickness(0, 8, 0, 20) });
        _state = new TextBlock { Style = Dialogs.S<TextBlock>("T.Muted"), Text = Loc.Get("common.loading") };
        root.Children.Add(_state);
        root.Children.Add(_list);
        _retry = Dialogs.Btn(Loc.Get("common.retry"), "Btn.Secondary", async (_, _) => await Refresh());
        _retry.HorizontalAlignment = HorizontalAlignment.Left;
        _retry.Margin = new Thickness(0, 12, 0, 0);
        _retry.Visibility = Visibility.Collapsed;
        root.Children.Add(_retry);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };

        if (!Api.DemoMode && ReadCache() is { } cached) Show(cached.page, cached.json);
        _ = Refresh();
    }

    static (NewsPage page, string json)? ReadCache()
    {
        try
        {
            if (!File.Exists(CacheFile)) return null;
            var json = File.ReadAllText(CacheFile);
            var page = JsonSerializer.Deserialize<NewsPage>(json);
            return page == null ? null : (page, json);
        }
        catch { return null; }
    }

    async Task Refresh()
    {
        _retry.IsEnabled = false;
        try
        {
            var page = await Api.News(Limit, 0);
            var json = JsonSerializer.Serialize(page);
            if (json != _shownJson) Show(page, json);
            if (!Api.DemoMode)
            {
                Paths.EnsureUserDir();
                File.WriteAllText(CacheFile, json);
                CleanImages(page);
            }
        }
        catch (Exception e)
        {
            Log.E("news", e);
            if (_list.Children.Count == 0)
            {
                _state.Text = Loc.Get("news.error") + " · " + Errors.Text(e);
                _state.Visibility = Visibility.Visible;
                _retry.Visibility = Visibility.Visible;
            }
        }
        finally { _retry.IsEnabled = true; }
    }

    void Show(NewsPage page, string json)
    {
        _shownJson = json;
        _list.Children.Clear();
        foreach (var n in page.items.Take(Limit)) _list.Children.Add(Card(n));
        _state.Text = Loc.Get("news.empty");
        _state.Visibility = _list.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _retry.Visibility = Visibility.Collapsed;
    }

    static void CleanImages(NewsPage page)
    {
        try
        {
            if (!Directory.Exists(ImageDir)) return;
            var keep = page.items.Where(n => n.image_id != null).Select(n => n.image_id!).ToHashSet();
            foreach (var f in Directory.GetFiles(ImageDir))
                if (!keep.Contains(Path.GetFileNameWithoutExtension(f))) File.Delete(f);
        }
        catch { }
    }

    FrameworkElement Card(NewsItem n)
    {
        var card = new Border { Style = Dialogs.S<Border>("Card"), Padding = new Thickness(22, 18, 22, 18), Margin = new Thickness(0, 0, 0, 14) };
        var p = new StackPanel();
        card.Child = p;
        if (DateTime.TryParse(n.published_at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d))
            p.Children.Add(new TextBlock
            {
                Text = d.ToLocalTime().ToString("d MMMM yyyy", new CultureInfo(Loc.En ? "en-US" : "ru-RU")),
                Style = Dialogs.S<TextBlock>("T.Caps")
            });
        p.Children.Add(new TextBlock { Text = n.Title, Style = Dialogs.S<TextBlock>("T.H2"), Margin = new Thickness(0, 6, 0, 10) });
        if (!string.IsNullOrEmpty(n.image_id))
        {
            var img = new Image { Stretch = Stretch.Uniform, MaxHeight = 320, HorizontalAlignment = HorizontalAlignment.Left };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            p.Children.Add(new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = img, Margin = new Thickness(0, 0, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Left });
            _ = LoadImage(img, n.image_id!, n.image_ext);
        }
        p.Children.Add(Body(n.Body));
        return card;
    }

    static async Task LoadImage(Image img, string id, string? ext)
    {
        try
        {
            var file = Path.Combine(ImageDir, id + (string.IsNullOrWhiteSpace(ext) ? "" : "." + ext));
            byte[] bytes;
            if (File.Exists(file)) bytes = await File.ReadAllBytesAsync(file);
            else
            {
                bytes = await Api.NewsImage(id, ext);
                if (bytes.Length == 0) return;
                if (!Api.DemoMode) { Directory.CreateDirectory(ImageDir); await File.WriteAllBytesAsync(file, bytes); }
            }
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = new MemoryStream(bytes);
            bi.EndInit();
            bi.Freeze();
            img.Source = bi;
        }
        catch (Exception e) { Log.E("news image", e); }
    }

    static readonly Regex Token = new(@"\*\*(.+?)\*\*|(https?://[^\s)]+)");

    /// <summary>News text: paragraphs, **bold**, clickable links (like NewsFormat.kt).</summary>
    public static TextBlock Body(string text)
    {
        var tb = new TextBlock { Style = Dialogs.S<TextBlock>("T.Body") };
        int pos = 0;
        foreach (Match m in Token.Matches(text))
        {
            if (m.Index > pos) tb.Inlines.Add(new Run(text[pos..m.Index]));
            if (m.Groups[1].Success) tb.Inlines.Add(new Bold(new Run(m.Groups[1].Value)));
            else
            {
                var url = m.Groups[2].Value;
                var link = new Hyperlink(new Run(url)) { Foreground = Ui.Res("AccentTextBrush") };
                link.Click += (_, _) => Updates.Open(url);
                tb.Inlines.Add(link);
            }
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) tb.Inlines.Add(new Run(text[pos..]));
        return tb;
    }
}
