using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CyberArkTerm.Core.Localization;

namespace CyberArkTerm.Core.KeePass;

/// <summary>
/// Format de fichier KeePass 2 (KDBX 3.1 et 4.x) : en-tête, déchiffrement, blocs vérifiés, compression, valeurs
/// protégées du XML. La réécriture garde la version, le chiffrement et la dérivation de clé du fichier lu, avec de
/// nouvelles graines et un nouveau vecteur d'initialisation à chaque enregistrement.
/// </summary>
internal static class KdbxFile
{
    private const uint Signature1 = 0x9AA2D903;
    private const uint Signature2 = 0xB54BFB67;
    private const uint Kdbx3_1 = 0x00030001;
    private const uint Kdbx4 = 0x00040000;
    private const int BlockSize = 1024 * 1024;

    /// <summary>Taille maximale du XML décompressé : un coffre de serveurs n'en approche pas.</summary>
    private const int MaxXmlSize = 512 * 1024 * 1024;

    public static readonly Guid Aes256 = new("31c1f2e6-bf71-4350-be58-05216afc5aff");
    public static readonly Guid ChaCha20Cipher = new("d6038a2b-8b6f-4cb5-a524-339a31dbb59a");
    private static readonly Guid Twofish = new("ad68f29f-576f-4bb9-a36a-d47af965346c");

    private static readonly byte[] Salsa20Nonce = [0xE8, 0x30, 0x09, 0x4B, 0x97, 0x20, 0x5D, 0x2A];

    private enum Field : byte
    {
        End = 0,
        CipherId = 2,
        Compression = 3,
        MasterSeed = 4,
        TransformSeed = 5,
        TransformRounds = 6,
        EncryptionIv = 7,
        ProtectedStreamKey = 8,
        StreamStartBytes = 9,
        InnerStreamId = 10,
        KdfParameters = 11,
        PublicCustomData = 12,
    }

    /// <summary>Lit et déchiffre un coffre ; <paramref name="cache"/> évite de refaire une dérivation de clé déjà faite.</summary>
    public static KeePassDatabase Read(byte[] file, KeePassKey key, TransformCache cache, CancellationToken cancellation = default)
    {
        try
        {
            return ReadCore(file, key, cache, cancellation);
        }
        // ArgumentException : valeurs d'en-tête incohérentes (vecteur d'initialisation de mauvaise taille…).
        catch (Exception e) when (e is EndOfStreamException or ArgumentException or IndexOutOfRangeException
                                      or FormatException or InvalidDataException or XmlException or OverflowException)
        {
            throw Corrupted(e.GetType().Name, e);
        }
    }

    /// <summary>
    /// Nouvelle graine de dérivation de clé (KDBX 3.1 : TransformSeed ; KDBX 4 : sel « S » d'Argon2 ou graine d'AES-KDF),
    /// et la clé dérivée qui va avec (une dérivation complète). Comme KeePass à chaque enregistrement : une clé dérivée
    /// capturée une fois (vidage mémoire) ne déchiffre plus les versions suivantes du fichier. Paramètres d'une forme
    /// inconnue : gardés tels quels.
    /// </summary>
    internal static void RenewKdfSeed(KeePassDatabase db, KeePassKey key, TransformCache cache, CancellationToken cancellation)
    {
        bool v4 = db.Version >= Kdbx4;
        byte[]? kdf = null, seed = null;
        if (v4)
        {
            kdf = db.KdfParameters.ToArray();
            if (VariantDictionary.Parse(kdf).GetBytes("S") is not { Length: >= 16 } current
                || !VariantDictionary.TryReplaceBytes(kdf, "S", RandomNumberGenerator.GetBytes(current.Length)))
            {
                return;
            }
        }
        else
        {
            seed = RandomNumberGenerator.GetBytes(32);
        }

        var composite = key.RevealComposite();
        try
        {
            var transformed = v4
                ? KeyDerivation.Transform(composite, VariantDictionary.Parse(kdf), cancellation)
                : KeyDerivation.AesTransform(composite, seed!, db.TransformRounds, cancellation);
            var fingerprint = v4 ? SHA256.HashData(kdf!) : SHA256.HashData([.. seed!, .. BitConverter.GetBytes(db.TransformRounds)]);
            cache.Set(fingerprint, composite, transformed);
            if (v4)
            {
                db.KdfParameters = kdf!;
            }
            else
            {
                db.TransformSeed = seed!;
            }

            db.TransformedKey.Dispose();
            db.TransformedKey = new SecretBytes(transformed);
            CryptographicOperations.ZeroMemory(transformed);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(composite);
        }
    }

