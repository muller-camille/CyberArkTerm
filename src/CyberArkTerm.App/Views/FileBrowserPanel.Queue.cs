using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Services;
using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.App.Views;

/// <summary>
/// File d'attente des transferts de l'onglet Fichiers : envois et téléchargements s'exécutent un par un, dans l'ordre
/// des demandes, quelle que soit la session (onglet) d'origine. La destination d'un envoi est le dossier affiché au
/// moment du dépôt. Un élément en attente peut être retiré, l'élément en cours annulé (le fichier interrompu,
/// incomplet, est supprimé) ; une erreur n'arrête pas la file. La navigation, la suppression, les droits, l'éditeur
/// et le glisser vers l'Explorateur passent entre deux fichiers.
/// </summary>
public partial class FileBrowserPanel
{
    private readonly TransferQueue _queue = new(ex => Describe(ex));
    private TransferItem? _watched;

    // Historique des transferts ; sans chemin (tests, avant l'initialisation), rien n'est écrit sur le disque.
    private TransferHistory _history = new();

    /// <summary>File des transferts (tests).</summary>
    internal TransferQueue Queue => _queue;

    private void InitializeQueue()
    {
        QueueList.ItemsSource = _queue.Items;
        _queue.Changed += OnQueueChanged;
        _queue.ItemFinished += OnTransferFinished;
        _queue.Drained += OnTransfersDrained;
    }

    /// <summary>Transferts en cours ou en attente d'une session, ou de toutes si <paramref name="session"/> est null.</summary>
    public int ActiveTransfers(SshSession? session) =>
        _queue.Items.Count(i => i.State is TransferState.Pending or TransferState.Running && (session is null || ReferenceEquals(i.Owner, session)));

