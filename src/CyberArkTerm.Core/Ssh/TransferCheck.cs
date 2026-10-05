using System.Security.Cryptography;
using System.Text;

namespace CyberArkTerm.Core.Ssh;

/// <summary>
/// Vérification d'un fichier transféré : taille et somme SHA-256 du fichier sur ce poste et sur le serveur. Envoi :
/// le fichier local est haché pendant l'envoi, puis le fichier du serveur est relu par SFTP et haché. Téléchargement :
/// les données reçues du serveur sont hachées, puis le fichier écrit sur le disque est relu et haché.
/// </summary>
/// <param name="Error">Relecture impossible (droits, fichier verrouillé...) : le transfert a eu lieu mais n'est pas vérifié.</param>
public sealed record TransferCheck(
    string Name, string LocalPath, string RemotePath, long LocalLength, byte[] LocalSha256, long RemoteLength, byte[] RemoteSha256,
    string? Error = null)
{
    /// <summary>Envoi vers le serveur (sinon téléchargement).</summary>
    public bool Upload { get; init; }

    /// <summary>Transfert annulé pendant ce fichier : la copie incomplète a été supprimée (voir <see cref="Error"/>).</summary>
    public bool Interrupted { get; init; }

    /// <summary>Transfert en échec pendant ce fichier (message dans <see cref="Error"/>).</summary>
    public bool Failed { get; init; }

    public bool Verified => Error is null;

    public bool Matches => Verified && LocalLength == RemoteLength && LocalSha256.AsSpan().SequenceEqual(RemoteSha256);

    public string LocalHash => Convert.ToHexStringLower(LocalSha256);

    public string RemoteHash => Convert.ToHexStringLower(RemoteSha256);

    /// <summary>
    /// Ligne au format de <c>sha256sum</c> pour le fichier du serveur, vérifiable avec <c>sha256sum -c</c> : somme de
    /// l'original (le fichier local pour un envoi, celui du serveur pour un téléchargement), deux espaces, chemin. Un
    /// chemin contenant « \ » ou un saut de ligne est échappé comme le fait sha256sum (ligne commençant par « \ »).
    /// </summary>
    public string ToSha256SumLine()
    {
        var hash = Upload ? LocalHash : RemoteHash;
        if (RemotePath.IndexOfAny(['\\', '\n', '\r']) < 0)
        {
            return $"{hash}  {RemotePath}";
        }

        var escaped = RemotePath.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal);
        return $"\\{hash}  {escaped}";
    }

    /// <summary>SHA-256 et taille d'un fichier local.</summary>
    internal static async Task<(byte[] Hash, long Length)> HashFileAsync(string path, CancellationToken ct)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hashing = new HashingStream(file);
        var buffer = new byte[81920];
        while (await hashing.ReadAsync(buffer, ct).ConfigureAwait(false) > 0)
        {
        }

        return (hashing.GetHash(), hashing.Count);
    }
}

/// <summary>
/// Flux qui calcule le SHA-256 des octets qui le traversent, lus depuis le flux interne ou écrits vers lui ; sans flux
/// interne, les octets écrits sont seulement hachés. Lecture et écriture séquentielles seulement.
/// </summary>
internal sealed class HashingStream(Stream? inner) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    /// <summary>Nombre d'octets hachés.</summary>
    public long Count { get; private set; }

    public override bool CanRead => inner?.CanRead ?? false;

    public override bool CanWrite => inner?.CanWrite ?? true;

    public override bool CanSeek => false;

    public override long Length => inner?.Length ?? throw new NotSupportedException();

    public override long Position
    {
        get => Count;
        set => throw new NotSupportedException();
    }

    private Stream Inner => inner ?? throw new NotSupportedException();

    public byte[] GetHash() => _hash.GetCurrentHash();

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int n = Inner.Read(buffer);
        Append(buffer[..n]);
        return n;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int n = await Inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Append(buffer.Span[..n]);
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Append(buffer);
        inner?.Write(buffer);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Append(buffer.Span);
        if (inner is not null)
        {
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => inner?.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner?.FlushAsync(cancellationToken) ?? Task.CompletedTask;

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>Le flux interne reste à fermer par son propriétaire.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hash.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Append(ReadOnlySpan<byte> data)
    {
        _hash.AppendData(data);
        Count += data.Length;
    }
}
