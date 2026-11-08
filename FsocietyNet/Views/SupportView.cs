using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FsocietyNet.Services;

namespace FsocietyNet.Views;

/// <summary>Support tickets: list (unread replies marked) + conversation; create / reply / close / delete.</summary>
public sealed class SupportView : UserControl
{
    readonly StackPanel _tickets = new();
    readonly StackPanel _messages = new();
    readonly ScrollViewer _msgScroll;
    readonly TextBlock _subject, _status, _listState;
    readonly TextBox _input;
    readonly Button _send, _close, _delete;
    readonly Grid _chat;
    readonly TextBlock _chatEmpty;
    TicketResponse? _current;
    string? _supportReadAt;
    int _shownCount;
    readonly System.Windows.Threading.DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(15) };

    static Style S(string k) => (Style)Application.Current.FindResource(k);

    public SupportView()
    {
        var root = new Grid { Margin = new Thickness(32, 44, 32, 24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        var head = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = Loc.Get("support.section"), Style = S("T.Label") });
        titles.Children.Add(new TextBlock { Text = Loc.Get("support.title"), Style = S("T.H1"), Margin = new Thickness(0, 8, 0, 0) });
        head.Children.Add(titles);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        var tg = Dialogs.Btn(Loc.Get("support.telegram"), "Btn.Ghost", (_, _) => Updates.Open("https://t.me/reich_kanzlei"));
        actions.Children.Add(tg);
        var create = Dialogs.Btn(Loc.Get("support.new"), "Btn.Primary", async (_, _) => await Create());
        create.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(create);
        head.Children.Add(actions);
        root.Children.Add(head);

        var body = new Grid();
        Grid.SetRow(body, 1);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        root.Children.Add(body);

        // Ticket list
        var listCard = new Border { Style = S("Card"), Padding = new Thickness(6) };
        var listGrid = new Grid();
        _listState = new TextBlock { Style = S("T.Muted"), Margin = new Thickness(14), Text = Loc.Get("common.loading") };
        listGrid.Children.Add(_listState);
        listGrid.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _tickets });
        listCard.Child = listGrid;
        body.Children.Add(listCard);

        // Conversation
        var chatCard = new Border { Style = S("Card") };
        Grid.SetColumn(chatCard, 2);
        body.Children.Add(chatCard);
        var chatHost = new Grid();
        chatCard.Child = chatHost;
        _chatEmpty = new TextBlock
        {
            Text = Loc.Get("support.pick"), Style = S("T.Muted"), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center
        };
        chatHost.Children.Add(_chatEmpty);
        _chat = new Grid { Visibility = Visibility.Collapsed };
        chatHost.Children.Add(_chat);
        _chat.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _chat.RowDefinitions.Add(new RowDefinition());
        _chat.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var chatHead = new Grid { Margin = new Thickness(20, 16, 14, 12) };
        var ch = new StackPanel { Margin = new Thickness(0, 0, 200, 0) };
        _subject = new TextBlock { Style = S("T.H3"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        _status = new TextBlock { Style = S("T.Caps"), Margin = new Thickness(0, 4, 0, 0) };
        ch.Children.Add(_subject);
        ch.Children.Add(_status);
        chatHead.Children.Add(ch);
        var chActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _close = Dialogs.Btn(Loc.Get("tickets.close"), "Btn.Secondary", async (_, _) => await CloseTicket(), 80);
        _close.Height = 32;
        _delete = new Button { Style = S("Btn.Icon"), Margin = new Thickness(6, 0, 0, 0), ToolTip = Loc.Get("tickets.delete"),
            Content = new Icon { Data = Ui.Geo("I.Trash"), Size = 17 } };
        _delete.Click += async (_, _) => await DeleteTicket();
        chActions.Children.Add(_close);
        chActions.Children.Add(_delete);
        chatHead.Children.Add(chActions);
        _chat.Children.Add(chatHead);

        _msgScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _messages, Padding = new Thickness(16, 0, 16, 8) };
        Grid.SetRow(_msgScroll, 1);
        _chat.Children.Add(_msgScroll);

        var inputRow = new Grid { Margin = new Thickness(16, 6, 16, 16) };
        Grid.SetRow(inputRow, 2);
        inputRow.ColumnDefinitions.Add(new ColumnDefinition());
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _input = new TextBox { Style = S("Input.Multi"), Tag = Loc.Get("tickets.message_hint"), MaxLength = 4000 };
        _input.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { e.Handled = true; await Send(); }
        };
        inputRow.Children.Add(_input);
        _send = new Button { Style = S("Btn.Primary"), Width = 44, Height = 42, Padding = new Thickness(0), Margin = new Thickness(8, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Content = new Icon { Data = Ui.Geo("I.Send"), Size = 18 } };
        _send.Click += async (_, _) => await Send();
        Grid.SetColumn(_send, 1);
        inputRow.Children.Add(_send);
        _chat.Children.Add(inputRow);

        Content = root;
        _ = LoadList();
        // Open ticket: new replies and «read by support» marks arrive without reopening it
        _poll.Tick += async (_, _) => await Poll();
        var shown = false;
        Loaded += (_, _) => { _poll.Start(); if (shown) _ = LoadList(); shown = true; };
        Unloaded += (_, _) => _poll.Stop();
    }

    static string When(string iso)
    {
        if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)) return "";
        d = d.ToLocalTime();
        return d.Date == DateTime.Today ? d.ToString("HH:mm") : d.ToString(Loc.En ? "MMM d, HH:mm" : "d MMM, HH:mm", new CultureInfo(Loc.En ? "en-US" : "ru-RU"));
    }

    /// <summary>`-render`: show the first ticket's conversation.</summary>
    internal async Task OpenFirstForPreview() { var l = await Api.Tickets(); if (l.Count > 0) await Open(l[0]); }

    async Task LoadList(string? select = null)
    {
        try
        {
            var list = await Api.Tickets();
            _tickets.Children.Clear();
            _listState.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _listState.Text = Loc.Get("tickets.empty");
            foreach (var t in list.OrderByDescending(t => t.status == "open").ThenByDescending(t => t.updated_at))
                _tickets.Children.Add(Row(t));
            var target = list.FirstOrDefault(t => t.id == (select ?? _current?.id));
            if (target != null) await Open(target);
            _ = MainWindow.Instance?.RefreshUnread();
        }
        catch (Exception e) { _listState.Text = Errors.Text(e); _listState.Visibility = Visibility.Visible; }
    }

    FrameworkElement Row(TicketResponse t)
    {
        var b = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(2), Cursor = Cursors.Hand, Tag = t };
        b.Background = System.Windows.Media.Brushes.Transparent;
        b.MouseEnter += (_, _) => { if (_current?.id != t.id) b.Background = Ui.Res("HoverBrush"); };
        b.MouseLeave += (_, _) => { if (_current?.id != t.id) b.Background = System.Windows.Media.Brushes.Transparent; };
        b.MouseLeftButtonUp += async (_, _) => await Open(t);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = t.subject, FontWeight = FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
        var meta = new TextBlock { Style = S("T.Caps"), Margin = new Thickness(0, 4, 0, 0) };
        meta.Inlines.Add(new System.Windows.Documents.Run(Loc.Get(t.status == "open" ? "tickets.open" : "tickets.closed"))
        { Foreground = Ui.Res(t.status == "open" ? "AccentTextBrush" : "FaintBrush") });
        meta.Inlines.Add(new System.Windows.Documents.Run("  " + When(t.updated_at)));
        p.Children.Add(meta);
        g.Children.Add(p);
        if (t.unread > 0)
        {
            var badge = new Border { Background = Ui.Res("AccentBrush"), CornerRadius = new CornerRadius(9), MinWidth = 18, Height = 18, Padding = new Thickness(5, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = t.unread.ToString(), FontSize = 10.5, FontWeight = FontWeights.Bold,
                    Foreground = Ui.Res("OnAccentBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            Grid.SetColumn(badge, 1);
            g.Children.Add(badge);
        }
        b.Child = g;
        return b;
    }

    async Task Open(TicketResponse t)
    {
        _current = t;
        foreach (Border r in _tickets.Children)
            r.Background = ((TicketResponse)r.Tag).id == t.id ? Ui.Res("AccentSoftBrush") : System.Windows.Media.Brushes.Transparent;
        _chatEmpty.Visibility = Visibility.Collapsed;
        _chat.Visibility = Visibility.Visible;
        _subject.Text = t.subject;
        SetStatus(t.status);
        _messages.Children.Clear();
        try
        {
            var d = await Api.Ticket(t.id);
            if (_current?.id != t.id) return;
            SetStatus(d.ticket.status);
            _supportReadAt = d.ticket.support_read_at;
            _shownCount = d.messages.Count;
            if (d.messages.Count == 0)
                _messages.Children.Add(new TextBlock { Text = Loc.Get("tickets.no_messages"), Style = S("T.Muted"), Margin = new Thickness(4, 8, 0, 0) });
            foreach (var m in d.messages) _messages.Children.Add(Bubble(m));
            _msgScroll.ScrollToEnd();
            if (t.unread > 0) { t.unread = 0; _ = MainWindow.Instance?.RefreshUnread(); RefreshRow(t); }
        }
        catch (Exception e)
        {
            Log.E("ticket open", e);
#if PREVIEW
            _messages.Children.Add(new TextBlock { Text = e.ToString(), Style = S("T.Note") });
#else
            _messages.Children.Add(new TextBlock { Text = Errors.Text(e), Style = S("T.Muted") });
#endif
        }
    }

    void RefreshRow(TicketResponse t)
    {
        for (int i = 0; i < _tickets.Children.Count; i++)
            if (((Border)_tickets.Children[i]).Tag is TicketResponse x && x.id == t.id)
            {
                var row = (Border)Row(t);
                row.Background = Ui.Res("AccentSoftBrush");
                _tickets.Children.RemoveAt(i);
                _tickets.Children.Insert(i, row);
                break;
            }
    }

    void SetStatus(string status)
    {
        var open = status == "open";
        _status.Text = Loc.Get(open ? "tickets.open" : "tickets.closed");
        _status.Foreground = Ui.Res(open ? "AccentTextBrush" : "FaintBrush");
        _close.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        _input.IsEnabled = _send.IsEnabled = open;
        _input.Tag = Loc.Get(open ? "tickets.message_hint" : "tickets.closed_hint");
    }

    async Task Poll()
    {
        if (_current is not { } t) return;
        try
        {
            var d = await Api.Ticket(t.id);
            if (_current?.id != t.id) return;
            if (d.messages.Count == _shownCount && d.ticket.support_read_at == _supportReadAt) return;
            var atEnd = _msgScroll.VerticalOffset >= _msgScroll.ScrollableHeight - 4;
            _supportReadAt = d.ticket.support_read_at;
            _shownCount = d.messages.Count;
            SetStatus(d.ticket.status);
            _messages.Children.Clear();
            foreach (var m in d.messages) _messages.Children.Add(Bubble(m));
            if (atEnd) _msgScroll.ScrollToEnd();
        }
        catch { }
    }

    static DateTime? Parse(string? v) =>
        DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>Support opened the ticket after this message was written (tickets.support_read_at, migration 019).</summary>
    bool ReadBySupport(TicketMessage m) => Parse(m.created_at) is { } c && Parse(_supportReadAt) is { } r && c <= r;

    FrameworkElement Bubble(TicketMessage m, bool pending = false)
    {
        var mine = m.sender == "user";
        var b = new Border
        {
            CornerRadius = new CornerRadius(14, 14, mine ? 4 : 14, mine ? 14 : 4),
            Background = Ui.Res(mine ? "AccentSoftBrush" : "Surface2Brush"),
            Padding = new Thickness(14, 10, 14, 8), Margin = new Thickness(mine ? 60 : 0, 6, mine ? 0 : 60, 0),
            HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left, MaxWidth = 520,
        };
        var p = new StackPanel();
        if (!mine) p.Children.Add(new TextBlock { Text = Loc.Get("tickets.support"), Style = S("T.Label"), Margin = new Thickness(0, 0, 0, 4) });
        var text = NewsView.Body(m.message);
        text.FontSize = 13;
        p.Children.Add(text);
        var meta = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
        meta.Children.Add(new TextBlock { Text = pending ? "" : When(m.created_at), Style = S("T.Caps"), VerticalAlignment = VerticalAlignment.Center });
        if (mine)
        {
            // clock = sending · ✓ = delivered (saved on the server) · ✓✓ = read by support
            var read = !pending && ReadBySupport(m);
            meta.Children.Add(new Icon
            {
                Data = Ui.Geo(pending ? "I.Clock" : read ? "I.Check2" : "I.Check"), Size = 15, Stroke = 2,
                Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Brush = Ui.Res(read ? "AccentTextBrush" : "FaintBrush"),
                ToolTip = Loc.Get(pending ? "tickets.sending" : read ? "tickets.read" : "tickets.delivered"),
            });
        }
        p.Children.Add(meta);
        b.Child = p;
        return b;
    }

    async Task Send()
    {
        var text = _input.Text.Trim();
        if (text.Length == 0 || _current == null) return;
        _send.IsEnabled = false;
        if (_messages.Children.Count == 1 && _messages.Children[0] is TextBlock) _messages.Children.Clear();
        var pending = Bubble(new TicketMessage { sender = "user", message = text }, pending: true);
        _messages.Children.Add(pending);
        _msgScroll.ScrollToEnd();
        _input.Text = "";
        try
        {
            var m = await Api.SendMessage(_current.id, text);
            var i = _messages.Children.IndexOf(pending);
            if (i >= 0) { _messages.Children.RemoveAt(i); _messages.Children.Insert(i, Bubble(m)); }
            _shownCount++;
        }
        catch (Exception e)
        {
            _messages.Children.Remove(pending);
            _input.Text = text;   // not lost: can be sent again
            Dialogs.Toast(Errors.Text(e));
        }
        finally { _send.IsEnabled = _current?.status != "closed"; }
    }

    async Task Create()
    {
        var subject = await Dialogs.Prompt(Loc.Get("tickets.new_title"), null, Loc.Get("tickets.new_subject"), Loc.Get("tickets.create"), 120);
        if (subject == null) return;
        try
        {
            var t = await Api.CreateTicket(subject);
            await LoadList(t.id);
            _input.Focus();
        }
        catch (ApiException e) when (e.Status == 429) { await Dialogs.Info(Loc.Get("tickets.limit_title"), Loc.Get("tickets.limit_msg")); }
        catch (Exception e) { Dialogs.Toast(Errors.Text(e)); }
    }

    async Task CloseTicket()
    {
        if (_current == null) return;
        try { await Api.CloseTicket(_current.id); _current.status = "closed"; await LoadList(_current.id); }
        catch (Exception e) { Dialogs.Toast(Errors.Text(e)); }
    }

    async Task DeleteTicket()
    {
        if (_current == null) return;
        if (!await Dialogs.Confirm(Loc.Get("tickets.delete_title"), Loc.F("tickets.delete_msg", _current.subject), Loc.Get("tickets.delete_confirm"), danger: true)) return;
        try
        {
            await Api.DeleteTicket(_current.id);
            _current = null;
            _chat.Visibility = Visibility.Collapsed;
            _chatEmpty.Visibility = Visibility.Visible;
            await LoadList();
        }
        catch (Exception e) { Dialogs.Toast(Errors.Text(e)); }
    }
}