    /// <summary>
    /// Chiffre et met en forme le coffre ; la graine principale, le vecteur d'initialisation et la clé du flot interne
    /// sont neufs à chaque fois (la graine de dérivation l'est par <see cref="RenewKdfSeed"/>).
    /// </summary>
    public static byte[] Write(KeePassDatabase db)
    {
        var masterSeed = RandomNumberGenerator.GetBytes(32);
        var iv = RandomNumberGenerator.GetBytes(db.CipherId == ChaCha20Cipher ? 12 : 16);
        bool v4 = db.Version >= Kdbx4;
        var streamKey = RandomNumberGenerator.GetBytes(v4 && db.InnerStreamId == 3 ? 64 : 32);
        var streamStart = RandomNumberGenerator.GetBytes(32);

        var header = new MemoryStream();
        var w = new BinaryWriter(header);
        w.Write(Signature1);
        w.Write(Signature2);
        w.Write(db.Version);
        void Put(Field id, byte[] data)
        {
            w.Write((byte)id);
            if (v4)
            {
                w.Write(data.Length);
            }
            else
            {
                w.Write((ushort)data.Length);
            }

            w.Write(data);
        }

        Put(Field.CipherId, db.CipherId.ToByteArray(bigEndian: true));
        Put(Field.Compression, BitConverter.GetBytes(db.Compressed ? 1u : 0u));
        Put(Field.MasterSeed, masterSeed);
        if (!v4)
        {
            Put(Field.TransformSeed, db.TransformSeed);
            Put(Field.TransformRounds, BitConverter.GetBytes(db.TransformRounds));
        }

        Put(Field.EncryptionIv, iv);
        if (v4)
        {
            Put(Field.KdfParameters, db.KdfParameters);
            if (db.PublicCustomData is not null)
            {
                Put(Field.PublicCustomData, db.PublicCustomData);
            }
        }
        else
        {
            Put(Field.ProtectedStreamKey, streamKey);
            Put(Field.StreamStartBytes, streamStart);
            Put(Field.InnerStreamId, BitConverter.GetBytes(db.InnerStreamId));
        }

        foreach (var (id, data) in db.OtherHeaderFields)
        {
            w.Write(id);
            if (v4)
            {
                w.Write(data.Length);
            }
            else
            {
                w.Write((ushort)data.Length);
            }

            w.Write(data);
        }

        Put(Field.End, "\r\n\r\n"u8.ToArray());
        w.Flush();
        var headerBytes = header.ToArray();

        var transformed = db.TransformedKey.Reveal();
        var finalKey = SHA256.HashData([.. masterSeed, .. transformed]);
        var hmacKey = SHA512.HashData([.. masterSeed, .. transformed, 0x01]);
        CryptographicOperations.ZeroMemory(transformed);
        try
        {
            if (!v4)
            {
                db.SetHeaderHash(SHA256.HashData(headerBytes));
            }

            byte[] plain;
            using (var payload = new SecretMemoryStream())
            {
                if (v4)
                {
                    WriteInnerHeader(payload, db, streamKey);
                }

                using (var stream = CreateInnerStream(db.InnerStreamId, streamKey))
                {
                    var xml = db.SerializeXml(stream);
                    payload.Write(xml);
                    CryptographicOperations.ZeroMemory(xml);
                }

                plain = db.Compressed ? Compress(payload.GetBuffer().AsSpan(0, (int)payload.Length)) : payload.ToArray();
            }

            var output = new MemoryStream();
            output.Write(headerBytes);
            if (v4)
            {
                output.Write(SHA256.HashData(headerBytes));
                output.Write(HMACSHA256.HashData(BlockHmacKey(hmacKey, ulong.MaxValue), headerBytes));
                var encrypted = Encrypt(db.CipherId, finalKey, iv, plain);
                WriteHmacBlocks(output, encrypted, hmacKey);
            }
            else
            {
                var blocks = new SecretMemoryStream();
                blocks.Write(streamStart);
                WriteHashedBlocks(blocks, plain);
                var clear = blocks.ToArrayAndClear();
                output.Write(Encrypt(db.CipherId, finalKey, iv, clear));
                CryptographicOperations.ZeroMemory(clear);
            }

            CryptographicOperations.ZeroMemory(plain);
            return output.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(finalKey);
            CryptographicOperations.ZeroMemory(hmacKey);
            CryptographicOperations.ZeroMemory(streamKey);
        }
    }

