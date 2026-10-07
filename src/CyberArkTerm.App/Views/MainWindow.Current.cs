using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core;

namespace CyberArkTerm.App.Views;

/// <summary>Onglet « Courants » : serveurs de travail de l'utilisateur, rangés en dossiers, chacun avec sa configuration.</summary>
public partial class MainWindow
{
    private const string SavedDragFormat = "CyberArkTerm.SavedItem";
    private const string AccountDragFormat = "CyberArkTerm.Account";

    private readonly HashSet<string> _collapsedFolders = new(StringComparer.OrdinalIgnoreCase);
    private bool _savedTreeFiltered;
    private Point _dragStart;
    private object? _dragCandidate;

    private string PvwaHost => _client?.BaseUri.Host ?? "";

    private void RefreshSaved()
    {
        RememberSavedExpansion();

        // Coffres KeePass en tête ; serveurs CyberArk ensuite (pas en accès d'urgence).
        var filter = SavedSearchBox.Text.Trim();
        bool filtered = filter.Length > 0;
        var items = KeePassNodes(filtered ? filter : null);
        if (!IsOffline)
        {
            items.AddRange(SharedNodes(filtered ? filter : null));
            items.AddRange(Children(SessionLibrary.BuildTree(_settings, PvwaHost, filter), expandAll: filtered));
        }

        SavedTree.ItemsSource = items;
        _savedTreeFiltered = filtered;
        NoSavedText.Text = filtered ? Text.Format(Strings.SavedSearchNoMatch, filter) : Strings.NoSavedHelp;
        NoSavedText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Mémorise les dossiers repliés par l'utilisateur, sauf pendant une recherche : les dossiers des résultats sont
    /// alors tous dépliés, ce qui n'est pas un choix de l'utilisateur.
    /// </summary>
    private void RememberSavedExpansion()
    {
        if (!_savedTreeFiltered && SavedTree.ItemsSource is IEnumerable<object> previous)
        {
            RememberExpansion(previous);
            RememberSharedExpansion(previous);
        }
    }

    private List<object> Children(SessionFolderNode node, bool expandAll)
    {
        var items = new List<object>();
        foreach (var folder in node.Folders)
        {
            bool expanded = expandAll || !_collapsedFolders.Contains(folder.Path);
            items.Add(new SavedFolderNode(folder.Path, Children(folder, expandAll), expanded) { Count = folder.TotalSessions });
        }

        foreach (var session in node.Sessions)
        {
            items.Add(new SavedSessionNode(session, _byId.GetValueOrDefault(session.AccountId)) { IsMarked = _savedMarks.Contains(session) });
        }

        return items;
    }

    private void RememberExpansion(IEnumerable<object> nodes)
    {
        foreach (var node in nodes.Where(n => n is KeePassFolderNode or KeePassGroupNode))
        {
            RememberKeePassExpansion(node);
        }

        foreach (var folder in nodes.OfType<SavedFolderNode>())
        {
            if (folder.IsExpanded)
            {
                _collapsedFolders.Remove(folder.Path);
            }
            else
            {
                _collapsedFolders.Add(folder.Path);
            }

            RememberExpansion(folder.Children);
        }
    }

    private void Expand(string folder)
    {
        for (var path = SessionFolders.Normalize(folder); path.Length > 0; path = SessionFolders.Parent(path))
        {
            _collapsedFolders.Remove(path);
        }
    }

    private void SaveAndRefreshSaved()
    {
        RememberSavedExpansion();
        SaveSettings();
        RefreshSaved();
    }

    // ===================== Recherche =====================

    private void OnSavedSearchChanged(object sender, TextChangedEventArgs e) => RefreshSaved();

    private void OnSavedSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Enter)
        {
            // Comme dans « Disponibles » : premier résultat sélectionné, Entrée une seconde fois pour se connecter.
            SavedTree.UpdateLayout();
            if (FirstSavedLeaf(SavedTree) is { } item)
            {
                item.IsSelected = true;
                item.Focus();
                item.BringIntoView();
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SavedSearchBox.Text.Length > 0)
        {
            SavedSearchBox.Clear();
            e.Handled = true;
        }
    }