    /// <summary>Vrai si l'on peut fermer : aucun transfert en cours ou en attente, ou l'utilisateur accepte de les annuler.</summary>
    public bool ConfirmCancelTransfers(Window owner, SshSession? session)
    {
        int count = ActiveTransfers(session);
        return count == 0 || MessageBox.Show(owner, Text.Format(Strings.QueueCloseConfirm, count), "CyberArkTerm",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Annule les transferts d'une session (de toutes si null) et attend l'arrêt de celui en cours, pour que son fichier
    /// incomplet soit supprimé avant la fermeture de la connexion (au plus 5 s).
    /// </summary>
    public async Task CancelTransfersAsync(SshSession? session)
    {
        bool Matches(TransferItem item) => session is null || ReferenceEquals(item.Owner, session);
        if (ActiveTransfers(session) == 0)
        {
            return;
        }

        _queue.CancelWhere(Matches);
        await _queue.WhenStoppedAsync(Matches, TimeSpan.FromSeconds(5));
    }

    /// <summary>Met en file l'envoi de fichiers ou de dossiers locaux vers le dossier affiché.</summary>
    private void EnqueueUpload(IReadOnlyList<string> paths)
    {
        var browser = _browser;
        var session = _session;
        if (browser is null || session is null || paths.Count == 0)
        {
            return;
        }

        var directory = browser.CurrentDirectory;
        var names = paths.Select(p => Path.GetFileName(p.TrimEnd('\\', '/'))).ToList();

        // Beaucoup de fichiers d'un coup : proposer une seule archive .tar.gz (option des Paramètres).
        string? archiveName = null;
        int archiveFiles = 0;
        if (_settings.OfferArchive)
        {
            var (files, bytes) = TarGzPacker.Measure(paths);
            if (files >= _settings.ArchiveThreshold)
            {
                var offer = new ArchiveOfferDialog(files, bytes, directory) { Owner = Window.GetWindow(this) };
                if (offer.ShowDialog() != true)
                {
                    return;
                }

                if (offer.DontOfferAgain)
                {
                    _settings.OfferArchive = false;
                    _saveSettings();
                }

                if (offer.UseArchive)
                {
                    archiveName = TarGzPacker.ArchiveName(paths, DateTime.Now);
                    archiveFiles = files;
                    // L'archive arrive dans le dossier, puis son extraction y recrée les éléments déposés.
                    names.Add(archiveName);
                }
            }
        }

        // Écrasement : noms déjà dans le dossier, ou attendus d'un envoi en attente vers le même dossier.
        var existing = (FileList.ItemsSource as IEnumerable<RemoteEntry> ?? []).Select(e => e.Name)
            .Concat(_queue.Items
                .Where(i => i.Upload && i.State is TransferState.Pending or TransferState.Running && ReferenceEquals(i.Owner, session) && i.Destination == directory)
                .SelectMany(i => i.Names))
            .ToHashSet(StringComparer.Ordinal);
        var conflicts = names.Where(existing.Contains).Distinct(StringComparer.Ordinal).ToList();
        if (conflicts.Count > 0 && MessageBox.Show(Window.GetWindow(this),
                Text.Format(Strings.UploadConflicts, directory, string.Join("\n", conflicts.Take(10).Select(c => "  • " + c))),
                Strings.UploadTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var protocol = _settings.UploadProtocol;
        if (archiveName is not null)
        {
            EnqueueArchive(browser, session, paths, directory, archiveName, archiveFiles, names);
            return;
        }

        var label = paths.Count > 1 ? Text.Format(Strings.QueueItems, paths.Count) : names[0] + (Directory.Exists(paths[0]) ? "/" : "");
        Enqueue(new TransferItem(true, label, directory, async (item, ct) =>
        {
            var progress = new Progress<TransferProgress>(item.Report);
            item.FileCount = await Task.Run(() => paths.Sum(CountFiles), ct);
            // Un dépôt de plusieurs éléments s'arrête à la première erreur ; la file passe ensuite à l'élément suivant.
            foreach (var path in paths)
            {
                await browser.UploadAsync(path, directory, protocol, item.Checks, progress, background: true, ct);
            }
        })
        {
            Owner = session,
            Protocol = Protocol,
            Names = names,
        });
    }

    /// <summary>
    /// Met en file l'envoi d'une archive .tar.gz des éléments déposés : créée dans un dossier temporaire de ce poste,
    /// envoyée et vérifiée (SHA-256), puis supprimée du poste. La commande d'extraction est donnée à la fin.
    /// </summary>
    private void EnqueueArchive(RemoteFileBrowser browser, SshSession session, IReadOnlyList<string> paths, string directory,
        string archiveName, int files, IReadOnlyList<string> names)
    {
        var protocol = _settings.UploadProtocol;
        Enqueue(new TransferItem(true, Text.Format(Strings.ArchiveLabel, archiveName, files), directory, async (item, ct) =>
        {
            var progress = new Progress<TransferProgress>(item.Report);
            item.FileCount = 1;
            var folder = Path.Combine(Path.GetTempPath(), "CyberArkTerm", "archives", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var archive = Path.Combine(folder, archiveName);
                await TarGzPacker.CreateAsync(paths, archive, progress, ct);
                await browser.UploadAsync(archive, directory, protocol, item.Checks, progress, background: true, ct);
                item.ExtractCommand = TarGzPacker.ExtractCommand(directory, archiveName);
            }
            finally
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Core.Diagnostics.DebugLog.Write("files", $"Archive temporaire non supprimée : {ex.Message}");
                }
            }
        })
        {
            Owner = session,
            Protocol = Protocol,
            Names = names,
        });
    }

    /// <summary>Met en file le téléchargement de fichiers vers un dossier (ou un fichier choisi, pour un seul).</summary>
    private void EnqueueDownload(IReadOnlyList<RemoteEntry> files, string folder, string? singleTarget)
    {
        var browser = _browser;
        if (browser is null || files.Count == 0)
        {
            return;
        }

        var label = files.Count == 1 ? files[0].Name : Text.Format(Strings.QueueFiles, files.Count);
        Enqueue(new TransferItem(false, label, folder, async (item, ct) =>
        {
            var progress = new Progress<TransferProgress>(item.Report);
            item.FileCount = files.Count;
            foreach (var file in files)
            {
                // Nom Unix nettoyé (« ..\ », « : », « CON »...) : rien ne s'écrit hors du dossier choisi.
                var target = singleTarget ?? Path.Combine(folder, WindowsFileName.Sanitize(file.Name));
                await browser.DownloadAsync(file, target, item.Checks, progress, background: true, ct);
            }
        })
        {
            Owner = _session,
        });
    }

    private static int CountFiles(string path)
    {
        try
        {
            return RemoteFileBrowser.CountFiles(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Compte inconnu : l'avancement l'affiche « … ». L'envoi signalera le dossier illisible.
            return 0;
        }
    }

    private void Enqueue(TransferItem item)
    {
        _queue.Enqueue(item);
        if (!ReferenceEquals(_queue.Current, item))
        {
            SetStatus(Text.Format(Strings.QueueAdded, item.Label, _queue.PendingCount));
        }
    }

    private void OnCancelTransfer(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TransferItem item)
        {
            _queue.Cancel(item);
        }
    }

    private void OnCancelAllTransfers(object sender, RoutedEventArgs e) => _queue.CancelAll();

    private void OnQueueChanged()
    {
        QueuePanel.Visibility = _queue.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CancelAllButton.IsEnabled = _queue.ActiveCount > 0;
        if (!ReferenceEquals(_watched, _queue.Current))
        {
            if (_watched is not null)
            {
                _watched.PropertyChanged -= OnCurrentTransferChanged;
            }

            _watched = _queue.Current;
            if (_watched is not null)
            {
                _watched.PropertyChanged += OnCurrentTransferChanged;
            }
        }

        UpdateTransferStatus();
    }

    private void OnCurrentTransferChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TransferItem.CurrentFile) or nameof(TransferItem.Verifying) or nameof(TransferItem.Packing)
            or nameof(TransferItem.FileCount))
        {
            UpdateTransferStatus();
        }
    }

    /// <summary>Barre d'état pendant la file : « Envoi SCP de deploy/ (3/12)… · 2 en attente ».</summary>
    private void UpdateTransferStatus()
    {
        if (_queue.Current is not { } item)
        {
            return;
        }

        var text = item.Packing ? Text.Format(Strings.ArchivePacking, item.Label)
            : item.Verifying ? Text.Format(Strings.VerifyingFile, item.CurrentFile)
            : item.Upload ? Text.Format(Strings.Uploading, item.Protocol, item.Label, TransferStatusConverter.FileNumber(item), TransferStatusConverter.FileTotal(item))
            : Text.Format(Strings.Downloading, item.CurrentFile ?? item.Label);
        if (_queue.PendingCount is > 0 and var pending)
        {
            text += " · " + Text.Format(Strings.QueuePending, pending);
        }

        SetStatus(text);
    }

    private void OnTransferFinished(TransferItem item)
    {
        // Élément retiré avant d'avoir commencé : rien n'a été transféré, rien à garder.
        if (item.State != TransferState.Cancelled || item.Checks.Count > 0)
        {
            Record(new TransferRecord
            {
                Time = DateTime.UtcNow,
                Upload = item.Upload,
                Server = (item.Owner as SshSession)?.Label ?? "",
                Label = item.Label,
                Destination = item.Destination,
                Protocol = item.Protocol,
                State = item.State,
                Error = item.Error,
                FileCount = item.FileCount,
                Files = [.. item.Checks],
            });
        }

        // Envoi terminé (ou arrêté) vers le dossier affiché : la liste est relue.
        if (item.Upload && ReferenceEquals(item.Owner, _session) && _browser is { } browser && browser.CurrentDirectory == item.Destination)
        {
            _ = NavigateAsync(item.Destination);
        }
    }

    /// <summary>Fin de la série : un seul bilan, et les sommes de contrôle de tous les fichiers passés.</summary>
    private void OnTransfersDrained(IReadOnlyList<TransferItem> run)
    {
        var checks = run.SelectMany(i => i.Checks).ToList();
        var failures = run.Where(i => i.State == TransferState.Failed).ToList();
        string message;
        if (run.Count == 1)
        {
            var item = run[0];
            message = item.State switch
            {
                TransferState.Done when item.ExtractCommand is not null => Text.Format(Strings.ArchiveSent, item.Label, item.Destination),
                TransferState.Done when item.Upload => Text.Format(Strings.Uploaded, item.Names.Count, item.Protocol, item.Destination),
                TransferState.Done => Text.Format(Strings.Downloaded, item.FileCount, item.Destination),
                TransferState.Failed => Failure(item),
                _ => Text.Format(Strings.QueueCancelledOne, item.Label),
            };
        }
        else
        {
            var parts = new List<string>();
            int done = run.Count(i => i.State == TransferState.Done);
            int cancelled = run.Count(i => i.State == TransferState.Cancelled);
            if (done > 0)
            {
                parts.Add(Text.Format(Strings.QueueSummaryDone, done));
            }

            if (failures.Count > 0)
            {
                parts.Add(Text.Format(Strings.QueueSummaryFailed, failures.Count));
            }

            if (cancelled > 0)
            {
                parts.Add(Text.Format(Strings.QueueSummaryCancelled, cancelled));
            }

            message = Text.Format(Strings.QueueSummary, string.Join(", ", parts));
            if (failures.Count > 0)
            {
                message += " — " + string.Join(" ; ", failures.Select(Failure));
            }
        }

        // Archives envoyées : la commande d'extraction se copie depuis la barre d'état.
        _extractCommands = string.Join("\n", run.Where(i => i.State == TransferState.Done).Select(i => i.ExtractCommand).OfType<string>());
        ExtractLink.Visibility = _extractCommands.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ReportChecks(message, checks, error: failures.Count > 0);

        static string Failure(TransferItem item) =>
            Text.Format(item.Upload ? Strings.UploadFailed : Strings.QueueDownloadFailed, item.Label, item.Error);
    }
}

