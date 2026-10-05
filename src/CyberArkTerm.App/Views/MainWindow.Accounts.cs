using System.Net;
using System.Windows;
using System.Windows.Interop;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Services;
using CyberArkTerm.Core;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Actions sur les comptes CyberArk, avec les droits de la session : ajout, modification, suppression, opérations du
/// CPM et copie du mot de passe.
/// </summary>
public partial class MainWindow
{
    /// <summary>Durée de vie d'un mot de passe copié dans le presse-papiers.</summary>
    private static readonly TimeSpan PasswordClipboardDelay = TimeSpan.FromSeconds(20);

    private SecureClipboard? _passwordClipboard;

    private static string AccountLabel(PvwaAccount account) => $"{account.UserName}@{account.Address}";

    /// <summary>Valeurs proposées dans les listes (safes, plateformes) : celles des comptes visibles, triées, sans doublon.</summary>
    private List<string> KnownValues(Func<PvwaAccount, string?> selector) =>
        _accounts.Select(selector).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();

    private void ShowAccountError(string message) =>
        MessageBox.Show(this, message, "CyberArkTerm", MessageBoxButton.OK, MessageBoxImage.Warning);

    // ===================== Ajout =====================

    private async void OnAddAccount(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.MenuItem)?.CommandParameter is string safe)
        {
            await AddAccountAsync(safe, _current);
        }
    }

    /// <summary>
    /// Crée un compte dans le safe (droit « Add accounts » nécessaire), puis recharge la liste des comptes. Le compte
    /// cliqué sert de modèle : sa plateforme et son domaine de connexion sont proposés.
    /// </summary>
    private async Task AddAccountAsync(string safe, PvwaAccount? template)
    {
        if (_client is not { } client)
        {
            return;
        }

        var platform = template?.PlatformId ?? MostCommonPlatform(safe);
        var dialog = new AccountDialog(KnownValues(a => a.SafeName), KnownValues(a => a.PlatformId), safe, platform, template?.LogonDomain,
            client.AddAccountAsync) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Saved is not { } created)
        {
            if (dialog.SessionExpired)
            {
                OnSessionExpired();
            }

            return;
        }

        await ReloadAndSelectAsync(created.Id);
        SetStatus(Text.Format(Strings.AccountCreated, AccountLabel(created), created.SafeName));
    }

    /// <summary>Plateforme la plus courante du safe (proposée quand on part du safe et non d'un compte).</summary>
    private string? MostCommonPlatform(string? safe) => _accounts
        .Where(a => string.Equals(a.SafeName, safe, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(a.PlatformId))
        .GroupBy(a => a.PlatformId!, StringComparer.OrdinalIgnoreCase)
        .MaxBy(g => g.Count())?.Key;

    // ===================== Import CSV =====================

    /// <summary>
    /// Import de comptes depuis un fichier CSV ; le safe et la plateforme du compte (ou du safe) cliqué servent de
    /// valeurs par défaut. La liste est rechargée si des comptes ont été créés.
    /// </summary>
    private async void OnImportAccounts(object sender, RoutedEventArgs e)
    {
        if (_client is not { } client)
        {
            return;
        }

        // Clic droit : safe cliqué ; barre d'outils : safe du compte sélectionné, s'il y en a un.
        var safe = (sender as System.Windows.Controls.MenuItem)?.CommandParameter as string ?? _current?.SafeName;
        var platform = _current?.PlatformId ?? MostCommonPlatform(safe);
        var choose = new ImportAccountsDialog(KnownValues(a => a.SafeName), KnownValues(a => a.PlatformId), safe, platform) { Owner = this };
        if (choose.ShowDialog() != true || choose.Confirmed is not { } import)
        {
            return;
        }

        var progress = new ImportProgressDialog(import, client.AddAccountAsync) { Owner = this };
        try
        {
            progress.ShowDialog();
        }
        finally
        {
            // Mots de passe pas encore envoyés (import arrêté) : effacés même si la fenêtre n'a pas pu s'ouvrir.
            import.Clear();
        }

        if (progress.SessionExpired)
        {
            OnSessionExpired();
            return;
        }

        if (progress.Created > 0)
        {
            await LoadAccountsAsync();
            SetStatus(Text.Format(Strings.AccountsImported, progress.Created));
        }
    }

    /// <summary>Recharge les comptes puis désigne <paramref name="accountId"/> comme cible des actions.</summary>
    private async Task ReloadAndSelectAsync(string accountId)
    {
        await LoadAccountsAsync();
        if (_byId.TryGetValue(accountId, out var account))
        {
            SetCurrent(account);
        }
    }

    // ===================== Modification et suppression =====================

    private async void OnEditAccount(object sender, RoutedEventArgs e)
    {
        if (_client is not { } client || _current is not { } account)
        {
            return;
        }

        var dialog = new AccountDialog(account, KnownValues(a => a.PlatformId),
            (operations, ct) => client.UpdateAccountAsync(account.Id, operations, ct)) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Saved is not { } saved)
        {
            if (dialog.SessionExpired)
            {
                OnSessionExpired();
            }

            return;
        }

        await ReloadAndSelectAsync(account.Id);
        SetStatus(Text.Format(Strings.AccountUpdated, AccountLabel(saved)));
    }

    private async void OnDeleteAccount(object sender, RoutedEventArgs e)
    {
        if (_client is not { } client || _current is not { } account)
        {
            return;
        }

        var label = AccountLabel(account);
        if (MessageBox.Show(this, Text.Format(Strings.DeleteAccountConfirm, label, account.SafeName), "CyberArkTerm",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await client.DeleteAccountAsync(account.Id, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (PvwaException ex) when (ex.IsUnauthorized)
        {
            OnSessionExpired();
            return;
        }
        catch (PvwaException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            ShowAccountError(Text.Format(Strings.DeleteAccountForbidden, ex.Message));
            return;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowAccountError(Text.Format(Strings.DeleteAccountFailed, ErrorText.Describe(ex)));
            return;
        }

        SetCurrent(null);
        await LoadAccountsAsync();
        SetStatus(Text.Format(Strings.AccountDeleted, label, account.SafeName));
    }

    // ===================== Opérations du CPM =====================

    private void OnCpmVerify(object sender, RoutedEventArgs e) => _ = RunCpmActionAsync(CpmAction.Verify);

    private void OnCpmChange(object sender, RoutedEventArgs e) => _ = RunCpmActionAsync(CpmAction.Change);

    private void OnCpmReconcile(object sender, RoutedEventArgs e) => _ = RunCpmActionAsync(CpmAction.Reconcile);

    /// <summary>
    /// Demande au CPM de vérifier, changer ou réconcilier le mot de passe du compte sélectionné (confirmation pour
    /// changer et réconcilier). Le CPM traite la demande ensuite : l'état se met à jour au prochain chargement (F5).
    /// </summary>
    private async Task RunCpmActionAsync(CpmAction action)
    {
        if (_client is not { } client)
        {
            return;
        }

        if (_current is not { } account)
        {
            if (_currentSaved is { } saved)
            {
                SetStatus(Text.Format(Strings.SavedAccountGone, saved.Name), isError: true);
            }

            return;
        }

        var label = AccountLabel(account);
        var confirm = action switch
        {
            CpmAction.Change => Strings.CpmChangeConfirm,
            CpmAction.Reconcile => Strings.CpmReconcileConfirm,
            _ => null,
        };
        if (confirm is not null && MessageBox.Show(this, Text.Format(confirm, label), "CyberArkTerm",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await client.RunCpmActionAsync(account.Id, action, _lifetime.Token);
            var name = action switch
            {
                CpmAction.Change => Strings.CpmActionChange,
                CpmAction.Reconcile => Strings.CpmActionReconcile,
                _ => Strings.CpmActionVerify,
            };
            SetStatus(Text.Format(Strings.CpmRequested, label, name));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Fenêtre en cours de fermeture.
        }
        catch (PvwaException ex) when (ex.IsUnauthorized)
        {
            OnSessionExpired();
        }
        catch (PvwaException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            ShowAccountError(Text.Format(Strings.CpmForbidden, ex.Message));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowAccountError(Text.Format(Strings.CpmFailed, ErrorText.Describe(ex)));
        }
    }

    // ===================== Copie du mot de passe =====================

    /// <summary>
    /// Récupère le mot de passe du compte sélectionné (motif et ticket demandés) et le copie dans le presse-papiers
    /// pour <see cref="PasswordClipboardDelay"/>, sans l'afficher.
    /// </summary>
    private void OnCopyPassword(object sender, RoutedEventArgs e)
    {
        if (_client is not { } client)
        {
            return;
        }

        if (_current is not { } account)
        {
            if (_currentSaved is { } saved)
            {
                SetStatus(Text.Format(Strings.SavedAccountGone, saved.Name), isError: true);
            }

            return;
        }

        var label = AccountLabel(account);
        _passwordClipboard ??= new SecureClipboard(new WindowInteropHelper(this).Handle, PasswordClipboardDelay,
            () => SetStatus(Strings.PasswordClipboardCleared));
        var reason = _currentSaved?.AccountId == account.Id ? _currentSaved.Reason : null;
        var dialog = new RetrievePasswordDialog(label, PasswordClipboardDelay, reason,
            (options, ct) => client.RetrievePasswordAsync(account.Id, options, ct), _passwordClipboard.Copy) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            SetStatus(Text.Format(Strings.PasswordCopied, label, (int)PasswordClipboardDelay.TotalSeconds));
        }
        else if (dialog.SessionExpired)
        {
            OnSessionExpired();
        }
    }

    /// <summary>Efface du presse-papiers un mot de passe copié (déconnexion, fermeture, verrouillage de Windows).</summary>
    private void ClearPasswordClipboard() => _passwordClipboard?.Clear();
}
