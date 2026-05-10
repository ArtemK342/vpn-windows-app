using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FsocietyNet.Models;
using FsocietyNet.Services;

namespace FsocietyNet.Controls;

public partial class SupportControl : UserControl
{
    private readonly string     _token;
    private readonly ApiService _api = new();
    private string?             _openTicketId;

    private Brush AccentBrush  => (Brush)Application.Current.Resources["AccentBrush"]!;
    private Brush MutedBrush   => (Brush)Application.Current.Resources["TextMutedBrush"]!;
    private Brush PrimaryBrush => (Brush)Application.Current.Resources["TextPrimaryBrush"]!;
    private Brush Bg2Brush     => (Brush)Application.Current.Resources["Bg2Brush"]!;
    private Brush BorderBrush2 => (Brush)Application.Current.Resources["BorderBrush2"]!;
    private Brush SurfaceBrush => (Brush)Application.Current.Resources["SurfaceBrush"]!;

    public SupportControl(string token)
    {
        _token = token;
        InitializeComponent();
        _ = LoadTicketsAsync();
    }

    // ──────────────────── Список тикетов ────────────────────

    private async Task LoadTicketsAsync()
    {
        ShowPanel(ListPanel);
        ListLoadingText.Visibility = Visibility.Visible;
        EmptyText.Visibility       = Visibility.Collapsed;
        TicketScroll.Visibility    = Visibility.Collapsed;
        TicketList.Children.Clear();

        try
        {
            var tickets = await _api.GetTicketsAsync(_token);
            ListLoadingText.Visibility = Visibility.Collapsed;

            if (tickets.Count == 0)
            {
                EmptyText.Visibility = Visibility.Visible;
                return;
            }

            TicketScroll.Visibility = Visibility.Visible;
            foreach (var t in tickets)
                TicketList.Children.Add(BuildTicketRow(t));
        }
        catch
        {
            ListLoadingText.Text = "Ошибка загрузки тикетов";
        }
    }

    private Border BuildTicketRow(TicketResponse t)
    {
        var isOpen  = t.status == "open";
        var dateStr = t.updated_at.Length >= 10 ? t.updated_at[..10] : t.updated_at;

        var row = new Border
        {
            BorderBrush     = BorderBrush2,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Cursor          = System.Windows.Input.Cursors.Hand,
            Background      = Brushes.Transparent
        };
        row.MouseEnter += (_, _) => row.Background = SurfaceBrush;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) => _ = OpenTicketAsync(t.id);

