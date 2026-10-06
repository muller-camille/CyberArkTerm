using System.Globalization;
using CyberArkTerm.Core.Localization;

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
    public string ModifiedText => LastWriteTime == default ? "" : LastWriteTime.ToString("g", CultureInfo.CurrentCulture);

    /// <summary>Entrée « .. » affichée en tête de liste pour remonter d'un niveau.</summary>
    public bool IsParentLink => Name == "..";

    public static RemoteEntry ParentLink(string directory) =>
        new("..", RemotePath.Parent(directory), true, false, 0, default, "");

    /// <summary>Dossiers d'abord, puis ordre alphabétique sans tenir compte de la casse.</summary>
    public static List<RemoteEntry> Sort(IEnumerable<RemoteEntry> entries) => Sort(entries, RemoteSortColumn.Name);

    /// <summary>
    /// Dossiers d'abord, puis selon la colonne (dans l'ordre inverse si <paramref name="descending"/>) ; à égalité, et
    /// pour la taille des dossiers (sans objet), par nom.
    /// </summary>
    public static List<RemoteEntry> Sort(IEnumerable<RemoteEntry> entries, RemoteSortColumn column, bool descending = false)
    {
        var comparer = Comparer<RemoteEntry>.Create((a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory)
            {
                return a.IsDirectory ? -1 : 1;
            }

            int order = column switch
            {
                RemoteSortColumn.Size when !a.IsDirectory => a.Length.CompareTo(b.Length),
                RemoteSortColumn.Size => 0,
                RemoteSortColumn.Modified => a.LastWriteTime.CompareTo(b.LastWriteTime),
                RemoteSortColumn.Permissions => string.CompareOrdinal(a.Permissions, b.Permissions),
                _ => ByName(a, b),
            };
            if (descending && !(column == RemoteSortColumn.Size && a.IsDirectory))
            {
                order = -order;
            }

            return order != 0 ? order : ByName(a, b);
        });
        return entries.OrderBy(e => e, comparer).ToList();
    }

    private static int ByName(RemoteEntry a, RemoteEntry b)
    {
        int order = StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
        return order != 0 ? order : string.CompareOrdinal(a.Name, b.Name);
    }

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

/// <summary>Colonne de tri de l'onglet Fichiers (clic sur l'en-tête).</summary>
public enum RemoteSortColumn
{
    Name,
    Size,
    Modified,
    Permissions,
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

public static class TransferProtocols
{
    /// <summary>« SCP » ou « SFTP ».</summary>
    public static string Label(this TransferProtocol protocol) => protocol == TransferProtocol.Scp ? "SCP" : "SFTP";

    /// <summary>L'autre protocole, qui prend le relais quand le serveur refuse le premier.</summary>
    public static TransferProtocol Other(this TransferProtocol protocol) =>
        protocol == TransferProtocol.Scp ? TransferProtocol.Sftp : TransferProtocol.Scp;

    /// <summary>
    /// Protocole d'un envoi pour le bilan et l'historique : <paramref name="preferred"/>, ou ceux réellement utilisés
    /// quand le serveur en a refusé un pour certains fichiers (« SCP (SFTP refusé) »).
    /// </summary>
    public static string? Describe(string? preferred, IEnumerable<TransferCheck> checks)
    {
        var list = checks.ToList();
        var refused = list.Select(c => c.Refused).OfType<TransferProtocol>().Distinct().ToList();
        if (refused.Count == 0)
        {
            return preferred;
        }

        var used = list.Select(c => c.Protocol).OfType<TransferProtocol>().Distinct();
        return string.Format(CultureInfo.CurrentCulture, CoreStrings.ProtocolFallback,
            string.Join(" + ", used.Select(Label)), string.Join(" + ", refused.Select(Label)));
    }
}

/// <summary>
/// Avancement d'un transfert ; <paramref name="Verifying"/> : relecture pour la vérification SHA-256 ;
/// <paramref name="Packing"/> : création de l'archive .tar.gz d'un envoi ; <paramref name="Protocol"/> : protocole de
/// l'envoi en cours (celui des Paramètres, ou l'autre quand le serveur l'a refusé).
/// </summary>
public sealed record TransferProgress(string FileName, long Transferred, long Total, bool Verifying = false, bool Packing = false,
    TransferProtocol? Protocol = null);
