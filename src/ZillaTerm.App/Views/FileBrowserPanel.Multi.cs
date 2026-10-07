using System.IO;
using System.Windows;
using ZillaTerm.App.Localization;
using ZillaTerm.App.Services;
using ZillaTerm.Core.Ssh;

namespace ZillaTerm.App.Views;

/// <summary>
/// Envoi des mêmes fichiers vers plusieurs serveurs : un élément de la file des transferts par serveur, vérifié par
/// SHA-256 comme un envoi ordinaire, et un seul bilan à la fin de la série.
/// </summary>
public partial class FileBrowserPanel
{
    // Au-delà, les noms ne sont pas tous vérifiés avant l'envoi (une demande par nom et par serveur).
    private const int MaxConflictChecks = 50;

    /// <summary>Sessions SSH ouvertes, dans l'ordre des onglets (fournies par la fenêtre principale).</summary>
    public Func<IReadOnlyList<RemoteSession>> OpenSessions { get; set; } = () => [];

    private void OnMultiUpload(object sender, RoutedEventArgs e) => ShowMultiUpload(_session is null ? [] : [_session]);

    /// <summary>Fenêtre d'envoi vers plusieurs serveurs (onglet Fichiers, vue parallèle).</summary>
    public void ShowMultiUpload(IReadOnlyCollection<RemoteSession> preselected, IReadOnlyList<string>? paths = null)
    {
        var sessions = OpenSessions();
        var owner = Window.GetWindow(this);
        if (sessions.Count == 0)
        {
            MessageBox.Show(owner, Strings.ParallelNoSession, Strings.MultiUploadTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var destination = _browser?.CurrentDirectory ?? "~";
        var dialog = new MultiUploadDialog(sessions, preselected, destination, Protocol, paths) { Owner = owner };
        if (dialog.ShowDialog() == true)
        {
            _ = EnqueueMultiUploadAsync(dialog.Paths, dialog.Destination, dialog.Sessions);
        }
    }

    /// <summary>
    /// Vérifie sur chaque serveur que le dossier existe et ce qui serait remplacé (une seule question pour tous), puis
    /// met en file un envoi par serveur.
    /// </summary>
    private async Task EnqueueMultiUploadAsync(IReadOnlyList<string> paths, string destination, IReadOnlyList<RemoteSession> sessions)
    {
        var names = paths.Select(p => Path.GetFileName(p.TrimEnd('\\', '/'))).ToList();
        var targets = new List<(RemoteSession Session, IRemoteFiles Browser, string Directory)>();
        var problems = new List<string>();
        var conflicts = new List<string>();
        SetStatus(Strings.MultiUploadChecking);
        foreach (var session in sessions)
        {
            try
            {
                var browser = await session.GetBrowserAsync();
                var directory = RemotePath.ResolveHome(destination, browser.HomeDirectory);
                if (!await browser.ExistsAsync(directory, CancellationToken.None))
                {
                    problems.Add(Text.Format(Strings.MultiUploadNoFolder, session.Label, directory));
                    continue;
                }

                var existing = new List<string>();
                foreach (var name in names.Take(MaxConflictChecks))
                {
                    if (await browser.ExistsAsync(RemotePath.Combine(directory, name), CancellationToken.None))
                    {
                        existing.Add(name);
                    }
                }

                // Noms attendus d'un envoi encore en attente vers le même dossier de ce serveur.
                existing.AddRange(_queue.Items
                    .Where(i => i.Upload && i.State is TransferState.Pending or TransferState.Running && ReferenceEquals(i.Owner, session) && i.Destination == directory)
                    .SelectMany(i => i.Names)
                    .Intersect(names, StringComparer.Ordinal));
                existing = existing.Distinct(StringComparer.Ordinal).ToList();
                if (existing.Count > 0)
                {
                    conflicts.Add($"{session.Label} : {string.Join(", ", existing.Take(5))}{(existing.Count > 5 ? ", …" : "")}");
                }

                targets.Add((session, browser, directory));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                problems.Add($"{session.Label} : {Describe(ex)}");
            }
        }

        SetStatus("");
        var owner = Window.GetWindow(this);
        if (problems.Count > 0)
        {
            var text = Text.Format(Strings.MultiUploadProblems, string.Join("\n", problems.Select(p => "  • " + p)));
            if (targets.Count == 0)
            {
                MessageBox.Show(owner, text, Strings.MultiUploadTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!ConfirmDialog.Confirm(owner, new ConfirmRequest
                {
                    Title = Strings.MultiUploadTitle,
                    Heading = Strings.MultiUploadSomeHeading,
                    Message = Text.Format(Strings.MultiUploadSomeMessage, targets.Count),
                    Items = problems,
                    Kind = ConfirmKind.Warning,
                    Actions = [Strings.MultiUploadSendOthers],
                }))
            {
                return;
            }
        }

        if (conflicts.Count > 0 && !ConfirmDialog.Confirm(owner, new ConfirmRequest
            {
                Title = Strings.MultiUploadTitle,
                Heading = Strings.MultiUploadConflictsHeading,
                Message = Strings.MultiUploadConflictsMessage,
                Items = conflicts,
                Kind = ConfirmKind.Warning,
                Actions = [Strings.ActionReplace],
                DangerAction = 0,
            }))
        {
            return;
        }

        var protocol = _settings.PreferredUploadProtocol;
        var what = paths.Count > 1 ? Text.Format(Strings.QueueItems, paths.Count) : names[0] + (Directory.Exists(paths[0]) ? "/" : "");
        foreach (var (session, browser, directory) in targets)
        {
            Enqueue(new TransferItem(true, Text.Format(Strings.MultiUploadLabel, what, session.Label), directory, async (item, ct) =>
            {
                var progress = new Progress<TransferProgress>(item.Report);
                item.FileCount = await Task.Run(() => paths.Sum(CountFiles), ct);
                foreach (var path in paths)
                {
                    await browser.UploadAsync(path, directory, protocol, item.Checks, progress, background: true, ct);
                }
            })
            {
                Owner = session,
                Protocol = ProtocolOf(browser),
                Names = names,
            });
        }
    }
}
