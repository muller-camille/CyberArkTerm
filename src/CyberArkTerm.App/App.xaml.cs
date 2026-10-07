using System.Globalization;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CyberArkTerm.App.Localization;
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

    // Une seule instance par session Windows : deux instances écraseraient l'une l'autre leurs réglages, leur coffre local
    // et les fichiers en cours de modification (nettoyage du dossier temporaire au démarrage).
    private Mutex? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        _instance = new Mutex(initiallyOwned: true, @"Local\CyberArkTerm.Instance", out bool first);
        if (!first)
        {
            _instance.Dispose();
            _instance = null;
            MessageBox.Show(Strings.AlreadyRunning, "CyberArkTerm", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Même icône pour toutes les fenêtres (connexion, fenêtre principale, dialogues).
        var icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/CyberArkTerm.ico", UriKind.Absolute));
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window { Icon: null } window)
            {
                window.Icon = icon;
            }
        }));
        // Contraste élevé de Windows : couleurs système à la place de la palette, avant la première fenêtre.
        Palette.Follow(this);
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
        FileBrowserPanel.CleanupDragFolders();
        _keePass = new KeePassManager();
        UiLanguage.Apply(UiLanguage.Resolve(_settings.Language, _systemCulture));
        if (_settings.SetAsideFile is { } aside)
        {
            MessageBox.Show(Text.Format(_settings.RestoredFromBackup ? Strings.SettingsSetAsideRestored : Strings.SettingsSetAsideReset, aside),
                "CyberArkTerm", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // Environnement de l'équipe : fichier à côté de l'exécutable et fichier central, proposés s'ils ont changé.
        EnvironmentImport.OfferAtStartup(_settings);
        StartSession();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DebugLog.Write("app", "Fermeture de l'application.");
        _keePass?.Dispose();
        _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
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
