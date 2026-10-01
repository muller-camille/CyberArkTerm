using System.IO;
using System.Windows;
using System.Windows.Controls;
using CyberArkTerm.Core;

namespace CyberArkTerm.App.Views;

public partial class LoginWindow : Window
{
    private readonly AppSettings _settings;
    private PvwaClient? _pending;
    private AuthMethod _pendingMethod;
    private bool _closed;

    public LoginWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        MethodBox.ItemsSource = Enum.GetValues<AuthMethod>();
        MethodBox.SelectedItem = settings.AuthMethod;
        UrlBox.Text = settings.PvwaUrl;
        UserBox.Text = settings.UserName;

        Loaded += (_, _) =>
        {
            if (UrlBox.Text.Length == 0)
            {
                UrlBox.Focus();
            }
            else if (UserBox.IsEnabled && UserBox.Text.Length == 0)
            {
                UserBox.Focus();
            }
            else if (PasswordBox.IsEnabled)
            {
                PasswordBox.Focus();
            }
            else
            {
                LoginButton.Focus();
            }
        };
        Closed += (_, _) =>
        {
            _closed = true;
            // Fenêtre fermée sans succès : on libère le client à moitié authentifié.
            if (Client is null)
            {
                _pending?.Dispose();
            }
        };
    }

    /// <summary>Client authentifié, disponible quand <see cref="Window.DialogResult"/> vaut <c>true</c>.</summary>
    public PvwaClient? Client { get; private set; }

    /// <summary>Nom affiché de l'utilisateur connecté.</summary>
    public string SessionUser { get; private set; } = "";

    private AuthMethod SelectedMethod => MethodBox.SelectedItem is AuthMethod m ? m : AuthMethod.CyberArk;

    private void OnMethodChanged(object sender, SelectionChangedEventArgs e)
    {
        bool needsCredentials = SelectedMethod != AuthMethod.Windows;
        UserBox.IsEnabled = needsCredentials;
        PasswordBox.IsEnabled = needsCredentials;
    }

    private async void OnLoginClick(object sender, RoutedEventArgs e)
    {
        bool answeringChallenge = ChallengePanel.Visibility == Visibility.Visible;
        var method = answeringChallenge ? _pendingMethod : SelectedMethod;
        string userName = UserBox.Text.Trim();
        string password = answeringChallenge ? ChallengeBox.Password : PasswordBox.Password;

        if (!answeringChallenge && method != AuthMethod.Windows && (userName.Length == 0 || password.Length == 0))
        {
            ShowError("Saisissez votre nom d'utilisateur et votre mot de passe.");
            return;
        }

        SetBusy(true);
        try
        {
            if (!answeringChallenge)
            {
                _pending?.Dispose();
                _pending = null;
                _pending = PvwaClient.Create(UrlBox.Text, method);
                _pendingMethod = method;
            }

            await _pending!.LogonAsync(method, userName, password);
            if (_closed)
            {
                // Fenêtre fermée pendant l'authentification.
                return;
            }

            SaveSettings(method, userName);
            Client = _pending;
            SessionUser = method == AuthMethod.Windows ? Environment.UserDomainName + "\\" + Environment.UserName : userName;
            DialogResult = true;
        }
        catch (PvwaException ex) when (!_closed && ex.IsRadiusChallenge)
        {
            ShowChallenge(ex.ServerMessage ?? "Saisissez la réponse demandée par le serveur RADIUS :");
        }
        catch (Exception ex) when (!_closed && ex is not OutOfMemoryException)
        {
            ShowError(ErrorText.Describe(ex));
            if (answeringChallenge)
            {
                // Le challenge a échoué : on repart d'une authentification complète.
                ChallengePanel.Visibility = Visibility.Collapsed;
                CredentialsPanel.Visibility = Visibility.Visible;
            }

            PasswordBox.Clear();
            PasswordBox.Focus();
        }
        finally
        {
            ChallengeBox.Clear();
            SetBusy(false);
        }
    }

    private void ShowChallenge(string prompt)
    {
        StatusText.Visibility = Visibility.Collapsed;
        CredentialsPanel.Visibility = Visibility.Collapsed;
        ChallengePanel.Visibility = Visibility.Visible;
        ChallengeText.Text = prompt;
        PasswordBox.Clear();
        Dispatcher.BeginInvoke(() => ChallengeBox.Focus());
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }

    private void SetBusy(bool busy)
    {
        Busy.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
        LoginButton.IsEnabled = !busy;
        CredentialsPanel.IsEnabled = !busy;
        ChallengePanel.IsEnabled = !busy;
        if (busy)
        {
            StatusText.Visibility = Visibility.Collapsed;
        }
    }

    private void SaveSettings(AuthMethod method, string userName)
    {
        _settings.PvwaUrl = UrlBox.Text.Trim();
        _settings.AuthMethod = method;
        if (method != AuthMethod.Windows)
        {
            _settings.UserName = userName;
        }

        try
        {
            _settings.Save(AppSettings.DefaultPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Préférences non enregistrées : sans conséquence pour la session en cours.
        }
    }
}