    private static KeePassDatabase ReadCore(byte[] file, KeePassKey key, TransformCache cache, CancellationToken cancellation)
    {
        var r = new BinaryReader(new MemoryStream(file));
        if (file.Length < 12 || r.ReadUInt32() != Signature1 || r.ReadUInt32() != Signature2)
        {
            throw new KeePassException(KeePassError.NotKeePass, CoreStrings.KeePassNotKdbx);
        }

        uint version = r.ReadUInt32();
        uint major = version >> 16;
        if (major is not (3 or 4) || version < Kdbx3_1 && major == 3)
        {
            throw new KeePassException(KeePassError.Unsupported,
                string.Format(CultureInfo.CurrentCulture, CoreStrings.KeePassUnsupportedVersion, $"{major}.{version & 0xFFFF}"));
        }

        bool v4 = major == 4;
        var db = new KeePassDatabase { Version = version };
        byte[]? masterSeed = null, iv = null, streamKey = null, streamStart = null, kdf = null;
        while (true)
        {
            var id = (Field)r.ReadByte();
            int size = v4 ? r.ReadInt32() : r.ReadUInt16();
            if (size < 0 || size > 1024 * 1024)
            {
                throw Corrupted("header");
            }

            var data = r.ReadBytes(size);
            if (data.Length != size)
            {
                throw new EndOfStreamException();
            }

            switch (id)
            {
                case Field.End:
                    break;
                case Field.CipherId:
                    db.CipherId = new Guid(data, bigEndian: true);
                    break;
                case Field.Compression:
                    db.Compressed = BinaryPrimitives.ReadUInt32LittleEndian(data) != 0;
                    break;
                case Field.MasterSeed:
                    masterSeed = data;
                    break;
                case Field.TransformSeed when !v4:
                    db.TransformSeed = data;
                    break;
                case Field.TransformRounds when !v4:
                    db.TransformRounds = BinaryPrimitives.ReadUInt64LittleEndian(data);
                    break;
                case Field.EncryptionIv:
                    iv = data;
                    break;
                case Field.ProtectedStreamKey when !v4:
                    streamKey = data;
                    break;
                case Field.StreamStartBytes when !v4:
                    streamStart = data;
                    break;
                case Field.InnerStreamId when !v4:
                    db.InnerStreamId = BinaryPrimitives.ReadUInt32LittleEndian(data);
                    break;
                case Field.KdfParameters when v4:
                    kdf = data;
                    break;
                case Field.PublicCustomData when v4:
                    db.PublicCustomData = data;
                    break;
                default:
                    db.OtherHeaderFields.Add(((byte)id, data));
                    break;
            }

            if (id == Field.End)
            {
                break;
            }
        }

        int headerLength = (int)r.BaseStream.Position;
        var headerBytes = file.AsSpan(0, headerLength);
        if (db.CipherId != Aes256 && db.CipherId != ChaCha20Cipher)
        {
            var name = db.CipherId == Twofish ? "Twofish" : db.CipherId.ToString();
            throw new KeePassException(KeePassError.Unsupported,
                string.Format(CultureInfo.CurrentCulture, CoreStrings.KeePassUnsupportedCipher, name));
        }

        if (masterSeed is not { Length: 32 } || iv is null || (v4 ? kdf is null : db.TransformSeed.Length != 32 || streamKey is null || streamStart is null))
        {
            throw Corrupted("header");
        }

        // Dérivation de clé : la plus coûteuse, réutilisée si le fichier n'a pas changé de paramètres.
        var fingerprint = v4 ? SHA256.HashData(kdf!) : SHA256.HashData([.. db.TransformSeed, .. BitConverter.GetBytes(db.TransformRounds)]);
        var composite = key.RevealComposite();
        byte[] transformed;
        try
        {
            transformed = cache.Get(fingerprint, composite) ?? (v4
                ? KeyDerivation.Transform(composite, VariantDictionary.Parse(kdf), cancellation)
                : KeyDerivation.AesTransform(composite, db.TransformSeed, db.TransformRounds, cancellation));
            cache.Set(fingerprint, composite, transformed);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(composite);
        }

        if (v4)
        {
            db.KdfParameters = kdf!;
        }

        db.TransformedKey = new SecretBytes(transformed);
        var finalKey = SHA256.HashData([.. masterSeed, .. transformed]);
        var hmacKey = SHA512.HashData([.. masterSeed, .. transformed, 0x01]);
        CryptographicOperations.ZeroMemory(transformed);
        try
        {
            byte[] plain;
            if (v4)
            {
                var hash = r.ReadBytes(32);
                if (!CryptographicOperations.FixedTimeEquals(hash, SHA256.HashData(headerBytes)))
                {
                    throw Corrupted("header SHA-256");
                }

                var hmac = r.ReadBytes(32);
                if (!CryptographicOperations.FixedTimeEquals(hmac, HMACSHA256.HashData(BlockHmacKey(hmacKey, ulong.MaxValue), headerBytes)))
                {
                    throw new KeePassException(KeePassError.InvalidKey, CoreStrings.KeePassInvalidKey);
                }

                var encrypted = ReadHmacBlocks(r, hmacKey);
                plain = Decrypt(db.CipherId, finalKey, iv, encrypted, wrongKeyMeansCorrupted: true);
            }
            else
            {
                var encrypted = file.AsSpan(headerLength).ToArray();
                var blocks = Decrypt(db.CipherId, finalKey, iv, encrypted, wrongKeyMeansCorrupted: false);
                if (blocks.Length < 32 || !CryptographicOperations.FixedTimeEquals(blocks.AsSpan(0, 32), streamStart))
                {
                    throw new KeePassException(KeePassError.InvalidKey, CoreStrings.KeePassInvalidKey);
                }

                plain = ReadHashedBlocks(blocks.AsSpan(32));
                CryptographicOperations.ZeroMemory(blocks);
            }

            if (db.Compressed)
            {
                var compressed = plain;
                plain = Decompress(compressed);
                CryptographicOperations.ZeroMemory(compressed);
            }

            int xmlStart = 0;
            if (v4)
            {
                xmlStart = ReadInnerHeader(plain, db, out streamKey);
            }

            if (streamKey is null)
            {
                throw Corrupted("ProtectedStreamKey");
            }

            using (var stream = CreateInnerStream(db.InnerStreamId, streamKey))
            {
                db.LoadXml(plain.AsMemory(xmlStart), stream);
            }

            CryptographicOperations.ZeroMemory(plain);
            if (!v4 && db.HeaderHash is { Length: > 0 } expected && !expected.SequenceEqual(SHA256.HashData(headerBytes)))
            {
                throw Corrupted("HeaderHash");
            }

            return db;
        }
        catch
        {
            db.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(finalKey);
            CryptographicOperations.ZeroMemory(hmacKey);
            if (streamKey is not null)
            {
                CryptographicOperations.ZeroMemory(streamKey);
            }
        }
    }