public partial class FileBrowserPanel
{
    /// <summary>Historique (tests).</summary>
    internal TransferHistory History => _history;

    private string _extractCommands = "";

    private void OnCopyExtractCommand(object sender, RoutedEventArgs e)
    {
        try
        {
            // Pas un secret : presse-papiers ordinaire, à coller dans le terminal de la session.
            Clipboard.SetText(_extractCommands + "\n");
            SetStatus(Strings.ArchiveCommandCopied);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            SetStatus(Strings.ClipboardBusy, error: true);
        }
    }

    private void OnHistory(object sender, RoutedEventArgs e) =>
        new TransferHistoryDialog(_history, SaveHistory) { Owner = Window.GetWindow(this) }.ShowDialog();

    private void Record(TransferRecord record)
    {
        _history.Add(record);
        SaveHistory();
    }

    /// <summary>Enregistre l'historique sans bloquer l'interface ; un échec d'écriture est seulement journalisé.</summary>
    private void SaveHistory()
    {
        var history = _history;
        _ = Task.Run(() =>
        {
            try
            {
                history.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Core.Diagnostics.DebugLog.Write("files", $"Historique des transferts non enregistré : {ex.Message}");
            }
        });
    }
}

/// <summary>Texte d'état d'un transfert de la file : en attente, avancement, vérification, résultat.</summary>
public sealed class TransferStatusConverter : IMultiValueConverter
{
    /// <summary>Rang du fichier en cours (les fichiers déjà traités ont chacun leur vérification).</summary>
    public static int FileNumber(TransferItem item) => Math.Max(1, Math.Min(item.Checks.Count + 1, Math.Max(item.FileCount, 1)));

