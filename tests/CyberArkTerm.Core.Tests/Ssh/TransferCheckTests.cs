using System.Security.Cryptography;
using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.Core.Tests.Ssh;

public sealed class TransferCheckTests
{
    private static readonly byte[] Data = [.. Enumerable.Range(0, 300_001).Select(i => (byte)(i * 7))];

    /// <summary>Le flux haché en lecture (envoi) et en écriture (téléchargement, relecture) donne la somme du contenu.</summary>
    [Fact]
    public async Task HashingStreamHashesWhatGoesThrough()
    {
        var expected = SHA256.HashData(Data);

        using (var read = new HashingStream(new MemoryStream(Data)))
        {
            var buffer = new byte[4096];
            while (await read.ReadAsync(buffer) > 0)
            {
            }

            Assert.Equal(expected, read.GetHash());
            Assert.Equal(Data.LongLength, read.Count);
        }

        var target = new MemoryStream();
        using (var write = new HashingStream(target))
        {
            await write.WriteAsync(Data.AsMemory(0, 1000));
            write.Write(Data, 1000, Data.Length - 1000);
            Assert.Equal(expected, write.GetHash());
        }

        Assert.Equal(Data, target.ToArray());

        // Sans flux interne : les octets sont seulement hachés (relecture d'un fichier du serveur).
        using var sink = new HashingStream(null);
        sink.Write(Data);
        Assert.Equal(expected, sink.GetHash());
        Assert.Equal(Data.LongLength, sink.Count);
        Assert.False(sink.CanSeek);
        Assert.Throws<NotSupportedException>(() => sink.ReadByte());
    }

    [Fact]
    public async Task HashesALocalFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cat-hash-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllBytesAsync(path, Data);
            var (hash, length) = await TransferCheck.HashFileAsync(path, default);
            Assert.Equal(SHA256.HashData(Data), hash);
            Assert.Equal(Data.LongLength, length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Identique seulement si la taille et la somme sont les mêmes des deux côtés, et si la relecture a eu lieu.</summary>
    [Fact]
    public void MatchesOnlyWhenSizeAndChecksumAgree()
    {
        var hash = SHA256.HashData(Data);
        var ok = new TransferCheck("f", @"C:\f", "/srv/f", Data.Length, hash, Data.Length, [.. hash]);
        var other = (byte[])hash.Clone();
        other[^1] ^= 1;

        Assert.True(ok.Matches);
        Assert.Equal(64, ok.LocalHash.Length);
        Assert.Equal(ok.LocalHash, ok.RemoteHash);
        Assert.False((ok with { RemoteSha256 = other }).Matches);
        Assert.False((ok with { RemoteLength = Data.Length - 1 }).Matches);
        var unverified = ok with { RemoteLength = -1, RemoteSha256 = [], Error = "Permission denied" };
        Assert.False(unverified.Verified);
        Assert.False(unverified.Matches);
        Assert.Equal("", unverified.RemoteHash);
    }

    /// <summary>
    /// Ligne pour <c>sha256sum -c</c> sur le serveur : somme de l'original (local pour un envoi, serveur pour un
    /// téléchargement) ; chemin avec « \ » ou saut de ligne échappé comme le fait sha256sum.
    /// </summary>
    [Fact]
    public void WritesSha256SumLines()
    {
        var check = new TransferCheck("f", @"C:\f", "/srv/app/f.tar.gz", 1, [0xAB, 0x01], 1, [0xCD, 0x02]);

        Assert.Equal("ab01  /srv/app/f.tar.gz", (check with { Upload = true }).ToSha256SumLine());
        Assert.Equal("cd02  /srv/app/f.tar.gz", check.ToSha256SumLine());
        Assert.Equal("\\ab01  /srv/a\\\\b\\nc", (check with { Upload = true, RemotePath = "/srv/a\\b\nc" }).ToSha256SumLine());
    }
}
