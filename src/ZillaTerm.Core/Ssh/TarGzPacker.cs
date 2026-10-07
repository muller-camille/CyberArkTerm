using System.Formats.Tar;
using System.IO.Compression;
using ZillaTerm.Core.Terminal;

namespace ZillaTerm.Core.Ssh;

/// <summary>
/// Archive .tar.gz des fichiers et dossiers déposés, pour envoyer un seul fichier au lieu de milliers (beaucoup plus
/// rapide via le PSMP : un seul fichier à ouvrir, transférer et vérifier). Chaque élément déposé est à la racine de
/// l'archive, comme il le serait dans le dossier d'arrivée d'un envoi normal.
/// </summary>
/// <remarks>
/// L'archive doit s'extraire sur tous les Unix rencontrés (Red Hat 5 à 9, HP-UX 11.11 et 11.31, Solaris, AIX…) : format
/// tar POSIX standard (ustar), compris par tous les tar, et pas le format GNU, que seul tar GNU lit ; gzip et tar
/// séparés (les tar sans option -z) ; gzip cherché là où ces systèmes l'installent, archive non compressée s'il manque.
/// </remarks>
public static class TarGzPacker
{
    /// <summary>Droits des fichiers extraits (comme un envoi SCP), et des dossiers.</summary>
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private const UnixFileMode DirectoryMode = FileMode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    /// <summary>
    /// Emplacements de gzip : Linux (/bin, /usr/bin), HP-UX (/usr/contrib/bin), AIX Toolbox (/opt/freeware/bin), Solaris
    /// (/usr/bin, /usr/sfw/bin, OpenCSW), installations locales. Le premier trouvé (par SFTP) sert à l'extraction.
    /// </summary>
    public static IReadOnlyList<string> GzipCandidates { get; } =
        ["/bin/gzip", "/usr/bin/gzip", "/usr/contrib/bin/gzip", "/usr/local/bin/gzip", "/opt/freeware/bin/gzip", "/usr/sfw/bin/gzip", "/opt/csw/bin/gzip"];

    /// <summary>Taille maximale d'un fichier au format ustar (11 chiffres octaux, 8 Gio).</summary>
    public const long MaxUstarFileBytes = 8589934591;

