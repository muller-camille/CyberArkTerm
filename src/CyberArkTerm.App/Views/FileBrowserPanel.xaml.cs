using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CyberArkTerm.App.Services;
using CyberArkTerm.Core;
using CyberArkTerm.Core.Ssh;
using Microsoft.Win32;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Onglet latéral « Fichiers » (comme le navigateur SFTP de MobaXterm) : parcourt le serveur de la session SSH
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

    public FileBrowserPanel()
    {
        InitializeComponent();
        ShowMessage("Ouvrez une session SSH (PSMP) pour parcourir les fichiers du serveur et y déposer des fichiers.", retry: false);
        HeaderText.Text = "Aucune session SSH";
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
            HeaderText.Text = "Aucune session SSH";
            ShowMessage("Ouvrez une session SSH (PSMP) pour parcourir les fichiers du serveur et y déposer des fichiers.", retry: false);
            UpdateToolbar();
            return;
        }

        HeaderText.Text = session.Label;
        FollowBox.IsEnabled = session.CanFollowTerminal;
        FollowBox.IsChecked = session.CanFollowTerminal && session.FollowTerminal;
        FollowBox.ToolTip = session.CanFollowTerminal
            ? "Le navigateur se place automatiquement dans le dossier courant du shell (cd)"
            : "Activez « Suivi du dossier du terminal » dans les paramètres";
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
                ? "Connexion SSH en cours…"
                : "La session SSH est fermée. Reconnectez-la pour parcourir ses fichiers.", retry: false);
            UpdateToolbar();
            return;
        }

        ShowMessage("Ouverture de la connexion SFTP…", retry: false);
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
                ShowMessage("Connexion SFTP impossible :\n" + ErrorText.Describe(ex), retry: true);
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
        SetStatus($"Lecture de {path}…");
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
            SetStatus($"{folders} dossier(s), {entries.Count - folders} fichier(s)");
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
                ShowMessage("Connexion SFTP perdue :\n" + ErrorText.Describe(ex), retry: true);
            }
            else
            {
                SetStatus($"Impossible d'ouvrir {path} : {Describe(ex)}", error: true);
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
            ShowMessage("La session SSH n'est pas connectée. Reconnectez-la pour parcourir ses fichiers.", retry: false);
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
        if (_session.FollowTerminal && _session.TerminalDirectory is { } dir)
        {
            _ = NavigateAsync(dir);
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
                "download" => selected.Count > 0 && selected.All(s => !s.IsDirectory),
                "delete" or "copy" => selected.Count > 0,
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

        var dialog = new InputDialog("Nouveau dossier", $"Nom du dossier à créer dans {browser.CurrentDirectory} :",
            validate: v => v.Contains('/') ? "Le nom ne doit pas contenir « / »." : null) { Owner = Window.GetWindow(this) };
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
            SetStatus("Création impossible : " + Describe(ex), error: true);
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e) => _ = DeleteAsync();

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
            names += $"\n  … et {selected.Count - 10} autre(s)";
        }

        var answer = MessageBox.Show(Window.GetWindow(this),
            $"Supprimer définitivement du serveur ({browser.CurrentDirectory}) :\n\n{names}\n\nLes dossiers doivent être vides.",
            "Supprimer", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
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
                SetStatus($"Suppression de {entry.Name}…");
                try
                {
                    await browser.DeleteAsync(entry, CancellationToken.None);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    errors.Add($"{entry.Name} : {Describe(ex, entry.IsDirectory)}");
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
            SetStatus("Suppression incomplète : " + string.Join(" ; ", errors), error: true);
        }
        else
        {
            SetStatus($"{selected.Count} élément(s) supprimé(s)");
        }
    }

    // ===================== Envoi (glisser-déposer) et téléchargement =====================

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        bool ok = _browser is not null && e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        if (ok)
        {
            DropHintText.Text = $"Déposer pour envoyer en {Protocol} vers\n{_browser!.CurrentDirectory}";
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

        var dialog = new OpenFileDialog { Title = $"Envoyer vers {_browser.CurrentDirectory}", Multiselect = true };
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
                $"Ces éléments existent déjà dans {directory} et seront remplacés :\n\n" +
                string.Join("\n", conflicts.Take(10).Select(c => "  • " + c)) + "\n\nContinuer ?",
                "Envoyer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        int done = 0;
        Interlocked.Increment(ref _busy);
        TransferBar.Visibility = Visibility.Visible;
        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                var name = Path.GetFileName(paths[i].TrimEnd('\\', '/'));
                SetStatus($"Envoi {Protocol} de {name} ({i + 1}/{paths.Count})…");
                TransferBar.Value = 0;
                var progress = new Progress<TransferProgress>(p =>
                {
                    TransferBar.Value = p.Total > 0 ? 100.0 * p.Transferred / p.Total : 0;
                });
                try
                {
                    await browser.UploadAsync(paths[i], directory, _settings.UploadProtocol, progress, CancellationToken.None);
                    done++;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    SetStatus($"Échec de l'envoi de {name} : {Describe(ex)}", error: true);
                    MessageBox.Show(Window.GetWindow(this), $"Échec de l'envoi de {name} :\n\n{Describe(ex)}", "Envoi",
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
            SetStatus($"{done} élément(s) envoyé(s) en {Protocol} vers {directory}");
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
            var save = new SaveFileDialog { Title = "Télécharger", FileName = files[0].Name };
            if (save.ShowDialog(Window.GetWindow(this)) != true)
            {
                return;
            }

            singleTarget = save.FileName;
            folder = Path.GetDirectoryName(save.FileName)!;
        }
        else
        {
            var pick = new OpenFolderDialog { Title = $"Télécharger {files.Count} fichiers dans…" };
            if (pick.ShowDialog(Window.GetWindow(this)) != true)
            {
                return;
            }

            folder = pick.FolderName;
        }

        Interlocked.Increment(ref _busy);
        TransferBar.Visibility = Visibility.Visible;
        try
        {
            foreach (var file in files)
            {
                SetStatus($"Téléchargement de {file.Name}…");
                TransferBar.Value = 0;
                var progress = new Progress<TransferProgress>(p => TransferBar.Value = p.Total > 0 ? 100.0 * p.Transferred / p.Total : 0);
                await browser.DownloadAsync(file, singleTarget ?? Path.Combine(folder, file.Name), progress, CancellationToken.None);
            }

            SetStatus($"{files.Count} fichier(s) téléchargé(s) dans {folder}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SetStatus("Échec du téléchargement : " + Describe(ex), error: true);
        }
        finally
        {
            Interlocked.Decrement(ref _busy);
            TransferBar.Visibility = Visibility.Collapsed;
        }
    }

    // ===================== Utilitaires =====================

    private List<RemoteEntry> SelectedEntries() =>
        FileList.SelectedItems.OfType<RemoteEntry>().Where(e => !e.IsParentLink).ToList();

    private static string Describe(Exception ex, bool directory = false) => ex switch
    {
        Renci.SshNet.Common.SftpPermissionDeniedException => "permission refusée",
        Renci.SshNet.Common.SftpPathNotFoundException => "chemin introuvable",
        Renci.SshNet.Common.SshException when directory => "le dossier n'est pas vide ou ne peut pas être supprimé",
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
