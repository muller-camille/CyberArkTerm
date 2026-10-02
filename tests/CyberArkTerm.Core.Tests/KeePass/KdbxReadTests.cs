using CyberArkTerm.Core.KeePass;

namespace CyberArkTerm.Core.Tests.KeePass;

/// <summary>Lecture de coffres créés par KeePassXC et pykeepass (voir Vaults/generate.py).</summary>
public class KdbxReadTests
{
    public const string Password = "Urgence-2026!";

    public static string VaultPath(string name) => Path.Combine(AppContext.BaseDirectory, "KeePass", "Vaults", name);

    public static KeePassDatabase Open(string vault, string? password = Password, string? keyFile = null)
    {
        using var key = KeePassKey.Create(password, keyFile is null ? null : VaultPath(keyFile));
        using var cache = new TransformCache();
        return KdbxFile.Read(File.ReadAllBytes(VaultPath(vault)), key, cache);
    }

    [Theory]
    [InlineData("py-kdbx4-argon2d-aes.kdbx", "KDBX 4.0")]
    [InlineData("py-kdbx4-argon2id-chacha20.kdbx", "KDBX 4.0")]
    [InlineData("py-kdbx4-aeskdf-aes.kdbx", "KDBX 4.0")]
    public void ReadsPykeepassKdbx4Vaults(string vault, string format)
    {
        using var db = Open(vault);

        Assert.Equal(format, db.FormatName);
        var entries = db.Entries.OrderBy(e => e.Title).ToList();
        Assert.Equal(["db01", "srv-lnx01", "srv-win01"], entries.Select(e => e.Title));
        var lnx = entries[1];
        Assert.Equal("root", lnx.UserName);
        Assert.Equal("ssh://srv-lnx01.corp.local:22", lnx.Url);
        Assert.Equal("Ligne 1\nLigne 2 — accents éàü", lnx.Notes);
        Assert.Equal("", lnx.Group);
        Assert.Equal("Root-Pass 1", db.RevealPassword(lnx.Id));
        var win = entries[2];
        Assert.Equal("Windows", win.Group);
        Assert.Equal(@"CORP\administrator", win.UserName);
        Assert.Equal("Adm!n W1n", db.RevealPassword(win.Id));
        var oracle = entries[0];
        Assert.Equal("Or@cle<&>\"", db.RevealPassword(oracle.Id));
        Assert.Equal("2222", oracle.CustomFields["Port"]);
        Assert.False(oracle.CustomFields.ContainsKey("Secret"));
        Assert.Equal("hidden", db.GetField(oracle.Id, "Secret"));
        Assert.Contains("prod", oracle.Tags);
    }

    [Fact]
    public void ReadsKeePassXcKdbx31WithHistory()
    {
        using var db = Open("kxc-kdbx31.kdbx");

        Assert.Equal("KDBX 3.1", db.FormatName);
        var lnx = db.Entries.Single(e => e.Title == "srv-lnx01");
        Assert.Equal("Root-Pass 1", db.RevealPassword(lnx.Id));
        Assert.Equal(["", "Windows"], db.Groups);
        Assert.Equal("Adm!n W1n", db.RevealPassword(db.Entries.Single(e => e.Group == "Windows").Id));
    }

    [Fact]
    public void ReadsKeePassXcKdbx41AndHidesRecycleBin()
    {
        using var db = Open("kxc-kdbx41.kdbx");

        Assert.Equal("KDBX 4.1", db.FormatName);
        Assert.Equal(["srv-lnx01", "srv-win01"], db.Entries.Select(e => e.Title).Order());
        Assert.DoesNotContain(db.Groups, g => g.Contains("Recycle", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("py-kdbx4-key-raw32.kdbx", "key-raw32.key")]
    [InlineData("py-kdbx4-key-hex64.kdbx", "key-hex64.key")]
    [InlineData("py-kdbx4-key-any.kdbx", "key-any.key")]
    [InlineData("py-kdbx4-key-v1.kdbx", "key-v1.keyx")]
    [InlineData("py-kdbx4-kxc-v2.kdbx", "kxc-v2.keyx")]
    public void ReadsVaultsWithPasswordAndKeyFile(string vault, string keyFile)
    {
        using var db = Open(vault, Password, keyFile);

        Assert.Equal(3, db.Entries.Count);
    }

    [Theory]
    [InlineData("py-kdbx4-keyonly.kdbx", "key-raw32.key", "Root-Pass 1")]
    [InlineData("kxc-kdbx31-keyonly.kdbx", "kxc-v2.keyx", "Key-Only 2")]
    public void ReadsVaultsWithKeyFileOnly(string vault, string keyFile, string firstPassword)
    {
        // Mot de passe laissé vide : seul le fichier clé compte.
        using var db = Open(vault, "", keyFile);

        Assert.Contains(db.Entries, e => db.RevealPassword(e.Id) == firstPassword);
    }

    [Theory]
    [InlineData("py-kdbx4-argon2d-aes.kdbx")]
    [InlineData("py-kdbx4-argon2id-chacha20.kdbx")]
    [InlineData("kxc-kdbx31.kdbx")]
    public void WrongPasswordIsReportedAsInvalidKey(string vault)
    {
        var ex = Assert.Throws<KeePassException>(() => Open(vault, "mauvais"));

        Assert.Equal(KeePassError.InvalidKey, ex.Kind);
    }

    [Fact]
    public void MissingKeyFileIsReportedAsInvalidKey()
    {
        var ex = Assert.Throws<KeePassException>(() => Open("py-kdbx4-kxc-v2.kdbx"));

        Assert.Equal(KeePassError.InvalidKey, ex.Kind);
    }

    [Fact]
    public void NonKeePassFileIsRejected()
    {
        using var key = KeePassKey.Create(Password, (byte[]?)null);
        using var cache = new TransformCache();

        var ex = Assert.Throws<KeePassException>(() => KdbxFile.Read("not a vault, just text"u8.ToArray(), key, cache));

        Assert.Equal(KeePassError.NotKeePass, ex.Kind);
    }

    [Fact]
    public void TamperedFileIsReportedAsCorrupted()
    {
        var file = File.ReadAllBytes(VaultPath("py-kdbx4-argon2d-aes.kdbx"));
        file[^40] ^= 0x01;
        using var key = KeePassKey.Create(Password, (byte[]?)null);
        using var cache = new TransformCache();

        var ex = Assert.Throws<KeePassException>(() => KdbxFile.Read(file, key, cache));

        Assert.Equal(KeePassError.Corrupted, ex.Kind);
    }
}