    private static int ReadInnerHeader(byte[] plain, KeePassDatabase db, out byte[]? streamKey)
    {
        streamKey = null;
        int pos = 0;
        while (true)
        {
            byte id = plain[pos];
            int size = BinaryPrimitives.ReadInt32LittleEndian(plain.AsSpan(pos + 1));
            if (size < 0 || pos + 5 + size > plain.Length)
            {
                throw Corrupted("inner header");
            }

            var data = plain.AsSpan(pos + 5, size);
            pos += 5 + size;
            switch (id)
            {
                case 0:
                    return pos;
                case 1:
                    db.InnerStreamId = BinaryPrimitives.ReadUInt32LittleEndian(data);
                    break;
                case 2:
                    streamKey = data.ToArray();
                    break;
                case 3:
                    db.Binaries.Add((data[0], new SecretBytes(data[1..])));
                    break;
                default:
                    // Champ d'une version future du format : gardé pour être réécrit tel quel.
                    db.OtherInnerFields.Add((id, new SecretBytes(data)));
                    break;
            }
        }
    }

    private static void WriteInnerHeader(Stream output, KeePassDatabase db, byte[] streamKey)
    {
        void Put(byte id, ReadOnlySpan<byte> data, byte? flags = null)
        {
            output.WriteByte(id);
            Span<byte> size = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(size, data.Length + (flags is null ? 0 : 1));
            output.Write(size);
            if (flags is { } f)
            {
                output.WriteByte(f);
            }

            output.Write(data);
        }

        Put(1, BitConverter.GetBytes(db.InnerStreamId));
        Put(2, streamKey);
        foreach (var (flags, data) in db.Binaries)
        {
            var bytes = data.Reveal();
            Put(3, bytes, flags);
            CryptographicOperations.ZeroMemory(bytes);
        }

        foreach (var (id, data) in db.OtherInnerFields)
        {
            var bytes = data.Reveal();
            Put(id, bytes);
            CryptographicOperations.ZeroMemory(bytes);
        }

        Put(0, []);
    }

