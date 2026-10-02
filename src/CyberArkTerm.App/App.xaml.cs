using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CyberArkTerm.App.Views;
using CyberArkTerm.Core;
using CyberArkTerm.Core.Localization;

namespace CyberArkTerm.App;

public partial class App : Application
{
    private AppSettings _settings = new();
    private CultureInfo _systemCulture = CultureInfo.CurrentUICulture;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        _systemCulture = CultureInfo.CurrentUICulture;
        _settings = AppSettings.Load(AppSettings.DefaultPath);
        StartSession();
    }

    /// <summary>Affiche l'écran de connexion puis, en cas de succès, la liste des comptes.</summary>
    private void StartSession()
    {
        // Langue choisie dans les préférences (ou celle de Windows) : appliquée à chaque retour à l'écran de connexion.
        UiLanguage.Apply(UiLanguage.Resolve(_settings.Language, _systemCulture));
        var login = new LoginWindow(_settings);
        bool ok = login.ShowDialog() == true && login.Client is not null;
        if (login.LanguageChanged)
        {
            // Langue changée depuis l'écran de connexion : on le rouvre dans la nouvelle langue.
            StartSession();
            return;
        }

        if (!ok || login.Client is null)
        {
            Shutdown();
            return;
        }

        var main = new MainWindow(login.Client, _settings, login.SessionUser, login.VaultUser);
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
        MessageBox.Show(ErrorText.Describe(e.Exception), "CyberArkTerm", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
