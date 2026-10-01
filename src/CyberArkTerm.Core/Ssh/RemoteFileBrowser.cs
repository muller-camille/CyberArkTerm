using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace CyberArkTerm.Core.Ssh;

/// <summary>
/// Navigation dans les fichiers du serveur via SFTP (ls, cd, rm, téléchargement) et dépôt de fichiers
/// par SCP ou SFTP. Les opérations sont sérialisées : une seule à la fois sur la connexion.
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
        RemoteEntry.FormatPermissions(isDirectory, f.IsSymbolicLink,
            f.OwnerCanRead, f.OwnerCanWrite, f.OwnerCanExecute,
            f.GroupCanRead, f.GroupCanWrite, f.GroupCanExecute,
            f.OthersCanRead, f.OthersCanWrite, f.OthersCanExecute));
}
