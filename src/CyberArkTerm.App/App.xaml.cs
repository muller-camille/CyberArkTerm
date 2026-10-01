using System.Windows;
using System.Windows.Threading;
using CyberArkTerm.App.Views;
using CyberArkTerm.Core;

namespace CyberArkTerm.App;

public partial class App : Application
{
    private AppSettings _settings = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        _settings = AppSettings.Load(AppSettings.DefaultPath);
        StartSession();
    }

    /// <summary>Affiche l'écran de connexion puis, en cas de succès, la liste des comptes.</summary>
    private void StartSession()
    {
        var login = new LoginWindow(_settings);
        if (login.ShowDialog() != true || login.Client is null)
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
