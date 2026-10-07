using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core;
using CyberArkTerm.Core.Diagnostics;
using CyberArkTerm.Core.Localization;

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
        CapsLockWarning.Attach(CapsLockText, PasswordBox, ChallengeBox);
        _settings = settings;

        MethodBox.ItemsSource = Enum.GetValues<AuthMethod>();
        MethodBox.SelectedItem = settings.AuthMethod;
        UrlBox.Text = settings.PvwaUrl;
        UserBox.Text = settings.UserName;

        // Langue affichée : celle en cours (choisie, ou à défaut celle de Windows).
        LanguageBox.DisplayMemberPath = "Value";
        LanguageBox.SelectedValuePath = "Key";
        LanguageBox.ItemsSource = UiLanguage.Supported
            .Select(code => new KeyValuePair<string, string>(code, UiLanguage.NativeName(code)))
            .ToList();
        LanguageBox.SelectedValue = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        Loaded += (_, _) =>
        {
            FocusFirstField();
            // Fenêtre rouverte après un changement de langue : elle doit reprendre le clavier.
            Activate();
        };
        // Le focus clavier n'est effectif qu'une fois la fenêtre active : on le replace à la première activation.
        EventHandler? firstActivation = null;
        firstActivation = (_, _) =>
        {
            Activated -= firstActivation;
            Dispatcher.BeginInvoke(FocusFirstField, System.Windows.Threading.DispatcherPriority.Input);
        };
        Activated += firstActivation;
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

    /// <summary>Nom d'utilisateur du coffre (utilisé pour se connecter au PSMP).</summary>
    public string VaultUser { get; private set; } = "";

    /// <summary>Vrai si la fenêtre a été fermée pour être rouverte dans une autre langue.</summary>
    public bool LanguageChanged { get; private set; }

    /// <summary>Vrai si l'utilisateur a choisi l'accès d'urgence (coffres KeePass, sans CyberArk).</summary>
    public bool EmergencyRequested { get; private set; }

    /// <summary>Environnement fourni par l'équipe : changements montrés puis appliqués ; l'adresse du PVWA est reprise.</summary>
    private void OnImportEnvironment(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = EnvironmentImport.FileFilter, CheckFileExists = true };
        if (dialog.ShowDialog(this) == true && EnvironmentImport.Offer(this, _settings, dialog.FileName, automatic: false))
        {
            UrlBox.Text = _settings.PvwaUrl;
            MethodBox.SelectedItem = _settings.AuthMethod;
            FocusFirstField();
        }
    }

    private void OnEmergencyClick(object sender, RoutedEventArgs e)
    {
        EmergencyRequested = true;
        DialogResult = true;
    }

    private AuthMethod SelectedMethod => MethodBox.SelectedItem is AuthMethod m ? m : AuthMethod.CyberArk;

    private void FocusFirstField()
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
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || LanguageBox.SelectedValue is not string code
            || code == CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
        {
            return;
        }

        // Saisies conservées (sauf le mot de passe) : la fenêtre est reconstruite dans la nouvelle langue.
        _settings.Language = code;
        _settings.PvwaUrl = UrlBox.Text.Trim();
        _settings.AuthMethod = SelectedMethod;
        if (SelectedMethod != AuthMethod.Windows)
        {
            _settings.UserName = UserBox.Text.Trim();
        }

        TrySaveSettings();
        LanguageChanged = true;
        Close();
    }

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
            ShowError(Strings.LoginMissingCredentials);
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

            DebugLog.Write("login", $"Connexion à {_pending!.BaseUri} (méthode {method}{(answeringChallenge ? ", réponse au challenge RADIUS" : "")})");
            await _pending!.LogonAsync(method, userName, password);
            DebugLog.Write("login", "Session PVWA ouverte.");
            if (_closed)
            {
                // Fenêtre fermée pendant l'authentification.
                return;
            }

            SaveSettings(method, userName);
            Client = _pending;
            SessionUser = method == AuthMethod.Windows ? Environment.UserDomainName + "\\" + Environment.UserName : userName;
            VaultUser = method == AuthMethod.Windows ? Environment.UserName : userName;
            DialogResult = true;
        }
        catch (PvwaException ex) when (!_closed && ex.IsRadiusChallenge)
        {
            DebugLog.Write("login", "Le PVWA demande une réponse au challenge RADIUS.");
            ShowChallenge(ex.ServerMessage ?? Strings.LoginRadiusPrompt);
        }
        catch (Exception ex) when (!_closed && ex is not OutOfMemoryException)
        {
            DebugLog.Write("login", "Échec de la connexion au PVWA", ex);
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
        // ErrorLine : annoncée par les lecteurs d'écran.
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }

    private void SetBusy(bool busy)
    {
        Busy.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
        LoginButton.IsEnabled = !busy;
        LanguageBox.IsEnabled = !busy && ChallengePanel.Visibility != Visibility.Visible;
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

        TrySaveSettings();
    }

    private void TrySaveSettings()
    {
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
