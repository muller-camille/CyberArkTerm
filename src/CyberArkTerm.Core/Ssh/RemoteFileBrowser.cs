using System.Globalization;
using CyberArkTerm.Core.Localization;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace CyberArkTerm.Core.Ssh;

/// <summary>
/// Navigation dans les fichiers du serveur via SFTP (ls, cd, rm, chmod, téléchargement, modification) et dépôt
/// de fichiers par SCP ou SFTP. Les opérations sont sérialisées : une seule à la fois sur la connexion. Chaque fichier
/// envoyé ou téléchargé est vérifié par sa somme SHA-256 des deux côtés (<see cref="TransferCheck"/>).
/// </summary>
public sealed class RemoteFileBrowser : IDisposable
{
    private readonly SftpClient _sftp;
    private readonly Func<CancellationToken, Task<ScpClient>> _scpFactory;
    private readonly PriorityGate _gate = new();

    // Une seule commande SCP à la fois sur la connexion SCP.
    private readonly SemaphoreSlim _scpGate = new(1, 1);
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
        using var entered = await _gate.EnterAsync(background: false, ct).ConfigureAwait(false);
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

    /// <summary>Supprime un fichier (rm) ou un dossier vide (rmdir).</summary>
    public async Task DeleteAsync(RemoteEntry entry, CancellationToken ct)
    {
        using var entered = await _gate.EnterAsync(background: false, ct).ConfigureAwait(false);
        if (entry.IsDirectory && !entry.IsSymbolicLink)
        {
            await _sftp.DeleteDirectoryAsync(entry.FullPath, ct).ConfigureAwait(false);
        }
        else
        {
            await _sftp.DeleteFileAsync(entry.FullPath, ct).ConfigureAwait(false);
        }
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken ct)
    {
        using var entered = await _gate.EnterAsync(background: false, ct).ConfigureAwait(false);
        await _sftp.CreateDirectoryAsync(path, ct).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken ct)
    {
        using var entered = await _gate.EnterAsync(background: false, ct).ConfigureAwait(false);
        return await _sftp.ExistsAsync(path, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Envoie un fichier ou un dossier local (récursivement) dans <paramref name="remoteDirectory"/>, un fichier à la
    /// fois : chaque fichier est envoyé (SCP ou SFTP), puis relu par SFTP pour comparer sa somme SHA-256 à celle du
    /// fichier local. Entre deux fichiers, les demandes interactives (navigation, suppression...) passent avant.
    /// </summary>
    /// <param name="checks">Reçoit la vérification de chaque fichier traité, y compris celui en échec ou interrompu.</param>
    /// <param name="background">Transfert de la file d'attente : passe après les demandes interactives.</param>
    /// <exception cref="OperationCanceledException">
    /// Annulé : le fichier en cours d'envoi, incomplet, est supprimé du serveur ; les fichiers déjà envoyés restent.
    /// Pendant la vérification, le fichier, complet, reste en place (non vérifié).
    /// </exception>
    public async Task UploadAsync(string localPath, string remoteDirectory, TransferProtocol protocol, ICollection<TransferCheck> checks,
        IProgress<TransferProgress>? progress, bool background, CancellationToken ct)
    {
        var name = Path.GetFileName(localPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var remote = RemotePath.Combine(remoteDirectory, name);
        if (Directory.Exists(localPath))
        {
            await UploadDirectoryAsync(localPath, remote, protocol, checks, progress, background, ct).ConfigureAwait(false);
        }
        else
        {
            await UploadFileAsync(localPath, remote, protocol, checks, progress, background, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Nombre de fichiers que <see cref="UploadAsync"/> enverra pour ce chemin local.</summary>
    public static int CountFiles(string localPath) =>
        Directory.Exists(localPath) ? Directory.EnumerateFiles(localPath, "*", SearchOption.AllDirectories).Count() : 1;

        /// <summary>Suivi d'un fichier du serveur (tail -f) par SFTP : taille, puis lecture de ce qui a été ajouté.</summary>
    public ITailSource TailSource(string path) => new SftpTailSource(this, path);

    /// <summary>Taille actuelle d'un fichier (lien symbolique suivi).</summary>
    public async Task<long> GetSizeAsync(string path, CancellationToken ct)
    {
        using var entered = await _gate.EnterAsync(background: false, ct).ConfigureAwait(false);
        return (await _sftp.GetAttributesAsync(path, ct).ConfigureAwait(false)).Size;
    }

    /// <summary>Lit au plus <paramref name="count"/> octets d'un fichier à partir de <paramref name="offset"/>.</summary>
    public async Task<byte[]> ReadAsync(string path, long offset, int count, CancellationToken ct)
    {
        using var entered = await _gate.EnterAsync(background: false, ct).ConfigureAwait(false);
        await using var stream = await _sftp.OpenAsync(path, FileMode.Open, FileAccess.Read, ct).ConfigureAwait(false);
        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read, count - read), ct).ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        return read == count ? buffer : buffer[..read];
    }

    /// <summary>
    /// Contenu entier d'un fichier, en mémoire (comparaison de fichiers : rien n'est écrit sur le poste).
    /// </summary>
    /// <exception cref="FileTooLargeException">Le fichier dépasse <paramref name="maxBytes"/>.</exception>
    public async Task<byte[]> ReadAllBytesAsync(string path, long maxBytes, CancellationToken ct)
    {
        using var entered = await _gate.EnterAsync(background: false, ct).ConfigureAwait(false);
        var attributes = await _sftp.GetAttributesAsync(path, ct).ConfigureAwait(false);
        if (attributes.Size > maxBytes)
        {
            throw new FileTooLargeException(path, attributes.Size, maxBytes);
        }

        await using var stream = await _sftp.OpenAsync(path, FileMode.Open, FileAccess.Read, ct).ConfigureAwait(false);
        using var content = new MemoryStream((int)Math.Max(0, attributes.Size));
        var buffer = new byte[256 * 1024];
        int n;
        while ((n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            content.Write(buffer, 0, n);
            if (content.Length > maxBytes)
            {
                // Fichier qui grossit pendant la lecture.
                throw new FileTooLargeException(path, content.Length, maxBytes);
            }
        }

        return content.ToArray();
    }

    private sealed class SftpTailSource(RemoteFileBrowser browser, string path) : ITailSource
    {
        public Task<long> GetSizeAsync(CancellationToken ct) => browser.GetSizeAsync(path, ct);

        public Task<byte[]> ReadAsync(long offset, int count, CancellationToken ct) => browser.ReadAsync(path, offset, count, ct);
    }

    /// <summary>Date de modification et taille actuelles d'un fichier.</summary>
    public async Task<(DateTime LastWriteTime, long Length)> GetStatAsync(string path, CancellationToken ct)
    {
        using var entered = await _gate.EnterAsync(background: false, ct).ConfigureAwait(false);
        var attributes = await _sftp.GetAttributesAsync(path, ct).ConfigureAwait(false);
        return (attributes.LastWriteTime, attributes.Size);
    }

    /// <summary>
    /// Remplace le contenu d'un fichier existant par SFTP, sur place : propriétaire, droits, ACL, contexte SELinux,
    /// liens physiques et symboliques sont conservés (un fichier temporaire renommé les perdrait, et SCP recrée le
    /// fichier en 644). Renvoie sa nouvelle date et sa taille.
    /// </summary>
    /// <exception cref="IOException">Le serveur n'a pas reçu tout le contenu.</exception>
    /// <remarks>
    /// Une coupure pendant l'écriture laisse le fichier du serveur incomplet : l'appelant garde le contenu et le renvoie
    /// en entier à la tentative suivante.
    /// </remarks>
    public async Task<(DateTime LastWriteTime, long Length)> WriteFileAsync(string remotePath, byte[] content, CancellationToken ct)
    {
        using var entered = await _gate.EnterAsync(background: false, ct).ConfigureAwait(false);
        using var stream = new MemoryStream(content, writable: false);
        await _sftp.UploadFileAsync(stream, remotePath, ct).ConfigureAwait(false);
        var attributes = await _sftp.GetAttributesAsync(remotePath, ct).ConfigureAwait(false);
        if (attributes.Size != content.Length)
        {
            throw new IOException(string.Format(CultureInfo.CurrentCulture, CoreStrings.WriteIncomplete, attributes.Size, content.Length));
        }

        return (attributes.LastWriteTime, attributes.Size);
    }

    /// <summary>
    /// Droits actuels (12 bits) de <paramref name="path"/>. Pour un lien symbolique, ceux de sa cible, celle que chmod
    /// modifie ; null si le serveur ne donne que ceux du lien.
    /// </summary>
    public async Task<int?> GetModeAsync(string path, CancellationToken ct)
    {
        using var entered = await _gate.EnterAsync(background: false, ct).ConfigureAwait(false);
        // SSH.NET résout le chemin (realpath) avant de lire les attributs : on obtient ceux de la cible.
        var attributes = await _sftp.GetAttributesAsync(path, ct).ConfigureAwait(false);
        return attributes.IsSymbolicLink ? null : UnixPermissions.FromAttributes(attributes);
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
        using var entered = await _gate.EnterAsync(background: false, ct).ConfigureAwait(false);
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

    /// <summary>
    /// Éléments choisis et, pour les dossiers, tout leur contenu (fichiers cachés compris), chacun avec son chemin
    /// relatif ; un dossier vient avant son contenu. À l'intérieur, les liens vers des dossiers ne sont pas suivis
    /// (boucles, sortie de l'arborescence) ; le dossier courant du navigateur ne change pas.
    /// </summary>
    /// <exception cref="InvalidOperationException">Plus de <paramref name="maxItems"/> éléments.</exception>
    public async Task<List<RemoteTreeItem>> ListTreeAsync(IReadOnlyList<RemoteEntry> roots, int maxItems, CancellationToken ct)
    {
        using var entered = await _gate.EnterAsync(background: false, ct).ConfigureAwait(false);
        var items = new List<RemoteTreeItem>();
        foreach (var root in roots)
        {
            await AddTreeAsync(root, [root.Name], items, maxItems, ct).ConfigureAwait(false);
        }

        return items;
    }

    private async Task AddTreeAsync(RemoteEntry entry, IReadOnlyList<string> path, List<RemoteTreeItem> items, int maxItems,
        CancellationToken ct)
    {
        if (items.Count >= maxItems)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, CoreStrings.TooManyFiles, maxItems));
        }

        items.Add(new RemoteTreeItem(entry, path));
        if (!entry.IsDirectory)
        {
            return;
        }

        var children = new List<RemoteEntry>();
        await foreach (var file in _sftp.ListDirectoryAsync(entry.FullPath, ct).ConfigureAwait(false))
        {
            if (file.Name is "." or "..")
            {
                continue;
            }

            if (!file.IsSymbolicLink)
            {
                children.Add(ToEntry(file, file.IsDirectory));
                continue;
            }

            try
            {
                // Lien vers un dossier : pas suivi. Lien vers un fichier : téléchargé (le contenu et la taille de la cible).
                var target = await _sftp.GetAttributesAsync(file.FullName, ct).ConfigureAwait(false);
                if (!target.IsDirectory)
                {
                    children.Add(ToEntry(file, false) with { Length = target.Size });
                }
            }
            catch (Exception e) when (e is Renci.SshNet.Common.SshException or InvalidOperationException)
            {
                // Lien cassé : rien à télécharger.
            }
        }

        foreach (var child in RemoteEntry.Sort(children))
        {
            await AddTreeAsync(child, [.. path, child.Name], items, maxItems, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Télécharge un fichier en hachant (SHA-256) les données reçues du serveur, puis relit le fichier écrit sur le
    /// disque pour comparer les deux sommes.
    /// </summary>
    public Task<TransferCheck> DownloadAsync(RemoteEntry entry, string localPath, IProgress<TransferProgress>? progress, CancellationToken ct) =>
        DownloadAsync(entry, localPath, null, progress, background: false, ct);

    /// <inheritdoc cref="DownloadAsync(RemoteEntry, string, IProgress{TransferProgress}?, CancellationToken)"/>
    /// <param name="checks">Reçoit la vérification, y compris en cas d'échec ou d'annulation.</param>
    /// <param name="background">Transfert de la file d'attente : passe après les demandes interactives.</param>
    /// <exception cref="OperationCanceledException">
    /// Annulé pendant le téléchargement : le fichier local, incomplet, est supprimé. Pendant la vérification, il reste.
    /// </exception>
    public async Task<TransferCheck> DownloadAsync(RemoteEntry entry, string localPath, ICollection<TransferCheck>? checks,
        IProgress<TransferProgress>? progress, bool background, CancellationToken ct)
    {
        byte[] remoteHash;
        long received;
        bool started = false;
        try
        {
            using (await _gate.EnterAsync(background, ct).ConfigureAwait(false))
            {
                started = true;
                await using var file = File.Create(localPath);
                using var hashing = new HashingStream(file);
                var report = progress is null ? null : new Progress<Renci.SshNet.DownloadFileProgressReport>(
                    p => progress.Report(new TransferProgress(entry.Name, (long)p.TotalBytesDownloaded, entry.Length)));
                await _sftp.DownloadFileAsync(entry.FullPath, hashing, report, ct).ConfigureAwait(false);
                remoteHash = hashing.GetHash();
                received = hashing.Count;
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException && started)
        {
            // Annulé ou en échec en cours de route : la copie locale, incomplète, est supprimée.
            var detail = DiscardLocal(localPath);
            bool cancelled = e is OperationCanceledException && ct.IsCancellationRequested;
            checks?.Add(new TransferCheck(entry.Name, localPath, entry.FullPath, -1, [], -1, [],
                cancelled ? detail : $"{e.Message} ({detail})") { Interrupted = cancelled, Failed = !cancelled });
            throw;
        }

        progress?.Report(new TransferProgress(entry.Name, 0, received, Verifying: true));
        TransferCheck check;
        try
        {
            var (localHash, localLength) = await TransferCheck.HashFileAsync(localPath, ct).ConfigureAwait(false);
            check = new TransferCheck(entry.Name, localPath, entry.FullPath, localLength, localHash, received, remoteHash);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            checks?.Add(new TransferCheck(entry.Name, localPath, entry.FullPath, -1, [], received, remoteHash, CoreStrings.TransferCheckCancelled));
            throw;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Fichier écrit mais illisible ensuite (antivirus, droits) : téléchargé, non vérifié.
            check = new TransferCheck(entry.Name, localPath, entry.FullPath, -1, [], received, remoteHash, e.Message);
        }

        checks?.Add(check);
        return check;
    }

        public void Dispose()
    {
        _sftp.Dispose();
        _scp?.Dispose();
        _scpGate.Dispose();
    }

    private async Task<ScpClient> GetScpAsync(CancellationToken ct)
    {
        if (_scp is { } current && IsUsable(current))
        {
            return current;
        }

        _scp?.Dispose();
        _scp = await _scpFactory(ct).ConfigureAwait(false);
        return _scp;
    }

    private async Task UploadDirectoryAsync(string localDirectory, string remoteDirectory, TransferProtocol protocol,
        ICollection<TransferCheck> checks, IProgress<TransferProgress>? progress, bool background, CancellationToken ct)
    {
        using (await _gate.EnterAsync(background, ct).ConfigureAwait(false))
        {
            await EnsureDirectoryAsync(remoteDirectory, ct).ConfigureAwait(false);
        }

        foreach (var file in Directory.EnumerateFiles(localDirectory))
        {
            await UploadFileAsync(file, RemotePath.Combine(remoteDirectory, Path.GetFileName(file)), protocol, checks, progress, background, ct)
                .ConfigureAwait(false);
        }

        foreach (var sub in Directory.EnumerateDirectories(localDirectory))
        {
            await UploadDirectoryAsync(sub, RemotePath.Combine(remoteDirectory, Path.GetFileName(sub)), protocol, checks, progress, background, ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Un fichier : envoi, puis vérification. Annulé pendant l'envoi : le fichier incomplet est supprimé du serveur
    /// (seulement si son envoi avait commencé, pour ne pas effacer un fichier existant resté intact). En échec : noté
    /// tel quel, le fichier du serveur pouvant être incomplet.
    /// </summary>
    private async Task UploadFileAsync(string localPath, string remotePath, TransferProtocol protocol, ICollection<TransferCheck> checks,
        IProgress<TransferProgress>? progress, bool background, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var name = Path.GetFileName(localPath);
        bool started = false;
        (byte[] Hash, long Length)? sent = null;
        try
        {
            if (protocol == TransferProtocol.Scp)
            {
                await SendScpAsync(localPath, remotePath, name, () => started = true, progress, ct).ConfigureAwait(false);
            }
            else
            {
                using (await _gate.EnterAsync(background, ct).ConfigureAwait(false))
                {
                    started = true;
                    sent = await SendSftpAsync(localPath, remotePath, name, progress, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (started)
            {
                checks.Add(await DiscardRemoteAsync(name, localPath, remotePath).ConfigureAwait(false));
            }

            throw;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            checks.Add(new TransferCheck(name, localPath, remotePath, -1, [], -1, [], e.Message) { Upload = true, Failed = true });
            throw;
        }

        // Vérification. Annulée : le fichier, complet, reste sur le serveur, non vérifié.
        try
        {
            // SCP lit le fichier lui-même : il est haché après coup.
            var (localHash, localLength) = sent ?? await TransferCheck.HashFileAsync(localPath, ct).ConfigureAwait(false);
            using (await _gate.EnterAsync(background, ct).ConfigureAwait(false))
            {
                checks.Add(await CheckRemoteAsync(name, localPath, remotePath, localLength, localHash, progress, ct).ConfigureAwait(false));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            checks.Add(new TransferCheck(name, localPath, remotePath, -1, [], -1, [], CoreStrings.TransferCheckCancelled) { Upload = true });
            throw;
        }
    }

    /// <summary>Envoi SFTP, en hachant le fichier local au fil de l'envoi.</summary>
    private async Task<(byte[] Hash, long Length)> SendSftpAsync(string localPath, string remotePath, string name,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        await using var file = File.OpenRead(localPath);
        using var hashing = new HashingStream(file);
        long total = file.Length;
        var report = progress is null ? null : new Progress<Renci.SshNet.UploadFileProgressReport>(
            p => progress.Report(new TransferProgress(name, (long)p.TotalBytesUploaded, total)));
        await _sftp.UploadFileAsync(hashing, remotePath, report, ct).ConfigureAwait(false);
        return (hashing.GetHash(), hashing.Count);
    }

    /// <summary>Après une annulation, temps laissé à l'envoi SCP pour s'arrêter de lui-même avant de couper la connexion.</summary>
    internal static TimeSpan ScpStopTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Attente avant de réessayer un envoi SCP dont le canal a été fermé par le serveur avant tout envoi.</summary>
    internal static TimeSpan ScpRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Envoi SCP (connexion SCP dédiée, la connexion SFTP reste libre pendant ce temps). Une annulation arrête l'envoi
    /// à la lecture suivante du fichier : seul son canal se ferme, la connexion SCP reste ouverte pour les envois
    /// suivants (rouvrir une session par le PSMP juste après en avoir coupé une peut être refusé). La connexion n'est
    /// coupée que si l'envoi ne s'arrête pas (serveur muet). Une connexion en échec est oubliée : l'envoi suivant en
    /// ouvre une neuve ; si le serveur ferme le canal avant le début de l'envoi, un second essai est fait.
    /// </summary>
    private async Task SendScpAsync(string localPath, string remotePath, string name, Action started,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        await _scpGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (int attempt = 1; ; attempt++)
            {
                var scp = await GetScpAsync(ct).ConfigureAwait(false);
                await using var file = File.OpenRead(localPath);
                using var source = new CancellableReadStream(file, ct);
                EventHandler<Renci.SshNet.Common.ScpUploadEventArgs> handler = (_, e) => progress?.Report(new TransferProgress(name, e.Uploaded, e.Size));
                scp.Uploading += handler;
                try
                {
                    started();
                    var upload = Task.Run(() => scp.Upload(source, remotePath), CancellationToken.None);
                    await using (ct.Register(() => _ = StopStuckScpAsync(upload, scp)))
                    {
                        await upload.ConfigureAwait(false);
                    }

                    return;
                }
                catch (Exception) when (ct.IsCancellationRequested)
                {
                    // Arrêtée à la lecture suivante : la connexion reste utilisable. Coupée (serveur muet) : oubliée.
                    if (!IsUsable(scp))
                    {
                        ForgetScp(scp);
                    }

                    throw new OperationCanceledException(ct);
                }
                catch (Renci.SshNet.Common.ScpException)
                {
                    // Refus de scp sur le serveur (droits, dossier absent…) : message du serveur, connexion intacte.
                    throw;
                }
                catch (Renci.SshNet.Common.SshException e) when (attempt == 1 && !source.WasRead)
                {
                    // Canal fermé par le serveur avant l'envoi du contenu (session PSMP refusée…) : nouvel essai, connexion neuve.
                    Diagnostics.DebugLog.Write("files", $"SCP : canal fermé par le serveur avant l'envoi de {name} ({e.Message}), nouvel essai");
                    ForgetScp(scp);
                    await Task.Delay(ScpRetryDelay, ct).ConfigureAwait(false);
                }
                catch (Renci.SshNet.Common.SshException e)
                {
                    ForgetScp(scp);
                    throw new IOException(string.Format(CultureInfo.CurrentCulture, CoreStrings.ScpClosedByServer, e.Message), e);
                }
                catch (Exception)
                {
                    ForgetScp(scp);
                    throw;
                }
                finally
                {
                    scp.Uploading -= handler;
                }
            }
        }
        finally
        {
            _scpGate.Release();
        }
    }

    /// <summary>Annulé mais l'envoi ne s'arrête pas (serveur qui ne lit plus) : la connexion SCP est coupée.</summary>
    private static async Task StopStuckScpAsync(Task upload, ScpClient scp)
    {
        if (await Task.WhenAny(upload, Task.Delay(ScpStopTimeout)).ConfigureAwait(false) != upload)
        {
            Diagnostics.DebugLog.Write("files", "SCP : l'envoi annulé ne s'arrête pas, connexion SCP coupée");
            AbortScp(scp);
        }
    }

    /// <summary>Connexion SCP en échec ou coupée : fermée, et la prochaine sera neuve.</summary>
    private void ForgetScp(ScpClient scp)
    {
        if (ReferenceEquals(_scp, scp))
        {
            _scp = null;
        }

        AbortScp(scp);
    }

    /// <summary>Connexion SCP encore ouverte (une annulation tombée à la fin d'un envoi a pu la fermer).</summary>
    private static bool IsUsable(ScpClient scp)
    {
        try
        {
            return scp.IsConnected;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static void AbortScp(ScpClient scp)
    {
        try
        {
            scp.Dispose();
        }
        catch (Exception e) when (e is Renci.SshNet.Common.SshException or ObjectDisposedException or InvalidOperationException or IOException)
        {
            // Connexion déjà coupée.
        }
    }

    /// <summary>Supprime du serveur le fichier dont l'envoi a été interrompu.</summary>
    private async Task<TransferCheck> DiscardRemoteAsync(string name, string localPath, string remotePath)
    {
        string detail;
        try
        {
            using (await _gate.EnterAsync(background: false, CancellationToken.None).ConfigureAwait(false))
            {
                await _sftp.DeleteFileAsync(remotePath, CancellationToken.None).ConfigureAwait(false);
            }

            detail = CoreStrings.TransferIncompleteDeleted;
        }
        catch (Renci.SshNet.Common.SftpPathNotFoundException)
        {
            detail = CoreStrings.TransferIncompleteDeleted;
        }
        catch (Exception e) when (e is Renci.SshNet.Common.SshException or ObjectDisposedException or InvalidOperationException)
        {
            detail = string.Format(CultureInfo.CurrentCulture, CoreStrings.TransferIncompleteNotDeleted, e.Message);
        }

        return new TransferCheck(name, localPath, remotePath, -1, [], -1, [], detail) { Upload = true, Interrupted = true };
    }

    /// <summary>Supprime la copie locale d'un téléchargement interrompu ; renvoie ce qui a été fait.</summary>
    private static string DiscardLocal(string localPath)
    {
        try
        {
            File.Delete(localPath);
            return CoreStrings.TransferIncompleteDeleted;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Format(CultureInfo.CurrentCulture, CoreStrings.TransferIncompleteNotDeleted, e.Message);
        }
    }

    /// <summary>Relit par SFTP le fichier arrivé sur le serveur et le compare au fichier local.</summary>
    private async Task<TransferCheck> CheckRemoteAsync(string name, string localPath, string remotePath, long localLength, byte[] localHash,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new TransferProgress(name, 0, localLength, Verifying: true));
        using var sink = new HashingStream(null);
        var report = progress is null ? null : new Progress<Renci.SshNet.DownloadFileProgressReport>(
            p => progress.Report(new TransferProgress(name, (long)p.TotalBytesDownloaded, localLength, Verifying: true)));
        try
        {
            await _sftp.DownloadFileAsync(remotePath, sink, report, ct).ConfigureAwait(false);
        }
        catch (Renci.SshNet.Common.SshException e) when (_sftp.IsConnected)
        {
            // Fichier envoyé mais illisible par le compte (droits, ACL) : envoyé, non vérifié.
            return new TransferCheck(name, localPath, remotePath, localLength, localHash, -1, [], e.Message) { Upload = true };
        }

        return new TransferCheck(name, localPath, remotePath, localLength, localHash, sink.Count, sink.GetHash()) { Upload = true };
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
