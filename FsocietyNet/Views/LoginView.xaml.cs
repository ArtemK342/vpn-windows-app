using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FsocietyNet.Services;

namespace FsocietyNet.Views;

public partial class LoginView : UserControl
{
    GenerateNumericResponse? _anon;
    bool _formatting;

    public LoginView()
    {
        InitializeComponent();
        LangBtn.Content = Loc.En ? "Русский" : "English";
        Loaded += (_, _) => Email.Focus();
    }

    public static string LegalUrl(string name) => $"{Api.Site}/legal/{name}{(Loc.En ? "_en" : "")}.html";

    void Show(StackPanel panel)
    {
        foreach (var p in new[] { PanelEmail, PanelCode, PanelRegister, PanelAnon, PanelAnonCode })
            p.Visibility = p == panel ? Visibility.Visible : Visibility.Collapsed;
        SetError(null);
        Info.Visibility = Visibility.Collapsed;
    }

    void SetError(string? text)
    {
        Error.Text = text ?? "";
        Error.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    void SetInfo(string text) { Info.Text = text; Info.Visibility = Visibility.Visible; }

    static async Task Enter(string access, string refresh)
    {
        Session.Set(access, refresh);
        try { await Api.Me(); } catch { }
        MainWindow.Instance?.ShowApp();
    }

    // ── Email ──

    async void Login_Click(object sender, RoutedEventArgs e)
    {
        var email = Email.Text.Trim();
        if (email.Length == 0 || Password.Password.Length == 0) { SetError(Loc.Get("login.error_empty")); return; }
        LoginBtn.IsEnabled = false;
        SetError(null);
        try
        {
            var r = await Api.Login(email, Password.Password);
            await Enter(r.access_token, r.refresh_token);
        }
        catch (ApiException ex)
        {
            var d = ex.Detail ?? "";
            SetError(ex.Status == 401 ? Loc.Get("login.error_credentials")
                : ex.Status == 403 && d.Contains("подтвержд", StringComparison.OrdinalIgnoreCase) ? Loc.Get("login.error_not_verified")
                : ex.Status == 403 ? Loc.Get("login.error_blocked")
                : ex.UserMessage());
        }
        catch (Exception ex) { SetError(Errors.Text(ex)); }
        finally { LoginBtn.IsEnabled = true; }
    }

    void Password_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Login_Click(sender, e); }

