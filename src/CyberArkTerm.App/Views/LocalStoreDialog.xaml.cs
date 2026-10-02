using System.Windows;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core.KeePass;

namespace CyberArkTerm.App.Views;

/// <summary>Coffre local des mots de passe maîtres KeePass : création, déverrouillage, changement de mot de passe.</summary>
public partial class LocalStoreDialog : Window
{
    public enum Mode
    {
        Create,
        Unlock,
        ChangePassword,
    }

    private readonly LocalSecretStore _store;
    private readonly Mode _mode;

    public LocalStoreDialog(LocalSecretStore store, Mode mode)
    {
        InitializeComponent();
        _store = store;
        _mode = mode;
        IntroText.Text = mode switch
        {
            Mode.Create => Strings.LocalStoreCreateIntro,
            Mode.Unlock => Strings.LocalStoreUnlockIntro,
            _ => Strings.LocalStoreChangeIntro,
        };
        OkButton.Content = mode switch
        {
            Mode.Create => Strings.LocalStoreCreateButton,
            Mode.Unlock => Strings.KeePassUnlock,
            _ => Strings.Save,
        };
        if (mode == Mode.Unlock)
        {
            ConfirmLabel.Visibility = ConfirmBox.Visibility = Visibility.Collapsed;
            CancelButton.Content = Strings.LocalStoreLater;
        }

        Loaded += (_, _) => PasswordBox.Focus();
    }

    private async void OnOk(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Password;
        if (_mode != Mode.Unlock)
        {
            if (password.Length < LocalSecretStore.MinPasswordLength)
            {
                ShowError(Text.Format(Core.Localization.CoreStrings.LocalStorePasswordTooShort, LocalSecretStore.MinPasswordLength));
                return;
            }

            if (password != ConfirmBox.Password)
            {
                ShowError(Strings.LocalStoreMismatch);
                return;
            }
        }

        IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        ErrorText.Visibility = Visibility.Collapsed;
        try
        {
            // Argon2id (64 Mio) : quelques centaines de millisecondes, hors du thread de l'interface.
            await Task.Run(() =>
            {
                switch (_mode)
                {
                    case Mode.Create:
                        _store.Create(password);
                        break;
                    case Mode.Unlock:
                        _store.Unlock(password);
                        break;
                    default:
                        _store.ChangePassword(password);
                        break;
                }
            });
            DialogResult = true;
        }
        catch (Exception ex) when (ex is KeePassException or ArgumentException or InvalidOperationException or System.IO.IOException
                                       or UnauthorizedAccessException)
        {
            IsEnabled = true;
            Progress.Visibility = Visibility.Collapsed;
            ShowError(ex.Message);
            PasswordBox.SelectAll();
            PasswordBox.Focus();
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
