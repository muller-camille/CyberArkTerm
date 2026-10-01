using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CyberArkTerm.Core;

namespace CyberArkTerm.App.Views;

/// <summary>Onglet « Courants » : serveurs de travail de l'utilisateur, rangés en dossiers, chacun avec sa configuration.</summary>
public partial class MainWindow
{
    private const string SavedDragFormat = "CyberArkTerm.SavedItem";
    private const string AccountDragFormat = "CyberArkTerm.Account";

    private readonly HashSet<string> _collapsedFolders = new(StringComparer.OrdinalIgnoreCase);
    private Point _dragStart;
    private object? _dragCandidate;

    private string PvwaHost => _client.BaseUri.Host;

    private void RefreshSaved()
    {
        if (SavedTree.ItemsSource is IEnumerable<object> previous)
        {
            RememberExpansion(previous);
        }

        var root = SessionLibrary.BuildTree(_settings, PvwaHost);
        SavedTree.ItemsSource = Children(root);
        NoSavedText.Visibility = root.TotalSessions == 0 && root.Folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private List<object> Children(SessionFolderNode node)
    {
        var items = new List<object>();
        foreach (var folder in node.Folders)
        {
            items.Add(new SavedFolderNode(folder.Path, Children(folder), !_collapsedFolders.Contains(folder.Path)) { Count = folder.TotalSessions });
        }

        foreach (var session in node.Sessions)
        {
            items.Add(new SavedSessionNode(session, _byId.GetValueOrDefault(session.AccountId)));
        }

        return items;
    }

    private void RememberExpansion(IEnumerable<object> nodes)
    {
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
        if (SavedTree.ItemsSource is IEnumerable<object> previous)
        {
            RememberExpansion(previous);
        }

        SaveSettings();
        RefreshSaved();
    }

    // ===================== Sélection et connexion =====================

    private void OnSavedSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is SavedSessionNode node)
        {
            SetCurrent(node.Account, node.Session);
        }
        else
        {
            SetCurrent(null);
        }
    }

    private void OnSavedItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeViewItem { DataContext: SavedSessionNode node })
        {
            e.Handled = true;
            ConnectSaved(node.Session, advanced: false);
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
            case Key.F2:
                e.Handled = true;
                RenameSelectedSaved();
                break;
            case Key.Delete:
                e.Handled = true;
                DeleteSelectedSaved();
                break;
        }
    }

    /// <summary>Ouvre le serveur avec sa configuration (mode, composant, machine, motif, dossier SFTP).</summary>
    private void ConnectSaved(SavedSession saved, bool advanced)
    {
        if (!_byId.TryGetValue(saved.AccountId, out var account))
        {
            SetStatus($"{saved.Name} : compte introuvable dans CyberArk (supprimé ou droits retirés).", isError: true);
            return;
        }

        var machines = AccountClassifier.RemoteMachineList(account);
        var request = new ConnectRequest(
            saved.Mode,
            string.IsNullOrWhiteSpace(saved.Component) ? _settings.ResolveComponent(account) : saved.Component,
            saved.RemoteMachine ?? (machines.Count == 1 ? machines[0] : null),
            saved.Reason);
        _ = ConnectAsync(account, request, showDialog: advanced, saved: saved);
    }

    private void OnConnectSelectedSaved(object sender, RoutedEventArgs e)
    {
        if (SavedTree.SelectedItem is SavedSessionNode node)
        {
            ConnectSaved(node.Session, advanced: false);
        }
    }

    private void OnConnectSelectedSavedAdvanced(object sender, RoutedEventArgs e)
    {
        if (SavedTree.SelectedItem is SavedSessionNode node)
        {
            ConnectSaved(node.Session, advanced: true);
        }
    }

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

    private void AddToCurrent(PvwaAccount account, string folder)
    {
        var session = SessionLibrary.AddSession(_settings, account, PvwaHost, folder);
        Expand(session.Folder);
        SaveAndRefreshSaved();
        SetStatus(session.Folder.Length > 0
            ? $"{session.Name} ajouté aux serveurs courants (dossier {session.Folder})"
            : $"{session.Name} ajouté aux serveurs courants");
    }

    /// <summary>Sous-menu « Ajouter aux serveurs courants » : racine, dossiers existants, nouveau dossier.</summary>
    private void BuildAddToCurrentMenu(MenuItem parent, PvwaAccount? account)
    {
        parent.Items.Clear();
        if (account is null)
        {
            return;
        }

        parent.Items.Add(MenuEntry("(racine)", () => AddToCurrent(account, "")));
        foreach (var folder in _settings.SessionFolderList.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            parent.Items.Add(MenuEntry(folder.Replace("/", " › "), () => AddToCurrent(account, folder)));
        }

        parent.Items.Add(new Separator());
        parent.Items.Add(MenuEntry("Nouveau dossier…", () =>
        {
            if (AskFolderName("Nouveau dossier", "", "") is { } path)
            {
                AddToCurrent(account, path);
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
        if (AskFolderName(parent.Length == 0 ? "Nouveau dossier" : $"Nouveau dossier dans {parent}", parent, "") is { } path)
        {
            SessionLibrary.AddFolder(_settings, path);
            Expand(path);
            SaveAndRefreshSaved();
        }
    }

    /// <summary>Demande un nom de dossier sous <paramref name="parent"/> ; renvoie le chemin complet.</summary>
    private string? AskFolderName(string title, string parent, string initial, string? except = null)
    {
        var dialog = new InputDialog(title, "Nom du dossier :", initial, value =>
        {
            if (value.Contains('/') || value.Contains('\\'))
            {
                return "Le nom ne doit pas contenir « / » ni « \\ ».";
            }

            var path = SessionFolders.Combine(parent, value);
            bool exists = _settings.SessionFolderList.Contains(path, StringComparer.OrdinalIgnoreCase);
            return exists && !string.Equals(path, except, StringComparison.OrdinalIgnoreCase) ? "Ce dossier existe déjà." : null;
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
                var dialog = new SessionPropertiesDialog(node.Session, node.Account, _settings.SessionFolderList, HasPsmp) { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    SessionLibrary.AddFolder(_settings, node.Session.Folder);
                    Expand(node.Session.Folder);
                    SaveAndRefreshSaved();
                    SetStatus($"Configuration de {node.Session.Name} enregistrée");
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
                var dialog = new InputDialog("Renommer", "Nom affiché :", node.Session.Name) { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    node.Session.Name = dialog.Value;
                    SaveAndRefreshSaved();
                }

                break;
            case SavedFolderNode folder:
                if (AskFolderName("Renommer le dossier", SessionFolders.Parent(folder.Path), folder.Name, except: folder.Path) is { } path)
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
                if (MessageBox.Show(this, $"Retirer « {node.Session.Name} » de vos serveurs courants ?\n\nLe compte reste disponible dans l'onglet « Disponibles ».",
                        "Serveurs courants", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _settings.Sessions.Remove(node.Session);
                    SaveAndRefreshSaved();
                }

                break;
            case SavedFolderNode folder:
                var message = folder.Count == 0
                    ? $"Supprimer le dossier « {folder.Path} » ?"
                    : $"Supprimer le dossier « {folder.Path} » et les {folder.Count} serveur(s) qu'il contient ?\n\nLes comptes restent disponibles dans l'onglet « Disponibles ».";
                if (MessageBox.Show(this, message, "Serveurs courants", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
                {
                    SessionLibrary.DeleteFolder(_settings, folder.Path);
                    SaveAndRefreshSaved();
                }

                break;
        }
    }

    // ===================== Glisser-déposer =====================

    private void OnDragSourceMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragCandidate = ItemUnder(e.OriginalSource)?.DataContext;
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
        var target = ItemUnder(e.OriginalSource)?.DataContext switch
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

    private static TreeViewItem? ItemUnder(object source)
    {
        var current = source as DependencyObject;
        while (current is not null and not TreeViewItem)
        {
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return current as TreeViewItem;
    }
}
