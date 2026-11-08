using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FsocietyNet.Services;

namespace FsocietyNet.Views;

/// <summary>
/// Password input: lock icon, placeholder inside, «show password» eye. Same look as the Input text fields;
/// API like PasswordBox (Password, Clear, Focus), Enter bubbles up as KeyDown.
/// </summary>
public sealed class PasswordField : UserControl
{
    readonly Border _bd;
    readonly PasswordBox _box;
    readonly TextBox _plain;
    readonly TextBlock _ph;
    readonly Icon _lock;
    readonly Icon _eye;
    bool _shown;

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(nameof(Placeholder), typeof(string),
        typeof(PasswordField), new PropertyMetadata("", (d, e) => ((PasswordField)d)._ph.Text = (string)e.NewValue));
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    static Style S(string k) => (Style)Application.Current.FindResource(k);

    public PasswordField()
    {
        Focusable = false;
        _bd = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Height = 42 };
        _bd.SetResourceReference(Border.BackgroundProperty, "Surface2Brush");
        _bd.SetResourceReference(Border.BorderBrushProperty, "LineBrush");

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _lock = new Icon { Data = Ui.Geo("I.Lock"), Size = 17, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _lock.SetResourceReference(Icon.BrushProperty, "FaintBrush");
        g.Children.Add(_lock);

        _ph = new TextBlock { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 13.5, IsHitTestVisible = false };
        _ph.SetResourceReference(TextBlock.ForegroundProperty, "FaintBrush");
        Grid.SetColumn(_ph, 1);
        g.Children.Add(_ph);

        _box = new PasswordBox { Style = S("BarePassword") };
        _box.PasswordChanged += (_, _) => UpdatePlaceholder();
        Grid.SetColumn(_box, 1);
        g.Children.Add(_box);

        _plain = new TextBox { Style = S("BareInput"), Visibility = Visibility.Collapsed };
        _plain.TextChanged += (_, _) => UpdatePlaceholder();
        Grid.SetColumn(_plain, 1);
        g.Children.Add(_plain);

        _eye = new Icon { Data = Ui.Geo("I.Eye"), Size = 17 };
        var eyeBtn = new Button { Style = S("Btn.Icon"), Width = 32, Height = 32, Margin = new Thickness(0, 0, 5, 0), Content = _eye, Focusable = false,
            ToolTip = Loc.Get("field.show_password") };
        eyeBtn.Click += (_, _) => Toggle();
        Grid.SetColumn(eyeBtn, 2);
        g.Children.Add(eyeBtn);

        _bd.Child = g;
        Content = _bd;

        IsKeyboardFocusWithinChanged += (_, _) => Highlight();
        MouseEnter += (_, _) => Highlight();
        MouseLeave += (_, _) => Highlight();
        MouseLeftButtonDown += (_, e) => { if (!IsKeyboardFocusWithin) { Focus(); e.Handled = true; } };
    }

    void Highlight()
    {
        _bd.SetResourceReference(Border.BorderBrushProperty,
            IsKeyboardFocusWithin ? "AccentBrush" : IsMouseOver ? "Line2Brush" : "LineBrush");
        _lock.SetResourceReference(Icon.BrushProperty, IsKeyboardFocusWithin ? "AccentTextBrush" : "FaintBrush");
    }

    void UpdatePlaceholder() => _ph.Visibility = Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    void Toggle()
    {
        var focused = IsKeyboardFocusWithin;
        _shown = !_shown;
        if (_shown) { _plain.Text = _box.Password; _plain.Visibility = Visibility.Visible; _box.Visibility = Visibility.Collapsed; }
        else { _box.Password = _plain.Text; _box.Visibility = Visibility.Visible; _plain.Visibility = Visibility.Collapsed; _plain.Clear(); }
        _eye.Data = Ui.Geo(_shown ? "I.EyeOff" : "I.Eye");
        if (focused) Focus();
    }

    public string Password
    {
        get => _shown ? _plain.Text : _box.Password;
        set { if (_shown) _plain.Text = value; else _box.Password = value; UpdatePlaceholder(); }
    }

    public void Clear() { _box.Clear(); _plain.Clear(); UpdatePlaceholder(); }

    public new void Focus()
    {
        if (_shown) { _plain.Focus(); _plain.CaretIndex = _plain.Text.Length; }
        else _box.Focus();
    }
}
