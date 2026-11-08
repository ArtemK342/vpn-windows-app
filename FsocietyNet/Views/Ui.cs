using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FsocietyNet.Services;

namespace FsocietyNet.Views;

/// <summary>Attached properties used by the styles in Themes/Styles.xaml.</summary>
public static class Ui
{
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached(
        "Radius", typeof(CornerRadius), typeof(Ui), new FrameworkPropertyMetadata(new CornerRadius(10)));
    public static CornerRadius GetRadius(DependencyObject o) => (CornerRadius)o.GetValue(RadiusProperty);
    public static void SetRadius(DependencyObject o, CornerRadius v) => o.SetValue(RadiusProperty, v);

    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(Geometry), typeof(Ui), new FrameworkPropertyMetadata(null));
    public static Geometry? GetIcon(DependencyObject o) => (Geometry?)o.GetValue(IconProperty);
    public static void SetIcon(DependencyObject o, Geometry? v) => o.SetValue(IconProperty, v);

    public static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
    public static Geometry Geo(string key) => (Geometry)Application.Current.FindResource(key);
}

/// <summary>Localised text in XAML: Text="{v:T home.connect}" (Services/Strings.cs).</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class T : MarkupExtension
{
    public string Key { get; set; }
    public T(string key) => Key = key;
    public override object ProvideValue(IServiceProvider sp) => Loc.Get(Key);
}

