using System.Text;

namespace CyberArkTerm.Core.Ssh;

/// <summary>Fichier suivi : taille actuelle et lecture d'une plage d'octets.</summary>
public interface ITailSource
{
    Task<long> GetSizeAsync(CancellationToken ct);

    /// <summary>Lit au plus <paramref name="count"/> octets à partir de <paramref name="offset"/>.</summary>
    Task<byte[]> ReadAsync(long offset, int count, CancellationToken ct);
}

/// <summary>Ce qu'un relevé a trouvé de nouveau.</summary>
/// <param name="Text">Texte nouveau (UTF-8 décodé, fins de ligne « \n »).</param>
/// <param name="Size">Taille du fichier.</param>
/// <param name="Restarted">Fichier tronqué ou remplacé (rotation) : relu depuis le début.</param>
/// <param name="Skipped">Octets sautés parce que le fichier a trop grossi depuis le relevé précédent.</param>
public readonly record struct TailUpdate(string Text, long Size, bool Restarted, long Skipped);

/// <summary>
/// Suivi d'un fichier comme <c>tail -f</c>, par relevés successifs : le premier donne la fin du fichier (à partir
/// d'une ligne entière), les suivants ce qui a été ajouté. Un fichier tronqué, ou remplacé par une rotation, est relu
/// depuis le début : il est devenu plus petit, ou ses premiers octets ont changé (SFTP ne donne pas le numéro d'inode
/// qu'utilise <c>tail -F</c>). Si le fichier a beaucoup grossi entre deux relevés, seule la fin est lue.
/// </summary>
public sealed class FileTail(ITailSource source)
{
    /// <summary>Fin du fichier montrée au premier relevé.</summary>
    public const int InitialBytes = 64 * 1024;

    /// <summary>Au-delà, entre deux relevés, le début de l'ajout est sauté.</summary>
    public const int MaxBytesPerPoll = 4 * 1024 * 1024;

    private const int ChunkBytes = 256 * 1024;

    /// <summary>Début du fichier gardé pour reconnaître un fichier remplacé par un autre plus grand.</summary>
    private const int FingerprintBytes = 64;

    private byte[] _fingerprint = [];

    // UTF-8 tolérant : un octet invalide devient « � » ; un caractère coupé entre deux relevés est recollé.
    private readonly Decoder _decoder = new UTF8Encoding(false, false).GetDecoder();
    private bool _pendingCarriageReturn;

    /// <summary>Position lue dans le fichier ; -1 avant le premier relevé.</summary>
    public long Position { get; private set; } = -1;

    /// <summary>
    /// Accès au fichier ; remplacé après une reconnexion. Le suivi reprend là où il s'était arrêté (les lignes écrites
    /// entre-temps sont lues), ou depuis le début si le fichier a été remplacé.
    /// </summary>
    public ITailSource Source { get; set; } = source;

    public async Task<TailUpdate> PollAsync(CancellationToken ct)
    {
        long size = await Source.GetSizeAsync(ct).ConfigureAwait(false);
        bool restarted = false;
        bool skipPartialLine = false;
        long skipped = 0;
        if (Position < 0)
        {
            Position = Math.Max(0, size - InitialBytes);
            skipPartialLine = Position > 0;
        }
        else if (size < Position || (size != Position && !await SameFileAsync(ct).ConfigureAwait(false)))
        {
            Position = 0;
            restarted = true;
            _fingerprint = [];
            _decoder.Reset();
            _pendingCarriageReturn = false;
        }

        if (size - Position > MaxBytesPerPoll)
        {
            skipped = size - InitialBytes - Position;
            Position = size - InitialBytes;
            skipPartialLine = true;
            _decoder.Reset();
        }

        var text = new StringBuilder();
        while (Position < size)
        {
            var data = await Source.ReadAsync(Position, (int)Math.Min(ChunkBytes, size - Position), ct).ConfigureAwait(false);
            if (data.Length == 0)
            {
                break;
            }

            Position += data.Length;
            var chars = new char[_decoder.GetCharCount(data, 0, data.Length)];
            _decoder.GetChars(data, 0, data.Length, chars, 0);
            text.Append(chars);
        }

        if (_fingerprint.Length < FingerprintBytes && size > _fingerprint.Length)
        {
            _fingerprint = await Source.ReadAsync(0, (int)Math.Min(FingerprintBytes, size), ct).ConfigureAwait(false);
        }

        var result = Normalize(text.ToString());
        if (skipPartialLine)
        {
            // Lecture commencée au milieu d'une ligne : elle est écartée, comme le fait tail.
            int newline = result.IndexOf('\n', StringComparison.Ordinal);
            result = newline < 0 ? "" : result[(newline + 1)..];
        }

        return new TailUpdate(result, size, restarted, skipped);
    }

    /// <summary>Les premiers octets du fichier n'ont pas changé : c'est toujours le même fichier.</summary>
    private async Task<bool> SameFileAsync(CancellationToken ct) =>
        _fingerprint.Length == 0
        || (await Source.ReadAsync(0, _fingerprint.Length, ct).ConfigureAwait(false)).AsSpan().SequenceEqual(_fingerprint);

    /// <summary>Fins de ligne « \r\n » ramenées à « \n », y compris quand « \r » et « \n » arrivent dans deux relevés.</summary>
    private string Normalize(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        int start = 0;
        if (_pendingCarriageReturn)
        {
            _pendingCarriageReturn = false;
            if (text[0] != '\n')
            {
                builder.Append('\r');
            }
        }

        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                if (i + 1 == text.Length)
                {
                    _pendingCarriageReturn = true;
                    continue;
                }

                if (text[i + 1] == '\n')
                {
                    continue;
                }
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
