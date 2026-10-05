using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Membres d'un safe et leurs droits, lus sur le PVWA (droit « View Safe Members » nécessaire) : qui peut ajouter des
/// comptes, et le détail des droits du membre sélectionné. Lecture seule.
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

    private readonly Func<CancellationToken, Task<List<SafeMember>>> _load;
    private readonly CancellationTokenSource _closing = new();
    private List<SafeMemberRow> _rows = [];

    public SafeMembersDialog(string safeName, Func<CancellationToken, Task<List<SafeMember>>> load)
    {
        InitializeComponent();
        _load = load;
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
        if (MembersGrid.SelectedItem is not SafeMemberRow row)
        {
            RightsText.Text = "";
            return;
        }

        var granted = row.Permissions.Granted().Select(SafePermissionText.Label).ToList();
        RightsText.Text = Text.Format(Strings.SafeMembersRights, row.Name, granted.Count == 0 ? Strings.SafeMembersNoRight : string.Join(", ", granted));
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
