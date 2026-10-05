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
    private RemoteFileBrowser? _browser;
    private int _generation;
    private int _busy;
    private IReadOnlyList<TransferCheck> _lastChecks = [];
    private bool _lastChecksUpload;

    public FileBrowserPanel()
    {
        InitializeComponent();
        ShowMessage(Strings.NoSshSessionHelp, retry: false);
        HeaderText.Text = Strings.NoSshSession;
        UpdateToolbar();
    }

    public void Initialize(AppSettings settings, Action saveSettings)
    {
        _settings = settings;
        _saveSettings = saveSettings;
        HiddenBox.IsChecked = settings.ShowHiddenFiles;
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
        if (session is null)
        {
            HeaderText.Text = Strings.NoSshSession;
            ShowMessage(Strings.NoSshSessionHelp, retry: false);
            UpdateToolbar();
            return;
        }

        HeaderText.Text = session.Label;
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
            ShowMessage(session.State == SshSessionState.Connecting
                ? Strings.SshConnecting
                : Strings.SshClosedBrowse, retry: false);
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

    private async Task NavigateAsync(string path)
    {
        var browser = _browser;
        if (browser is null)
        {
            return;
        }

        int generation = _generation;
        SetStatus(Text.Format(Strings.Reading, path));
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

            items.AddRange(entries);
            FileList.ItemsSource = items;
            PathBox.Text = directory;
            if (_session is not null)
            {
                _session.BrowserDirectory = directory;
            }

            int folders = entries.Count(e => e.IsDirectory);
            SetStatus(Text.Format(Strings.FolderSummary, folders, entries.Count - folders));
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

        if (_session.State == SshSessionState.Connected && _browser is null)
        {
            _ = OpenAsync();
        }
        else if (_session.State is SshSessionState.Failed or SshSessionState.Closed && _browser is null)
        {
            ShowMessage(Strings.SshNotConnectedBrowse, retry: false);
        }

        UpdateToolbar();
    }

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

    private void Open(RemoteEntry entry)
    {
        if (entry.IsDirectory)
        {
            _ = NavigateAsync(entry.FullPath);
        }
        else
        {
            _ = DownloadAsync([entry]);
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
        foreach (var item in ((ContextMenu)sender).Items.OfType<MenuItem>())
        {
            item.IsEnabled = item.Tag switch
            {
                "open" => selected.Count == 1,
                "edit" => selected is [{ IsDirectory: false }],
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
            DropHintText.Text = Text.Format(Strings.DropHint, Protocol, _browser!.CurrentDirectory);
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
            _ = UploadAsync(paths);
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
            _ = UploadAsync(dialog.FileNames);
        }
    }

    private string Protocol => _settings.UploadProtocol == TransferProtocol.Scp ? "SCP" : "SFTP";

    private async Task UploadAsync(IReadOnlyList<string> paths)
    {
        var browser = _browser;
        if (browser is null || _busy > 0)
        {
            return;
        }

        var directory = browser.CurrentDirectory;
        var existing = (FileList.ItemsSource as IEnumerable<RemoteEntry> ?? [])
            .Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        var conflicts = paths.Select(p => Path.GetFileName(p.TrimEnd('\\', '/'))).Where(existing.Contains).ToList();
        if (conflicts.Count > 0 && MessageBox.Show(Window.GetWindow(this),
                Text.Format(Strings.UploadConflicts, directory, string.Join("\n", conflicts.Take(10).Select(c => "  • " + c))),
                Strings.UploadTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        int done = 0;
        var checks = new List<TransferCheck>();
        HideChecks();
        Interlocked.Increment(ref _busy);
        TransferBar.Visibility = Visibility.Visible;
        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                var name = Path.GetFileName(paths[i].TrimEnd('\\', '/'));
                var sending = Text.Format(Strings.Uploading, Protocol, name, i + 1, paths.Count);
                SetStatus(sending);
                TransferBar.Value = 0;
                var progress = new Progress<TransferProgress>(p =>
                {
                    // Relecture sur le serveur pour la vérification SHA-256, puis envoi du fichier suivant d'un dossier.
                    var status = p.Verifying ? Text.Format(Strings.VerifyingFile, p.FileName) : sending;
                    if (StatusText.Text != status)
                    {
                        SetStatus(status);
                    }

                    TransferBar.Value = p.Total > 0 ? 100.0 * p.Transferred / p.Total : 0;
                });
                try
                {
                    checks.AddRange(await browser.UploadAsync(paths[i], directory, _settings.UploadProtocol, progress, CancellationToken.None));
                    done++;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    SetStatus(Text.Format(Strings.UploadFailed, name, Describe(ex)), error: true);
                    MessageBox.Show(Window.GetWindow(this), Text.Format(Strings.UploadFailedDetails, name, Describe(ex)), Strings.UploadTitle,
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    break;
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _busy);
            TransferBar.Visibility = Visibility.Collapsed;
        }

        if (browser == _browser)
        {
            await NavigateAsync(directory);
        }

        if (done == paths.Count)
        {
            ReportChecks(Text.Format(Strings.Uploaded, done, Protocol, directory), checks, upload: true);
        }
        else
        {
            // Échec en cours de route (déjà signalé) : la vérification des fichiers déjà envoyés reste consultable.
            KeepChecks(checks, upload: true);
        }
    }

    private void OnDownload(object sender, RoutedEventArgs e) =>
        _ = DownloadAsync(SelectedEntries().Where(s => !s.IsDirectory).ToList());

    private async Task DownloadAsync(IReadOnlyList<RemoteEntry> files)
    {
        var browser = _browser;
        if (browser is null || files.Count == 0 || _busy > 0)
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

        var checks = new List<TransferCheck>();
        HideChecks();
        Interlocked.Increment(ref _busy);
        TransferBar.Visibility = Visibility.Visible;
        try
        {
            foreach (var file in files)
            {
                SetStatus(Text.Format(Strings.Downloading, file.Name));
                TransferBar.Value = 0;
                var progress = new Progress<TransferProgress>(p =>
                {
                    if (p.Verifying)
                    {
                        SetStatus(Text.Format(Strings.VerifyingFile, p.FileName));
                    }

                    TransferBar.Value = p.Total > 0 ? 100.0 * p.Transferred / p.Total : 0;
                });
                // Nom Unix nettoyé (« ..\ », « : », « CON »...) : rien ne s'écrit hors du dossier choisi.
                checks.Add(await browser.DownloadAsync(file, singleTarget ?? Path.Combine(folder, WindowsFileName.Sanitize(file.Name)), progress,
                    CancellationToken.None));
            }

            ReportChecks(Text.Format(Strings.Downloaded, files.Count, folder), checks, upload: false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SetStatus(Text.Format(Strings.DownloadFailed, Describe(ex)), error: true);
            KeepChecks(checks, upload: false);
        }
        finally
        {
            Interlocked.Decrement(ref _busy);
            TransferBar.Visibility = Visibility.Collapsed;
        }
    }

    // ===================== Vérification des transferts =====================

    /// <summary>
    /// Bilan de la vérification SHA-256 après un transfert : confirmation dans la barre d'état et lien « Sommes de
    /// contrôle… ». Si un fichier diffère de l'original, l'erreur est affichée et le détail s'ouvre de lui-même.
    /// </summary>
    private void ReportChecks(string done, IReadOnlyList<TransferCheck> checks, bool upload)
    {
        KeepChecks(checks, upload);
        int different = checks.Count(c => c.Verified && !c.Matches);
        if (different > 0)
        {
            SetStatus(Text.Format(Strings.TransferMismatch, different), error: true);
            ShowChecks();
            return;
        }

        int verified = checks.Count(c => c.Matches);
        int unverified = checks.Count - verified;
        var parts = new List<string> { done };
        if (verified > 0)
        {
            parts.Add(checks.Count == 1 ? Text.Format(Strings.TransferVerifiedOne, checks[0].RemoteHash[..12]) : Text.Format(Strings.TransferVerified, verified));
        }

        if (unverified > 0)
        {
            parts.Add(Text.Format(Strings.TransferNotVerified, unverified));
        }

        SetStatus(string.Join(" · ", parts));
    }

    private void KeepChecks(IReadOnlyList<TransferCheck> checks, bool upload)
    {
        _lastChecks = checks;
        _lastChecksUpload = upload;
        ChecksLink.Visibility = checks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideChecks() => KeepChecks([], false);

    private void OnShowChecks(object sender, RoutedEventArgs e) => ShowChecks();

    private void ShowChecks()
    {
        if (_lastChecks.Count > 0)
        {
            new TransferChecksDialog(_lastChecks, _lastChecksUpload) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
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
