using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace CyberArkTerm.Core.Ssh;

/// <summary>
/// Navigation dans les fichiers du serveur via SFTP (ls, cd, rm, chmod, téléchargement, modification) et dépôt
/// de fichiers par SCP ou SFTP. Les opérations sont sérialisées : une seule à la fois sur la connexion.
/// </summary>
public sealed class RemoteFileBrowser : IDisposable
{
    private readonly SftpClient _sftp;
    private readonly Func<CancellationToken, Task<ScpClient>> _scpFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ScpClient? _scp;

    public RemoteFileBrowser(SftpClient sftp, Func<CancellationToken, Task<ScpClient>> scpFactory)
    {
        _sftp = sftp;
        _scpFactory = scpFactory;
        HomeDirectory = sftp.WorkingDirectory;
        CurrentDirectory = HomeDirectory;
    }

    /// <summary>Dossier de connexion (répertoire personnel du compte cible).</summary>
    public string HomeDirectory { get; }

    public string CurrentDirectory { get; private set; }

    public bool IsConnected => _sftp.IsConnected;

    /// <summary>Liste un dossier (chemin absolu ou relatif au dossier courant) et en fait le dossier courant.</summary>
    public async Task<List<RemoteEntry>> ListAsync(string path, bool showHidden, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var target = path.StartsWith('/') ? path : RemotePath.Combine(CurrentDirectory, path);
            // ChangeDirectory renvoie le chemin canonique (liens symboliques résolus, comme « cd » puis « pwd -P »).
            await _sftp.ChangeDirectoryAsync(target, ct).ConfigureAwait(false);
            var directory = _sftp.WorkingDirectory;
            var entries = new List<RemoteEntry>();
            await foreach (var file in _sftp.ListDirectoryAsync(directory, ct).ConfigureAwait(false))
            {
                if (file.Name is "." or ".." || (!showHidden && file.Name.StartsWith('.')))
                {
                    continue;
                }

                bool isDirectory = file.IsDirectory;
                if (file.IsSymbolicLink)
                {
                    try
                    {
                        // La liste donne les attributs du lien ; on suit le lien pour savoir s'il mène à un dossier.
                        isDirectory = (await _sftp.GetAttributesAsync(file.FullName, ct).ConfigureAwait(false)).IsDirectory;
                    }
                    catch (Exception e) when (e is Renci.SshNet.Common.SshException or InvalidOperationException)
                    {
                        // Lien cassé : affiché comme un fichier.
                    }
                }

                entries.Add(ToEntry(file, isDirectory));
            }

            CurrentDirectory = directory;
            return RemoteEntry.Sort(entries);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Supprime un fichier (rm) ou un dossier vide (rmdir).</summary>
    public async Task DeleteAsync(RemoteEntry entry, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (entry.IsDirectory && !entry.IsSymbolicLink)
            {
                await _sftp.DeleteDirectoryAsync(entry.FullPath, ct).ConfigureAwait(false);
            }
            else
            {
                await _sftp.DeleteFileAsync(entry.FullPath, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _sftp.CreateDirectoryAsync(path, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await _sftp.ExistsAsync(path, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Envoie un fichier ou un dossier local (récursivement) dans <paramref name="remoteDirectory"/>.</summary>
    public async Task UploadAsync(string localPath, string remoteDirectory, TransferProtocol protocol,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var name = Path.GetFileName(localPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var remote = RemotePath.Combine(remoteDirectory, name);
            if (Directory.Exists(localPath))
            {
                await EnsureDirectoryAsync(remote, ct).ConfigureAwait(false);
                if (protocol == TransferProtocol.Scp)
                {
                    var scp = await GetScpAsync(ct).ConfigureAwait(false);
                    await RunScpAsync(scp, s => s.Upload(new DirectoryInfo(localPath), remote), name, progress, ct).ConfigureAwait(false);
                }
                else
                {
                    await UploadDirectorySftpAsync(localPath, remote, progress, ct).ConfigureAwait(false);
                }
            }
            else if (protocol == TransferProtocol.Scp)
            {
                var scp = await GetScpAsync(ct).ConfigureAwait(false);
                await RunScpAsync(scp, s => s.Upload(new FileInfo(localPath), remote), name, progress, ct).ConfigureAwait(false);
            }
            else
            {
                await UploadFileSftpAsync(localPath, remote, progress, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Date de modification et taille actuelles d'un fichier.</summary>
    public async Task<(DateTime LastWriteTime, long Length)> GetStatAsync(string path, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var attributes = await _sftp.GetAttributesAsync(path, ct).ConfigureAwait(false);
            return (attributes.LastWriteTime, attributes.Size);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Remplace le contenu d'un fichier existant par SFTP : ses droits et son propriétaire sont conservés
    /// (contrairement à SCP qui recrée le fichier en 644). Renvoie sa nouvelle date et sa taille.
    /// </summary>
    public async Task<(DateTime LastWriteTime, long Length)> WriteFileAsync(string remotePath, byte[] content, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            await _sftp.UploadFileAsync(stream, remotePath, ct).ConfigureAwait(false);
            var attributes = await _sftp.GetAttributesAsync(remotePath, ct).ConfigureAwait(false);
            return (attributes.LastWriteTime, attributes.Size);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Change les droits (chmod) d'un fichier ou d'un dossier, et si <paramref name="recursive"/> de tout son contenu.
    /// Seuls les droits sont envoyés au serveur : taille, dates et propriétaire restent inchangés.
    /// </summary>
    /// <param name="includeSpecial">
    /// Applique aussi les bits spéciaux (setuid, setgid, sticky) à l'élément choisi ; sinon les siens sont conservés.
    /// Le contenu garde toujours ses propres bits spéciaux.
    /// </param>
    /// <param name="executeOnlyIfAlready">
    /// Pour le contenu : les bits x ne sont donnés qu'aux dossiers et aux fichiers déjà exécutables (« X » de chmod),
    /// pour ne pas rendre tous les fichiers exécutables.
    /// </param>
    /// <param name="progress">Nombre d'éléments traités.</param>
    public async Task<PermissionsResult> SetPermissionsAsync(string path, int mode, bool includeSpecial, bool recursive,
        bool executeOnlyIfAlready, IProgress<int>? progress, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var result = new PermissionsResult();
            var attributes = await _sftp.GetAttributesAsync(path, ct).ConfigureAwait(false);
            await ApplyPermissionsAsync(path, attributes, mode, includeSpecial, ct).ConfigureAwait(false);
            result.Changed++;
            progress?.Report(result.Changed);
            if (recursive && attributes.IsDirectory)
            {
                await ApplyToContentsAsync(path, mode & UnixPermissions.RwxMask, executeOnlyIfAlready, result, progress, ct).ConfigureAwait(false);
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ApplyToContentsAsync(string directory, int mode, bool executeOnlyIfAlready, PermissionsResult result,
        IProgress<int>? progress, CancellationToken ct)
    {
        var entries = new List<ISftpFile>();
        try
        {
            await foreach (var file in _sftp.ListDirectoryAsync(directory, ct).ConfigureAwait(false))
            {
                // Comme « chmod -R », les liens symboliques ne sont pas suivis.
                if (file.Name is not ("." or "..") && !file.IsSymbolicLink)
                {
                    entries.Add(file);
                }
            }
        }
        catch (Exception e) when (e is Renci.SshNet.Common.SshException or InvalidOperationException)
        {
            result.Errors.Add($"{directory} : {e.Message}");
            return;
        }

        foreach (var file in entries)
        {
            ct.ThrowIfCancellationRequested();
            var target = mode;
            const int executeBits = 0x49;
            if (executeOnlyIfAlready && !file.IsDirectory && (UnixPermissions.FromAttributes(file.Attributes) & executeBits) == 0)
            {
                target &= ~executeBits;
            }

            try
            {
                await ApplyPermissionsAsync(file.FullName, file.Attributes, target, includeSpecial: false, ct).ConfigureAwait(false);
                result.Changed++;
                progress?.Report(result.Changed);
            }
            catch (Exception e) when (e is Renci.SshNet.Common.SshException or InvalidOperationException)
            {
                result.Errors.Add($"{file.FullName} : {e.Message}");
            }

            if (file.IsDirectory)
            {
                await ApplyToContentsAsync(file.FullName, mode, executeOnlyIfAlready, result, progress, ct).ConfigureAwait(false);
            }
        }
    }

    private Task ApplyPermissionsAsync(string path, SftpFileAttributes attributes, int mode, bool includeSpecial, CancellationToken ct)
    {
        attributes.OwnerCanRead = (mode & 0x100) != 0;
        attributes.OwnerCanWrite = (mode & 0x80) != 0;
        attributes.OwnerCanExecute = (mode & 0x40) != 0;
        attributes.GroupCanRead = (mode & 0x20) != 0;
        attributes.GroupCanWrite = (mode & 0x10) != 0;
        attributes.GroupCanExecute = (mode & 0x08) != 0;
        attributes.OthersCanRead = (mode & 0x04) != 0;
        attributes.OthersCanWrite = (mode & 0x02) != 0;
        attributes.OthersCanExecute = (mode & 0x01) != 0;
        if (includeSpecial)
        {
            attributes.IsUIDBitSet = (mode & UnixPermissions.SetUid) != 0;
            attributes.IsGroupIDBitSet = (mode & UnixPermissions.SetGid) != 0;
            attributes.IsStickyBitSet = (mode & UnixPermissions.Sticky) != 0;
        }

        // SSH.NET n'envoie que les attributs modifiés : seuls les droits changent sur le serveur.
        return Task.Run(() => _sftp.SetAttributes(path, attributes), ct);
    }

    public async Task DownloadAsync(RemoteEntry entry, string localPath, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var file = File.Create(localPath);
            var report = progress is null ? null : new Progress<Renci.SshNet.DownloadFileProgressReport>(
                p => progress.Report(new TransferProgress(entry.Name, (long)p.TotalBytesDownloaded, entry.Length)));
            await _sftp.DownloadFileAsync(entry.FullPath, file, report, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _sftp.Dispose();
        _scp?.Dispose();
        _gate.Dispose();
    }

    private async Task<ScpClient> GetScpAsync(CancellationToken ct)
    {
        if (_scp is { IsConnected: true })
        {
            return _scp;
        }

        _scp?.Dispose();
        _scp = await _scpFactory(ct).ConfigureAwait(false);
        return _scp;
    }

    private static async Task RunScpAsync(ScpClient scp, Action<ScpClient> upload, string name,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        EventHandler<Renci.SshNet.Common.ScpUploadEventArgs> handler = (_, e) =>
            progress?.Report(new TransferProgress(e.Filename, e.Uploaded, e.Size));
        scp.Uploading += handler;
        try
        {
            await Task.Run(() => upload(scp), ct).ConfigureAwait(false);
            progress?.Report(new TransferProgress(name, 1, 1));
        }
        finally
        {
            scp.Uploading -= handler;
        }
    }

    private async Task UploadFileSftpAsync(string localPath, string remotePath, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var name = Path.GetFileName(localPath);
        await using var file = File.OpenRead(localPath);
        long total = file.Length;
        var report = progress is null ? null : new Progress<Renci.SshNet.UploadFileProgressReport>(
            p => progress.Report(new TransferProgress(name, (long)p.TotalBytesUploaded, total)));
        await _sftp.UploadFileAsync(file, remotePath, report, ct).ConfigureAwait(false);
    }

    private async Task UploadDirectorySftpAsync(string localDirectory, string remoteDirectory, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(localDirectory))
        {
            await UploadFileSftpAsync(file, RemotePath.Combine(remoteDirectory, Path.GetFileName(file)), progress, ct).ConfigureAwait(false);
        }

        foreach (var sub in Directory.EnumerateDirectories(localDirectory))
        {
            var remoteSub = RemotePath.Combine(remoteDirectory, Path.GetFileName(sub));
            await EnsureDirectoryAsync(remoteSub, ct).ConfigureAwait(false);
            await UploadDirectorySftpAsync(sub, remoteSub, progress, ct).ConfigureAwait(false);
        }
    }

    private async Task EnsureDirectoryAsync(string remote, CancellationToken ct)
    {
        if (!await _sftp.ExistsAsync(remote, ct).ConfigureAwait(false))
        {
            await _sftp.CreateDirectoryAsync(remote, ct).ConfigureAwait(false);
        }
    }

    private static RemoteEntry ToEntry(ISftpFile f, bool isDirectory) => new(
        f.Name,
        f.FullName,
        isDirectory,
        f.IsSymbolicLink,
        f.Length,
        f.LastWriteTime,
        RemoteEntry.FormatPermissions(isDirectory, f.IsSymbolicLink, UnixPermissions.FromAttributes(f.Attributes)));
}
