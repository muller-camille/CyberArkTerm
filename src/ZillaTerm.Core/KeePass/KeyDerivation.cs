using System.Globalization;
using System.Runtime.Intrinsics;
using System.Security.Cryptography;
using ZillaTerm.Core.Localization;
using Konscious.Security.Cryptography;

using Sse2 = System.Runtime.Intrinsics.X86.Sse2;
using X86Aes = System.Runtime.Intrinsics.X86.Aes;

namespace ZillaTerm.Core.KeePass;

/// <summary>Dérivation de la clé maîtresse : AES-KDF (KDBX 3.1 et 4) ou Argon2d / Argon2id (KDBX 4).</summary>
internal static class KeyDerivation
{
    public static readonly Guid AesKdf = new("c9d9f39a-628a-4460-bf74-0d08c18a4fea");
    public static readonly Guid AesKdfKdbx4 = new("7c02bb82-79a7-4ac0-927d-114a00648238");
    public static readonly Guid Argon2d = new("ef636ddf-8c29-444b-91f7-a9a403e30a0c");
    public static readonly Guid Argon2id = new("9e298b19-56db-4773-b23d-fc3ec6f0a1e6");

    // Garde-fous contre un fichier aux paramètres démesurés (mémoire ou temps de calcul).
    private const ulong MaxArgon2Memory = 4UL * 1024 * 1024 * 1024;
    private const ulong MaxArgon2Iterations = 10_000;

    // Travail total (mémoire × passes) : chaque borne seule laissait 4 Gio × 10 000 passes, soit des heures de calcul
    // qu'on ne peut pas interrompre. 64 Gio parcourus prennent environ une minute et demie.
    private const ulong MaxArgon2Work = 64UL * 1024 * 1024 * 1024;
    private const ulong MaxAesRounds = 2_000_000_000;

    /// <summary>KDBX 4 : paramètres lus dans l'en-tête.</summary>
    public static byte[] Transform(byte[] compositeKey, VariantDictionary parameters, CancellationToken cancellation)
    {
        var id = parameters.GetBytes("$UUID") is { Length: 16 } uuid ? KdbxGuid(uuid) : Guid.Empty;
        if (id == AesKdf || id == AesKdfKdbx4)
        {
            var seed = parameters.GetBytes("S") ?? throw Corrupted("S");
            var rounds = parameters.GetUInt64("R") ?? throw Corrupted("R");
            return AesTransform(compositeKey, seed, rounds, cancellation);
        }

        if (id == Argon2d || id == Argon2id)
        {
            return Argon2(compositeKey, parameters, id == Argon2id);
        }

        throw new KeePassException(KeePassError.Unsupported,
            string.Format(CultureInfo.CurrentCulture, CoreStrings.KeePassUnsupportedKdf, id));
    }

    /// <summary>AES-KDF : la clé (2 blocs de 16 octets) chiffrée <paramref name="rounds"/> fois en AES-256 ECB, puis SHA-256.</summary>
    public static byte[] AesTransform(byte[] compositeKey, byte[] seed, ulong rounds, CancellationToken cancellation)
    {
        if (seed.Length != 32 || compositeKey.Length != 32)
        {
            throw Corrupted("AES-KDF");
        }

        if (rounds > MaxAesRounds)
        {
            throw TooCostly($"AES-KDF {rounds}");
        }

        return X86Aes.IsSupported
            ? AesTransformNative(compositeKey, seed, rounds, cancellation)
            : AesTransformManaged(compositeKey, seed, rounds, cancellation);
    }

