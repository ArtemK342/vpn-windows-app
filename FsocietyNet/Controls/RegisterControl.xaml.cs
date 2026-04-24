using System.Windows;
using System.Windows.Controls;
using FsocietyNet.Services;

namespace FsocietyNet.Controls;

public partial class RegisterControl : UserControl
{
    private readonly ApiService _api = new();

    public RegisterControl() => InitializeComponent();

    private async void RegisterBtn_Click(object sender, RoutedEventArgs e)
    {
        var email = EmailBox.Text.Trim();
        var pw1   = PasswordBox.Password;
        var pw2   = Password2Box.Password;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(pw1) || string.IsNullOrEmpty(pw2))
        { ShowError("Заполните все поля"); return; }
        if (pw1 != pw2)
        { ShowError("Пароли не совпадают"); return; }
        if (pw1.Length < 8)
        { ShowError("Пароль минимум 8 символов"); return; }

        RegisterBtn.IsEnabled = false;
        RegisterBtn.Content   = "Регистрация...";
        ErrorText.Visibility  = Visibility.Collapsed;
        SuccessText.Visibility = Visibility.Collapsed;

        try
        {
            await _api.RegisterAsync(email, pw1);
            SuccessText.Text       = $"Письмо отправлено на {email} — подтвердите аккаунт";
            SuccessText.Visibility = Visibility.Visible;
        }
        catch
        {
            ShowError("Email уже зарегистрирован или ошибка сервера");
        }
        finally
        {
            RegisterBtn.IsEnabled = true;
            RegisterBtn.Content   = "ЗАРЕГИСТРИРОВАТЬСЯ →";
        }
    }

    private void BackLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        MainWindow.Navigate(new LoginControl());

    private void ShowError(string msg)
    {
        ErrorText.Text       = msg;
        ErrorText.Visibility = Visibility.Visible;
    }
}
