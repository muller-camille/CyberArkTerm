using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Services;
using CyberArkTerm.Core;
using CyberArkTerm.Core.Ssh;
using Microsoft.Win32;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Onglet latéral « Fichiers » (navigateur SFTP) : parcourt le serveur de la session SSH
/// active, dépose des fichiers par glisser-déposer (SCP ou SFTP), supprime, télécharge, et peut suivre
/// le dossier courant du terminal.
/// </summary>
public partial class FileBrowserPanel : UserControl
{
    private AppSettings _settings = new();
    private Action _saveSettings = () => { };
    private SshSession? _session;
    private IRemoteFiles? _browser;
    private int _generation;
    private int _busy;

    public FileBrowserPanel()
    {
        InitializeComponent();
        InitializeQueue();
        ShowMessage(Strings.NoSshSessionHelp, retry: false);
        HeaderText.Text = Strings.NoSshSession;
        UpdateToolbar();
    }

    /// <param name="history">Historique des transferts ; par défaut celui du profil Windows (les tests en donnent un en mémoire).</param>
    public void Initialize(AppSettings settings, Action saveSettings, TransferHistory? history = null)
    {
        _settings = settings;
        _saveSettings = saveSettings;
        _history = history ?? TransferHistory.Load(TransferHistory.DefaultPath);
        HiddenBox.IsChecked = settings.ShowHiddenFiles;
        ShowSortArrow();
    }

    /// <summary>Associe le panneau à la session de l'onglet actif (ou à aucune).</summary>
    public void Attach(SshSession? session)
    {
        if (ReferenceEquals(_session, session))
        {
            return;
        }

        if (_session is not null)
        {
            _session.TerminalDirectoryChanged -= OnTerminalDirectory;
            _session.StateChanged -= OnSessionStateChanged;
        }

        _session = session;
        _browser = null;
        _generation++;
        FileList.ItemsSource = null;
        PathBox.Text = "";
        StatusText.Text = "";
        UpdateExtractPanel();
        if (session is null)
        {
            UpdateTailFilesButton();
            HeaderText.Text = Strings.NoSshSession;
            ShowMessage(Strings.NoSshSessionHelp, retry: false);
            UpdateToolbar();
            return;
        }

        HeaderText.Text = session.Label;
        UpdateTailFilesButton();
        // Session de fichiers seuls (SFTP, FTP) : pas de terminal à suivre.
        FollowBox.Visibility = session.HasTerminal ? Visibility.Visible : Visibility.Collapsed;
        FollowBox.IsEnabled = session.CanFollowTerminal;
        FollowBox.IsChecked = session.CanFollowTerminal && session.FollowTerminal;
        FollowBox.ToolTip = session.CanFollowTerminal
            ? Strings.FollowTip
            : Strings.FollowDisabledTip;
        session.TerminalDirectoryChanged += OnTerminalDirectory;
        session.StateChanged += OnSessionStateChanged;
        _ = OpenAsync();
    }

    // ===================== Connexion et navigation =====================

    private async Task OpenAsync()
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        int generation = _generation;
        if (session.State != SshSessionState.Connected)
        {
            ShowMessage(session.State == SshSessionState.Connecting ? ConnectingText(session)
                : session.HasTerminal ? Strings.SshClosedBrowse
                : Strings.FilesClosedBrowse, retry: false);
            UpdateToolbar();
            return;
        }

