using System.IO;
using System.Windows;
using System.Windows.Controls;
using FsocietyNet.Services;

namespace FsocietyNet.Views;

/// <summary>
/// «For another app» (right click on a server): a line for AmneziaVPN or the VLESS line + subscription link for
/// Happ / v2rayNG / Hiddify — same as the Android dialog and the «Other app» tab of the site dashboard.
/// Subscribers and staff only (the server decides: `entitled`).
/// </summary>
public static class ExternalDialog
{
    public static async Task<ExternalOverview?> Show(ServerResponse server, ExternalOverview ext)
    {
        var w = MainWindow.Instance!;
        var p = Dialogs.Panel(server.DisplayName);
        p.Children.Insert(0, new TextBlock { Text = Loc.Get("ext.title"), Style = Dialogs.S<TextBlock>("T.Label"), Margin = new Thickness(0, 0, 0, 6) });
        var body = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        p.Children.Add(body);
        var close = Dialogs.Btn(Loc.Get("common.close"), "Btn.Ghost", (_, _) => w.CloseDialog(false));
        var buttons = Dialogs.Buttons(close);
        p.Children.Add(buttons);
        ExternalOverview? updated = null;

        void Text(string t, string style = "T.Muted", double top = 0) =>
            body.Children.Add(new TextBlock { Text = t, Style = Dialogs.S<TextBlock>(style), Margin = new Thickness(0, top, 0, 0) });

        void Secret(string label, string value, Action? save = null)
        {
            body.Children.Add(new TextBlock { Text = label, Style = Dialogs.S<TextBlock>("T.Caps"), Margin = new Thickness(0, 14, 0, 6) });
            var box = new TextBox
            {
                Style = Dialogs.S<TextBox>("Input.Multi"), Text = value, IsReadOnly = true, MaxHeight = 92,
                FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("MonoFont"), FontSize = 11.5,
            };
            body.Children.Add(box);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(Dialogs.Btn(Loc.Get("ext.copy"), "Btn.Secondary", (_, _) => Dialogs.Copy(value)));
            if (save != null)
            {
                var b = Dialogs.Btn(Loc.Get("ext.save_conf"), "Btn.Secondary", (_, _) => save());
                b.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(b);
            }
            body.Children.Add(row);
        }

        void Slots(ExternalOverview o) => Text(Loc.F("ext.slots", o.slots.used, o.slots.max), "T.Note", 10);

        void Render(ExternalOverview o)
        {
            body.Children.Clear();
            if (!o.entitled) { Text(Loc.Get("ext.need_sub")); return; }
            if (o.IsAwg(server.id))
            {
                Text(Loc.Get("ext.awg_desc"));
                Slots(o);
                var get = Dialogs.Btn(Loc.Get("ext.awg_get"), "Btn.Primary", async (s, _) =>
                {
                    ((Button)s).IsEnabled = false;
                    try
                    {
                        var r = await Api.CreateExternalAwg(server.id);
                        body.Children.Clear();
                        Text(Loc.Get("ext.awg_ready"), "T.Muted");
                        Secret(Loc.Get("ext.awg_line"), r.amnezia_key, () =>
                        {
                            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = string.IsNullOrEmpty(r.filename) ? "fsociety.conf" : r.filename, Filter = "WireGuard|*.conf" };
                            if (dlg.ShowDialog() == true) { File.WriteAllText(dlg.FileName, r.config); Dialogs.Toast(Loc.Get("common.saved")); }
                        });
                        Text(Loc.Get("ext.manage"), "T.Note", 12);
                        try { updated = await Api.External(); } catch { }
                    }
                    catch (Exception e) { ((Button)s).IsEnabled = true; Text(Vpn.Message(e), "T.Muted", 10); }
                });
                get.Margin = new Thickness(0, 14, 0, 0);
                get.HorizontalAlignment = HorizontalAlignment.Left;
                body.Children.Add(get);
            }
            else if (o.IsVless(server.id))
            {
                Text(Loc.Get("ext.vless_desc"));
                if (!string.IsNullOrEmpty(o.sub_url))
                {
                    if (o.vless_lines.TryGetValue(server.id, out var line)) Secret(Loc.Get("ext.vless_line"), line);
                    Secret(Loc.Get("ext.vless_sub"), o.sub_url!);
                    Text(Loc.Get("ext.manage"), "T.Note", 12);
                }
                else
                {
                    Slots(o);
                    var get = Dialogs.Btn(Loc.Get("ext.vless_get"), "Btn.Primary", async (s, _) =>
                    {
                        ((Button)s).IsEnabled = false;
                        try
                        {
                            await Api.CreateExternalSub();
                            updated = await Api.External();
                            Render(updated);
                        }
                        catch (Exception e) { ((Button)s).IsEnabled = true; Text(Vpn.Message(e), "T.Muted", 10); }
                    });
                    get.Margin = new Thickness(0, 14, 0, 0);
                    get.HorizontalAlignment = HorizontalAlignment.Left;
                    body.Children.Add(get);
                }
            }
        }

        Render(ext);
        await w.ShowDialog(p, true, 520);
        return updated;
    }
}
