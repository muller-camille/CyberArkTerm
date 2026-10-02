using System.Security.Cryptography;
using CyberArkTerm.Core.Localization;

namespace CyberArkTerm.Core.KeePass;

/// <summary>
/// Coffre KeePass ouvert depuis un fichier, en lecture et en écriture. Chaque modification relit le fichier, s'applique
/// à son contenu du moment (les changements faits ailleurs entre-temps sont gardés), est vérifiée en relisant le
/// résultat, puis remplace le fichier d'un coup ; la version précédente est gardée dans « fichier.kdbx.bak ».
/// </summary>
public sealed class KeePassVault : IDisposable
{
    private const int SaveAttempts = 3;

    private readonly KeePassKey _key;
    private readonly TransformCache _cache;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private byte[] _fileHash = [];

    private KeePassVault(string path, KeePassKey key, TransformCache cache, KeePassDatabase database, byte[] fileHash)
    {
        FilePath = path;
        _key = key;
        _cache = cache;
        Database = database;
        _fileHash = fileHash;
    }

    public string FilePath { get; }

    /// <summary>Contenu lu au dernier chargement ou enregistrement.</summary>
    public KeePassDatabase Database { get; private set; }

    public string BackupPath => FilePath + ".bak";

    /// <summary>Ouvre le coffre ; la clé appartient ensuite au coffre (libérée avec lui), même en cas d'échec.</summary>
    public static async Task<KeePassVault> OpenAsync(string path, KeePassKey key, CancellationToken cancellation = default)
    {
        var cache = new TransformCache();
        try
        {
            var bytes = await ReadSharedAsync(path, cancellation).ConfigureAwait(false);
            var database = await Task.Run(() => KdbxFile.Read(bytes, key, cache, cancellation), cancellation).ConfigureAwait(false);
            return new KeePassVault(path, key, cache, database, SHA256.HashData(bytes));
        }
        catch
        {
            cache.Dispose();
            key.Dispose();
            throw;
        }
    }

    /// <summary>Vrai si le fichier a changé depuis le dernier chargement ou enregistrement (autre programme).</summary>
    public async Task<bool> HasChangedOnDiskAsync(CancellationToken cancellation = default)
    {
        var bytes = await ReadSharedAsync(FilePath, cancellation).ConfigureAwait(false);
        return !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), _fileHash);
    }

    /// <summary>Relit le fichier (après une modification faite par un autre programme).</summary>
    public async Task ReloadAsync(CancellationToken cancellation = default)
    {
        await _lock.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var bytes = await ReadSharedAsync(FilePath, cancellation).ConfigureAwait(false);
            var database = await Task.Run(() => KdbxFile.Read(bytes, _key, _cache, cancellation), cancellation).ConfigureAwait(false);
            Database.Dispose();
            Database = database;
            _fileHash = SHA256.HashData(bytes);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Applique <paramref name="change"/> au contenu actuel du fichier et enregistre. Si le fichier change pendant
    /// l'enregistrement, tout est refait sur la nouvelle version ; rien n'est écrit si la vérification échoue.
    /// </summary>
    public async Task<T> SaveAsync<T>(Func<KeePassDatabase, T> change, CancellationToken cancellation = default)
    {
        await _lock.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            for (int attempt = 0; attempt < SaveAttempts; attempt++)
            {
                var original = await ReadSharedAsync(FilePath, cancellation).ConfigureAwait(false);
                var (database, result, output) = await Task.Run(() => Prepare(original, change, cancellation), cancellation).ConfigureAwait(false);
                try
                {
                    if (!await WriteAsync(original, output, cancellation).ConfigureAwait(false))
                    {
                        // Un autre programme a enregistré entre-temps : on recommence sur sa version.
                        database.Dispose();
                        continue;
                    }
                }
                catch
                {
                    database.Dispose();
                    throw;
                }

                Database.Dispose();
                Database = database;
                _fileHash = SHA256.HashData(output);
                return result;
            }

            throw new KeePassException(KeePassError.Busy, CoreStrings.KeePassBusy);
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task SaveAsync(Action<KeePassDatabase> change, CancellationToken cancellation = default) =>
        SaveAsync(db =>
        {
            change(db);
            return true;
        }, cancellation);

    public void Dispose()
    {
        Database.Dispose();
        _cache.Dispose();
        _key.Dispose();
        _lock.Dispose();
    }

    private (KeePassDatabase Database, T Result, byte[] Output) Prepare<T>(byte[] original, Func<KeePassDatabase, T> change, CancellationToken cancellation)
    {
        var database = KdbxFile.Read(original, _key, _cache, cancellation);
        try
        {
            var result = change(database);
            var output = KdbxFile.Write(database);
            Verify(database, output, cancellation);
            return (database, result, output);
        }
        catch
        {
            database.Dispose();
            throw;
        }
    }

    /// <summary>Relit le fichier produit : il doit se déchiffrer avec la même clé et contenir les mêmes entrées.</summary>
    private void Verify(KeePassDatabase expected, byte[] output, CancellationToken cancellation)
    {
        try
        {
            using var check = KdbxFile.Read(output, _key, _cache, cancellation);
            var want = expected.Entries.Select(e => (e.Id, e.Title, e.Modified)).Order().ToList();
            var got = check.Entries.Select(e => (e.Id, e.Title, e.Modified)).Order().ToList();
            if (!want.SequenceEqual(got) || check.Groups.Count != expected.Groups.Count)
            {
                throw new KeePassException(KeePassError.Corrupted, CoreStrings.KeePassVerifyFailed);
            }
        }
        catch (KeePassException e) when (e.Message != CoreStrings.KeePassVerifyFailed)
        {
            throw new KeePassException(KeePassError.Corrupted, CoreStrings.KeePassVerifyFailed, e);
        }
    }

    /// <summary>
    /// Écrit dans un fichier temporaire du même dossier puis le substitue à l'original (gardé en .bak) ; faux si
    /// l'original a changé depuis <paramref name="original"/>.
    /// </summary>
    private async Task<bool> WriteAsync(byte[] original, byte[] output, CancellationToken cancellation)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(FilePath))!;
        var temp = Path.Combine(directory, $"{Path.GetFileName(FilePath)}.{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))}.tmp");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await stream.WriteAsync(output, cancellation).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            var current = await ReadSharedAsync(FilePath, cancellation).ConfigureAwait(false);
            if (!current.AsSpan().SequenceEqual(original))
            {
                return false;
            }

            try
            {
                File.Replace(temp, FilePath, BackupPath, ignoreMetadataErrors: true);
            }
            catch (Exception e) when (e is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
            {
                // Système de fichiers sans remplacement atomique (certains partages réseau) : copie de sauvegarde puis déplacement.
                File.Copy(FilePath, BackupPath, overwrite: true);
                File.Move(temp, FilePath, overwrite: true);
            }

            return true;
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private static async Task<byte[]> ReadSharedAsync(string path, CancellationToken cancellation)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
        var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellation).ConfigureAwait(false);
        return buffer.ToArray();
    }
}
