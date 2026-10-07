using System.ComponentModel;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Ajout d'un membre à un safe ou modification de ses droits, avec les droits de la session (« Gérer les membres du
/// safe »). Les 22 droits sont regroupés comme dans le PVWA.
/// </summary>
public partial class SafeMemberDialog : Window
{
    /// <summary>Droits par groupe, par leur nom dans l'API (titres lus à chaque ouverture : la langue peut changer).</summary>
    private static (string Title, string[] Permissions)[] Groups() =>
    [
        (Strings.SafeMemberGroupAccess, [nameof(SafePermissions.ListAccounts), nameof(SafePermissions.UseAccounts), nameof(SafePermissions.RetrieveAccounts)]),
        (Strings.SafeMemberGroupAccounts,
        [
            nameof(SafePermissions.AddAccounts), nameof(SafePermissions.UpdateAccountContent), nameof(SafePermissions.UpdateAccountProperties),
            nameof(SafePermissions.InitiateCPMAccountManagementOperations), nameof(SafePermissions.SpecifyNextAccountContent),
            nameof(SafePermissions.RenameAccounts), nameof(SafePermissions.DeleteAccounts), nameof(SafePermissions.UnlockAccounts),
        ]),
        (Strings.SafeMemberGroupSafe,
        [
            nameof(SafePermissions.ManageSafe), nameof(SafePermissions.ManageSafeMembers), nameof(SafePermissions.BackupSafe),
            nameof(SafePermissions.ViewAuditLog), nameof(SafePermissions.ViewSafeMembers),
        ]),
        (Strings.SafeMemberGroupWorkflow,
        [
            nameof(SafePermissions.RequestsAuthorizationLevel1), nameof(SafePermissions.RequestsAuthorizationLevel2),
            nameof(SafePermissions.AccessWithoutConfirmation),
        ]),
        (Strings.SafeMemberGroupFolders,
            [nameof(SafePermissions.CreateFolders), nameof(SafePermissions.DeleteFolders), nameof(SafePermissions.MoveAccountsAndFolders)]),
    ];

    private readonly Dictionary<string, CheckBox> _boxes = [];
    private readonly Func<SafeMemberChange, CancellationToken, Task> _save;
    private readonly CancellationTokenSource _closing = new();
    private bool _busy;

    /// <summary>Ajout d'un membre : « lister » et « utiliser » cochés au départ.</summary>
    public SafeMemberDialog(string safe, Func<SafeMemberChange, CancellationToken, Task> save)
        : this(save, new SafePermissions { ListAccounts = true, UseAccounts = true })
    {
        HeadingText.Text = Text.Format(Strings.SafeMemberAddTitle, safe);
        Loaded += (_, _) => NameBox.Focus();
    }

    /// <summary>Modification des droits de <paramref name="member"/> (le nom, le type et l'annuaire ne changent pas).</summary>
    public SafeMemberDialog(string safe, SafeMember member, Func<SafeMemberChange, CancellationToken, Task> save)
        : this(save, member.Permissions)
    {
        HeadingText.Text = Text.Format(Strings.SafeMemberEditTitle, member.MemberName, safe);
        NameBox.Text = member.MemberName;
        NameBox.IsReadOnly = true;
        TypeBox.SelectedIndex = member.IsGroup ? 1 : 0;
        TypeBox.IsEnabled = false;
        SearchInLabel.Visibility = SearchInBox.Visibility = Visibility.Collapsed;
        UntilBox.SelectedDate = member.Expires?.Date;
    }

    private SafeMemberDialog(Func<SafeMemberChange, CancellationToken, Task> save, SafePermissions permissions)
    {
        InitializeComponent();
        _save = save;
        TypeBox.ItemsSource = new[] { Strings.SafeMemberUser, Strings.SafeMemberGroup };
        TypeBox.SelectedIndex = 0;
        var granted = permissions.All().ToDictionary(p => p.Name, p => p.Granted);
        var groups = Groups();
        for (int i = 0; i < groups.Length; i++)
        {
            var (title, names) = groups[i];
            var panel = new StackPanel();
            foreach (var name in names)
            {
                var box = new CheckBox { Content = SafePermissionText.Label(name), IsChecked = granted[name], Margin = new Thickness(0, 2, 0, 2) };
                _boxes[name] = box;
                panel.Children.Add(box);
            }

            (i < 2 ? LeftGroups : RightGroups).Children.Add(
                new GroupBox { Header = title, Content = panel, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(6, 4, 6, 4) });
        }

        Closed += (_, _) => _closing.Cancel();
    }

    /// <summary>Le PVWA a répondu que la session avait expiré : la fenêtre principale doit se déconnecter.</summary>
    public bool SessionExpired { get; private set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = _busy && DialogResult is null;
        base.OnClosing(e);
    }

    private void OnAll(object sender, RoutedEventArgs e) => SetAll(true);

    private void OnNone(object sender, RoutedEventArgs e) => SetAll(false);

    private void SetAll(bool granted)
    {
        foreach (var box in _boxes.Values)
        {
            box.IsChecked = granted;
        }
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (NameBox.Text.Trim().Length == 0)
        {
            ShowError(Strings.SafeMemberNameRequired);
            NameBox.Focus();
            return;
        }

        var permissions = new SafePermissions();
        foreach (var (name, box) in _boxes)
        {
            permissions.Set(name, box.IsChecked == true);
        }

        var change = new SafeMemberChange
        {
            MemberName = NameBox.Text.Trim(),
            MemberType = TypeBox.SelectedIndex == 1 ? "Group" : "User",
            SearchIn = SearchInBox.Text,
            Expires = UntilBox.SelectedDate,
            Permissions = permissions,
        };
        try
        {
            SetBusy(true);
            await _save(change, _closing.Token);
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
            ShowError(Text.Format(Strings.SafeMemberForbidden, ex.Message));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowError(Text.Format(Strings.SafeMemberFailed, ErrorText.Describe(ex)));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SaveButton.IsEnabled = !busy;
        // Pendant l'envoi au PVWA, ni « Annuler » ni Échap : la demande est partie, son résultat doit être connu.
        CancelButton.IsEnabled = !busy;
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