    /// <summary>Nombre de fichiers, « … » tant qu'il n'est pas connu.</summary>
    public static string FileTotal(TransferItem item) => item.FileCount > 0 ? item.FileCount.ToString(CultureInfo.CurrentCulture) : "…";

    // Les autres valeurs (état, avancement...) ne servent qu'à déclencher la mise à jour.
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length > 0 && values[0] is TransferItem item ? StateText(item) : "";

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();

    public static string StateText(TransferItem item) => item.State switch
    {
        TransferState.Pending => Strings.QueueStateWaiting,
        TransferState.Running when item.CancelRequested => Strings.QueueStateCancelling,
        TransferState.Running when item.Packing => Text.Format(Strings.QueueStatePacking, Math.Round(item.Percent)),
        TransferState.Running when item.Verifying => Text.Format(Strings.QueueStateVerifying, FileNumber(item), FileTotal(item)),
        TransferState.Running => Text.Format(Strings.QueueStateRunning, Math.Round(item.Percent), FileNumber(item), FileTotal(item)),
        TransferState.Done when item.Checks.Count(c => c.Verified && !c.Matches) is > 0 and var different =>
            Text.Format(Strings.QueueStateDifferent, different),
        TransferState.Done => Strings.QueueStateDone,
        TransferState.Failed => "✗ " + item.Error,
        _ => Strings.QueueStateCancelled,
    };
}