    async void Forgot_Click(object sender, RoutedEventArgs e)
    {
        var email = await Dialogs.Prompt(Loc.Get("forgot.title"), Loc.Get("forgot.desc"), "you@example.com", Loc.Get("forgot.send"));
        if (email == null) return;
        if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) { await Dialogs.Info(Loc.Get("forgot.title"), Loc.Get("forgot.bad_email")); return; }
        try
        {
            await Api.ForgotPassword(email);
            await Dialogs.Info(Loc.Get("forgot.title"), Loc.Get("forgot.sent"));
        }
        catch (ApiException ex) when (ex.Status == 429) { await Dialogs.Info(Loc.Get("forgot.title"), Loc.Get("forgot.rate_limit")); }
        catch (Exception ex) { await Dialogs.Info(Loc.Get("forgot.title"), Errors.Text(ex)); }
    }

    // ── Anonymous code ──

    void Code_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_formatting) return;
        _formatting = true;
        var digits = new string(Code.Text.Where(char.IsDigit).Take(16).ToArray());
        var formatted = string.Join("-", Enumerable.Range(0, (digits.Length + 3) / 4)
            .Select(i => digits.Substring(i * 4, Math.Min(4, digits.Length - i * 4))));
        if (formatted != Code.Text) { Code.Text = formatted; Code.CaretIndex = formatted.Length; }
        _formatting = false;
    }

    void Code_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) CodeLogin_Click(sender, e); }

    async void CodeLogin_Click(object sender, RoutedEventArgs e)
    {
        var digits = new string(Code.Text.Where(char.IsDigit).ToArray());
        if (digits.Length != 16) { SetError(Loc.Get("login.error_code_length")); return; }
        CodeBtn.IsEnabled = false;
        SetError(null);
        try
        {
            var r = await Api.LoginNumeric(Code.Text);
            await Enter(r.access_token, r.refresh_token);
        }
        catch (ApiException ex)
        {
            SetError(ex.Status == 401 ? Loc.Get("login.error_code_not_found")
                : ex.Status == 403 ? Loc.Get("login.error_blocked") : ex.UserMessage());
        }
        catch (Exception ex) { SetError(Errors.Text(ex)); }
        finally { CodeBtn.IsEnabled = true; }
    }

    async void CreateAnon_Click(object sender, RoutedEventArgs e)
    {
        if (AcceptAnon.IsChecked != true) { SetError(Loc.Get("privacy.must_accept")); return; }
        AnonBtn.IsEnabled = false;
        SetError(null);
        try
        {
            _anon = await Api.GenerateNumeric();
            AnonCode.Text = _anon.code;
            Show(PanelAnonCode);
        }
        catch (ApiException ex) when (ex.Status == 429) { SetError(Loc.Get("anon.rate_limit")); }
        catch (Exception ex) { SetError(Errors.Text(ex)); }
        finally { AnonBtn.IsEnabled = true; }
    }

    void CopyCode_Click(object sender, MouseButtonEventArgs e)
    {
        if (_anon != null) Dialogs.Copy(_anon.code, Loc.Get("anon.copied"));
    }

    void SaveCode_Click(object sender, RoutedEventArgs e)
    {
        if (_anon == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = "fsociety-code.txt", Filter = "Text|*.txt" };
        if (dlg.ShowDialog() != true) return;
        try { File.WriteAllText(dlg.FileName, Loc.F("anon.share_text", _anon.code)); Dialogs.Toast(Loc.Get("common.saved")); }
        catch (Exception ex) { Dialogs.Toast(ex.Message); }
    }

    async void AnonEnter_Click(object sender, RoutedEventArgs e)
    {
        if (_anon == null) return;
        if (!await Dialogs.Confirm(Loc.Get("anon.back_title"), Loc.Get("anon.back_text"), Loc.Get("anon.login"),
                cancel: Loc.Get("anon.back_stay"))) return;
        await Enter(_anon.access_token, _anon.refresh_token);
    }

    // ── Registration ──

    async void Register_Click(object sender, RoutedEventArgs e)
    {
        var email = RegEmail.Text.Trim();
        if (email.Length == 0 || RegPassword.Password.Length == 0) { SetError(Loc.Get("login.error_empty")); return; }
        if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) { SetError(Loc.Get("forgot.bad_email")); return; }
        if (RegPassword.Password.Length < 8) { SetError(Loc.Get("register.error_length")); return; }
        if (RegPassword.Password != RegPassword2.Password) { SetError(Loc.Get("register.error_mismatch")); return; }
        if (Accept.IsChecked != true) { SetError(Loc.Get("privacy.must_accept")); return; }
        RegBtn.IsEnabled = false;
        SetError(null);
        try
        {
            await Api.Register(email, RegPassword.Password);
            Done();
        }
        // «already registered» gets the same neutral answer: the screen does not tell whether an email is taken
        catch (ApiException ex) when (ex.Status == 400 && (ex.Detail ?? "").Contains("зарегистр")) { Done(); }
        catch (Exception ex) { SetError(Errors.Text(ex)); }
        finally { RegBtn.IsEnabled = true; }

        void Done()
        {
            Show(PanelEmail);
            Email.Text = email;
            SetInfo(Loc.Get("register.success"));
        }
    }

    // ── Navigation between forms ──

    void ToCode_Click(object sender, RoutedEventArgs e) { Show(PanelCode); Code.Focus(); }
    void ToEmail_Click(object sender, RoutedEventArgs e) { Show(PanelEmail); Email.Focus(); }
    void ToRegister_Click(object sender, RoutedEventArgs e) { Show(PanelRegister); RegEmail.Text = Email.Text; RegEmail.Focus(); }
    void ToAnon_Click(object sender, RoutedEventArgs e) => Show(PanelAnon);

    void Privacy_Click(object sender, RoutedEventArgs e) => Updates.Open(LegalUrl("privacy"));
    void Terms_Click(object sender, RoutedEventArgs e) => Updates.Open(LegalUrl("terms"));
    void Telegram_Click(object sender, RoutedEventArgs e) => Updates.Open("https://t.me/reich_kanzlei");

    void Lang_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.Language = Loc.En ? "ru" : "en";
        AppSettings.Save();
        MainWindow.Instance?.Reload();
    }
}