    /// <summary>AES-KDF avec l'AES de .NET (processeurs sans AES-NI).</summary>
    internal static byte[] AesTransformManaged(byte[] compositeKey, byte[] seed, ulong rounds, CancellationToken cancellation)
    {
        var key = (byte[])compositeKey.Clone();
        try
        {
            // Les deux blocs sont indépendants : un par cœur.
            Parallel.For(0, 2, new ParallelOptions { CancellationToken = cancellation }, half =>
            {
                using var aes = Aes.Create();
                aes.Key = seed;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                using var encryptor = aes.CreateEncryptor();
                var a = new byte[16];
                var b = new byte[16];
                Buffer.BlockCopy(key, half * 16, a, 0, 16);
                for (ulong i = 0; i < rounds; i++)
                {
                    encryptor.TransformBlock(a, 0, 16, b, 0);
                    (a, b) = (b, a);
                    if ((i & 0xFFFFF) == 0)
                    {
                        cancellation.ThrowIfCancellationRequested();
                    }
                }

                Buffer.BlockCopy(a, 0, key, half * 16, 16);
                CryptographicOperations.ZeroMemory(a);
                CryptographicOperations.ZeroMemory(b);
            });
            return SHA256.HashData(key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// AES-KDF avec les instructions AES du processeur (AES-NI) : une dizaine de fois plus rapide, ce qui compte pour
    /// les coffres réglés sur plusieurs millions de tours.
    /// </summary>
    internal static byte[] AesTransformNative(byte[] compositeKey, byte[] seed, ulong rounds, CancellationToken cancellation)
    {
        var k = ExpandKey256(seed);
        Vector128<byte> k0 = k[0], k1 = k[1], k2 = k[2], k3 = k[3], k4 = k[4], k5 = k[5], k6 = k[6], k7 = k[7];
        Vector128<byte> k8 = k[8], k9 = k[9], k10 = k[10], k11 = k[11], k12 = k[12], k13 = k[13], k14 = k[14];
        Array.Clear(k);
        var a = Vector128.Create(compositeKey.AsSpan(0, 16));
        var b = Vector128.Create(compositeKey.AsSpan(16, 16));
        for (ulong i = 0; i < rounds; i++)
        {
            // Les deux blocs sont chiffrés en même temps : le processeur traite les deux chaînes en parallèle.
            a = Sse2.Xor(a, k0);
            b = Sse2.Xor(b, k0);
            a = X86Aes.Encrypt(a, k1);
            b = X86Aes.Encrypt(b, k1);
            a = X86Aes.Encrypt(a, k2);
            b = X86Aes.Encrypt(b, k2);
            a = X86Aes.Encrypt(a, k3);
            b = X86Aes.Encrypt(b, k3);
            a = X86Aes.Encrypt(a, k4);
            b = X86Aes.Encrypt(b, k4);
            a = X86Aes.Encrypt(a, k5);
            b = X86Aes.Encrypt(b, k5);
            a = X86Aes.Encrypt(a, k6);
            b = X86Aes.Encrypt(b, k6);
            a = X86Aes.Encrypt(a, k7);
            b = X86Aes.Encrypt(b, k7);
            a = X86Aes.Encrypt(a, k8);
            b = X86Aes.Encrypt(b, k8);
            a = X86Aes.Encrypt(a, k9);
            b = X86Aes.Encrypt(b, k9);
            a = X86Aes.Encrypt(a, k10);
            b = X86Aes.Encrypt(b, k10);
            a = X86Aes.Encrypt(a, k11);
            b = X86Aes.Encrypt(b, k11);
            a = X86Aes.Encrypt(a, k12);
            b = X86Aes.Encrypt(b, k12);
            a = X86Aes.Encrypt(a, k13);
            b = X86Aes.Encrypt(b, k13);
            a = X86Aes.EncryptLast(a, k14);
            b = X86Aes.EncryptLast(b, k14);
            if ((i & 0xFFFFF) == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }
        }

        var key = new byte[32];
        a.CopyTo(key);
        b.CopyTo(key.AsSpan(16));
        try
        {
            return SHA256.HashData(key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Clés des 15 tours d'AES-256 (méthode du livre blanc Intel AES-NI).</summary>
    private static Vector128<byte>[] ExpandKey256(byte[] key)
    {
        var t1 = Vector128.Create(key.AsSpan(0, 16));
        var t3 = Vector128.Create(key.AsSpan(16, 16));
        var keys = new Vector128<byte>[15];
        keys[0] = t1;
        keys[1] = t3;
        keys[2] = t1 = Odd(t1, X86Aes.KeygenAssist(t3, 0x01));
        keys[3] = t3 = Even(t3, X86Aes.KeygenAssist(t1, 0x00));
        keys[4] = t1 = Odd(t1, X86Aes.KeygenAssist(t3, 0x02));
        keys[5] = t3 = Even(t3, X86Aes.KeygenAssist(t1, 0x00));
        keys[6] = t1 = Odd(t1, X86Aes.KeygenAssist(t3, 0x04));
        keys[7] = t3 = Even(t3, X86Aes.KeygenAssist(t1, 0x00));
        keys[8] = t1 = Odd(t1, X86Aes.KeygenAssist(t3, 0x08));
        keys[9] = t3 = Even(t3, X86Aes.KeygenAssist(t1, 0x00));
        keys[10] = t1 = Odd(t1, X86Aes.KeygenAssist(t3, 0x10));
        keys[11] = t3 = Even(t3, X86Aes.KeygenAssist(t1, 0x00));
        keys[12] = t1 = Odd(t1, X86Aes.KeygenAssist(t3, 0x20));
        keys[13] = Even(t3, X86Aes.KeygenAssist(t1, 0x00));
        keys[14] = Odd(t1, X86Aes.KeygenAssist(keys[13], 0x40));
        return keys;

        static Vector128<byte> Odd(Vector128<byte> t, Vector128<byte> assist) =>
            Sse2.Xor(ShiftXor(t), Sse2.Shuffle(assist.AsUInt32(), 0xFF).AsByte());

        static Vector128<byte> Even(Vector128<byte> t, Vector128<byte> assist) =>
            Sse2.Xor(ShiftXor(t), Sse2.Shuffle(assist.AsUInt32(), 0xAA).AsByte());

        // t ^ (t << 32) ^ (t << 64) ^ (t << 96), par décalages d'octets du registre entier.
        static Vector128<byte> ShiftXor(Vector128<byte> t)
        {
            var s = Sse2.ShiftLeftLogical128BitLane(t, 4);
            t = Sse2.Xor(t, s);
            s = Sse2.ShiftLeftLogical128BitLane(s, 4);
            t = Sse2.Xor(t, s);
            s = Sse2.ShiftLeftLogical128BitLane(s, 4);
            return Sse2.Xor(t, s);
        }
    }

    private static byte[] Argon2(byte[] compositeKey, VariantDictionary p, bool id)
    {
        var salt = p.GetBytes("S") ?? throw Corrupted("S");
        var parallelism = p.GetUInt64("P") ?? throw Corrupted("P");
        var memory = p.GetUInt64("M") ?? throw Corrupted("M");
        var iterations = p.GetUInt64("I") ?? throw Corrupted("I");
        var version = p.GetUInt64("V") ?? 0x13;
        if (version != 0x13)
        {
            throw new KeePassException(KeePassError.Unsupported,
                string.Format(CultureInfo.CurrentCulture, CoreStrings.KeePassUnsupportedKdf, $"Argon2 v{version:X}"));
        }

        if (iterations == 0 || parallelism is 0 or > 256 || memory < 8 * 1024 * parallelism)
        {
            // Paramètres impossibles pour Argon2 (au moins une passe, 8 Kio par fil).
            throw Corrupted($"Argon2 M={memory}, I={iterations}, P={parallelism}");
        }

        if (memory > MaxArgon2Memory || iterations > MaxArgon2Iterations || memory * iterations > MaxArgon2Work)
        {
            throw TooCostly($"Argon2 M={memory / (1024 * 1024)} Mio, I={iterations}, P={parallelism}");
        }

        using Argon2 argon = id ? new Argon2id(compositeKey) : new Argon2d(compositeKey);
        argon.Salt = salt;
        argon.DegreeOfParallelism = (int)parallelism;
        argon.MemorySize = (int)(memory / 1024);
        argon.Iterations = (int)iterations;
        if (p.GetBytes("K") is { Length: > 0 } secret)
        {
            argon.KnownSecret = secret;
        }

        if (p.GetBytes("A") is { Length: > 0 } associated)
        {
            argon.AssociatedData = associated;
        }

        try
        {
            return argon.GetBytes(32);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or AggregateException)
        {
            throw Corrupted("Argon2");
        }
    }

    /// <summary>Identifiant KeePass (16 octets dans l'ordre de leur écriture hexadécimale) → Guid.</summary>
    public static Guid KdbxGuid(ReadOnlySpan<byte> bytes) => new(bytes, bigEndian: true);

    private static KeePassException Corrupted(string what) =>
        new(KeePassError.Corrupted, string.Format(CultureInfo.CurrentCulture, CoreStrings.KeePassCorrupted, what));

    private static KeePassException TooCostly(string what) =>
        new(KeePassError.Unsupported, string.Format(CultureInfo.CurrentCulture, CoreStrings.KeePassKdfTooCostly, what));
}
