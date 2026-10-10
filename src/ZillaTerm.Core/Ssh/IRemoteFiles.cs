namespace ZillaTerm.Core.Ssh;

/// <summary>
/// Fichiers d'un serveur, tels que l'onglet Fichiers les manipule : navigation, transferts vérifiés par SHA-256,
/// lecture et écriture (éditeur, comparaison, suivi), droits. SFTP/SCP (<see cref="RemoteFileBrowser"/>) ou FTP/FTPS
/// (<see cref="Ftp.FtpFileBrowser"/>). Les opérations sont sérialisées sur la connexion.
/// </summary>
public interface IRemoteFiles : IDisposable
{
    /// <summary>Dossier de connexion (répertoire personnel du compte).</summary>
    string HomeDirectory { get; }

    string CurrentDirectory { get; }

    bool IsConnected { get; }

    /// <summary>Envoi par SCP ou SFTP, au choix (Paramètres) ; faux : un seul protocole, <see cref="UploadProtocol"/>.</summary>
    bool ChoosesUploadProtocol { get; }

    /// <summary>Protocole des envois quand il n'y a pas de choix (FTP, FTPS).</summary>
    TransferProtocol UploadProtocol { get; }

    /// <summary>Liste un dossier (chemin absolu ou relatif au dossier courant) et en fait le dossier courant.</summary>
    Task<List<RemoteEntry>> ListAsync(string path, bool showHidden, CancellationToken ct);

    /// <summary>Liste un dossier (chemin absolu) sans en faire le dossier courant.</summary>
    Task<List<RemoteEntry>> BrowseAsync(string directory, bool showHidden, CancellationToken ct);

    /// <summary>Supprime un fichier ou un dossier vide.</summary>
    Task DeleteAsync(RemoteEntry entry, CancellationToken ct);

    Task CreateDirectoryAsync(string path, CancellationToken ct);

    /// <summary>
    /// Renomme un fichier, un dossier ou un lien symbolique lui-même, jamais sa cible (<paramref name="newPath"/> dans le
    /// même dossier). Ne remplace jamais un élément existant : <see cref="IOException"/> si <paramref name="newPath"/>
    /// existe déjà.
    /// </summary>
    Task RenameAsync(RemoteEntry entry, string newPath, CancellationToken ct);

    Task<bool> ExistsAsync(string path, CancellationToken ct);

    /// <summary>Envoie un fichier ou un dossier local (récursivement) ; chaque fichier est vérifié (SHA-256).</summary>
    /// <param name="protocol">Protocole préféré quand il y a le choix (<see cref="ChoosesUploadProtocol"/>).</param>
    Task UploadAsync(string localPath, string remoteDirectory, TransferProtocol protocol, ICollection<TransferCheck> checks,
        IProgress<TransferProgress>? progress, bool background, CancellationToken ct);

    /// <summary>Suivi d'un fichier (tail -f) : taille, puis lecture de ce qui a été ajouté.</summary>
    ITailSource TailSource(string path);

    Task<long> GetSizeAsync(string path, CancellationToken ct);

    Task<byte[]> ReadAsync(string path, long offset, int count, CancellationToken ct);

    /// <exception cref="FileTooLargeException">Le fichier dépasse <paramref name="maxBytes"/>.</exception>
    Task<byte[]> ReadAllBytesAsync(string path, long maxBytes, CancellationToken ct);

    Task<(DateTime LastWriteTime, long Length)> GetStatAsync(string path, CancellationToken ct);

    /// <summary>Remplace le contenu d'un fichier ; renvoie sa nouvelle date et sa taille.</summary>
    Task<(DateTime LastWriteTime, long Length)> WriteFileAsync(string remotePath, byte[] content, CancellationToken ct);

    /// <summary>Droits actuels (12 bits) ; null s'ils ne sont pas connus.</summary>
    Task<int?> GetModeAsync(string path, CancellationToken ct);

    /// <summary>
    /// Applique <paramref name="change"/> à l'élément et, si demandé, à tout le contenu d'un dossier (sans les bits
    /// spéciaux, liens non suivis). Annulable entre deux éléments.
    /// </summary>
    Task<PermissionsResult> SetPermissionsAsync(string path, PermissionChange change, bool recursive,
        bool executeOnlyIfAlready, IProgress<int>? progress, CancellationToken ct);

    /// <summary>Éléments choisis et tout le contenu des dossiers, chacun avec son chemin relatif.</summary>
    Task<List<RemoteTreeItem>> ListTreeAsync(IReadOnlyList<RemoteEntry> roots, int maxItems, CancellationToken ct);

    /// <summary>Télécharge un fichier en hachant les données reçues, puis relit la copie locale pour comparer.</summary>
    Task<TransferCheck> DownloadAsync(RemoteEntry entry, string localPath, IProgress<TransferProgress>? progress, CancellationToken ct);

    Task<TransferCheck> DownloadAsync(RemoteEntry entry, string localPath, ICollection<TransferCheck>? checks,
        IProgress<TransferProgress>? progress, bool background, CancellationToken ct);
}