/// <summary>
/// Line icon: a 24×24 geometry (Themes/Icons.xaml) drawn as a vector at the element's real size and DPI.
/// Colour = Brush, or the inherited text colour (so icons inside buttons follow the button's Foreground).
/// </summary>
public sealed class Icon : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(nameof(Data), typeof(Geometry),
        typeof(Icon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(nameof(Size), typeof(double),
        typeof(Icon), new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(nameof(Brush), typeof(Brush),
        typeof(Icon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FilledProperty = DependencyProperty.Register(nameof(Filled), typeof(bool),
        typeof(Icon), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(double),
        typeof(Icon), new FrameworkPropertyMetadata(1.8, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(Icon),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public Geometry? Data { get => (Geometry?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public Brush? Brush { get => (Brush?)GetValue(BrushProperty); set => SetValue(BrushProperty, value); }
    public bool Filled { get => (bool)GetValue(FilledProperty); set => SetValue(FilledProperty, value); }
    public double Stroke { get => (double)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size _) => new(Size, Size);

    protected override void OnRender(DrawingContext dc)
    {
        if (Data == null) return;
        var brush = Brush ?? Foreground;
        var k = Size / 24.0;
        dc.PushTransform(new ScaleTransform(k, k));
        var pen = new Pen(brush, Stroke)
        {
            StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
        };
        dc.DrawGeometry(Filled ? brush : null, pen, Data);
        dc.Pop();
    }
}

/// <summary>Country flag drawn in code (vector, rounded). Unknown country → globe outline.</summary>
public sealed class Flag : FrameworkElement
{
    public static readonly DependencyProperty CountryProperty = DependencyProperty.Register(nameof(Country), typeof(string),
        typeof(Flag), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Country { get => (string)GetValue(CountryProperty); set => SetValue(CountryProperty, value); }

    public Flag() { Width = 24; Height = 16; SnapsToDevicePixels = true; }

    static SolidColorBrush B(uint rgb) { var b = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)); b.Freeze(); return b; }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var box = new Rect(0, 0, w, h);
        var r = Math.Min(w, h) * 0.18;
        var clip = new RectangleGeometry(box, r, r);
        void Rows(params uint[] c) { for (int i = 0; i < c.Length; i++) dc.DrawRectangle(B(c[i]), null, new Rect(0, h * i / c.Length, w, h / c.Length + 0.5)); }
        void Cols(params uint[] c) { for (int i = 0; i < c.Length; i++) dc.DrawRectangle(B(c[i]), null, new Rect(w * i / c.Length, 0, w / c.Length + 0.5, h)); }
        void Nordic(uint bg, uint cross, uint? inner = null)
        {
            dc.DrawRectangle(B(bg), null, box);
            dc.DrawRectangle(B(cross), null, new Rect(w * 0.28, 0, w * 0.18, h));
            dc.DrawRectangle(B(cross), null, new Rect(0, h * 0.37, w, h * 0.26));
            if (inner is uint i2)
            {
                dc.DrawRectangle(B(i2), null, new Rect(w * 0.32, 0, w * 0.10, h));
                dc.DrawRectangle(B(i2), null, new Rect(0, h * 0.43, w, h * 0.14));
            }
        }

        dc.PushClip(clip);
        switch (CountryCode(Country))
        {
            case "FI": Nordic(0xFFFFFF, 0x003580); break;
            case "SE": Nordic(0x006AA7, 0xFECC00); break;
            case "NO": Nordic(0xBA0C2F, 0xFFFFFF, 0x00205B); break;
            case "DK": Nordic(0xC8102E, 0xFFFFFF); break;
            case "DE": Rows(0x000000, 0xDD0000, 0xFFCE00); break;
            case "NL": Rows(0xAE1C28, 0xFFFFFF, 0x21468B); break;
            case "RU": Rows(0xFFFFFF, 0x0039A6, 0xD52B1E); break;
            case "EE": Rows(0x0072CE, 0x000000, 0xFFFFFF); break;
            case "LT": Rows(0xFDB913, 0x006A44, 0xC1272D); break;
            case "AT": Rows(0xED2939, 0xFFFFFF, 0xED2939); break;
            case "PL": Rows(0xFFFFFF, 0xDC143C); break;
            case "UA": Rows(0x0057B7, 0xFFD700); break;
            case "LV":
                dc.DrawRectangle(B(0x9E3039), null, box);
                dc.DrawRectangle(B(0xFFFFFF), null, new Rect(0, h * 0.4, w, h * 0.2));
                break;
            case "FR": Cols(0x0055A4, 0xFFFFFF, 0xEF4135); break;
            case "IT": Cols(0x009246, 0xFFFFFF, 0xCE2B37); break;
            case "BE": Cols(0x000000, 0xFDDA24, 0xEF3340); break;
            case "IE": Cols(0x169B62, 0xFFFFFF, 0xFF883E); break;
            case "CH":
                dc.DrawRectangle(B(0xDA291C), null, box);
                double c = h * 0.6, t = h * 0.2;
                dc.DrawRectangle(Brushes.White, null, new Rect((w - t) / 2, (h - c) / 2, t, c));
                dc.DrawRectangle(Brushes.White, null, new Rect((w - c) / 2, (h - t) / 2, c, t));
                break;
            case "JP":
                dc.DrawRectangle(Brushes.White, null, box);
                dc.DrawEllipse(B(0xBC002D), null, new Point(w / 2, h / 2), h * 0.3, h * 0.3);
                break;
            case "TR":
                dc.DrawRectangle(B(0xE30A17), null, box);
                dc.DrawEllipse(Brushes.White, null, new Point(w * 0.40, h / 2), h * 0.25, h * 0.25);
                dc.DrawEllipse(B(0xE30A17), null, new Point(w * 0.45, h / 2), h * 0.2, h * 0.2);
                break;
            default:
                dc.Pop();
                var muted = (Brush)FindResource("MutedBrush");
                var k = Math.Min(w, h) / 24.0;
                dc.PushTransform(new TranslateTransform((w - 24 * k) / 2, (h - 24 * k) / 2));
                dc.PushTransform(new ScaleTransform(k, k));
                dc.DrawGeometry(null, new Pen(muted, 1.6), (Geometry)FindResource("I.Globe"));
                dc.Pop(); dc.Pop();
                return;
        }
        dc.Pop();
        dc.DrawGeometry(null, new Pen((Brush)FindResource("FlagEdgeBrush"), 1), new RectangleGeometry(new Rect(0.5, 0.5, w - 1, h - 1), r, r));
    }

    public static string CountryCode(string? country) => (country ?? "").Trim().ToLowerInvariant() switch
    {
        "finland" or "fi" => "FI", "germany" or "de" => "DE", "netherlands" or "nl" => "NL",
        "switzerland" or "ch" => "CH", "russia" or "ru" => "RU", "sweden" or "se" => "SE",
        "norway" or "no" => "NO", "denmark" or "dk" => "DK", "estonia" or "ee" => "EE",
        "latvia" or "lv" => "LV", "lithuania" or "lt" => "LT", "austria" or "at" => "AT",
        "poland" or "pl" => "PL", "ukraine" or "ua" => "UA", "france" or "fr" => "FR",
        "italy" or "it" => "IT", "belgium" or "be" => "BE", "ireland" or "ie" => "IE",
        "japan" or "jp" => "JP", "turkey" or "türkiye" or "tr" => "TR",
        _ => ""
    };
}

public enum BrandState { Normal, Connected, Off, Busy, Blocked }

/// <summary>
/// The «f» mark: dark rounded square with a lime «f». One vector source for the window icon, app.ico
/// (all sizes 16…256) and the tray icons, so every size is drawn crisp instead of scaling one bitmap.
/// </summary>
public static class BrandMark
{
    static Geometry? _glyph;

    static Geometry Glyph()
    {
        if (_glyph != null) return _glyph;
        var ft = new FormattedText("f", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Bahnschrift"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            100, Brushes.White, 1.0);
        var g = ft.BuildGeometry(new Point(0, 0));
        g.Freeze();
        return _glyph = g;
    }

    public static void Draw(DrawingContext dc, double size, BrandState state)
    {
        var lime = Color.FromRgb(0xC8, 0xF1, 0x35);
        var dark = Color.FromRgb(0x0E, 0x0E, 0x10);
        Color bg = state == BrandState.Connected ? lime : dark;
        Color fg = state switch
        {
            BrandState.Connected => dark,
            BrandState.Off => Color.FromRgb(0x9A, 0x9A, 0xA2),
            BrandState.Blocked => Color.FromRgb(0xFF, 0x5A, 0x5A),
            _ => lime
        };
        var r = size * 0.24;
        var rect = new Rect(0, 0, size, size);
        dc.DrawRoundedRectangle(new SolidColorBrush(bg), state == BrandState.Connected ? null :
            new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), Math.Max(1, size / 32)),
            new Rect(rect.X + size / 64, rect.Y + size / 64, size - size / 32, size - size / 32), r, r);

        var glyph = Glyph();
        var b = glyph.Bounds;
        var target = size * (size <= 20 ? 0.74 : 0.66);   // a bigger glyph at tray sizes reads better
        var k = target / b.Height;
        var tg = new TransformGroup();
        tg.Children.Add(new TranslateTransform(-b.X - b.Width / 2, -b.Y - b.Height / 2));
        tg.Children.Add(new ScaleTransform(k, k));
        tg.Children.Add(new TranslateTransform(size / 2 + size * 0.02, size / 2));
        dc.PushTransform(tg);
        // Outline in the same colour thickens the glyph a little: reads better at 16–24 px
        var fgBrush = new SolidColorBrush(fg);
        dc.DrawGeometry(fgBrush, new Pen(fgBrush, 3.2 / k * size / 100) { LineJoin = PenLineJoin.Round }, glyph);
        dc.Pop();

        if (state == BrandState.Busy)
        {
            var d = size * 0.30;
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xB5, 0x47)), new Pen(new SolidColorBrush(dark), size / 16),
                new Point(size - d / 2 - size * 0.04, size - d / 2 - size * 0.04), d / 2, d / 2);
        }
    }

    public static BitmapSource Render(int px, BrandState state)
    {
        var dv = new DrawingVisual();
        RenderOptions.SetEdgeMode(dv, EdgeMode.Unspecified);
        using (var dc = dv.RenderOpen()) Draw(dc, px, state);
        var bmp = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Multi-size .ico (PNG entries) for the exe / window / taskbar.</summary>
    public static void WriteIco(string path, BrandState state, params int[] sizes)
    {
        var pngs = sizes.Select(s =>
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(Render(s, state)));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return (s, ms.ToArray());
        }).ToList();
        using var f = File.Create(path);
        using var w = new BinaryWriter(f);
        w.Write((short)0); w.Write((short)1); w.Write((short)pngs.Count);
        var offset = 6 + 16 * pngs.Count;
        foreach (var (s, data) in pngs)
        {
            w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(data.Length); w.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in pngs) w.Write(data);
    }
}

/// <summary>Brand mark as a UI element (sidebar, login).</summary>
public sealed class Brand : FrameworkElement
{
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(nameof(Size), typeof(double),
        typeof(Brand), new FrameworkPropertyMetadata(28.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    protected override System.Windows.Size MeasureOverride(System.Windows.Size _) => new(Size, Size);
    protected override void OnRender(DrawingContext dc) => BrandMark.Draw(dc, Size, BrandState.Normal);
}
