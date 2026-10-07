using System.Buffers.Binary;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using ZillaTerm.Core.Localization;

namespace ZillaTerm.Core.Vnc;

/// <summary>Zone de l'écran distant (pixels).</summary>
public readonly record struct VncRect(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public VncRect Union(VncRect other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        int x = Math.Min(X, other.X), y = Math.Min(Y, other.Y);
        return new VncRect(x, y, Math.Max(X + Width, other.X + other.Width) - x, Math.Max(Y + Height, other.Y + other.Height) - y);
    }
}

/// <summary>Image de l'écran distant : pixels BGRX de 32 bits, ligne après ligne.</summary>
public sealed class VncFramebuffer
{
    /// <summary>Taille d'écran acceptée (un serveur qui annonce plus est refusé : la mémoire de l'image serait démesurée).</summary>
    public const int MaxSide = 8192;
    public const int MaxPixels = 40_000_000;

    internal VncFramebuffer(int width, int height) => Resize(width, height, Allocate(width, height));

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int Stride => Width * 4;

    public byte[] Pixels { get; private set; } = [];

    /// <summary>Verrou à tenir pour lire <see cref="Pixels"/> : le décodage l'écrit depuis un autre fil.</summary>
    public object Sync { get; } = new();

    /// <summary>Image vide pour une taille d'écran ; refusée au-delà des limites.</summary>
    internal static byte[] Allocate(int width, int height)
    {
        if (width is < 1 or > MaxSide || height is < 1 or > MaxSide || (long)width * height > MaxPixels)
        {
            throw new InvalidDataException(string.Format(CultureInfo.CurrentCulture, CoreStrings.VncScreenTooLarge, width, height));
        }

        return new byte[width * height * 4];
    }

    /// <summary>Remplace l'image (allouée par <see cref="Allocate"/>, hors du verrou : l'affichage n'attend pas).</summary>
    internal void Resize(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }
}

/// <summary>
/// Client VNC (protocole RFB 3.3, 3.7 et 3.8, RFC 6143) : authentification « aucune » ou « mot de passe VNC », image en
/// couleurs vraies 32 bits (encodages Raw, CopyRect, Hextile, changement de taille d'écran), clavier, souris et
/// presse-papiers texte. Le flux n'est pas chiffré : ni l'image, ni les frappes, ni le presse-papiers.
/// </summary>
public sealed class RfbClient : IDisposable
{
    public const int DefaultPort = 5900;

    // Encodages (RFC 6143, 7.7) et pseudo-encodage de changement de taille (7.8.2).
    private const int EncodingRaw = 0;
    private const int EncodingCopyRect = 1;
    private const int EncodingHextile = 5;
    private const int EncodingDesktopSize = -223;

    private const int MaxCutText = 1024 * 1024;
    private const int MaxReason = 64 * 1024;

    private readonly TcpClient _tcp;
    private readonly NetworkStream _network;
    private readonly BufferedStream _reader;
    private readonly Channel<byte[]> _outgoing = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _closing = new();
    private readonly Queue<long> _resizes = new();
    private byte _buttons;
    private int _disposed;

    /// <summary>Changements de taille d'écran acceptés au plus pendant <see cref="ResizeWindow"/>.</summary>
    internal const int MaxResizes = 10;

    private static readonly TimeSpan ResizeWindow = TimeSpan.FromSeconds(10);

    private RfbClient(TcpClient tcp, NetworkStream network, BufferedStream reader, int minor, string name, int width, int height)
    {
        _tcp = tcp;
        _network = network;
        _reader = reader;
        ProtocolVersion = $"3.{minor}";
        DesktopName = name;
        Framebuffer = new VncFramebuffer(width, height);
    }

    /// <summary>Version du protocole convenue (« 3.3 », « 3.7 » ou « 3.8 »).</summary>
    public string ProtocolVersion { get; }

    /// <summary>Nom du bureau annoncé par le serveur.</summary>
    public string DesktopName { get; }

    public VncFramebuffer Framebuffer { get; }

    /// <summary>Zone de l'image modifiée (après chaque mise à jour reçue ; depuis le fil de lecture).</summary>
    public event Action<VncRect>? Updated;

