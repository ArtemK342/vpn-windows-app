using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using FsocietyNet.Services;

namespace FsocietyNet.Controls;

public partial class LoginControl : UserControl
{
    private readonly ApiService _api = new();

    public LoginControl() => InitializeComponent();

    private async void LoginBtn_Click(object sender, RoutedEventArgs e)
    {
        var email    = EmailBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            ShowError("Заполните все поля");
            return;
        }

        LoginBtn.IsEnabled = false;
        LoginBtn.Content   = "Вход...";
        ErrorText.Visibility = Visibility.Collapsed;

        try
        {
            var resp = await _api.LoginAsync(email, password);
            MainWindow.SaveToken(resp.access_token);
            MainWindow.Navigate(new AppShell(resp.access_token));
        }
        catch (Exception ex) when (ex.Message.Contains("401") || ex.Message.Contains("Unauthorized"))
        {
            ShowError("Неверный email или пароль");
        }
        catch
        {
            ShowError("Ошибка сети, попробуйте ещё раз");
        }
        finally
        {
            LoginBtn.IsEnabled = true;
            LoginBtn.Content   = "ВОЙТИ →";
        }
    }

    private void RegisterBtn_Click(object sender, RoutedEventArgs e) =>
        MainWindow.Navigate(new RegisterControl());

    private void TelegramLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://t.me/reich_kanzlei") { UseShellExecute = true });

    private void ShowError(string msg)
    {
        ErrorText.Text       = msg;
        ErrorText.Visibility = Visibility.Visible;
    }
}