        ShowMessage(Strings.SftpOpening, retry: false);
        try
        {
            var browser = await session.GetBrowserAsync();
            if (generation != _generation)
            {
                return;
            }

            _browser = browser;
            HideMessage();
            var start = session.BrowserDirectory
                ?? session.Saved?.StartDirectory
                ?? (session.FollowTerminal ? session.TerminalDirectory : null)
                ?? browser.HomeDirectory;
            await NavigateAsync(start);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (generation == _generation)
            {
                ShowMessage(Text.Format(Strings.SftpFailed, ErrorText.Describe(ex)), retry: true);
            }
        }
        finally
        {
            UpdateToolbar();
        }
    }

    /// <param name="quiet">
    /// Relecture après un transfert : la barre d'état garde le bilan des transferts (seule une erreur la remplace).
    /// </param>
    private async Task NavigateAsync(string path, bool quiet = false)
    {
        var browser = _browser;
        if (browser is null)
        {
            return;
        }

        int generation = _generation;
        if (!quiet)
        {
            SetStatus(Text.Format(Strings.Reading, path));
        }

        try
        {
            var entries = await browser.ListAsync(path, _settings.ShowHiddenFiles, CancellationToken.None);
            if (generation != _generation)
            {
                return;
            }

            var directory = browser.CurrentDirectory;
            var items = new List<RemoteEntry>(entries.Count + 1);
            if (directory != "/")
            {
                items.Add(RemoteEntry.ParentLink(directory));
            }

            items.AddRange(RemoteEntry.Sort(entries, _settings.FileSortColumn, _settings.FileSortDescending));
            FileList.ItemsSource = items;
            PathBox.Text = directory;
            if (_session is not null)
            {
                _session.BrowserDirectory = directory;
            }

            if (!quiet)
            {
                int folders = entries.Count(e => e.IsDirectory);
                SetStatus(Text.Format(Strings.FolderSummary, folders, entries.Count - folders));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (generation != _generation)
            {
                return;
            }

            PathBox.Text = browser.CurrentDirectory;
            if (!browser.IsConnected)
            {
                _browser = null;
                ShowMessage(Text.Format(Strings.SftpLost, ErrorText.Describe(ex)), retry: true);
            }
            else
            {
                SetStatus(Text.Format(Strings.CannotOpen, path, Describe(ex)), error: true);
            }
        }
    }

    private Task RefreshAsync() => _browser is null ? OpenAsync() : NavigateAsync(_browser.CurrentDirectory);

    private void OnTerminalDirectory(string directory)
    {
        if (FollowBox.IsChecked == true && _browser is not null && directory != _browser.CurrentDirectory)
        {
            _ = NavigateAsync(directory);
        }
    }

    private void OnSessionStateChanged()
    {
        if (_session is null)
        {
            return;
        }

        UpdateExtractPanel();

        if (_session.State == SshSessionState.Connected && _browser is null)
        {
            _ = OpenAsync();
        }
        else if (_session.State is SshSessionState.Failed or SshSessionState.Closed && _browser is null)
        {
            ShowMessage(NotConnectedText(_session), retry: false);
        }
        else if (_session.State == SshSessionState.Connecting && _browser is null)
        {
            ShowMessage(ConnectingText(_session), retry: false);
        }

        UpdateToolbar();
    }

    private static string NotConnectedText(SshSession session) =>
        session.HasTerminal ? Strings.SshNotConnectedBrowse : Strings.FilesClosedBrowse;

    private static string ConnectingText(SshSession session) =>
        session.HasTerminal ? Strings.SshConnecting : Text.Format(Strings.FilesConnectingBrowse, session.FilesProtocol);

    // ===================== Actions =====================

    private void OnParent(object sender, RoutedEventArgs e)
    {
        if (_browser is not null)
        {
            _ = NavigateAsync(RemotePath.Parent(_browser.CurrentDirectory));
        }
    }

    private void OnHome(object sender, RoutedEventArgs e)
    {
        if (_browser is not null)
        {
            _ = NavigateAsync(_browser.HomeDirectory);
        }
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => _ = RefreshAsync();

    private void OnRetry(object sender, RoutedEventArgs e) => _ = OpenAsync();

    private void OnPathKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _browser is not null && PathBox.Text.Trim().Length > 0)
        {
            e.Handled = true;
            _ = NavigateAsync(PathBox.Text.Trim());
        }
    }

    private void OnFollowChanged(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        _session.FollowTerminal = FollowBox.IsChecked == true;
        if (_session.FollowTerminal)
        {
            // (Ré)installe le suivi dans le shell courant : utile après « sudo -i » ou si l'installation automatique
            // n'a pas pu se faire (sans effet en double si le suivi est déjà actif).
            _session.InstallFolderTracking();
            if (_session.TerminalDirectory is { } dir)
            {
                _ = NavigateAsync(dir);
            }
        }
    }

    // ===================== Tri (clic sur un en-tête de colonne) =====================

    /// <summary>
    /// Tri par la colonne cliquée, puis dans l'autre sens au clic suivant (la taille et la date commencent par les plus
    /// grandes et les plus récentes). Dossiers toujours en tête ; le tri est gardé d'un dossier et d'une session à l'autre.
    /// </summary>
    private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Column: { } column, Role: not GridViewColumnHeaderRole.Padding })
        {
            return;
        }

        if (SortColumns().FirstOrDefault(c => c.Column == column) is not { Column: not null } clicked)
        {
            return;
        }

        var sort = clicked.Sort;
        SortBy(sort, sort == _settings.FileSortColumn
            ? !_settings.FileSortDescending
            : sort is RemoteSortColumn.Size or RemoteSortColumn.Modified);
    }

    internal void SortBy(RemoteSortColumn column, bool descending)
    {
        _settings.FileSortColumn = column;
        _settings.FileSortDescending = descending;
        _saveSettings();
        ShowSortArrow();
        if (FileList.ItemsSource is not IEnumerable<RemoteEntry> shown)
        {
            return;
        }

        var selected = FileList.SelectedItems.Cast<RemoteEntry>().ToList();
        var entries = shown.ToList();
        FileList.ItemsSource = entries.Where(e => e.IsParentLink)
            .Concat(RemoteEntry.Sort(entries.Where(e => !e.IsParentLink), column, descending))
            .ToList();
        foreach (var entry in selected)
        {
            FileList.SelectedItems.Add(entry);
        }

        if (selected.Count > 0)
        {
            FileList.ScrollIntoView(selected[0]);
        }
    }

    /// <summary>Colonnes triables : colonne de la liste, titre, tri correspondant.</summary>
    private (GridViewColumn Column, string Text, RemoteSortColumn Sort)[] SortColumns() =>
    [
        (NameColumn, Core.Localization.CoreStrings.ColumnName, RemoteSortColumn.Name),
        (SizeColumn, Strings.ColumnSize, RemoteSortColumn.Size),
        (ModifiedColumn, Strings.ColumnModified, RemoteSortColumn.Modified),
        (PermissionsColumn, Strings.ColumnPermissions, RemoteSortColumn.Permissions),
    ];

    /// <summary>
    /// Triangle vers le haut (croissant) ou vers le bas (décroissant) dans l'en-tête de la colonne de tri, dessiné pour
    /// ne pas dépendre de la police. Le sens est aussi dans <c>Tag</c> de l'en-tête.
    /// </summary>
    private void ShowSortArrow()
    {
        foreach (var (column, text, sort) in SortColumns())
        {
            if (sort != _settings.FileSortColumn)
            {
                column.Header = text;
                continue;
            }

            bool descending = _settings.FileSortDescending;
            var arrow = new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse(descending ? "M0,0 L8,0 L4,4.5 Z" : "M0,4.5 L8,4.5 L4,0 Z"),
                Fill = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            column.Header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Tag = descending,
                Children = { new TextBlock { Text = text }, arrow },
            };
        }
    }

    private void OnHiddenChanged(object sender, RoutedEventArgs e)
    {
        _settings.ShowHiddenFiles = HiddenBox.IsChecked == true;
        _saveSettings();
        _ = RefreshAsync();
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem { DataContext: RemoteEntry entry })
        {
            e.Handled = true;
            Open(entry);
        }
    }

    private void OnOpenSelected(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is RemoteEntry entry)
        {
            Open(entry);
        }
    }

    /// <summary>
    /// Double-clic, Entrée ou « Ouvrir » : un dossier s'affiche, un fichier s'ouvre dans l'éditeur (renvoyé au serveur à
    /// l'enregistrement) ; une archive, une image ou un exécutable est téléchargé.
    /// </summary>
    private void Open(RemoteEntry entry)
    {
        if (entry.IsDirectory)
        {
            _ = NavigateAsync(entry.FullPath);
        }
        else if (_session?.Editor is { } editor && _browser is not null && !EditedFile.LooksBinary(entry.Name))
        {
            _ = editor.EditAsync(entry);
        }
        else
        {
            RequestDownload([entry]);
        }
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when FileList.SelectedItem is RemoteEntry entry:
                Open(entry);
                e.Handled = true;
                break;
            case Key.Delete:
                _ = DeleteAsync();
                e.Handled = true;
                break;
            case Key.F4:
                OnEdit(sender, e);
                e.Handled = true;
                break;
            case Key.Back:
                OnParent(sender, e);
                e.Handled = true;
                break;
            case Key.F5:
                _ = RefreshAsync();
                e.Handled = true;
                break;
        }
    }

    private void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        var selected = SelectedEntries();
        bool files = selected.Count > 0 && selected.All(s => !s.IsDirectory);
        foreach (var item in ((ContextMenu)sender).Items.OfType<MenuItem>())
        {
            if (item.Tag is "tailadd")
            {
                FillTailAddMenu(item, files);
                continue;
            }

            if (item.Tag is "compare")
            {
                UpdateCompareMenu(item, selected);
                continue;
            }

            item.IsEnabled = item.Tag switch
            {
                "open" => selected.Count == 1,
                "edit" => selected is [{ IsDirectory: false }],
                "tail" => files,
                "download" => selected.Count > 0 && selected.All(s => !s.IsDirectory),
                "delete" or "copy" or "chmod" => selected.Count > 0,
                _ => _browser is not null,
            };
        }
    }

    private void OnCopyPath(object sender, RoutedEventArgs e)
    {
        var selected = SelectedEntries();
        if (selected.Count > 0)
        {
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, selected.Select(s => s.FullPath)));
            }
            catch (System.Runtime.InteropServices.COMException)
            {
            }
        }
    }

    private async void OnNewFolder(object sender, RoutedEventArgs e)
    {
        var browser = _browser;
        if (browser is null)
        {
            return;
        }

        var dialog = new InputDialog(Strings.NewFolder, Text.Format(Strings.NewRemoteFolderPrompt, browser.CurrentDirectory),
            validate: v => v.Contains('/') ? Strings.NameNoSlash : null) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await browser.CreateDirectoryAsync(RemotePath.Combine(browser.CurrentDirectory, dialog.Value), CancellationToken.None);
            await NavigateAsync(browser.CurrentDirectory);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SetStatus(Text.Format(Strings.CreateFailed, Describe(ex)), error: true);
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e) => _ = DeleteAsync();

    /// <summary>Le contenu d'un dossier du serveur a changé (fichier renvoyé depuis l'éditeur) : actualisation s'il est affiché.</summary>
    public void OnRemoteChanged(SshSession session, string directory)
    {
        if (ReferenceEquals(session, _session) && _browser is { } browser && browser.CurrentDirectory == directory && _busy == 0)
        {
            _ = NavigateAsync(directory);
        }
    }

    private async void OnEdit(object sender, RoutedEventArgs e)
    {
        if (_session?.Editor is { } editor && _browser is not null && SelectedEntries() is [{ IsDirectory: false } entry])
        {
            await editor.EditAsync(entry);
        }
    }

    private void OnPermissions(object sender, RoutedEventArgs e) => _ = ChangePermissionsAsync();

    private async Task ChangePermissionsAsync()
    {
        var browser = _browser;
        var selected = SelectedEntries();
        if (browser is null || selected.Count == 0 || _busy > 0)
        {
            return;
        }

        var target = selected.Count == 1 ? selected[0].Name : Text.Format(Strings.ItemsCount, selected.Count);
        int mode = UnixPermissions.FromSymbolic(selected[0].Permissions);
        if (selected[0].IsSymbolicLink)
        {
            // La liste montre les droits du lien lui-même (lrwxrwxrwx) alors que chmod change ceux de sa cible :
            // partir de 777 rendrait la cible accessible à tous si l'on validait sans rien changer.
            int? targetMode;
            try
            {
                targetMode = await browser.GetModeAsync(selected[0].FullPath, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                SetStatus(Text.Format(Strings.PermissionsFailed, Describe(ex)), error: true);
                return;
            }

            if (targetMode is not { } resolved)
            {
                SetStatus(Text.Format(Strings.PermissionsLinkUnknown, selected[0].Name), error: true);
                return;
            }

            mode = resolved;
        }

        var dialog = new PermissionsDialog(target, browser.CurrentDirectory, mode, selected.Any(s => s.IsDirectory && !s.IsSymbolicLink))
        {
            Owner = Window.GetWindow(this),
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var octal = UnixPermissions.ToOctal(dialog.Mode);
        var errors = new List<string>();
        int changed = 0;
        Interlocked.Increment(ref _busy);
        try
        {
            foreach (var entry in selected)
            {
                SetStatus(Text.Format(Strings.PermissionsApplying, entry.Name));
                int before = changed;
                var progress = new Progress<int>(n => SetStatus(Text.Format(Strings.PermissionsProgress, before + n)));
                try
                {
                    // Pas de propagation à travers un lien symbolique sélectionné (seule sa cible change de droits).
                    bool recursive = dialog.Recursive && entry.IsDirectory && !entry.IsSymbolicLink;
                    var result = await browser.SetPermissionsAsync(entry.FullPath, dialog.Mode, dialog.SpecialChanged,
                        recursive, dialog.ExecuteOnlyIfAlready, progress, CancellationToken.None);
                    changed += result.Changed;
                    errors.AddRange(result.Errors);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    errors.Add(Text.Format(Strings.ItemError, entry.Name, Describe(ex)));
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _busy);
        }

        await NavigateAsync(browser.CurrentDirectory);
        if (errors.Count > 0)
        {
            SetStatus(Text.Format(Strings.PermissionsFailed, string.Join(" ; ", errors.Take(5))), error: true);
        }
        else
        {
            SetStatus(dialog.Recursive
                ? Text.Format(Strings.PermissionsDoneCount, octal, changed)
                : Text.Format(Strings.PermissionsDone, octal, target));
        }
    }

    private async Task DeleteAsync()
    {
        var browser = _browser;
        var selected = SelectedEntries();
        if (browser is null || selected.Count == 0 || _busy > 0)
        {
            return;
        }

        var names = string.Join("\n", selected.Take(10).Select(s => "  • " + s.Name + (s.IsDirectory ? "/" : "")));
        if (selected.Count > 10)
        {
            names += "\n" + Text.Format(Strings.AndMore, selected.Count - 10);
        }

        var answer = MessageBox.Show(Window.GetWindow(this),
            Text.Format(Strings.DeleteConfirm, browser.CurrentDirectory, names),
            Strings.DeleteTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var errors = new List<string>();
        Interlocked.Increment(ref _busy);
        try
        {
            foreach (var entry in selected)
            {
                SetStatus(Text.Format(Strings.Deleting, entry.Name));
                try
                {
                    await browser.DeleteAsync(entry, CancellationToken.None);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    errors.Add(Text.Format(Strings.ItemError, entry.Name, Describe(ex, entry.IsDirectory)));
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _busy);
        }

        await NavigateAsync(browser.CurrentDirectory);
        if (errors.Count > 0)
        {
            SetStatus(Text.Format(Strings.DeleteIncomplete, string.Join(" ; ", errors)), error: true);
        }
        else
        {
            SetStatus(Text.Format(Strings.Deleted, selected.Count));
        }
    }

    // ===================== Envoi (glisser-déposer) et téléchargement =====================

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        bool ok = _browser is not null && e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        if (ok)
        {
            DropHintText.Text = Text.Format(_queue.ActiveCount > 0 ? Strings.DropHintQueued : Strings.DropHint, Protocol, _browser!.CurrentDirectory);
            DropHint.Visibility = Visibility.Visible;
        }

        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => DropHint.Visibility = Visibility.Collapsed;

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            _ = EnqueueUploadAsync(paths);
        }
    }

    private void OnUploadDialog(object sender, RoutedEventArgs e)
    {
        if (_browser is null)
        {
            return;
        }

        var dialog = new OpenFileDialog { Title = Text.Format(Strings.UploadTo, _browser.CurrentDirectory), Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            _ = EnqueueUploadAsync(dialog.FileNames);
        }
    }

    private string Protocol => ProtocolOf(_browser);

    /// <summary>Protocole des envois vers ce serveur : celui des Paramètres (SCP ou SFTP), sinon le seul possible (FTP, FTPS).</summary>
    private string ProtocolOf(IRemoteFiles? browser) =>
        browser is { ChoosesUploadProtocol: false } ? browser.UploadProtocol.Label() : _settings.PreferredUploadProtocol.Label();

    private void OnDownload(object sender, RoutedEventArgs e) =>
        RequestDownload(SelectedEntries().Where(s => !s.IsDirectory).ToList());

    /// <summary>Demande où enregistrer, puis met le téléchargement en file.</summary>
    private void RequestDownload(IReadOnlyList<RemoteEntry> files)
    {
        if (_browser is null || files.Count == 0)
        {
            return;
        }

        string folder;
        string? singleTarget = null;
        if (files.Count == 1)
        {
            var save = new SaveFileDialog { Title = Strings.DownloadTitle, FileName = WindowsFileName.Sanitize(files[0].Name) };
            if (save.ShowDialog(Window.GetWindow(this)) != true)
            {
                return;
            }

            singleTarget = save.FileName;
            folder = Path.GetDirectoryName(save.FileName)!;
        }
        else
        {
            var pick = new OpenFolderDialog { Title = Text.Format(Strings.DownloadManyTitle, files.Count) };
            if (pick.ShowDialog(Window.GetWindow(this)) != true)
            {
                return;
            }

            folder = pick.FolderName;
        }

        EnqueueDownload(files, folder, singleTarget);
    }

    // ===================== Vérification des transferts =====================

    /// <summary>
    /// Bilan de la vérification SHA-256 après des transferts, dans la barre d'état ; le détail de chaque transfert est
    /// dans l'historique. Si un fichier diffère de l'original, l'erreur est affichée et le détail s'ouvre de lui-même.
    /// </summary>
    /// <param name="note">Ajouté à la fin du bilan (protocole refusé par le serveur…).</param>
    private void ReportChecks(string done, IReadOnlyList<TransferCheck> checks, bool error = false, string? note = null)
    {
        int different = checks.Count(c => c.Verified && !c.Matches);
        if (different > 0)
        {
            SetStatus(Text.Format(Strings.TransferMismatch, different), error: true);
            new TransferChecksDialog(checks) { Owner = Window.GetWindow(this) }.ShowDialog();
            return;
        }

        int verified = checks.Count(c => c.Matches);
        // Fichiers transférés mais non relus (droits, vérification annulée) ; pas ceux en échec ou interrompus.
        int unverified = checks.Count(c => !c.Verified && !c.Failed && !c.Interrupted);
        var parts = new List<string> { done };
        if (verified > 0)
        {
            parts.Add(checks.Count == 1 ? Text.Format(Strings.TransferVerifiedOne, checks[0].RemoteHash[..12]) : Text.Format(Strings.TransferVerified, verified));
        }

        if (unverified > 0)
        {
            parts.Add(Text.Format(Strings.TransferNotVerified, unverified));
        }

        if (note is not null)
        {
            parts.Add(note);
        }

        SetStatus(string.Join(" · ", parts), error);
    }

    // ===================== Utilitaires =====================

    private List<RemoteEntry> SelectedEntries() =>
        FileList.SelectedItems.OfType<RemoteEntry>().Where(e => !e.IsParentLink).ToList();

    private static string Describe(Exception ex, bool directory = false) => ex switch
    {
        Renci.SshNet.Common.SftpPermissionDeniedException => Strings.PermissionDenied,
        Renci.SshNet.Common.SftpPathNotFoundException => Strings.PathNotFound,
        Renci.SshNet.Common.SshException when directory => Strings.DirectoryNotEmpty,
        _ => ErrorText.Describe(ex),
    };

    private void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = error ? System.Windows.Media.Brushes.Firebrick : (System.Windows.Media.Brush)FindResource("MutedBrush");
    }

    private void ShowMessage(string text, bool retry)
    {
        MessageText.Text = text;
        RetryButton.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
        MessagePanel.Visibility = Visibility.Visible;
    }

    private void HideMessage() => MessagePanel.Visibility = Visibility.Collapsed;

    private void UpdateToolbar()
    {
        bool ready = _browser is not null;
        Toolbar.IsEnabled = ready;
        PathBox.IsEnabled = ready;
        HiddenBox.IsEnabled = ready;
    }
}
