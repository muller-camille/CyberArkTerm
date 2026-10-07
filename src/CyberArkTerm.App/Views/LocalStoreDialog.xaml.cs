using System.Windows;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Services;
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
    private readonly IEnumerable<string>? _remembered;

    /// <param name="remembered">
    /// Coffres KeePass dont le mot de passe doit rester mémorisé : au déverrouillage, les autres secrets (coffres retirés
    /// ou « se souvenir » décoché pendant que le coffre local était verrouillé) sont oubliés.
    /// </param>
    /// <param name="offerLater">Déverrouillage demandé en passant (ouverture d'une base KeePass) : « Plus tard » plutôt
    /// qu'« Annuler ».</param>
    public LocalStoreDialog(LocalSecretStore store, Mode mode, IEnumerable<string>? remembered = null, bool offerLater = true)
    {
        InitializeComponent();
        CapsLockWarning.Attach(CapsLockText, PasswordBox, ConfirmBox);
        _store = store;
        _mode = mode;
        _remembered = remembered;
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
            ConfirmLabel.Visibility = ConfirmBox.Visibility = MinLengthText.Visibility = Visibility.Collapsed;
            if (offerLater)
            {
                CancelButton.Content = Strings.LocalStoreLater;
            }
        }
        else
        {
            // Annoncé avant la saisie, pas seulement après une erreur.
            MinLengthText.Text = Text.Format(Strings.LocalStoreMinLength, LocalSecretStore.MinPasswordLength);
        }

        Loaded += (_, _) => PasswordBox.Focus();
    }

    private async void OnOk(object sender, RoutedEventArgs e)
    {
        // Lu sans chaîne .NET (effaçable), et effacé dès que le coffre local en a dérivé sa clé.
        var password = SecretInput.Read(PasswordBox);
        try
        {
            if (_mode != Mode.Unlock)
            {
                if (password.Length < LocalSecretStore.MinPasswordLength)
                {
                    ShowError(Text.Format(Core.Localization.CoreStrings.LocalStorePasswordTooShort, LocalSecretStore.MinPasswordLength));
                    return;
                }

                var confirm = SecretInput.Read(ConfirmBox);
                bool same = password.AsSpan().SequenceEqual(confirm);
                SecretInput.Clear(confirm);
                if (!same)
                {
                    ShowError(Strings.LocalStoreMismatch);
                    return;
                }
            }

            await RunAsync(password);
        }
        finally
        {
            SecretInput.Clear(password);
        }
    }

    private async Task RunAsync(char[] password)
    {
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
                        if (_remembered is not null)
                        {
                            _store.RemoveAllExcept(_remembered);
                        }

                        break;
                    default:
                        _store.ChangePassword(password);
                        break;
                }
            });
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            // Session Windows verrouillée pendant le calcul : le coffre local reste verrouillé.
            DialogResult = false;
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

    protected override void OnClosed(EventArgs e)
    {
        PasswordBox.Clear();
        ConfirmBox.Clear();
        base.OnClosed(e);
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
