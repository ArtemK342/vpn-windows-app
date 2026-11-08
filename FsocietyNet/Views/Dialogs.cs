using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FsocietyNet.Services;

namespace FsocietyNet.Views;

/// <summary>In-window dialogs (overlay in MainWindow) — styled like the app instead of system message boxes.</summary>
public static class Dialogs
{
    static MainWindow W => MainWindow.Instance!;

    public static StackPanel Panel(string title, string? text = null)
    {
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = title, Style = S<TextBlock>("T.H2") });
        if (!string.IsNullOrEmpty(text))
            p.Children.Add(new TextBlock { Text = text, Style = S<TextBlock>("T.Muted"), Margin = new Thickness(0, 10, 0, 0) });
        return p;
    }

    public static Style S<T>(string key) => (Style)Application.Current.FindResource(key);

    public static Button Btn(string text, string style, RoutedEventHandler click, double minWidth = 96)
    {
        var b = new Button { Content = text, Style = S<Button>(style), MinWidth = minWidth };
        b.Click += click;
        return b;
    }

    public static StackPanel Buttons(params Button[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        for (int i = 0; i < buttons.Length; i++)
        {
            if (i > 0) buttons[i].Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(buttons[i]);
        }
        return row;
    }

    public static Task<bool> Confirm(string title, string text, string ok, bool danger = false, string? cancel = null)
    {
        var p = Panel(title, text);
        p.Children.Add(Buttons(
            Btn(cancel ?? Loc.Get("common.cancel"), "Btn.Ghost", (_, _) => W.CloseDialog(false)),
            Btn(ok, danger ? "Btn.Danger" : "Btn.Primary", (_, _) => W.CloseDialog(true))));
        return W.ShowDialog(p);
    }

    public static Task Info(string title, string text, string? ok = null)
    {
        var p = Panel(title, text);
        p.Children.Add(Buttons(Btn(ok ?? Loc.Get("common.ok"), "Btn.Primary", (_, _) => W.CloseDialog(true))));
        return W.ShowDialog(p);
    }

    public static async Task<string?> Prompt(string title, string? text, string placeholder, string ok, int maxLength = 200)
    {
        var p = Panel(title, text);
        var box = new TextBox { Style = S<TextBox>("Input"), Tag = placeholder, MaxLength = maxLength, Margin = new Thickness(0, 16, 0, 0) };
        p.Children.Add(box);
        var okBtn = Btn(ok, "Btn.Primary", (_, _) => { if (box.Text.Trim().Length > 0) W.CloseDialog(true); });
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter && box.Text.Trim().Length > 0) W.CloseDialog(true); };
        p.Children.Add(Buttons(Btn(Loc.Get("common.cancel"), "Btn.Ghost", (_, _) => W.CloseDialog(false)), okBtn));
        var task = W.ShowDialog(p);
        box.Dispatcher.BeginInvoke(() => box.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        return await task ? box.Text.Trim() : null;
    }

    public static void Toast(string text) => W.ShowToast(text);

    public static void Copy(string text, string? toast = null)
    {
        for (int i = 0; i < 5; i++)
        {
            try { Clipboard.SetDataObject(text, true); break; }
            catch { Thread.Sleep(60); }   // clipboard briefly locked by another program
        }
        Toast(toast ?? Loc.Get("common.copied"));
    }
}