    /// <summary>Flot qui masque les valeurs protégées du XML : Salsa20 (2) ou ChaCha20 (3).</summary>
    private static StreamCipher CreateInnerStream(uint id, byte[] key)
    {
        switch (id)
        {
            case 2:
                var salsaKey = SHA256.HashData(key);
                try
                {
                    return new Salsa20(salsaKey, Salsa20Nonce);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(salsaKey);
                }

            case 3:
                var hash = SHA512.HashData(key);
                try
                {
                    return new ChaCha20(hash.AsSpan(0, 32), hash.AsSpan(32, 12));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(hash);
                }

            default:
                throw new KeePassException(KeePassError.Unsupported,
                    string.Format(CultureInfo.CurrentCulture, CoreStrings.KeePassUnsupportedCipher, $"inner stream {id}"));
        }
    }

    private static byte[] Decrypt(Guid cipher, byte[] key, byte[] iv, byte[] data, bool wrongKeyMeansCorrupted)
    {
        if (cipher == ChaCha20Cipher)
        {
            using var chacha = new ChaCha20(key, iv);
            var plain = (byte[])data.Clone();
            chacha.Xor(plain);
            return plain;
        }

        using var aes = Aes.Create();
        aes.Key = key;
        try
        {
            return aes.DecryptCbc(data, iv, PaddingMode.PKCS7);
        }
        catch (CryptographicException e)
        {
            // KDBX 3.1 : une mauvaise clé donne en général un remplissage invalide.
            throw wrongKeyMeansCorrupted ? Corrupted("AES", e) : new KeePassException(KeePassError.InvalidKey, CoreStrings.KeePassInvalidKey, e);
        }
    }

    private static byte[] Encrypt(Guid cipher, byte[] key, byte[] iv, byte[] data)
    {
        if (cipher == ChaCha20Cipher)
        {
            using var chacha = new ChaCha20(key, iv);
            var encrypted = (byte[])data.Clone();
            chacha.Xor(encrypted);
            return encrypted;
        }

        using var aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptCbc(data, iv, PaddingMode.PKCS7);
    }

    private static byte[] ReadHashedBlocks(ReadOnlySpan<byte> data)
    {
        using var output = new SecretMemoryStream(data.Length);
        int pos = 0;
        while (true)
        {
            var hash = data.Slice(pos + 4, 32);
            int size = BinaryPrimitives.ReadInt32LittleEndian(data[(pos + 36)..]);
            pos += 40;
            if (size == 0)
            {
                return output.ToArray();
            }

            if (size < 0 || pos + size > data.Length)
            {
                throw Corrupted("block");
            }

            var block = data.Slice(pos, size);
            if (!CryptographicOperations.FixedTimeEquals(hash, SHA256.HashData(block)))
            {
                throw Corrupted("block SHA-256");
            }

            output.Write(block);
            pos += size;
        }
    }

    private static void WriteHashedBlocks(Stream output, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        uint index = 0;
        for (int pos = 0; ; pos += BlockSize)
        {
            int size = Math.Min(BlockSize, data.Length - pos);
            BinaryPrimitives.WriteUInt32LittleEndian(number, index++);
            output.Write(number);
            output.Write(size > 0 ? SHA256.HashData(data.AsSpan(pos, size)) : new byte[32]);
            BinaryPrimitives.WriteInt32LittleEndian(number, Math.Max(size, 0));
            output.Write(number);
            if (size <= 0)
            {
                return;
            }

            output.Write(data, pos, size);
        }
    }

