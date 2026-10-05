using System.Formats.Tar;
using System.IO.Compression;
using CyberArkTerm.Core.Terminal;

namespace CyberArkTerm.Core.Ssh;

/// <summary>
/// Archive .tar.gz des fichiers et dossiers déposés, pour envoyer un seul fichier au lieu de milliers (beaucoup plus
/// rapide via le PSMP : un seul fichier à ouvrir, transférer et vérifier). Chaque élément déposé est à la racine de
/// l'archive, comme il le serait dans le dossier d'arrivée d'un envoi normal.
/// </summary>
public static class TarGzPacker
{
    /// <summary>Droits des fichiers extraits (comme un envoi SCP), et des dossiers.</summary>
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private const UnixFileMode DirectoryMode = FileMode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

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

    /// <summary>Nom de l'archive : celui du fichier ou du dossier déposé seul, sinon « upload-AAAAMMJJ-HHMMSS ».</summary>
    public static string ArchiveName(IReadOnlyList<string> paths, DateTime now) =>
        (paths.Count == 1 ? Path.GetFileName(paths[0].TrimEnd('\\', '/')) : $"upload-{now:yyyyMMdd-HHmmss}") + ".tar.gz";

    /// <summary>
    /// Commande à lancer sur le serveur pour extraire l'archive dans son dossier, puis la supprimer si l'extraction a
    /// réussi : <c>cd '/dossier' &amp;&amp; gzip -dc './a.tar.gz' | tar -xf - &amp;&amp; rm -f './a.tar.gz'</c>. gzip et tar
    /// séparés : fonctionne aussi avec les tar sans option -z (AIX, Solaris). Null si un nom contient un caractère de
    /// contrôle.
    /// </summary>
    public static string? ExtractCommand(string remoteDirectory, string archiveName)
    {
        try
        {
            var archive = WorkingDirectory.ShellQuote("./" + archiveName);
            return $"cd {WorkingDirectory.ShellQuote(remoteDirectory)} && gzip -dc {archive} | tar -xf - && rm -f {archive}";
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Crée l'archive : format GNU (noms longs, UTF-8), chemins « dossier/sous-dossier/fichier », droits 0644 pour les
    /// fichiers et 0755 pour les dossiers, dates de modification conservées.
    /// </summary>
    /// <param name="progress">Octets archivés sur le total (<see cref="TransferProgress.Packing"/>).</param>
    /// <returns>Nombre de fichiers archivés.</returns>
    public static async Task<int> CreateAsync(IReadOnlyList<string> paths, string archivePath, IProgress<TransferProgress>? progress,
        CancellationToken ct)
    {
        var name = Path.GetFileName(archivePath);
        long total = Measure(paths).Bytes;
        long done = 0;
        int files = 0;
        await using var file = new FileStream(archivePath, System.IO.FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await using var gzip = new GZipStream(file, CompressionLevel.Optimal);
        await using var tar = new TarWriter(gzip, TarEntryFormat.Gnu, leaveOpen: true);

        async Task AddFileAsync(FileInfo info, string entryName)
        {
            ct.ThrowIfCancellationRequested();
            await using var data = new FileStream(info.FullName, System.IO.FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            var entry = new GnuTarEntry(TarEntryType.RegularFile, entryName)
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
            await tar.WriteEntryAsync(new GnuTarEntry(TarEntryType.Directory, entryName + "/")
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
