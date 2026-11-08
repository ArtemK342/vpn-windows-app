using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FsocietyNet.Services;

namespace FsocietyNet.Views;

/// <summary>News feed (GET /news, same as the app tab and fsociety-vpn.org/news). Built in code: a simple column of cards.</summary>
public sealed class NewsView : UserControl
{
    readonly StackPanel _list = new();
    readonly Button _more;
    readonly TextBlock _state;
    int _offset;
    const int Page = 10;

    public NewsView()
    {
        var root = new StackPanel { Margin = new Thickness(32, 44, 32, 28), MaxWidth = 780, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(new TextBlock { Text = Loc.Get("news.section"), Style = Dialogs.S<TextBlock>("T.Label") });
        root.Children.Add(new TextBlock { Text = Loc.Get("news.title"), Style = Dialogs.S<TextBlock>("T.H1"), Margin = new Thickness(0, 8, 0, 20) });
        _state = new TextBlock { Style = Dialogs.S<TextBlock>("T.Muted"), Text = Loc.Get("common.loading") };
        root.Children.Add(_state);
        root.Children.Add(_list);
        _more = Dialogs.Btn(Loc.Get("news.more"), "Btn.Secondary", async (_, _) => await Load());
        _more.HorizontalAlignment = HorizontalAlignment.Left;
        _more.Margin = new Thickness(0, 6, 0, 0);
        _more.Visibility = Visibility.Collapsed;
        root.Children.Add(_more);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        _ = Load();
    }

    async Task Load()
    {
        _more.IsEnabled = false;
        try
        {
            var page = await Api.News(Page, _offset);
            _offset += page.items.Count;
            foreach (var n in page.items) _list.Children.Add(Card(n));
            _state.Visibility = _list.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _state.Text = Loc.Get("news.empty");
            _more.Visibility = page.has_more ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception e)
        {
            if (_list.Children.Count == 0)
            {
                _state.Text = Loc.Get("news.error") + " · " + Errors.Text(e);
                _state.Visibility = Visibility.Visible;
                _more.Content = Loc.Get("common.retry");
                _more.Visibility = Visibility.Visible;
            }
        }
        finally { _more.IsEnabled = true; }
    }

    FrameworkElement Card(NewsItem n)
    {
        var card = new Border { Style = Dialogs.S<Border>("Card"), Padding = new Thickness(22, 18, 22, 18), Margin = new Thickness(0, 0, 0, 14) };
        var p = new StackPanel();
        card.Child = p;
        if (DateTime.TryParse(n.published_at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d))
            p.Children.Add(new TextBlock
            {
                Text = d.ToLocalTime().ToString(Loc.En ? "d MMMM yyyy" : "d MMMM yyyy", new CultureInfo(Loc.En ? "en-US" : "ru-RU")),
                Style = Dialogs.S<TextBlock>("T.Caps")
            });
        p.Children.Add(new TextBlock { Text = n.Title, Style = Dialogs.S<TextBlock>("T.H2"), Margin = new Thickness(0, 6, 0, 10) });
        if (!string.IsNullOrEmpty(n.image_id))
        {
            var img = new Image { Stretch = Stretch.Uniform, MaxHeight = 320, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            p.Children.Add(new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = img });
            _ = LoadImage(img, n.image_id!, n.image_ext);
        }
        p.Children.Add(Body(n.Body));
        return card;
    }

    static async Task LoadImage(Image img, string id, string? ext)
    {
        try
        {
            var bytes = await Api.NewsImage(id, ext);
            if (bytes.Length == 0) return;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = new MemoryStream(bytes);
            bi.EndInit();
            bi.Freeze();
            img.Source = bi;
        }
        catch { }
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
