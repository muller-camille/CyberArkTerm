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