    /// <summary>L'écran distant a changé de taille (depuis le fil de lecture).</summary>
    public event Action? Resized;

    /// <summary>Texte copié sur le serveur (depuis le fil de lecture).</summary>
    public event Action<string>? ClipboardReceived;

    public event Action? Bell;

    /// <summary>
    /// Connexion et authentification. <paramref name="password"/> n'est appelé que si le serveur demande le mot de
    /// passe VNC (8 caractères au plus pris en compte par le protocole).
    /// </summary>
    /// <exception cref="RfbException">Protocole, authentification ou écran refusés.</exception>
    public static async Task<RfbClient> ConnectAsync(string host, int port, Func<string?> password, CancellationToken ct)
    {
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await tcp.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            var network = tcp.GetStream();
            var reader = new BufferedStream(network, 64 * 1024);
            var client = await HandshakeAsync(tcp, network, reader, password, timeout.Token).ConfigureAwait(false);
            return client;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            tcp.Dispose();
            throw new RfbException(CoreStrings.VncTimeout);
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    private static async Task<RfbClient> HandshakeAsync(TcpClient tcp, NetworkStream network, BufferedStream reader, Func<string?> password,
        CancellationToken ct)
    {
        // Version : « RFB 003.008\n ».
        var version = await ReadBytesAsync(reader, 12, ct).ConfigureAwait(false);
        var text = Encoding.ASCII.GetString(version);
        if (!text.StartsWith("RFB ", StringComparison.Ordinal) || text[^1] != '\n'
            || !int.TryParse(text.AsSpan(4, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(text.AsSpan(8, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var serverMinor))
        {
            throw new RfbException(CoreStrings.VncNotRfb);
        }

        if (major < 3)
        {
            throw new RfbException(string.Format(CultureInfo.CurrentCulture, CoreStrings.VncUnsupportedVersion, text.Trim()));
        }

        // Serveur plus récent (RealVNC annonce 4.x ou 5.x) : il accepte la 3.8, comme les autres clients la demandent.
        int minor = major > 3 || serverMinor >= 8 ? 8 : serverMinor == 7 ? 7 : 3;
        await network.WriteAsync(Encoding.ASCII.GetBytes($"RFB 003.00{minor}\n"), ct).ConfigureAwait(false);

        // Méthodes d'authentification proposées.
        byte[] offered;
        if (minor == 3)
        {
            var type = await ReadU32Async(reader, ct).ConfigureAwait(false);
            if (type == 0)
            {
                throw new RfbException(string.Format(CultureInfo.CurrentCulture, CoreStrings.VncRefused, await ReadReasonAsync(reader, ct).ConfigureAwait(false)));
            }

            offered = [(byte)Math.Min(type, 255)];
        }
        else
        {
            int count = await ReadU8Async(reader, ct).ConfigureAwait(false);
            if (count == 0)
            {
                throw new RfbException(string.Format(CultureInfo.CurrentCulture, CoreStrings.VncRefused, await ReadReasonAsync(reader, ct).ConfigureAwait(false)));
            }

            offered = await ReadBytesAsync(reader, count, ct).ConfigureAwait(false);
        }

        // Mot de passe lu seulement s'il sert : pour choisir entre les deux méthodes, ou pour le mot de passe VNC.
        string? secret = null;
        bool HasPassword() => !string.IsNullOrEmpty(secret ??= password());
        var security = ChooseSecurity(offered, HasPassword);
        if (minor != 3)
        {
            await network.WriteAsync(new[] { security }, ct).ConfigureAwait(false);
        }

        if (security == SecurityVnc)
        {
            var challenge = await ReadBytesAsync(reader, 16, ct).ConfigureAwait(false);
            var response = VncAuthResponse(challenge, secret ?? password());
            await network.WriteAsync(response, ct).ConfigureAwait(false);
        }

        // Résultat : toujours après le mot de passe VNC ; en 3.8, aussi sans authentification.
        if (security == SecurityVnc || minor == 8)
        {
            var result = await ReadU32Async(reader, ct).ConfigureAwait(false);
            if (result != 0)
            {
                var reason = minor == 8 ? await ReadReasonAsync(reader, ct).ConfigureAwait(false) : "";
                throw new RfbException(security == SecurityVnc
                    ? string.Format(CultureInfo.CurrentCulture, CoreStrings.VncAuthFailed, reason)
                    : string.Format(CultureInfo.CurrentCulture, CoreStrings.VncRefused, reason));
            }
        }

        // Session partagée : les autres visionneuses déjà connectées le restent.
        await network.WriteAsync(new byte[] { 1 }, ct).ConfigureAwait(false);
        var init = await ReadBytesAsync(reader, 24, ct).ConfigureAwait(false);
        int width = BinaryPrimitives.ReadUInt16BigEndian(init), height = BinaryPrimitives.ReadUInt16BigEndian(init.AsSpan(2));
        int nameLength = checked((int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(init.AsSpan(20)), MaxReason + 1));
        if (nameLength > MaxReason)
        {
            throw new RfbException(CoreStrings.VncProtocolError);
        }

        var name = Encoding.UTF8.GetString(await ReadBytesAsync(reader, nameLength, ct).ConfigureAwait(false));
        RfbClient client;
        try
        {
            client = new RfbClient(tcp, network, reader, minor, name, width, height);
        }
        catch (InvalidDataException e)
        {
            throw new RfbException(e.Message);
        }

        // Couleurs vraies 32 bits, petit-boutiste : chaque pixel arrive en B, G, R, X, comme l'image WPF Bgr32.
        var format = new byte[20];
        format[0] = 0;
        format[4] = 32;
        format[5] = 24;
        format[6] = 0;
        format[7] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(format.AsSpan(8), 255);
        BinaryPrimitives.WriteUInt16BigEndian(format.AsSpan(10), 255);
        BinaryPrimitives.WriteUInt16BigEndian(format.AsSpan(12), 255);
        format[14] = 16;
        format[15] = 8;
        format[16] = 0;
        int[] encodings = [EncodingCopyRect, EncodingHextile, EncodingRaw, EncodingDesktopSize];
        var setEncodings = new byte[4 + 4 * encodings.Length];
        setEncodings[0] = 2;
        BinaryPrimitives.WriteUInt16BigEndian(setEncodings.AsSpan(2), (ushort)encodings.Length);
        for (int i = 0; i < encodings.Length; i++)
        {
            BinaryPrimitives.WriteInt32BigEndian(setEncodings.AsSpan(4 + 4 * i), encodings[i]);
        }

        await network.WriteAsync(format, ct).ConfigureAwait(false);
        await network.WriteAsync(setEncodings, ct).ConfigureAwait(false);
        return client;
    }

    private const byte SecurityNone = 1;
    private const byte SecurityVnc = 2;

    /// <summary>
    /// Mot de passe VNC si le serveur le propose, sauf s'il propose aussi « aucune » et que l'entrée n'a pas de mot de
    /// passe ; sinon « aucune ».
    /// </summary>
    internal static byte ChooseSecurity(IReadOnlyCollection<byte> offered, Func<bool> hasPassword)
    {
        if (offered.Contains(SecurityVnc) && !(offered.Contains(SecurityNone) && !hasPassword()))
        {
            return SecurityVnc;
        }

        if (offered.Contains(SecurityNone))
        {
            return SecurityNone;
        }

        throw new RfbException(string.Format(CultureInfo.CurrentCulture, CoreStrings.VncSecurityUnsupported,
            string.Join(", ", offered.Select(SecurityName))));
    }

    private static string SecurityName(byte type) => type switch
    {
        5 or 6 => $"RA2 ({type})",
        16 => "Tight (16)",
        18 => "TLS (18)",
        19 => "VeNCrypt (19)",
        30 => "Apple (30)",
        _ => type.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Réponse au défi du mot de passe VNC : le défi chiffré en DES avec le mot de passe (8 premiers caractères, bits
    /// de chaque octet inversés). La clé est effacée après usage.
    /// </summary>
    internal static byte[] VncAuthResponse(byte[] challenge, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new RfbException(CoreStrings.VncPasswordRequired);
        }

        var key = new byte[8];
        try
        {
            for (int i = 0; i < Math.Min(8, password.Length); i++)
            {
                int c = password[i];
                key[i] = ReverseBits((byte)(c <= 0xFF ? c : '?'));
            }

            return VncDes.EncryptEcb(key, challenge);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte ReverseBits(byte b)
    {
        int r = 0;
        for (int i = 0; i < 8; i++)
        {
            r |= ((b >> i) & 1) << (7 - i);
        }

        return (byte)r;
    }

    /// <summary>
    /// Reçoit les mises à jour de l'écran jusqu'à la fermeture. Se termine par une exception si le serveur coupe la
    /// connexion ou envoie des données incorrectes.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _closing.Token);
        var token = linked.Token;
        var writer = WriteLoopAsync(token);
        try
        {
            RequestUpdate(incremental: false);
            while (true)
            {
                int type = await ReadU8Async(_reader, token).ConfigureAwait(false);
                switch (type)
                {
                    case 0:
                        await ReadFramebufferUpdateAsync(token).ConfigureAwait(false);
                        RequestUpdate(incremental: true);
                        break;
                    case 1:
                        // Palette : inutile en couleurs vraies, lue et ignorée.
                        var header = await ReadBytesAsync(_reader, 5, token).ConfigureAwait(false);
                        await SkipAsync(BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(3)) * 6, token).ConfigureAwait(false);
                        break;
                    case 2:
                        Bell?.Invoke();
                        break;
                    case 3:
                        await ReadCutTextAsync(token).ConfigureAwait(false);
                        break;
                    default:
                        throw new RfbException(CoreStrings.VncProtocolError);
                }
            }
        }
        finally
        {
            _outgoing.Writer.TryComplete();
            await _closing.CancelAsync().ConfigureAwait(false);
            try
            {
                await writer.ConfigureAwait(false);
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException or SocketException)
            {
                // Écriture interrompue par la fermeture.
            }
        }
    }

    private async Task WriteLoopAsync(CancellationToken ct)
    {
        await foreach (var message in _outgoing.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            await _network.WriteAsync(message, ct).ConfigureAwait(false);
        }
    }

    private async Task ReadFramebufferUpdateAsync(CancellationToken ct)
    {
        var header = await ReadBytesAsync(_reader, 3, ct).ConfigureAwait(false);
        int count = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(1));
        var dirty = default(VncRect);
        for (int i = 0; i < count; i++)
        {
            var r = await ReadBytesAsync(_reader, 12, ct).ConfigureAwait(false);
            var rect = new VncRect(BinaryPrimitives.ReadUInt16BigEndian(r), BinaryPrimitives.ReadUInt16BigEndian(r.AsSpan(2)),
                BinaryPrimitives.ReadUInt16BigEndian(r.AsSpan(4)), BinaryPrimitives.ReadUInt16BigEndian(r.AsSpan(6)));
            int encoding = BinaryPrimitives.ReadInt32BigEndian(r.AsSpan(8));
            if (encoding == EncodingDesktopSize)
            {
                if (rect.Width != Framebuffer.Width || rect.Height != Framebuffer.Height)
                {
                    Resize(rect);
                    Resized?.Invoke();
                }

                dirty = new VncRect(0, 0, rect.Width, rect.Height);
                continue;
            }

            if (encoding is not (EncodingRaw or EncodingCopyRect or EncodingHextile))
            {
                throw new RfbException(CoreStrings.VncProtocolError);
            }

            if (rect.IsEmpty)
            {
                // Rien à dessiner, mais CopyRect porte toujours la position de sa source (4 octets) : à lire, sans quoi
                // la suite du flux serait décalée.
                if (encoding == EncodingCopyRect)
                {
                    await ReadBytesAsync(_reader, 4, ct).ConfigureAwait(false);
                }

                continue;
            }

            if (rect.X + rect.Width > Framebuffer.Width || rect.Y + rect.Height > Framebuffer.Height)
            {
                throw new RfbException(CoreStrings.VncProtocolError);
            }

            switch (encoding)
            {
                case EncodingRaw:
                    await ReadRawAsync(rect, ct).ConfigureAwait(false);
                    break;
                case EncodingCopyRect:
                    await ReadCopyRectAsync(rect, ct).ConfigureAwait(false);
                    break;
                case EncodingHextile:
                    await ReadHextileAsync(rect, ct).ConfigureAwait(false);
                    break;
                default:
                    throw new RfbException(CoreStrings.VncProtocolError);
            }

            dirty = dirty.Union(rect);
        }

        if (!dirty.IsEmpty)
        {
            Updated?.Invoke(dirty);
        }
    }

    private async Task ReadRawAsync(VncRect rect, CancellationToken ct)
    {
        int rowBytes = rect.Width * 4;
        // Par bandes de lignes : le verrou de l'image n'est jamais tenu pendant une attente du réseau.
        int rowsPerChunk = Math.Max(1, 256 * 1024 / rowBytes);
        var buffer = new byte[rowBytes * Math.Min(rowsPerChunk, rect.Height)];
        for (int row = 0; row < rect.Height; row += rowsPerChunk)
        {
            int rows = Math.Min(rowsPerChunk, rect.Height - row);
            await _reader.ReadExactlyAsync(buffer.AsMemory(0, rows * rowBytes), ct).ConfigureAwait(false);
            lock (Framebuffer.Sync)
            {
                for (int r = 0; r < rows; r++)
                {
                    Buffer.BlockCopy(buffer, r * rowBytes, Framebuffer.Pixels,
                        (rect.Y + row + r) * Framebuffer.Stride + rect.X * 4, rowBytes);
                }
            }
        }
    }

    /// <summary>
    /// Nouvelle taille d'écran : l'image est allouée hors du verrou (l'affichage n'attend pas). Un serveur qui en change
    /// sans cesse (chaque changement réalloue jusqu'à 160 Mo) est déconnecté.
    /// </summary>
    private void Resize(VncRect rect)
    {
        long now = Environment.TickCount64;
        while (_resizes.Count > 0 && now - _resizes.Peek() > (long)ResizeWindow.TotalMilliseconds)
        {
            _resizes.Dequeue();
        }

        if (_resizes.Count >= MaxResizes)
        {
            throw new RfbException(CoreStrings.VncProtocolError);
        }

        _resizes.Enqueue(now);
        byte[] pixels;
        try
        {
            pixels = VncFramebuffer.Allocate(rect.Width, rect.Height);
        }
        catch (InvalidDataException e)
        {
            throw new RfbException(e.Message);
        }

        lock (Framebuffer.Sync)
        {
            Framebuffer.Resize(rect.Width, rect.Height, pixels);
        }
    }

    /// <summary>
    /// Copie d'une zone de l'image vers une autre, sur place : ligne par ligne, dans le sens qui ne relit jamais une
    /// ligne déjà écrasée (source et destination peuvent se chevaucher), par bandes pour ne pas garder le verrou de
    /// l'image longtemps.
    /// </summary>
    private async Task ReadCopyRectAsync(VncRect rect, CancellationToken ct)
    {
        var source = await ReadBytesAsync(_reader, 4, ct).ConfigureAwait(false);
        int sx = BinaryPrimitives.ReadUInt16BigEndian(source), sy = BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(2));
        var fb = Framebuffer;
        if (sx + rect.Width > fb.Width || sy + rect.Height > fb.Height)
        {
            throw new RfbException(CoreStrings.VncProtocolError);
        }

        int rowBytes = rect.Width * 4;
        int rowsPerBand = Math.Max(1, 256 * 1024 / rowBytes);
        bool upward = rect.Y > sy;
        for (int done = 0; done < rect.Height; done += rowsPerBand)
        {
            lock (fb.Sync)
            {
                for (int i = done; i < Math.Min(done + rowsPerBand, rect.Height); i++)
                {
                    int r = upward ? rect.Height - 1 - i : i;
                    // Une même ligne peut se chevaucher elle-même (décalage horizontal) : CopyTo le gère.
                    fb.Pixels.AsSpan((sy + r) * fb.Stride + sx * 4, rowBytes)
                        .CopyTo(fb.Pixels.AsSpan((rect.Y + r) * fb.Stride + rect.X * 4, rowBytes));
                }
            }
        }
    }

    /// <summary>Hextile (RFC 6143, 7.7.4) : tuiles de 16 × 16, brutes ou fond uni et petits rectangles.</summary>
    private async Task ReadHextileAsync(VncRect rect, CancellationToken ct)
    {
        const int Raw = 1, BackgroundSpecified = 2, ForegroundSpecified = 4, AnySubrects = 8, SubrectsColoured = 16;
        var tile = new byte[16 * 16 * 4];
        uint background = 0, foreground = 0;
        for (int ty = rect.Y; ty < rect.Y + rect.Height; ty += 16)
        {
            int th = Math.Min(16, rect.Y + rect.Height - ty);
            for (int tx = rect.X; tx < rect.X + rect.Width; tx += 16)
            {
                int tw = Math.Min(16, rect.X + rect.Width - tx);
                int mask = await ReadU8Async(_reader, ct).ConfigureAwait(false);
                if ((mask & Raw) != 0)
                {
                    await _reader.ReadExactlyAsync(tile.AsMemory(0, tw * th * 4), ct).ConfigureAwait(false);
                }
                else
                {
                    if ((mask & BackgroundSpecified) != 0)
                    {
                        background = await ReadPixelAsync(ct).ConfigureAwait(false);
                    }

                    if ((mask & ForegroundSpecified) != 0)
                    {
                        foreground = await ReadPixelAsync(ct).ConfigureAwait(false);
                    }

                    Fill(tile, tw, 0, 0, tw, th, background);
                    if ((mask & AnySubrects) != 0)
                    {
                        int subrects = await ReadU8Async(_reader, ct).ConfigureAwait(false);
                        bool coloured = (mask & SubrectsColoured) != 0;
                        var data = await ReadBytesAsync(_reader, subrects * (coloured ? 6 : 2), ct).ConfigureAwait(false);
                        for (int s = 0, o = 0; s < subrects; s++)
                        {
                            uint colour = foreground;
                            if (coloured)
                            {
                                colour = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(o));
                                o += 4;
                            }

                            int sx = data[o] >> 4, sy = data[o] & 15, sw = (data[o + 1] >> 4) + 1, sh = (data[o + 1] & 15) + 1;
                            o += 2;
                            if (sx + sw > tw || sy + sh > th)
                            {
                                throw new RfbException(CoreStrings.VncProtocolError);
                            }

                            Fill(tile, tw, sx, sy, sw, sh, colour);
                        }
                    }
                }

                lock (Framebuffer.Sync)
                {
                    for (int r = 0; r < th; r++)
                    {
                        Buffer.BlockCopy(tile, r * tw * 4, Framebuffer.Pixels, (ty + r) * Framebuffer.Stride + tx * 4, tw * 4);
                    }
                }
            }
        }
    }

    private static void Fill(byte[] tile, int tileWidth, int x, int y, int width, int height, uint colour)
    {
        var span = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(tile.AsSpan());
        for (int r = y; r < y + height; r++)
        {
            span.Slice(r * tileWidth + x, width).Fill(BitConverter.IsLittleEndian ? colour : BinaryPrimitives.ReverseEndianness(colour));
        }
    }

    private async Task<uint> ReadPixelAsync(CancellationToken ct) =>
        BinaryPrimitives.ReadUInt32LittleEndian(await ReadBytesAsync(_reader, 4, ct).ConfigureAwait(false));

    private async Task ReadCutTextAsync(CancellationToken ct)
    {
        var header = await ReadBytesAsync(_reader, 7, ct).ConfigureAwait(false);
        uint length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(3));
        if (length > MaxCutText)
        {
            // Texte démesuré : ignoré (lu pour garder le fil du protocole).
            await SkipAsync(length, ct).ConfigureAwait(false);
            return;
        }

        var text = Encoding.Latin1.GetString(await ReadBytesAsync(_reader, (int)length, ct).ConfigureAwait(false));
        ClipboardReceived?.Invoke(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal));
    }