    /// <summary>Premier serveur ou entrée KeePass visible, dans les dossiers dépliés seulement.</summary>
    private static TreeViewItem? FirstSavedLeaf(ItemsControl parent)
    {
        foreach (var data in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(data) is not TreeViewItem item)
            {
                continue;
            }

            if (data is SavedSessionNode or SharedServerNode or KeePassEntryNode)
            {
                return item;
            }

            if (item.IsExpanded && item.HasItems)
            {
                item.UpdateLayout();
                if (FirstSavedLeaf(item) is { } found)
                {
                    return found;
                }
            }
        }

        return null;
    }

    // ===================== Sélection et connexion =====================

    private void OnSavedSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => SetCurrentFrom(e.NewValue);

    private void OnSavedItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // L'événement remonte aux éléments parents : seul l'élément double-cliqué compte.
        if (sender is not TreeViewItem { IsSelected: true } item)
        {
            return;
        }

        if (item.DataContext is SavedSessionNode node)
        {
            e.Handled = true;
            ConnectSaved(node.Session, advanced: false);
        }
        else if (item.DataContext is SharedServerNode shared)
        {
            e.Handled = true;
            ConnectSaved(shared.Session, advanced: false);
        }
        else if (item.DataContext is KeePassEntryNode or KeePassHintNode or KeePassFolderNode { IsUnlocked: false })
        {
            e.Handled = true;
            ActivateKeePassNode(item.DataContext);
        }
    }

    private void OnSavedKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when SavedTree.SelectedItem is SavedSessionNode node:
                e.Handled = true;
                ConnectSaved(node.Session, advanced: false);
                break;
            case Key.Enter when SavedTree.SelectedItem is SharedServerNode shared:
                e.Handled = true;
                ConnectSaved(shared.Session, advanced: false);
                break;
            case Key.Delete when SavedTree.SelectedItem is SharedServerNode or SharedFolderNode:
                e.Handled = true;
                _ = RemoveSharedAsync(SavedTree.SelectedItem);
                break;
            case Key.F2 or Key.Delete when SavedTree.SelectedItem is SharedListNode:
                e.Handled = true;
                break;
            case Key.Enter:
                e.Handled = ActivateKeePassNode(SavedTree.SelectedItem);
                break;
            case Key.F2 when SavedTree.SelectedItem is KeePassEntryNode entry:
                e.Handled = true;
                _ = EditKeePassEntryAsync(entry);
                break;
            case Key.Delete when SavedTree.SelectedItem is KeePassEntryNode entry:
                e.Handled = true;
                _ = DeleteKeePassEntryAsync(entry);
                break;
            case Key.F2:
                e.Handled = true;
                RenameSelectedSaved();
                break;
            case Key.Delete:
                e.Handled = true;
                DeleteSelectedSaved();
                break;
            case Key.Escape when _savedMarks.Count > 0:
                e.Handled = true;
                ClearSavedMarks();
                break;
        }
    }

    /// <summary>Ouvre le serveur avec sa configuration (mode, composant, machine, motif, dossier SFTP).</summary>
    private void ConnectSaved(SavedSession saved, bool advanced)
    {
        // Serveur d'une liste partagée créée pour un autre PVWA : son ID de compte ne vaut pas ici.
        if (!SessionLibrary.IsForHost(saved, PvwaHost))
        {
            SetStatus(Text.Format(Strings.SharedOtherPvwaConnect, saved.Name, saved.PvwaHost, PvwaHost), isError: true);
            return;
        }

        if (!_byId.TryGetValue(saved.AccountId, out var account))
        {
            SetStatus(MissingAccountText(Text.Format(Strings.SavedAccountGone, saved.Name)), isError: true);
            return;
        }

        if (!ConfirmSharedTarget(saved, account))
        {
            return;
        }

        _ = ConnectAsync(account, SavedRequest(saved, account), showDialog: advanced, saved: saved);
    }

    private void OnSafeMembersOfSelectedSaved(object sender, RoutedEventArgs e)
    {
        switch (SavedTree.SelectedItem)
        {
            case SavedSessionNode node:
                ShowSafeMembers(node.Account?.SafeName ?? node.Session.SafeName);
                break;
            case SharedServerNode shared:
                ShowSafeMembers(shared.Account?.SafeName ?? shared.Session.SafeName);
                break;
        }
    }

    private void OnConnectSelectedSaved(object sender, RoutedEventArgs e)
    {
        if (SelectedSavedSession() is { } session)
        {
            ConnectSaved(session, advanced: false);
        }
    }

    private void OnConnectSelectedSavedAdvanced(object sender, RoutedEventArgs e)
    {
        if (SelectedSavedSession() is { } session)
        {
            ConnectSaved(session, advanced: true);
        }
    }

    /// <summary>Serveur sélectionné, de « Mes serveurs » ou d'une liste partagée.</summary>
    private SavedSession? SelectedSavedSession() => SavedTree.SelectedItem switch
    {
        SavedSessionNode node => node.Session,
        SharedServerNode shared => shared.Session,
        _ => null,
    };

    // ===================== Ajout depuis « Disponibles » =====================

    private void OnAddToCurrent(object sender, RoutedEventArgs e)
    {
        if (_current is { } account && _currentSaved is null)
        {
            AddToCurrent(account, SelectedSavedFolder() ?? "");
        }
    }

    private string? SelectedSavedFolder() => SavedTree.SelectedItem switch
    {
        SavedFolderNode folder => folder.Path,
        SavedSessionNode node => node.Session.Folder,
        _ => null,
    };

    private void AddToCurrent(PvwaAccount account, string folder) =>
        ShowAddedToCurrent(SessionLibrary.AddSession(_settings, account, PvwaHost, folder));

    /// <summary>Connexion récente ajoutée aux « Courants » avec son mode, son composant et sa machine cible.</summary>
    private void AddToCurrent(PvwaAccount account, RecentSession recent, string folder) =>
        ShowAddedToCurrent(SessionLibrary.AddFromRecent(_settings, account, recent, PvwaHost, folder));

    private void ShowAddedToCurrent(SavedSession session)
    {
        Expand(session.Folder);
        SaveAndRefreshSaved();
        SetStatus(session.Folder.Length > 0
            ? Text.Format(Strings.AddedToMyServersFolder, session.Name, session.Folder)
            : Text.Format(Strings.AddedToMyServers, session.Name));
    }

    /// <summary>
    /// Sous-menu « Ajouter aux serveurs courants » : racine, dossiers existants, nouveau dossier. <paramref name="add"/>
    /// reçoit le dossier choisi ; null = rien à ajouter.
    /// </summary>
    private void BuildAddToCurrentMenu(MenuItem parent, Action<string>? add)
    {
        parent.Items.Clear();
        if (add is null)
        {
            return;
        }

        parent.Items.Add(MenuEntry(Strings.RootFolder, () => add("")));
        foreach (var folder in _settings.SessionFolderList.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            parent.Items.Add(MenuEntry(folder.Replace("/", " › "), () => add(folder)));
        }

        parent.Items.Add(new Separator());
        parent.Items.Add(MenuEntry(Strings.NewFolderEntry, () =>
        {
            if (AskFolderName(Strings.NewFolder, "", "") is { } path)
            {
                add(path);
            }
        }));
    }

    private static MenuItem MenuEntry(string text, Action action)
    {
        // TextBlock plutôt que texte brut : un « _ » dans un nom de dossier n'est pas un raccourci clavier.
        var item = new MenuItem { Header = new TextBlock { Text = text } };
        item.Click += (_, _) => action();
        return item;
    }

    // ===================== Dossiers =====================

    private void OnNewFolder(object sender, RoutedEventArgs e) =>
        CreateFolder(SavedTree.SelectedItem is SavedFolderNode folder ? folder.Path : "");

    private void OnNewSubFolder(object sender, RoutedEventArgs e) =>
        CreateFolder(SavedTree.SelectedItem is SavedFolderNode folder ? folder.Path : "");

    private void CreateFolder(string parent)
    {
        if (AskFolderName(parent.Length == 0 ? Strings.NewFolder : Text.Format(Strings.NewFolderIn, parent), parent, "") is { } path)
        {
            SessionLibrary.AddFolder(_settings, path);
            Expand(path);
            SaveAndRefreshSaved();
        }
    }

    /// <summary>Demande un nom de dossier sous <paramref name="parent"/> ; renvoie le chemin complet.</summary>
    private string? AskFolderName(string title, string parent, string initial, string? except = null)
    {
        var dialog = new InputDialog(title, Strings.FolderNameLabel, initial, value =>
        {
            if (value.Contains('/') || value.Contains('\\'))
            {
                return Strings.FolderNameNoSlashes;
            }

            var path = SessionFolders.Combine(parent, value);
            bool exists = _settings.SessionFolderList.Contains(path, StringComparer.OrdinalIgnoreCase);
            return exists && !string.Equals(path, except, StringComparison.OrdinalIgnoreCase) ? Strings.FolderExists : null;
        })
        { Owner = this };
        return dialog.ShowDialog() == true ? SessionFolders.Combine(parent, dialog.Value) : null;
    }

    // ===================== Propriétés, renommage, suppression =====================

    private void OnEditSelectedSaved(object sender, RoutedEventArgs e)
    {
        switch (SavedTree.SelectedItem)
        {
            case SavedSessionNode node:
                var dialog = new SessionPropertiesDialog(node.Session, node.Account, _settings.SessionFolderList, HasPsmp,
                    _settings.KnownComponents(node.Account?.PlatformId ?? node.Session.PlatformId)) { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    SessionLibrary.AddFolder(_settings, node.Session.Folder);
                    Expand(node.Session.Folder);
                    SaveAndRefreshSaved();
                    SetStatus(Text.Format(Strings.SavedSettingsSaved, node.Session.Name));
                }

                break;
            case SavedFolderNode:
                RenameSelectedSaved();
                break;
        }
    }

    private void OnRenameSelectedSaved(object sender, RoutedEventArgs e) => RenameSelectedSaved();

    private void RenameSelectedSaved()
    {
        switch (SavedTree.SelectedItem)
        {
            case SavedSessionNode node:
                var dialog = new InputDialog(Strings.RenameTitle, Strings.DisplayNameLabel, node.Session.Name) { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    node.Session.Name = dialog.Value;
                    SaveAndRefreshSaved();
                }

                break;
            case SavedFolderNode folder:
                if (AskFolderName(Strings.RenameFolderTitle, SessionFolders.Parent(folder.Path), folder.Name, except: folder.Path) is { } path)
                {
                    bool collapsed = _collapsedFolders.Contains(folder.Path);
                    SessionLibrary.RenameFolder(_settings, folder.Path, SessionFolders.Name(path));
                    if (!collapsed)
                    {
                        Expand(path);
                    }

                    SaveAndRefreshSaved();
                }

                break;
        }
    }

    private void OnDeleteSelectedSaved(object sender, RoutedEventArgs e) => DeleteSelectedSaved();

    private void DeleteSelectedSaved()
    {
        switch (SavedTree.SelectedItem)
        {
            case SavedSessionNode node:
                if (MessageBox.Show(this, Text.Format(Strings.RemoveSavedConfirm, node.Session.Name),
                        Strings.MyServers, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _settings.Sessions.Remove(node.Session);
                    SaveAndRefreshSaved();
                }

                break;
            case SavedFolderNode folder:
                var message = folder.Count == 0
                    ? Text.Format(Strings.DeleteEmptyFolderConfirm, folder.Path)
                    : Text.Format(Strings.DeleteFolderConfirm, folder.Path, folder.Count);
                if (MessageBox.Show(this, message, Strings.MyServers, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
                {
                    SessionLibrary.DeleteFolder(_settings, folder.Path);
                    SaveAndRefreshSaved();
                }

                break;
            case SharedServerNode or SharedFolderNode:
                _ = RemoveSharedAsync(SavedTree.SelectedItem);
                break;
        }
    }

    // ===================== Glisser-déposer =====================

    private void OnDragSourceMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragCandidate = ItemUnder<TreeViewItem>(e.OriginalSource)?.DataContext;
    }

    private void OnAvailableDragMove(object sender, MouseEventArgs e)
    {
        if (ShouldStartDrag(e) && _dragCandidate is AccountNode node)
        {
            _dragCandidate = null;
            DragDrop.DoDragDrop(SessionTree, new DataObject(AccountDragFormat, node.Account), DragDropEffects.Copy);
        }
    }

    private void OnSavedDragMove(object sender, MouseEventArgs e)
    {
        if (ShouldStartDrag(e) && _dragCandidate is SavedSessionNode or SavedFolderNode)
        {
            var item = _dragCandidate;
            _dragCandidate = null;
            DragDrop.DoDragDrop(SavedTree, new DataObject(SavedDragFormat, item), DragDropEffects.Move);
        }
    }

    private bool ShouldStartDrag(MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is null)
        {
            return false;
        }

        var delta = e.GetPosition(null) - _dragStart;
        return Math.Abs(delta.X) > SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(delta.Y) > SystemParameters.MinimumVerticalDragDistance;
    }

    /// <summary>Un compte glissé depuis « Disponibles » sur l'onglet « Courants » ouvre cet onglet.</summary>
    private void OnCurrentTabDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(AccountDragFormat))
        {
            SideTabs.SelectedItem = CurrentTab;
        }
    }

    private void OnSavedDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(AccountDragFormat) ? DragDropEffects.Copy
            : e.Data.GetDataPresent(SavedDragFormat) ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnSavedDrop(object sender, DragEventArgs e)
    {
        var over = ItemUnder<TreeViewItem>(e.OriginalSource)?.DataContext;
        if (SharedListOf(over) is { } sharedList)
        {
            _ = DropOnSharedAsync(sharedList, over!, e.Data);
            return;
        }

        var target = over switch
        {
            SavedFolderNode folder => folder.Path,
            SavedSessionNode node => node.Session.Folder,
            _ => "",
        };

        if (e.Data.GetData(AccountDragFormat) is PvwaAccount account)
        {
            AddToCurrent(account, target);
            return;
        }

        switch (e.Data.GetData(SavedDragFormat))
        {
            case SavedSessionNode node when !string.Equals(node.Session.Folder, target, StringComparison.OrdinalIgnoreCase):
                SessionLibrary.MoveSession(_settings, node.Session, target);
                break;
            case SavedFolderNode folder:
                var destination = SessionFolders.Combine(target, folder.Name);
                if (string.Equals(destination, folder.Path, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                try
                {
                    SessionLibrary.MoveFolder(_settings, folder.Path, destination);
                }
                catch (ArgumentException ex)
                {
                    SetStatus(ex.Message, isError: true);
                    return;
                }

                break;
            default:
                return;
        }

        Expand(target);
        SaveAndRefreshSaved();
    }

    /// <summary>Ligne (<typeparamref name="T"/>) qui contient l'élément cliqué, texte (Run) compris.</summary>
    private static T? ItemUnder<T>(object source) where T : DependencyObject
    {
        var current = source as DependencyObject;
        while (current is not null and not T)
        {
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return current as T;
    }
}
