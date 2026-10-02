using System.Globalization;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CyberArkTerm.App.Services;
using CyberArkTerm.App.Services.KeePass;
using CyberArkTerm.App.Views;
using CyberArkTerm.Core;
using CyberArkTerm.Core.Diagnostics;
using CyberArkTerm.Core.Localization;

namespace CyberArkTerm.App;

public partial class App : Application
{
    private AppSettings _settings = new();
    private KeePassManager? _keePass;
    private CultureInfo _systemCulture = CultureInfo.CurrentUICulture;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        // Même icône pour toutes les fenêtres (connexion, fenêtre principale, dialogues).
        var icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/CyberArkTerm.ico", UriKind.Absolute));
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window { Icon: null } window)
            {
                window.Icon = icon;
            }
        }));
        _systemCulture = CultureInfo.CurrentUICulture;
        _settings = AppSettings.Load(AppSettings.DefaultPath);
        AppDebugLog.Apply(_settings);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                DebugLog.Write("app", "Exception non gérée (fin de l'application)", ex);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) => DebugLog.Write("app", "Exception d'une tâche non observée", args.Exception);
        RemoteEditor.CleanupStale();
        _keePass = new KeePassManager();
        UiLanguage.Apply(UiLanguage.Resolve(_settings.Language, _systemCulture));
        UnlockLocalStore();
        StartSession();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DebugLog.Write("app", "Fermeture de l'application.");
        _keePass?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Coffre local des mots de passe maîtres KeePass : son mot de passe est demandé à l'ouverture de l'application
    /// (« Plus tard » pour s'en passer ; il sera redemandé au besoin).
    /// </summary>
    private void UnlockLocalStore()
    {
        if (_keePass is { Store.Exists: true } && _settings.KeePassFolders.Any(f => f.RememberPassword))
        {
            new LocalStoreDialog(_keePass.Store, LocalStoreDialog.Mode.Unlock) { WindowStartupLocation = WindowStartupLocation.CenterScreen }
                .ShowDialog();
        }
    }

    /// <summary>Affiche l'écran de connexion puis, en cas de succès, la liste des comptes.</summary>
    private void StartSession()
    {
        // Langue choisie dans les préférences (ou celle de Windows) : appliquée à chaque retour à l'écran de connexion.
        UiLanguage.Apply(UiLanguage.Resolve(_settings.Language, _systemCulture));
        var login = new LoginWindow(_settings);
        bool ok = login.ShowDialog() == true && (login.Client is not null || login.EmergencyRequested);
        if (login.LanguageChanged)
        {
            // Langue changée depuis l'écran de connexion : on le rouvre dans la nouvelle langue.
            StartSession();
            return;
        }

        if (!ok)
        {
            Shutdown();
            return;
        }

        // Client null : accès d'urgence, sans CyberArk (coffres KeePass seulement).
        var main = new MainWindow(login.EmergencyRequested ? null : login.Client, _settings, login.SessionUser, login.VaultUser, _keePass!);
        MainWindow = main;
        main.Closed += (_, _) =>
        {
            if (main.LogoutRequested)
            {
                StartSession();
            }
            else
            {
                Shutdown();
            }
        };
        main.Show();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        DebugLog.Write("app", "Exception non gérée", e.Exception);
        MessageBox.Show(ErrorText.Describe(e.Exception), "CyberArkTerm", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
