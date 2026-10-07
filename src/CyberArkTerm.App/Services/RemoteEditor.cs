using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core;
using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.App.Services;

/// <summary>
/// « Modifier » de l'onglet Fichiers : copie le fichier du serveur dans un dossier temporaire, l'ouvre dans
/// l'éditeur de texte choisi par l'utilisateur et, à chaque enregistrement, propose de le renvoyer sur le serveur
/// (par SFTP, droits conservés ; alerte si le fichier a changé sur le serveur entre-temps).
/// Une instance par session SSH ; les copies locales sont supprimées à la fermeture de la session.
/// </summary>
public sealed class RemoteEditor : IDisposable
{
    /// <summary>Au-delà, on demande confirmation avant d'ouvrir le fichier dans un éditeur de texte.</summary>
    private const long LargeFile = 20L * 1024 * 1024;

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "CyberArkTerm", "edit");

    private readonly RemoteSession _session;
    private readonly Window _owner;
    private readonly AppSettings _settings;
    private readonly Action<string, bool> _status;
    private readonly Action<string> _remoteChanged;
    private readonly string _directory = Path.Combine(Root, Guid.NewGuid().ToString("N"));
    private readonly List<EditedFile> _files = [];
    private readonly HashSet<EditedFile> _busy = [];
    private readonly HashSet<EditedFile> _savedAgain = [];
    private bool _disposed;

    /// <param name="status">Message pour la barre d'état (texte, erreur).</param>
    /// <param name="remoteChanged">Dossier du serveur dont le contenu vient de changer (pour l'actualiser).</param>
    public RemoteEditor(RemoteSession session, Window owner, AppSettings settings, Action<string, bool> status, Action<string> remoteChanged)
    {
        _session = session;
        _owner = owner;
        _settings = settings;
        _status = status;
        _remoteChanged = remoteChanged;
    }

    /// <summary>Fichiers modifiés localement mais pas encore renvoyés sur le serveur.</summary>
    public IReadOnlyList<EditedFile> Unsent => _files.Where(f => f.HasUnsentChanges).ToList();

    public async Task EditAsync(RemoteEntry entry)
    {
        var open = _files.FirstOrDefault(f => f.RemotePath == entry.FullPath);
        if (open is not null)
        {
            // Déjà ouvert : on rouvre l'éditeur sur la même copie.
            LaunchEditor(open.LocalPath);
            return;
        }

        if (entry.Length > LargeFile && MessageBox.Show(_owner, Text.Format(Strings.EditLargeFile, entry.Name, entry.SizeText),
                Strings.FileEdit.Replace("_", ""), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        _status(Text.Format(Strings.EditOpening, entry.Name), false);
        EditedFile file;
        try
        {
            var browser = await _session.GetBrowserAsync();
            var folder = Path.Combine(_directory, Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(folder);
            var local = Path.Combine(folder, EditedFile.LocalCopyName(entry.Name));
            // Date et taille relevées avant la copie : un changement sur le serveur pendant la copie sera signalé au renvoi.
            var (time, length) = await browser.GetStatAsync(entry.FullPath, CancellationToken.None);
            await browser.DownloadAsync(entry, local, null, CancellationToken.None);
            file = new EditedFile(entry.FullPath, local, time, length);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _status(Text.Format(Strings.CannotOpen, entry.Name, ErrorText.Describe(ex)), true);
            return;
        }

        if (_disposed)
        {
            file.Dispose();
            return;
        }

        file.Saved += f => _owner.Dispatcher.BeginInvoke(() => { _ = OnSavedAsync(f); });
        _files.Add(file);
        if (LaunchEditor(file.LocalPath))
        {
            _status(Text.Format(Strings.EditOpened, entry.Name), false);
        }
    }

    /// <summary>Demande à l'utilisateur s'il faut renvoyer ce qui n'a pas été envoyé ; vrai si l'on peut fermer.</summary>
    public static bool ConfirmClose(Window owner, IEnumerable<RemoteEditor> editors)
    {
        var unsent = editors.SelectMany(e => e.Unsent.Select(f => $"  • {e._session.Label}:{f.RemotePath}")).ToList();
        return unsent.Count == 0
            || MessageBox.Show(owner, Text.Format(Strings.EditPendingOnClose, string.Join("\n", unsent)), Strings.EditPendingTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    /// <summary>Supprime les copies laissées par une session qui n'a pas pu se fermer normalement (plus d'un jour).</summary>
    public static void CleanupStale()
    {
        try
        {
            if (!Directory.Exists(Root))
            {
                return;
            }

            foreach (var dir in Directory.EnumerateDirectories(Root))
            {
                if (Directory.GetLastWriteTimeUtc(dir) < DateTime.UtcNow.AddDays(-1))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var file in _files)
        {
            file.Dispose();
        }

        _files.Clear();
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Fichier encore ouvert par l'éditeur : supprimé au prochain démarrage (CleanupStale).
        }
    }

    private bool LaunchEditor(string path)
    {
        var editor = _settings.TextEditor.Trim().Trim('"');
        if (editor.Length == 0)
        {
            editor = "notepad.exe";
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(editor) { ArgumentList = { path }, UseShellExecute = false });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            MessageBox.Show(_owner, Text.Format(Strings.EditorLaunchFailed, editor, ex.Message), Strings.FileEdit.Replace("_", ""),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private async Task OnSavedAsync(EditedFile file)
    {
        if (_disposed || !_files.Contains(file))
        {
            return;
        }

        if (!_busy.Add(file))
        {
            // Question ou envoi déjà en cours pour ce fichier : on y reviendra ensuite.
            _savedAgain.Add(file);
            return;
        }

        try
        {
            do
            {
                _savedAgain.Remove(file);
                if (!file.HasUnsentChanges)
                {
                    break;
                }

                var target = $"{_session.Label}:{file.RemotePath}";
                if (MessageBox.Show(_owner, Text.Format(Strings.EditUploadPrompt, file.Name, target), Strings.EditUploadTitle,
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    break;
                }

                if (!await UploadAsync(file))
                {
                    break;
                }
            }
            while (_savedAgain.Contains(file) && !_disposed);
        }
        finally
        {
            _busy.Remove(file);
            _savedAgain.Remove(file);
        }
    }

    private async Task<bool> UploadAsync(EditedFile file)
    {
        bool writing = false;
        try
        {
            var browser = await _session.GetBrowserAsync();
            bool changed;
            try
            {
                var (time, length) = await browser.GetStatAsync(file.RemotePath, CancellationToken.None);
                changed = time != file.RemoteWriteTime || length != file.RemoteLength;
            }
            catch (Renci.SshNet.Common.SftpPathNotFoundException)
            {
                changed = true;
            }

            // Après un envoi coupé, c'est notre propre écriture partielle qui a changé le fichier : on le remplace.
            if (changed && !file.WriteInterrupted && MessageBox.Show(_owner, Text.Format(Strings.EditRemoteChanged, file.Name), Strings.EditUploadTitle,
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return false;
            }

            _status(Text.Format(Strings.EditUploading, file.Name), false);
            var content = EditedFile.ReadAllBytesShared(file.LocalPath);
            writing = true;
            var (newTime, newLength) = await browser.WriteFileAsync(file.RemotePath, content, CancellationToken.None);
            writing = false;
            file.MarkSent(content, newTime, newLength);
            _status(Text.Format(Strings.EditUploaded, file.Name), false);
            _remoteChanged(RemotePath.Parent(file.RemotePath));
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var message = ex switch
            {
                Renci.SshNet.Common.SftpPermissionDeniedException => Strings.PermissionDenied,
                _ => ErrorText.Describe(ex),
            };
            if (writing)
            {
                file.MarkWriteInterrupted();
            }

            _status(Text.Format(Strings.UploadFailed, file.Name, message), true);
            MessageBox.Show(_owner, Text.Format(writing ? Strings.EditWriteInterrupted : Strings.EditUploadFailed, file.Name, message),
                Strings.EditUploadTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }
}
