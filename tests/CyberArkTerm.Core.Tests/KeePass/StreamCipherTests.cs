using CyberArkTerm.Core.KeePass;

namespace CyberArkTerm.Core.Tests.KeePass;

public class StreamCipherTests
{
    // Suites de référence produites par pycryptodome (clé 00..1F).
    private const string ChaChaReference =
        "efda33228dc01766bbaba892678fc9c4fc5f6595c01a760a1c71190f136603df7a8ef3c0e20286a439390ae1ee9e76bf6049812e4e1a315a161c" +
        "080ea3179b0e3411bbca98a57652fbde157ff289d89370c692349f50ebadc63ff6b2fa32ccf3cab9b2e0af3a4a739fefcaede8a3e8c8c4db8f1d1f" +
        "6b36e95ebfeb9604b651f1124c35649fb56068eb466cbbd4df13cf61129815a77d";

    private const string SalsaReference =
        "b8423b470a96c4599b4b1d11c5ac47b9cdd51e79d212f64c61df0564b4e8e6b43ed26a06d9cb5f116006cc3ace1d91fc47437bf698cd6fa6a986" +
        "3bde443295fe2d7f9604974758c5f9b9a628347fd6a482bee613cac52d76ef03e1e726c38215878e128d4bb32f591ca2fed1b7e9a76ec3417b4d" +
        "d630bd2679d9907d8397641e2ccb4e4de4d09fa64cc62edf55f5a763458b109ebbee";

    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void ChaCha20MatchesReferenceAcrossUnevenCalls()
    {
        using var cipher = new ChaCha20(Key, Enumerable.Range(100, 12).Select(i => (byte)i).ToArray());

        Assert.Equal(ChaChaReference, Convert.ToHexStringLower(KeyStream(cipher, 150)));
    }

    [Fact]
    public void Salsa20MatchesReferenceAcrossUnevenCalls()
    {
        using var cipher = new Salsa20(Key, Enumerable.Range(200, 8).Select(i => (byte)i).ToArray());

        Assert.Equal(SalsaReference, Convert.ToHexStringLower(KeyStream(cipher, 150)));
    }

    [Fact]
    public void ChaCha20MatchesRfc8439EncryptionExample()
    {
        // RFC 8439, 2.4.2 : clé 00..1F, nonce 00 00 00 00 00 00 00 4a 00 00 00 00, compteur 1.
        var nonce = Convert.FromHexString("000000000000004a00000000");
        var plain = "Ladies and Gentlemen of the class of '99: If I could offer you only one tip for the future, sunscreen would be it."u8.ToArray();
        using var cipher = new ChaCha20(Key, nonce, counter: 1);

        cipher.Xor(plain);

        Assert.StartsWith("6e2e359a2568f98041ba0728dd0d6981e97e7aec1d4360c20a27afccfd9fae0b", Convert.ToHexStringLower(plain));
        Assert.EndsWith("5af90bbf74a35be6b40b8eedf2785e42874d", Convert.ToHexStringLower(plain));
    }

    [Fact]
    public void RejectsWrongKeyOrNonceSizes()
    {
        Assert.Throws<ArgumentException>(() => new ChaCha20(new byte[16], new byte[12]));
        Assert.Throws<ArgumentException>(() => new Salsa20(new byte[32], new byte[12]));
    }

    /// <summary>Suite obtenue en chiffrant des zéros par morceaux de tailles variées.</summary>
    private static byte[] KeyStream(StreamCipher cipher, int length)
    {
        var data = new byte[length];
        int offset = 0;
        foreach (var size in new[] { 1, 63, 2, 64, 20 })
        {
            cipher.Xor(data.AsSpan(offset, size));
            offset += size;
        }

        return data;
    }
}