    private async Task SkipAsync(long count, CancellationToken ct)
    {
        var buffer = new byte[(int)Math.Min(count, 64 * 1024)];
        while (count > 0)
        {
            int n = (int)Math.Min(count, buffer.Length);
            await _reader.ReadExactlyAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            count -= n;
        }
    }

    // ===================== Envois =====================

    private void Send(byte[] message) => _outgoing.Writer.TryWrite(message);

    private void RequestUpdate(bool incremental)
    {
        var message = new byte[10];
        message[0] = 3;
        message[1] = incremental ? (byte)1 : (byte)0;
        lock (Framebuffer.Sync)
        {
            BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(6), (ushort)Framebuffer.Width);
            BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(8), (ushort)Framebuffer.Height);
        }

        Send(message);
    }

    /// <summary>Touche enfoncée ou relâchée (symbole de touche X11, voir <see cref="VncKeys"/>).</summary>
    public void SendKey(uint keysym, bool down)
    {
        var message = new byte[8];
        message[0] = 4;
        message[1] = down ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(4), keysym);
        Send(message);
    }

    /// <summary>Position de la souris (pixels de l'écran distant) et boutons enfoncés (1 gauche, 2 milieu, 4 droit).</summary>
    public void SendPointer(int x, int y, byte buttons)
    {
        _buttons = buttons;
        var message = new byte[6];
        message[0] = 5;
        message[1] = buttons;
        lock (Framebuffer.Sync)
        {
            BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(2), (ushort)Math.Clamp(x, 0, Framebuffer.Width - 1));
            BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(4), (ushort)Math.Clamp(y, 0, Framebuffer.Height - 1));
        }

        Send(message);
    }

    /// <summary>Molette : un cran vers le haut (positif) ou vers le bas, en gardant les boutons enfoncés.</summary>
    public void SendWheel(int x, int y, int delta)
    {
        byte wheel = delta > 0 ? (byte)8 : (byte)16;
        var held = _buttons;
        SendPointer(x, y, (byte)(held | wheel));
        SendPointer(x, y, held);
    }

    /// <summary>Texte du presse-papiers local envoyé au serveur (Latin-1 ; les autres caractères deviennent « ? »).</summary>
    public void SendClipboard(string text)
    {
        var bytes = Encoding.Latin1.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal));
        var message = new byte[8 + bytes.Length];
        message[0] = 6;
        BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(4), (uint)bytes.Length);
        bytes.CopyTo(message, 8);
        Send(message);
    }

    // ===================== Lectures =====================

    private static async Task<byte[]> ReadBytesAsync(Stream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        try
        {
            await stream.ReadExactlyAsync(buffer, ct).ConfigureAwait(false);
        }
        catch (EndOfStreamException)
        {
            throw new RfbException(CoreStrings.VncClosedByServer);
        }

        return buffer;
    }

    private static async Task<int> ReadU8Async(Stream stream, CancellationToken ct) => (await ReadBytesAsync(stream, 1, ct).ConfigureAwait(false))[0];

    private static async Task<uint> ReadU32Async(Stream stream, CancellationToken ct) =>
        BinaryPrimitives.ReadUInt32BigEndian(await ReadBytesAsync(stream, 4, ct).ConfigureAwait(false));

    private static async Task<string> ReadReasonAsync(Stream stream, CancellationToken ct)
    {
        uint length = await ReadU32Async(stream, ct).ConfigureAwait(false);
        if (length > MaxReason)
        {
            throw new RfbException(CoreStrings.VncProtocolError);
        }

        return Encoding.UTF8.GetString(await ReadBytesAsync(stream, (int)length, ct).ConfigureAwait(false)).Trim();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _closing.Cancel();
        _outgoing.Writer.TryComplete();
        _tcp.Dispose();
        _reader.Dispose();
    }
}

/// <summary>Erreur de protocole, d'authentification ou refus du serveur VNC (message pour l'utilisateur).</summary>
public sealed class RfbException(string message) : Exception(message);