        var grid = new Grid { Margin = new Thickness(24, 14, 24, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        left.Children.Add(new TextBlock
        {
            Text       = t.subject,
            Foreground = PrimaryBrush,
            FontSize   = 13,
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        left.Children.Add(new TextBlock
        {
            Text       = $"#{t.id[..8]}  ·  обновлён {dateStr}",
            Foreground = MutedBrush,
            FontFamily = new FontFamily("Consolas"),
            FontSize   = 10,
            Margin     = new Thickness(0, 4, 0, 0)
        });

        var statusText = new TextBlock
        {
            Text       = isOpen ? "● ОТКРЫТ" : "○ ЗАКРЫТ",
            Foreground = isOpen ? AccentBrush : MutedBrush,
            FontFamily = new FontFamily("Consolas"),
            FontSize   = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin     = new Thickness(16, 0, 0, 0)
        };

        Grid.SetColumn(left,       0);
        Grid.SetColumn(statusText, 1);
        grid.Children.Add(left);
        grid.Children.Add(statusText);
        row.Child = grid;
        return row;
    }

    // ──────────────────── Создание тикета ────────────────────

    private void NewTicketBtn_Click(object sender, RoutedEventArgs e)
    {
        SubjectBox.Text = "";
        CreateErrorText.Visibility = Visibility.Collapsed;
        ShowPanel(CreatePanel);
        SubjectBox.Focus();
    }

    private async void SubmitTicketBtn_Click(object sender, RoutedEventArgs e)
    {
        var subject = SubjectBox.Text.Trim();
        if (subject.Length < 5)
        {
            CreateErrorText.Text       = "Тема должна быть не менее 5 символов";
            CreateErrorText.Visibility = Visibility.Visible;
            return;
        }

        SubmitTicketBtn.IsEnabled = false;
        SubmitTicketBtn.Content   = "Создание...";
        CreateErrorText.Visibility = Visibility.Collapsed;

        try
        {
            var ticket = await _api.CreateTicketAsync(_token, subject);
            await LoadTicketsAsync();
            await OpenTicketAsync(ticket.id);
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            CreateErrorText.Text = msg.Contains("3") ? "Достигнут лимит (3 открытых тикета)"
                                 : "Ошибка создания тикета";
            CreateErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            SubmitTicketBtn.IsEnabled = true;
            SubmitTicketBtn.Content   = "СОЗДАТЬ ТИКЕТ →";
        }
    }

    // ──────────────────── Детали тикета ────────────────────

    private async Task OpenTicketAsync(string ticketId)
    {
        _openTicketId = ticketId;
        ShowPanel(DetailPanel);
        DetailSubjectText.Text = "Загрузка...";
        MessagesList.Children.Clear();

        try
        {
            var detail = await _api.GetTicketAsync(_token, ticketId);
            var t = detail.ticket;

            DetailSubjectText.Text = t.subject;
            DetailStatusText.Text  = t.status == "open" ? "● ОТКРЫТ" : "○ ЗАКРЫТ";
            DetailStatusText.Foreground = t.status == "open" ? AccentBrush : MutedBrush;

            CloseTicketBtn.Visibility = t.status == "open" ? Visibility.Visible : Visibility.Collapsed;
            ReplyBox.IsEnabled        = t.status == "open";
            SendReplyBtn.IsEnabled    = t.status == "open";

            foreach (var m in detail.messages)
                MessagesList.Children.Add(BuildMessageBubble(m));

            // Прокрутить вниз
            MessagesScroll.UpdateLayout();
            MessagesScroll.ScrollToBottom();
        }
        catch
        {
            DetailSubjectText.Text = "Ошибка загрузки";
        }
    }

    private Border BuildMessageBubble(TicketMessageResponse m)
    {
        var isSupport = m.sender != "user";
        var dateStr   = m.created_at.Length >= 16 ? m.created_at[..16].Replace('T', ' ') : m.created_at;

        var bubble = new Border
        {
            Background      = isSupport ? Bg2Brush : SurfaceBrush,
            BorderBrush     = BorderBrush2,
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(14, 10, 14, 10),
            Margin          = new Thickness(isSupport ? 0 : 48, 0, isSupport ? 48 : 0, 10),
            MaxWidth        = 9999
        };

        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text       = isSupport ? "● ПОДДЕРЖКА" : "Вы",
            Foreground = isSupport ? AccentBrush : MutedBrush,
            FontFamily = new FontFamily("Consolas"),
            FontSize   = 10,
            Margin     = new Thickness(0, 0, 0, 6)
        });
        sp.Children.Add(new TextBlock
        {
            Text        = m.message,
            Foreground  = PrimaryBrush,
            FontSize    = 13,
            TextWrapping = TextWrapping.Wrap
        });
        sp.Children.Add(new TextBlock
        {
            Text       = dateStr,
            Foreground = MutedBrush,
            FontFamily = new FontFamily("Consolas"),
            FontSize   = 10,
            Margin     = new Thickness(0, 6, 0, 0)
        });

        bubble.Child = sp;
        return bubble;
    }

    private async void SendReplyBtn_Click(object sender, RoutedEventArgs e)
    {
        var msg = ReplyBox.Text.Trim();
        if (string.IsNullOrEmpty(msg) || _openTicketId == null) return;

        SendReplyBtn.IsEnabled = false;
        ReplyBox.IsEnabled     = false;

        try
        {
            var newMsg = await _api.AddTicketMessageAsync(_token, _openTicketId, msg);
            ReplyBox.Text = "";
            MessagesList.Children.Add(BuildMessageBubble(newMsg));
            MessagesScroll.UpdateLayout();
            MessagesScroll.ScrollToBottom();
        }
        catch
        {
            // silent fail — можно добавить UI-ошибку
        }
        finally
        {
            SendReplyBtn.IsEnabled = true;
            ReplyBox.IsEnabled     = true;
            ReplyBox.Focus();
        }
    }

    private async void CloseTicketBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_openTicketId == null) return;
        try
        {
            await _api.CloseTicketAsync(_token, _openTicketId);
            await OpenTicketAsync(_openTicketId); // обновить статус
        }
        catch { }
    }

    // ──────────────────── Навигация ────────────────────

    private void BackToList_Click(object sender, RoutedEventArgs e) =>
        _ = LoadTicketsAsync();

    private void ShowPanel(UIElement panel)
    {
        ListPanel.Visibility   = Visibility.Collapsed;
        CreatePanel.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Collapsed;
        panel.Visibility       = Visibility.Visible;
    }
}
