using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Création d'un compte dans un safe (<c>POST /API/Accounts</c>) avec les droits de la session. Le mot de passe est lu
/// dans les champs masqués sans passer par une chaîne, envoyé une fois puis effacé de la mémoire.
/// </summary>
public partial class AddAccountDialog : Window
{
    private readonly Func<NewAccount, CancellationToken, Task<PvwaAccount>> _create;
    private readonly CancellationTokenSource _closing = new();
    private bool _busy;

    /// <param name="safes">Safes proposés (ceux des comptes visibles) ; la saisie reste libre.</param>
    /// <param name="platforms">Plateformes proposées (celles des comptes visibles) ; la saisie reste libre.</param>
    public AddAccountDialog(IEnumerable<string> safes, IEnumerable<string> platforms, string safe, string? platform, string? logonDomain,
        Func<NewAccount, CancellationToken, Task<PvwaAccount>> create)
    {
        InitializeComponent();
        _create = create;
        SafeBox.ItemsSource = safes.ToList();
        SafeBox.Text = safe;
        PlatformBox.ItemsSource = platforms.ToList();
        PlatformBox.Text = platform ?? "";
        DomainBox.Text = logonDomain ?? "";
        Loaded += (_, _) => (PlatformBox.Text.Length == 0 ? (Control)PlatformBox : AddressBox).Focus();
        Closed += (_, _) =>
        {
            _closing.Cancel();
            PasswordBox.Clear();
            ConfirmBox.Clear();
        };
    }

    /// <summary>Compte créé par le PVWA, quand <see cref="Window.DialogResult"/> vaut <c>true</c>.</summary>
    public PvwaAccount? Created { get; private set; }

    /// <summary>Le PVWA a répondu que la session avait expiré : la fenêtre principale doit se déconnecter.</summary>
    public bool SessionExpired { get; private set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Pas de fermeture par la croix pendant l'envoi : le compte serait peut-être créé sans que la liste le montre.
        e.Cancel = _busy && DialogResult is null;
        base.OnClosing(e);
    }

    private void OnCpmChanged(object sender, RoutedEventArgs e) => ReasonBox.IsEnabled = CpmBox.IsChecked != true;

    private async void OnCreate(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var safe = SafeBox.Text.Trim();
        var platform = PlatformBox.Text.Trim();
        var address = AddressBox.Text.Trim();
        var user = UserBox.Text.Trim();
        if (safe.Length == 0 || platform.Length == 0 || address.Length == 0 || user.Length == 0)
        {
            ShowError(Strings.AddAccountRequired);
            return;
        }

        var secret = ReadPassword(PasswordBox);
        var confirm = ReadPassword(ConfirmBox);
        try
        {
            if (!secret.AsSpan().SequenceEqual(confirm))
            {
                ShowError(Strings.AddAccountMismatch);
                ConfirmBox.Clear();
                ConfirmBox.Focus();
                return;
            }

            var account = new NewAccount
            {
                SafeName = safe,
                PlatformId = platform,
                Address = address,
                UserName = user,
                Name = NameBox.Text,
                LogonDomain = DomainBox.Text,
                Secret = secret,
                AutomaticManagement = CpmBox.IsChecked == true,
                ManualManagementReason = ReasonBox.Text,
                RemoteMachines = MachinesBox.Text,
            };
            SetBusy(true);
            Created = await _create(account, _closing.Token);
            DialogResult = true;
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // Fenêtre fermée.
        }
        catch (PvwaException ex) when (ex.IsUnauthorized)
        {
            SessionExpired = true;
            DialogResult = false;
        }
        catch (PvwaException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            ShowError(Text.Format(Strings.AddAccountForbidden, ex.Message));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowError(Text.Format(Strings.AddAccountFailed, ErrorText.Describe(ex)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(secret.AsSpan()));
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(confirm.AsSpan()));
            SetBusy(false);
        }
    }

    /// <summary>Contenu d'un champ masqué, copié directement de sa mémoire protégée (pas de chaîne non effaçable).</summary>
    private static char[] ReadPassword(PasswordBox box)
    {
        using var secure = box.SecurePassword;
        var chars = new char[secure.Length];
        var pointer = Marshal.SecureStringToGlobalAllocUnicode(secure);
        try
        {
            Marshal.Copy(pointer, chars, 0, chars.Length);
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(pointer);
        }

        return chars;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        CreateButton.IsEnabled = !busy;
        StatusText.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy)
        {
            ErrorMessage.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowError(string message)
    {
        ErrorMessage.Text = message;
        ErrorMessage.Visibility = Visibility.Visible;
    }
}
