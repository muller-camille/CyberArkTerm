using System.Diagnostics;
using System.Security.Cryptography;
using CyberArkTerm.Core.KeePass;
using Xunit.Abstractions;

namespace CyberArkTerm.Core.Tests.KeePass;

public class KeyDerivationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(1000UL)]
    [InlineData(60001UL)]
    public void NativeAesKdfMatchesDotNetAes(ulong rounds)
    {
        if (!System.Runtime.Intrinsics.X86.Aes.IsSupported)
        {
            output.WriteLine("Processeur sans AES-NI : seule la version .NET est utilisée.");
            return;
        }

        var key = RandomNumberGenerator.GetBytes(32);
        var seed = RandomNumberGenerator.GetBytes(32);

        Assert.Equal(KeyDerivation.AesTransformManaged(key, seed, rounds, default), KeyDerivation.AesTransformNative(key, seed, rounds, default));
    }

    /// <summary>
    /// Paramètres Argon2 lus dans l'en-tête d'un coffre (non authentifié à ce stade) : un travail démesuré est refusé avant
    /// de commencer (il ne pourrait pas être interrompu), des valeurs impossibles donnent « coffre endommagé ».
    /// </summary>
    [Theory]
    [InlineData(4UL * 1024 * 1024 * 1024, 10_000UL, 1UL, KeePassError.Unsupported)]
    [InlineData(1024UL * 1024 * 1024, 100UL, 1UL, KeePassError.Unsupported)]
    [InlineData(64UL * 1024 * 1024, 0UL, 1UL, KeePassError.Corrupted)]
    [InlineData(8UL * 1024, 2UL, 4UL, KeePassError.Corrupted)]
    public void RefusesArgon2ParametersBeforeRunning(ulong memory, ulong iterations, ulong parallelism, KeePassError expected)
    {
        var parameters = VariantDictionary.Parse(Argon2Parameters(memory, iterations, parallelism));
        var watch = Stopwatch.StartNew();

        var error = Assert.Throws<KeePassException>(() => KeyDerivation.Transform(new byte[32], parameters, default));

        Assert.Equal(expected, error.Kind);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1));
    }

    private static byte[] Argon2Parameters(ulong memory, ulong iterations, ulong parallelism)
    {
        using var data = new MemoryStream();
        using var writer = new BinaryWriter(data);
        writer.Write((ushort)0x0100);
        void Item(byte type, string name, byte[] value)
        {
            writer.Write(type);
            writer.Write(System.Text.Encoding.UTF8.GetByteCount(name));
            writer.Write(System.Text.Encoding.UTF8.GetBytes(name));
            writer.Write(value.Length);
            writer.Write(value);
        }

        Item(VariantDictionary.Bytes, "$UUID", KeyDerivation.Argon2d.ToByteArray(bigEndian: true));
        Item(VariantDictionary.Bytes, "S", new byte[32]);
        Item(VariantDictionary.UInt64, "M", BitConverter.GetBytes(memory));
        Item(VariantDictionary.UInt64, "I", BitConverter.GetBytes(iterations));
        Item(VariantDictionary.UInt32, "P", BitConverter.GetBytes((uint)parallelism));
        writer.Write((byte)0);
        return data.ToArray();
    }

    [Fact]
    public void AesKdfMatchesKnownValue()
    {
        // SHA-256 de 32 octets nuls chiffrés 10 fois en AES-256 ECB avec une clé nulle (calculé avec pycryptodome).
        var result = KeyDerivation.AesTransform(new byte[32], new byte[32], 10, default);

        Assert.Equal(ExpectedTenRounds, Convert.ToHexStringLower(result));
    }

    [Fact]
    public void MillionsOfRoundsStayFast()
    {
        var watch = Stopwatch.StartNew();
        KeyDerivation.AesTransform(new byte[32], new byte[32], 6_000_000, default);
        output.WriteLine($"AES-KDF 6 000 000 tours : {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void CancellationStopsAesKdf()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => KeyDerivation.AesTransform(new byte[32], new byte[32], 100_000_000, cancel.Token));
    }

    private const string ExpectedTenRounds = "f7d4f8a60c510e83e6cd4e309887d86619050f66006c385e1f84d2183e1bc9e9";
}
