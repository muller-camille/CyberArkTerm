using CyberArkTerm.Core.KeePass;

namespace CyberArkTerm.Core.Tests.KeePass;

/// <summary>Écriture de coffres : relecture à l'identique, modifications, historique, corbeille, conflits.</summary>
public sealed class KeePassVaultTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("cat-kp-").FullName;

    public static TheoryData<string, string?, string?> AllVaults => new()
    {
        { "kxc-kdbx31.kdbx", KdbxReadTests.Password, null },
        { "kxc-kdbx41.kdbx", KdbxReadTests.Password, null },
        { "kxc-kdbx31-keyonly.kdbx", null, "kxc-v2.keyx" },
        { "py-kdbx4-argon2d-aes.kdbx", KdbxReadTests.Password, null },
        { "py-kdbx4-argon2id-chacha20.kdbx", KdbxReadTests.Password, null },
        { "py-kdbx4-aeskdf-aes.kdbx", KdbxReadTests.Password, null },
        { "py-kdbx4-kxc-v2.kdbx", KdbxReadTests.Password, "kxc-v2.keyx" },
    };

    [Theory]
    [MemberData(nameof(AllVaults))]
    public async Task SavingKeepsEverythingAndMakesABackup(string vault, string? password, string? keyFile)
    {
        var path = Copy(vault);
        var original = await File.ReadAllBytesAsync(path);
        List<(string Title, string User, string Url, string Notes, string? Password)> before;
        string format;
        using (var v = await Open(path, password, keyFile))
        {
            before = Snapshot(v.Database);
            format = v.Database.FormatName;
            await v.SaveAsync(_ => { });
        }

        Assert.Equal(original, await File.ReadAllBytesAsync(path + ".bak"));
        Assert.NotEqual(original, await File.ReadAllBytesAsync(path));
        using var reopened = await Open(path, password, keyFile);
        Assert.Equal(format, reopened.Database.FormatName);
        Assert.Equal(before, Snapshot(reopened.Database));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Theory]
    [MemberData(nameof(AllVaults))]
    public async Task AddUpdateAndDeleteEntries(string vault, string? password, string? keyFile)
    {
        var path = Copy(vault);
        using var v = await Open(path, password, keyFile);
        var count = v.Database.Entries.Count;

        var id = await v.SaveAsync(db => db.AddEntry(new KeePassEntryData("urgence-01", "secours", "Mot de passe <&> é ✓", "ssh://10.0.0.1:2222", "créée par le test"), "Urgence/Linux"));
        var added = v.Database.Entries.Single(e => e.Id == id);
        Assert.Equal("Urgence/Linux", added.Group);
        Assert.Equal("Mot de passe <&> é ✓", v.Database.RevealPassword(id));
        Assert.Equal(count + 1, v.Database.Entries.Count);

        await v.SaveAsync(db => db.UpdateEntry(id, new KeePassEntryData("urgence-01", "secours2", "Nouveau!", "ssh://10.0.0.1:22", "modifiée"), added.Modified.AddSeconds(0)));
        var updated = v.Database.Entries.Single(e => e.Id == id);
        Assert.Equal("secours2", updated.UserName);
        Assert.Equal("Nouveau!", v.Database.RevealPassword(id));
        Assert.Equal(1, v.Database.HistoryCount(id));
        Assert.Equal("Mot de passe <&> é ✓", v.Database.HistoryPassword(id, 0));

        // Mot de passe non fourni : inchangé.
        await v.SaveAsync(db => db.UpdateEntry(id, new KeePassEntryData("urgence-01", "secours3", null, "", ""), updated.Modified));
        Assert.Equal("Nouveau!", v.Database.RevealPassword(id));
        Assert.Equal(2, v.Database.HistoryCount(id));

        var last = v.Database.Entries.Single(e => e.Id == id);
        await v.SaveAsync(db => db.DeleteEntry(id, last.Modified));
        Assert.DoesNotContain(v.Database.Entries, e => e.Id == id);
        Assert.Contains("urgence-01", v.Database.RecycledTitles);

        // Tout est relu par un nouveau chargement du fichier.
        using var reopened = await Open(path, password, keyFile);
        Assert.Equal(count, reopened.Database.Entries.Count);
        Assert.Contains("urgence-01", reopened.Database.RecycledTitles);
    }

    [Fact]
    public async Task StaleEditIsRefused()
    {
        var path = Copy("py-kdbx4-argon2d-aes.kdbx");
        using var v = await Open(path, KdbxReadTests.Password, null);
        var entry = v.Database.Entries.First();
        await v.SaveAsync(db => db.UpdateEntry(entry.Id, new KeePassEntryData("A", "a", "a", "", ""), entry.Modified));

        var ex = await Assert.ThrowsAsync<KeePassException>(() =>
            v.SaveAsync(db => db.UpdateEntry(entry.Id, new KeePassEntryData("B", "b", "b", "", ""), entry.Modified.AddDays(-1))));

        Assert.Equal(KeePassError.Conflict, ex.Kind);
        Assert.Equal("A", v.Database.Entries.Single(e => e.Id == entry.Id).Title);
    }

    [Fact]
    public async Task ChangesMadeElsewhereAreKept()
    {
        var path = Copy("kxc-kdbx31.kdbx");
        using var first = await Open(path, KdbxReadTests.Password, null);
        using (var second = await Open(path, KdbxReadTests.Password, null))
        {
            await second.SaveAsync(db => db.AddEntry(new KeePassEntryData("ajout-ailleurs", "x", "x", "", "")));
        }

        // « first » n'a pas relu le fichier : son enregistrement part pourtant de la version actuelle.
        await first.SaveAsync(db => db.AddEntry(new KeePassEntryData("ajout-ici", "y", "y", "", "")));

        var titles = first.Database.Entries.Select(e => e.Title).ToList();
        Assert.Contains("ajout-ailleurs", titles);
        Assert.Contains("ajout-ici", titles);
    }

    [Fact]
    public async Task DetectsChangesOnDisk()
    {
        var path = Copy("py-kdbx4-argon2d-aes.kdbx");
        using var first = await Open(path, KdbxReadTests.Password, null);
        Assert.False(await first.HasChangedOnDiskAsync());
        using (var second = await Open(path, KdbxReadTests.Password, null))
        {
            await second.SaveAsync(db => db.AddEntry(new KeePassEntryData("autre", "x", "x", "", "")));
        }

        Assert.True(await first.HasChangedOnDiskAsync());
        await first.ReloadAsync();
        Assert.False(await first.HasChangedOnDiskAsync());
        Assert.Contains(first.Database.Entries, e => e.Title == "autre");
    }

    [Fact]
    public async Task FailedChangeLeavesFileUntouched()
    {
        var path = Copy("py-kdbx4-argon2d-aes.kdbx");
        var original = await File.ReadAllBytesAsync(path);
        using var v = await Open(path, KdbxReadTests.Password, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => v.SaveAsync(_ => throw new InvalidOperationException()));

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public async Task WrongKeyIsReportedAndKeyIsReleased()
    {
        var path = Copy("py-kdbx4-argon2d-aes.kdbx");
        var key = KeePassKey.Create("faux", (byte[]?)null);

        var ex = await Assert.ThrowsAsync<KeePassException>(() => KeePassVault.OpenAsync(path, key));

        Assert.Equal(KeePassError.InvalidKey, ex.Kind);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static List<(string, string, string, string, string?)> Snapshot(KeePassDatabase db) =>
        [.. db.Entries.OrderBy(e => e.Id).Select(e => (e.Title, e.UserName, e.Url, e.Notes, db.RevealPassword(e.Id)))];

    private static Task<KeePassVault> Open(string path, string? password, string? keyFile) =>
        KeePassVault.OpenAsync(path, KeePassKey.Create(password ?? "", keyFile is null ? null : KdbxReadTests.VaultPath(keyFile)));

    private string Copy(string vault)
    {
        var path = Path.Combine(_directory, vault);
        File.Copy(KdbxReadTests.VaultPath(vault), path);
        return path;
    }
}
