using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Membres d'un safe et leurs droits, lus sur le PVWA (droit « View Safe Members » nécessaire) : qui peut ajouter des
/// comptes, et le détail des droits du membre sélectionné. Ajout, modification des droits et retrait de membres quand
/// <see cref="SafeMemberActions"/> est fourni (droit « Gérer les membres du safe » nécessaire côté PVWA).
/// </summary>
public partial class SafeMembersDialog : Window
{
    /// <summary>Droits affichés en colonnes ; les autres figurent dans le détail du membre sélectionné.</summary>
    private static readonly string[] Columns =
    [
        nameof(SafePermissions.ListAccounts),
        nameof(SafePermissions.UseAccounts),
        nameof(SafePermissions.RetrieveAccounts),
        nameof(SafePermissions.AddAccounts),
        nameof(SafePermissions.UpdateAccountContent),
        nameof(SafePermissions.UpdateAccountProperties),
        nameof(SafePermissions.DeleteAccounts),
        nameof(SafePermissions.ManageSafeMembers),
        nameof(SafePermissions.AccessWithoutConfirmation),
    ];

    private readonly string _safe;
    private readonly Func<CancellationToken, Task<List<SafeMember>>> _load;
    private readonly SafeMemberActions? _actions;
    private readonly CancellationTokenSource _closing = new();
    private List<SafeMemberRow> _rows = [];

    /// <param name="actions">Ajout, modification et retrait de membres ; null = consultation seule.</param>
    public SafeMembersDialog(string safeName, Func<CancellationToken, Task<List<SafeMember>>> load, SafeMemberActions? actions = null)
    {
        InitializeComponent();
        _safe = safeName;
        _load = load;
        _actions = actions;
        HeadingText.Text = Text.Format(Strings.SafeMembersHeading, safeName);
        BuildColumns();
        Loaded += async (_, _) => await LoadAsync();
        Closed += (_, _) => _closing.Cancel();
    }

    /// <summary>Le PVWA a répondu que la session avait expiré : la fenêtre principale doit se déconnecter.</summary>
    public bool SessionExpired { get; private set; }

    private void BuildColumns()
    {
        var check = new CheckMarkConverter();
        var wrapped = (DataTemplate)FindResource("WrappedHeader");
        MembersGrid.Columns.Add(new DataGridTextColumn
        {
            Header = Strings.SafeMemberColumn, Binding = new Binding(nameof(SafeMemberRow.Name)), Width = new DataGridLength(2, DataGridLengthUnitType.Star),
        });
        MembersGrid.Columns.Add(new DataGridTextColumn { Header = Strings.SafeMemberTypeColumn, Binding = new Binding(nameof(SafeMemberRow.Type)) });
        MembersGrid.Columns.Add(new DataGridTextColumn { Header = Strings.SafeMemberUntilColumn, Binding = new Binding(nameof(SafeMemberRow.Until)) });
        foreach (var name in Columns)
        {
            MembersGrid.Columns.Add(new DataGridTextColumn
            {
                // Texte « ✓ » plutôt que des cases : la copie (Ctrl+C) donne « ✓ » et pas « True ».
                Header = SafePermissionText.Label(name),
                HeaderTemplate = wrapped,
                Binding = new Binding($"{nameof(SafeMemberRow.Permissions)}.{name}") { Converter = check },
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 60,
                ElementStyle = CenteredText,
            });
        }
    }