    private static byte[] ReadHmacBlocks(BinaryReader r, byte[] hmacKey)
    {
        var output = new MemoryStream();
        for (ulong index = 0; ; index++)
        {
            var hmac = r.ReadBytes(32);
            int size = r.ReadInt32();
            if (size < 0 || size > 256 * 1024 * 1024)
            {
                throw Corrupted("block");
            }

            var data = r.ReadBytes(size);
            if (data.Length != size || hmac.Length != 32)
            {
                throw new EndOfStreamException();
            }

            if (!CryptographicOperations.FixedTimeEquals(hmac, BlockHmac(hmacKey, index, data)))
            {
                throw Corrupted("block HMAC");
            }

            if (size == 0)
            {
                return output.ToArray();
            }

            output.Write(data);
        }
    }

    private static void WriteHmacBlocks(Stream output, byte[] data, byte[] hmacKey)
    {
        Span<byte> number = stackalloc byte[4];
        ulong index = 0;
        for (int pos = 0; ; pos += BlockSize, index++)
        {
            int size = Math.Max(0, Math.Min(BlockSize, data.Length - pos));
            var block = size > 0 ? data.AsSpan(pos, size).ToArray() : [];
            output.Write(BlockHmac(hmacKey, index, block));
            BinaryPrimitives.WriteInt32LittleEndian(number, size);
            output.Write(number);
            output.Write(block);
            if (size == 0)
            {
                return;
            }
        }
    }

    private static byte[] BlockHmac(byte[] hmacKey, ulong index, byte[] data)
    {
        var message = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(message, index);
        BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(8), data.Length);
        data.CopyTo(message, 12);
        var key = BlockHmacKey(hmacKey, index);
        try
        {
            return HMACSHA256.HashData(key, message);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] BlockHmacKey(byte[] hmacKey, ulong index)
    {
        var input = new byte[8 + hmacKey.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(input, index);
        hmacKey.CopyTo(input, 8);
        try
        {
            return SHA512.HashData(input);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    private static byte[] Decompress(byte[] data)
    {
        using var gzip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        // Taille prévue : le XML compressé fait souvent le dixième du XML (moins de copies en grandissant).
        using var output = new SecretMemoryStream((int)Math.Min(16 * 1024 * 1024, (long)data.Length * 8));
        var buffer = new byte[81920];
        try
        {
            int read;
            while ((read = gzip.Read(buffer)) > 0)
            {
                if (output.Length + read > MaxXmlSize)
                {
                    throw Corrupted("size");
                }

                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static byte[] Compress(ReadOnlySpan<byte> data)
    {
        using var output = new SecretMemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(data);
        }

        return output.ToArray();
    }

    private static KeePassException Corrupted(string what, Exception? inner = null) =>
        new(KeePassError.Corrupted, string.Format(CultureInfo.CurrentCulture, CoreStrings.KeePassCorrupted, what), inner);
}

/// <summary>
/// Dernière dérivation de clé calculée (souvent une seconde ou plus), réutilisée tant que la clé et les paramètres
/// de dérivation du fichier ne changent pas : relire le coffre après un enregistrement est alors immédiat.
/// </summary>
internal sealed class TransformCache : IDisposable
{
    private byte[]? _fingerprint;
    private SecretBytes? _transformed;

    public byte[]? Get(byte[] kdfFingerprint, byte[] compositeKey)
    {
        var id = Id(kdfFingerprint, compositeKey);
        return _fingerprint is not null && _transformed is not null && CryptographicOperations.FixedTimeEquals(_fingerprint, id)
            ? _transformed.Reveal()
            : null;
    }

    public void Set(byte[] kdfFingerprint, byte[] compositeKey, byte[] transformed)
    {
        _transformed?.Dispose();
        _fingerprint = Id(kdfFingerprint, compositeKey);
        _transformed = new SecretBytes(transformed);
    }

    public void Dispose()
    {
        _transformed?.Dispose();
        _transformed = null;
        _fingerprint = null;
    }

    private static byte[] Id(byte[] kdfFingerprint, byte[] compositeKey) => SHA256.HashData([.. kdfFingerprint, .. compositeKey]);
}