    /// <summary>Premier emplacement de gzip présent sur le serveur, ou null (archive envoyée sans compression).</summary>
    /// <param name="exists">Test d'existence d'un fichier distant (SFTP).</param>
    public static async Task<string?> FindGzipAsync(Func<string, CancellationToken, Task<bool>> exists, CancellationToken ct)
    {
        foreach (var path in GzipCandidates)
        {
            try
            {
                if (await exists(path, ct).ConfigureAwait(false))
                {
                    return path;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Dossier interdit en lecture, par exemple : emplacement suivant.
            }
        }

        return null;
    }

    /// <summary>
    /// Premier élément (chemin dans l'archive) qui ne tient pas dans le format ustar : nom trop long, ou fichier de plus
    /// de 8 Gio. Null si tout tient : l'archive peut alors être proposée.
    /// </summary>
    public static string? UstarProblem(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var trimmed = path.TrimEnd('\\', '/');
            var root = Path.GetFileName(trimmed);
            try
            {
                if (!Directory.Exists(trimmed))
                {
                    if (!FitsUstar(root) || new FileInfo(trimmed).Length > MaxUstarFileBytes)
                    {
                        return root;
                    }

                    continue;
                }

                var top = new DirectoryInfo(trimmed);
                if (!FitsUstar(root + "/"))
                {
                    return root + "/";
                }

                foreach (var entry in top.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
                {
                    var name = root + "/" + Path.GetRelativePath(top.FullName, entry.FullName).Replace('\\', '/');
                    if (entry is DirectoryInfo)
                    {
                        name += "/";
                    }

                    if (!FitsUstar(name) || entry is FileInfo { Length: > MaxUstarFileBytes })
                    {
                        return name;
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Illisible : l'archivage le signalera.
            }
        }

        return null;
    }

    /// <summary>
    /// Vrai si le nom tient dans un en-tête ustar (100 octets, ou un préfixe de 155 octets et un nom de 100 octets
    /// séparés par un « / ») : vérifié en écrivant l'en-tête à blanc, avec le découpage même de l'archivage.
    /// </summary>
    public static bool FitsUstar(string name)
    {
        try
        {
            using var writer = new TarWriter(Stream.Null, TarEntryFormat.Ustar, leaveOpen: true);
            writer.WriteEntry(new UstarTarEntry(name.EndsWith('/') ? TarEntryType.Directory : TarEntryType.RegularFile, name));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Nombre de fichiers et taille totale de ce qui est déposé (dossiers parcourus entièrement).</summary>
    public static (int Files, long Bytes) Measure(IEnumerable<string> paths)
    {
        int files = 0;
        long bytes = 0;
        foreach (var path in paths)
        {
            try
            {
                var entries = Directory.Exists(path)
                    ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories)
                    : [new FileInfo(path)];
                foreach (var file in entries)
                {
                    files++;
                    bytes += file.Length;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Dossier illisible : compté tel quel, l'envoi le signalera.
            }
        }

        return (files, bytes);
    }

    /// <summary>
    /// Nom de l'archive : celui du fichier ou du dossier déposé seul, sinon « upload-AAAAMMJJ-HHMMSS » ; « .tar.gz », ou
    /// « .tar » sans compression.
    /// </summary>
    public static string ArchiveName(IReadOnlyList<string> paths, DateTime now, bool compressed = true) =>
        (paths.Count == 1 ? Path.GetFileName(paths[0].TrimEnd('\\', '/')) : $"upload-{now:yyyyMMdd-HHmmss}") + (compressed ? ".tar.gz" : ".tar");

    /// <summary>
    /// Commande à lancer sur le serveur pour extraire l'archive dans son dossier, puis la supprimer si l'extraction a
    /// réussi : <c>cd '/dossier' &amp;&amp; /usr/bin/gzip -dc './a.tar.gz' | tar xf - &amp;&amp; rm -f './a.tar.gz'</c>, ou
    /// <c>tar xf './a.tar'</c> sans gzip. Forme lue par tous les tar (lettres-clés sans tiret, archive lue sur l'entrée
    /// standard) et par tous les shells, csh compris. Null si un nom contient un caractère de contrôle.
    /// </summary>
    /// <param name="gzip">Emplacement de gzip trouvé sur le serveur (<see cref="GzipCandidates"/>), ou null : archive .tar.</param>
    public static string? ExtractCommand(string remoteDirectory, string archiveName, string? gzip)
    {
        if (gzip is not null && !GzipCandidates.Contains(gzip))
        {
            throw new ArgumentOutOfRangeException(nameof(gzip));
        }

        try
        {
            var directory = WorkingDirectory.TypedQuote(remoteDirectory);
            var archive = WorkingDirectory.TypedQuote("./" + archiveName);
            return gzip is null
                ? $"cd {directory} && tar xf {archive} && rm -f {archive}"
                : $"cd {directory} && {gzip} -dc {archive} | tar xf - && rm -f {archive}";
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Crée l'archive : format ustar (POSIX, lu par tous les tar ; noms en UTF-8), chemins « dossier/sous-dossier/fichier »,
    /// droits 0644 pour les fichiers et 0755 pour les dossiers, propriétaire 0 (l'utilisateur qui extrait en devient
    /// propriétaire, root reste root), dates de modification conservées. À vérifier d'abord avec <see cref="UstarProblem"/>.
    /// </summary>
    /// <param name="compress">Compression gzip (.tar.gz), sinon simple .tar.</param>
    /// <param name="progress">Octets archivés sur le total (<see cref="TransferProgress.Packing"/>).</param>
    /// <returns>Nombre de fichiers archivés.</returns>
    public static async Task<int> CreateAsync(IReadOnlyList<string> paths, string archivePath, bool compress,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var name = Path.GetFileName(archivePath);
        long total = Measure(paths).Bytes;
        long done = 0;
        int files = 0;
        await using var file = new FileStream(archivePath, System.IO.FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await using Stream output = compress ? new GZipStream(file, CompressionLevel.Optimal) : file;
        await using var tar = new TarWriter(output, TarEntryFormat.Ustar, leaveOpen: true);

        async Task AddFileAsync(FileInfo info, string entryName)
        {
            ct.ThrowIfCancellationRequested();
            await using var data = new FileStream(info.FullName, System.IO.FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            var entry = new UstarTarEntry(TarEntryType.RegularFile, entryName)
            {
                DataStream = data,
                Mode = FileMode,
                ModificationTime = info.LastWriteTimeUtc,
            };
            await tar.WriteEntryAsync(entry, ct).ConfigureAwait(false);
            files++;
            done += info.Length;
            progress?.Report(new TransferProgress(name, done, total, Packing: true));
        }

        async Task AddDirectoryAsync(DirectoryInfo directory, string entryName)
        {
            ct.ThrowIfCancellationRequested();
            await tar.WriteEntryAsync(new UstarTarEntry(TarEntryType.Directory, entryName + "/")
            {
                Mode = DirectoryMode,
                ModificationTime = directory.LastWriteTimeUtc,
            }, ct).ConfigureAwait(false);
            foreach (var child in directory.EnumerateFiles())
            {
                await AddFileAsync(child, $"{entryName}/{child.Name}").ConfigureAwait(false);
            }

            foreach (var child in directory.EnumerateDirectories())
            {
                await AddDirectoryAsync(child, $"{entryName}/{child.Name}").ConfigureAwait(false);
            }
        }

        foreach (var path in paths)
        {
            var trimmed = path.TrimEnd('\\', '/');
            if (Directory.Exists(trimmed))
            {
                await AddDirectoryAsync(new DirectoryInfo(trimmed), Path.GetFileName(trimmed)).ConfigureAwait(false);
            }
            else
            {
                await AddFileAsync(new FileInfo(trimmed), Path.GetFileName(trimmed)).ConfigureAwait(false);
            }
        }

        return files;
    }
}