    private static Style CenteredText { get; } = new(typeof(TextBlock))
    {
        Setters = { new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center) },
    };

    private async Task LoadAsync()
    {
        ErrorMessage.Visibility = Visibility.Collapsed;
        StatusText.Visibility = Visibility.Visible;
        try
        {
            var members = await _load(_closing.Token);
            _rows = members
                .OrderBy(m => m.IsGroup ? 0 : 1)
                .ThenBy(m => m.MemberName, StringComparer.OrdinalIgnoreCase)
                .Select(m => new SafeMemberRow(m))
                .ToList();
            ShowRows();
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // Fenêtre fermée pendant le chargement.
        }
        catch (PvwaException ex) when (ex.IsUnauthorized)
        {
            SessionExpired = true;
            Close();
        }
        catch (PvwaException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            ShowError(Strings.SafeMembersForbidden);
        }
        catch (PvwaException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            ShowError(Strings.SafeMembersNotFound);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowError(Text.Format(Strings.SafeMembersFailed, ErrorText.Describe(ex)));
        }
    }

    private void ShowRows()
    {
        var canAdd = _rows.Where(r => r.Permissions.AddAccounts).ToList();
        StatusText.Text = Text.Format(Strings.SafeMembersCount, _rows.Count);
        CanAddText.Text = canAdd.Count == 0
            ? Strings.SafeMembersNobodyCanAdd
            : Text.Format(Strings.SafeMembersCanAdd, string.Join(", ", canAdd.Select(r => r.Member.IsGroup ? r.Name + Strings.SafeMembersGroupSuffix : r.Name)));
        SummaryPanel.Visibility = Visibility.Visible;
        MembersGrid.Visibility = Visibility.Visible;
        ManagePanel.Visibility = _actions is null ? Visibility.Collapsed : Visibility.Visible;
        ApplyFilter();
    }

    private void ApplyFilter() =>
        MembersGrid.ItemsSource = OnlyAddBox.IsChecked == true ? _rows.Where(r => r.Permissions.AddAccounts).ToList() : _rows;

    private void ShowError(string message)
    {
        StatusText.Visibility = Visibility.Collapsed;
        ErrorMessage.Text = message;
        ErrorMessage.Visibility = Visibility.Visible;
    }

    private void OnOnlyAddClick(object sender, RoutedEventArgs e) => ApplyFilter();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Utilisateurs prédéfinis du coffre (Administrator, Auditors...) : gérés par le coffre lui-même.
        bool editable = MembersGrid.SelectedItems.Count == 1 && MembersGrid.SelectedItem is SafeMemberRow { Member.IsPredefinedUser: false };
        EditButton.IsEnabled = RemoveButton.IsEnabled = editable;
        if (MembersGrid.SelectedItem is not SafeMemberRow row)
        {
            RightsText.Text = "";
            return;
        }

        var granted = row.Permissions.Granted().Select(SafePermissionText.Label).ToList();
        RightsText.Text = Text.Format(Strings.SafeMembersRights, row.Name, granted.Count == 0 ? Strings.SafeMembersNoRight : string.Join(", ", granted));
    }

    private void OnGridDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (EditButton.IsEnabled && ItemsControl.ContainerFromElement(MembersGrid, (DependencyObject)e.OriginalSource) is DataGridRow)
        {
            OnEditMember(sender, e);
        }
    }

    private void OnAddMember(object sender, RoutedEventArgs e)
    {
        if (_actions is not null)
        {
            ShowMemberDialog(new SafeMemberDialog(_safe, _actions.Add) { Owner = this });
        }
    }

    private void OnEditMember(object sender, RoutedEventArgs e)
    {
        if (_actions is not null && MembersGrid.SelectedItem is SafeMemberRow row)
        {
            ShowMemberDialog(new SafeMemberDialog(_safe, row.Member, _actions.Update) { Owner = this });
        }
    }

    private void ShowMemberDialog(SafeMemberDialog dialog)
    {
        if (dialog.ShowDialog() == true)
        {
            _ = LoadAsync();
        }
        else if (dialog.SessionExpired)
        {
            SessionExpired = true;
            Close();
        }
    }

    private async void OnRemoveMember(object sender, RoutedEventArgs e)
    {
        if (_actions is null || MembersGrid.SelectedItem is not SafeMemberRow row
            || !ConfirmDialog.Destructive(this, Strings.SafeMembersTitle, Text.Format(Strings.SafeMemberRemoveHeading, row.Name),
                Strings.SafeMemberRemoveAction, subject: Text.Format(Strings.SafeSubject, _safe), message: Strings.SafeMemberRemoveMessage))
        {
            return;
        }

        try
        {
            await _actions.Remove(row.Name, _closing.Token);
            await LoadAsync();
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // Fenêtre fermée.
        }
        catch (PvwaException ex) when (ex.IsUnauthorized)
        {
            SessionExpired = true;
            Close();
        }
        catch (PvwaException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            MessageBox.Show(this, Text.Format(Strings.SafeMemberForbidden, ex.Message), Strings.SafeMembersTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            MessageBox.Show(this, Text.Format(Strings.SafeMemberRemoveFailed, ErrorText.Describe(ex)), Strings.SafeMembersTitle,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Ligne du tableau : membre, type et fin d'appartenance lisibles.</summary>
    public sealed class SafeMemberRow(SafeMember member)
    {
        public SafeMember Member { get; } = member;

        public string Name => Member.MemberName;

        public string Type => Member.IsGroup ? Strings.SafeMemberGroup : Strings.SafeMemberUser;

        public string Until => Member.Expires?.ToString("d", System.Globalization.CultureInfo.CurrentCulture) ?? "";

        public SafePermissions Permissions => Member.Permissions;
    }

    private sealed class CheckMarkConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is true ? "✓" : "";

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}

/// <summary>Opérations de gestion des membres d'un safe (droit « Gérer les membres du safe »).</summary>
public sealed record SafeMemberActions(
    Func<SafeMemberChange, CancellationToken, Task> Add,
    Func<SafeMemberChange, CancellationToken, Task> Update,
    Func<string, CancellationToken, Task> Remove);
