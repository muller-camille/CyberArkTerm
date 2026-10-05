namespace CyberArkTerm.Core.Ssh;

/// <summary>Fichier ou dossier distant listé par le navigateur SFTP.</summary>
public sealed record RemoteEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    bool IsSymbolicLink,
    long Length,
    DateTime LastWriteTime,
    string Permissions)
{
    public bool IsHidden => Name.StartsWith('.');

    public string SizeText => IsDirectory ? "" : RemotePath.FormatSize(Length);

    /// <summary>Date courte et heure selon les réglages régionaux (01/10/2026 21:05, 10/1/2026 9:05 PM...).</summary>
    public string ModifiedText => LastWriteTime == default ? "" : LastWriteTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Entrée « .. » affichée en tête de liste pour remonter d'un niveau.</summary>
    public bool IsParentLink => Name == "..";

    public static RemoteEntry ParentLink(string directory) =>
        new("..", RemotePath.Parent(directory), true, false, 0, default, "");

    /// <summary>Dossiers d'abord, puis ordre alphabétique sans tenir compte de la casse.</summary>
    public static List<RemoteEntry> Sort(IEnumerable<RemoteEntry> entries) =>
        entries.OrderByDescending(e => e.IsDirectory)
               .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
               .ThenBy(e => e.Name, StringComparer.Ordinal)
               .ToList();

    /// <summary>Droits au format <c>ls -l</c>, bits spéciaux compris (ex. <c>drwxrwxrwt</c>, <c>-rwsr-xr-x</c>).</summary>
    public static string FormatPermissions(bool isDirectory, bool isSymbolicLink, int mode) =>
        (isSymbolicLink ? 'l' : isDirectory ? 'd' : '-') + UnixPermissions.ToSymbolic(mode);

    /// <summary>Droits au format <c>ls -l</c> (ex. <c>drwxr-x---</c>).</summary>
    public static string FormatPermissions(bool isDirectory, bool isSymbolicLink, params bool[] rwx)
    {
        var chars = new char[10];
        chars[0] = isSymbolicLink ? 'l' : isDirectory ? 'd' : '-';
        for (int i = 0; i < 9; i++)
        {
            chars[i + 1] = i < rwx.Length && rwx[i] ? "rwx"[i % 3] : '-';
        }

        return new string(chars);
    }
}

/// <summary>Élément à télécharger avec son chemin relatif (noms Unix, depuis l'élément choisi).</summary>
public sealed record RemoteTreeItem(RemoteEntry Entry, IReadOnlyList<string> Path);

/// <summary>Résultat d'un changement de droits : éléments modifiés et erreurs rencontrées (chemin : message).</summary>
public sealed class PermissionsResult
{
    public int Changed { get; set; }

    public List<string> Errors { get; } = [];
}

public enum TransferProtocol
{
    /// <summary>Envoi par SCP (connexion dédiée au PSMP).</summary>
    Scp,

    /// <summary>Envoi par la connexion SFTP du navigateur.</summary>
    Sftp,
}

/// <summary>
/// Avancement d'un transfert ; <paramref name="Verifying"/> : relecture pour la vérification SHA-256 ;
/// <paramref name="Packing"/> : création de l'archive .tar.gz d'un envoi.
/// </summary>
public sealed record TransferProgress(string FileName, long Transferred, long Total, bool Verifying = false, bool Packing = false);
