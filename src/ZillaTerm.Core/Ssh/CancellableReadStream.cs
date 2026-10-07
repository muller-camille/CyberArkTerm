namespace ZillaTerm.Core.Ssh;

/// <summary>
/// Lecture d'un fichier à envoyer qui s'arrête dès l'annulation : l'envoi SCP de SSH.NET ne prend pas de jeton
/// d'annulation, mais il lit le fichier morceau par morceau. Le flux lu n'est pas fermé avec celui-ci.
/// </summary>
internal sealed class CancellableReadStream(Stream inner, CancellationToken ct) : Stream
{
    /// <summary>Le contenu a commencé à être lu : le serveur a accepté l'envoi.</summary>
    public bool WasRead { get; private set; }

    /// <summary>
    /// La taille a été lue : SSH.NET la lit une fois la commande scp acceptée, juste avant d'annoncer le fichier (nom
    /// et taille) au serveur.
    /// </summary>
    public bool LengthRead { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length
    {
        get
        {
            LengthRead = true;
            return inner.Length;
        }
    }

    public override long Position
    {
        get => inner.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ct.ThrowIfCancellationRequested();
        WasRead = true;
        return inner.Read(buffer);
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
