using System.IO;
using System.Security.Cryptography;
using System.Windows;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Services;
using CyberArkTerm.App.Services.KeePass;
using CyberArkTerm.Core.KeePass;
using Microsoft.Win32;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Déverrouillage d'un coffre KeePass : mot de passe maître et/ou fichier clé, et option pour garder le mot de passe
/// dans le coffre local. La dérivation de clé se fait en arrière-plan (elle peut prendre quelques secondes).
/// </summary>
public partial class KeePassUnlockDialog : Window
{
    private readonly KeePassFolder _folder;
    private readonly KeePassManager _manager;
    private readonly Func<bool> _ensureLocalStore;
    private CancellationTokenSource? _unlocking;
    private bool _closed;

    /// <param name="ensureLocalStore">Déverrouille ou crée le coffre local ; faux si l'utilisateur renonce.</param>
    internal KeePassUnlockDialog(KeePassFolder folder, KeePassManager manager, Func<bool> ensureLocalStore, string? message = null)
    {
        InitializeComponent();
        CapsLockWarning.Attach(CapsLockText, PasswordBox);
        _folder = folder;
        _manager = manager;
        _ensureLocalStore = ensureLocalStore;
        NameText.Text = folder.DisplayName;
        PathText.Text = folder.FilePath;
        PathText.ToolTip = folder.FilePath;
        KeyFileBox.Text = folder.KeyFilePath ?? "";
        RememberBox.IsChecked = folder.RememberPassword;
        if (!folder.UsesPassword)
        {
            PasswordLabel.Visibility = PasswordBox.Visibility = RememberBox.Visibility = RememberHint.Visibility = Visibility.Collapsed;
        }

        if (message is not null)
        {
            ShowMessage(message, error: true);
        }

        Loaded += (_, _) =>
        {
            if (folder.UsesPassword)
            {
                PasswordBox.Focus();
            }
            else
            {
                UnlockButton.Focus();
            }
        };
    }

    private void OnBrowseKeyFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = Strings.AllFilesFilter, CheckFileExists = true, FileName = KeyFileBox.Text };
        if (dialog.ShowDialog(this) == true)
        {
            KeyFileBox.Text = dialog.FileName;
        }
    }

    private async void OnUnlock(object sender, RoutedEventArgs e)
    {
        var keyFile = KeyFileBox.Text.Trim().Trim('"');
        if (keyFile.Length > 0 && !File.Exists(keyFile))
        {
            ShowMessage(Text.Format(Strings.KeePassFileMissing, keyFile), error: true);
            return;
        }

        if (!_folder.UsesPassword && keyFile.Length == 0)
        {
            ShowMessage(Strings.KeePassNeedsKey, error: true);
            return;
        }

        var password = SecretInput.ReadUtf8(PasswordBox);
        bool remember = _folder.UsesPassword && RememberBox.IsChecked == true;
        SetBusy(true);
        _unlocking = new CancellationTokenSource();
        try
        {
            await _manager.UnlockAsync(_folder, password, keyFile.Length > 0 ? keyFile : null, _unlocking.Token);
            if (_closed)
            {
                // Fenêtre fermée pendant la dérivation de clé : l'utilisateur a renoncé, le coffre ne reste pas ouvert.
                _manager.Lock(_folder.Id);
                return;
            }

            _folder.KeyFilePath = keyFile.Length > 0 ? Path.GetFullPath(keyFile) : null;
            SaveRemembered(password, remember);
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            SetBusy(false);
        }
        catch (Exception ex) when (ex is KeePassException or IOException or UnauthorizedAccessException)
        {
            SetBusy(false);
            ShowMessage(ex.Message, error: true);
            PasswordBox.SelectAll();
            PasswordBox.Focus();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
            _unlocking?.Dispose();
            _unlocking = null;
        }
    }

    /// <summary>« Mémoriser » était coché mais le coffre local n'a pas été ouvert : le mot de passe n'est pas gardé.</summary>
    public bool NotRemembered { get; private set; }

    /// <summary>Garde ou oublie le mot de passe maître dans le coffre local, selon la case.</summary>
    private void SaveRemembered(byte[] password, bool remember)
    {
        var store = _manager.Store;
        if (remember && password.Length > 0 && _ensureLocalStore())
        {
            store.Set(_folder.Id, password);
            _folder.RememberPassword = true;
        }
        else if (remember && password.Length > 0)
        {
            // Coffre local refusé (création ou déverrouillage annulé) : la base est ouverte, mais rien n'est mémorisé.
            NotRemembered = true;
        }
        else if (!remember)
        {
            if (store.IsUnlocked)
            {
                store.Remove(_folder.Id);
            }

            _folder.RememberPassword = false;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => _unlocking?.Cancel();

    /// <summary>Fermer la fenêtre (croix, Échap) pendant le déverrouillage l'annule aussi.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _unlocking?.Cancel();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        PasswordBox.Clear();
        base.OnClosed(e);
    }

    private void SetBusy(bool busy)
    {
        Form.IsEnabled = !busy;
        UnlockButton.IsEnabled = !busy;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy)
        {
            ShowMessage(Strings.KeePassUnlocking, error: false);
        }
        else
        {
            MessageText.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowMessage(string text, bool error)
    {
        MessageText.Text = text;
        MessageText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, error ? "ErrorBrush" : "MutedBrush");
        MessageText.Visibility = Visibility.Visible;
    }
}
