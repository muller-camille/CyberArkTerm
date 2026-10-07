using System.Text;
using System.Text.Json.Nodes;
using ZillaTerm.Core.KeePass;

namespace ZillaTerm.Core.Tests.KeePass;

public sealed class LocalSecretStoreTests : IDisposable
{
    // Argon2id allégé pour les tests.
    private static readonly LocalSecretStore.KdfSettings Fast = new(8 * 1024, 1, 1);
    private readonly string _directory = Directory.CreateTempSubdirectory("cat-store-").FullName;

    private string StorePath => Path.Combine(_directory, "secrets.dat");

    [Fact]
    public void SecretsSurviveLockAndReopen()
    {
        using (var store = new LocalSecretStore(StorePath, null, Fast))
        {
            store.Create("mot-de-passe-local");
            store.Set("folder-1", "Maître é ✓ \"quotes\"");
            store.Set("folder-2", "autre");
            store.Remove("folder-2");
        }

        using var reopened = new LocalSecretStore(StorePath, null, Fast);
        Assert.True(reopened.Exists);
        Assert.False(reopened.IsUnlocked);
        reopened.Unlock("mot-de-passe-local");
        Assert.Equal("Maître é ✓ \"quotes\"", Encoding.UTF8.GetString(reopened.Get("folder-1")!));
        Assert.Null(reopened.Get("folder-2"));
        Assert.Equal(["folder-1"], reopened.Ids);
    }

    [Fact]
    public void FileContainsNoSecretInClear()
    {
        using var store = new LocalSecretStore(StorePath, null, Fast);
        store.Create("mot-de-passe-local");
        store.Set("folder-1", "SecretEnClair123");

        var content = File.ReadAllText(StorePath);
        Assert.DoesNotContain("SecretEnClair123", content);
        Assert.DoesNotContain("folder-1", content);
        Assert.DoesNotContain("mot-de-passe-local", content);
    }

    [Fact]
    public void WrongPasswordIsRefused()
    {
        using (var store = new LocalSecretStore(StorePath, null, Fast))
        {
            store.Create("mot-de-passe-local");
        }

        using var reopened = new LocalSecretStore(StorePath, null, Fast);
        var ex = Assert.Throws<KeePassException>(() => reopened.Unlock("mauvais mot de passe"));
        Assert.Equal(KeePassError.InvalidKey, ex.Kind);
        Assert.False(reopened.IsUnlocked);
    }

    [Fact]
    public void WeakenedSettingsInTheFileAreDetected()
    {
        using (var store = new LocalSecretStore(StorePath, null, new LocalSecretStore.KdfSettings(16 * 1024, 2, 1)))
        {
            store.Create("mot-de-passe-local");
            store.Set("a", "b");
        }

        // Quelqu'un réduit le coût d'Argon2id dans le fichier : l'authentification échoue.
        var json = JsonNode.Parse(File.ReadAllText(StorePath))!;
        json["Iterations"] = 1;
        File.WriteAllText(StorePath, json.ToJsonString());

        using var reopened = new LocalSecretStore(StorePath, null, Fast);
        Assert.Throws<KeePassException>(() => reopened.Unlock("mot-de-passe-local"));
    }

    [Fact]
    public void ChangedPasswordReplacesTheOldOne()
    {
        using (var store = new LocalSecretStore(StorePath, null, Fast))
        {
            store.Create("ancien-mot-de-passe");
            store.Set("x", "valeur");
            store.ChangePassword("nouveau-mot-de-passe");
        }

        using var reopened = new LocalSecretStore(StorePath, null, Fast);
        Assert.Throws<KeePassException>(() => reopened.Unlock("ancien-mot-de-passe"));
        reopened.Unlock("nouveau-mot-de-passe");
        Assert.Equal("valeur", Encoding.UTF8.GetString(reopened.Get("x")!));
    }

    [Fact]
    public void ProtectorWrapsTheFile()
    {
        var protector = new XorProtector();
        using (var store = new LocalSecretStore(StorePath, protector, Fast))
        {
            store.Create("mot-de-passe-local");
            store.Set("x", "valeur");
        }

        Assert.Throws<KeePassException>(() => new LocalSecretStore(StorePath, null, Fast).Unlock("mot-de-passe-local"));
        using var reopened = new LocalSecretStore(StorePath, protector, Fast);
        reopened.Unlock("mot-de-passe-local");
        Assert.Equal("valeur", Encoding.UTF8.GetString(reopened.Get("x")!));
    }

    [Fact]
    public void ShortPasswordAndLockedAccessAreRefused()
    {
        using var store = new LocalSecretStore(StorePath, null, Fast);
        Assert.Throws<ArgumentException>(() => store.Create("court"));
        Assert.Throws<InvalidOperationException>(() => store.Set("x", "y"));
    }

    [Fact]
    public void DeleteRemovesTheFile()
    {
        using var store = new LocalSecretStore(StorePath, null, Fast);
        store.Create("mot-de-passe-local");

        store.Delete();

        Assert.False(store.Exists);
        Assert.False(store.IsUnlocked);
    }

    [Fact]
    public void StoredPasswordOpensAKeePassVault()
    {
        using var store = new LocalSecretStore(StorePath, null, Fast);
        store.Create("mot-de-passe-local");
        store.Set("vault", KdbxReadTests.Password);

        var password = store.Get("vault")!;
        using var key = KeePassKey.CreateFromUtf8(password, null);
        using var cache = new TransformCache();
        using var db = KdbxFile.Read(File.ReadAllBytes(KdbxReadTests.VaultPath("py-kdbx4-argon2d-aes.kdbx")), key, cache);

        Assert.Equal(3, db.Entries.Count);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void EmptyPasswordIsAWrongPassword()
    {
        using (var setup = new LocalSecretStore(StorePath, null, Fast))
        {
            setup.Create("mot-de-passe-local");
        }

        using var store = new LocalSecretStore(StorePath, null, Fast);
        var e = Assert.Throws<KeePassException>(() => store.Unlock(""));
        Assert.Equal(KeePassError.InvalidKey, e.Kind);
        Assert.False(store.IsUnlocked);
    }

    /// <summary>Session Windows verrouillée pendant le calcul de la clé : le coffre reste verrouillé, rien n'est écrit.</summary>
    [Fact]
    public void LockDuringUnlockKeepsTheStoreLocked()
    {
        using (var setup = new LocalSecretStore(StorePath, null, Fast))
        {
            setup.Create("mot-de-passe-local");
            setup.Set("folder-1", "secret");
        }

        var protector = new LockingProtector();
        using var store = new LocalSecretStore(StorePath, protector, Fast);
        File.WriteAllBytes(StorePath, protector.Protect(File.ReadAllBytes(StorePath)));
        protector.Store = store;

        Assert.Throws<OperationCanceledException>(() => store.Unlock("mot-de-passe-local"));
        Assert.False(store.IsUnlocked);
        Assert.Empty(store.Ids);

        protector.Store = null;
        store.Unlock("mot-de-passe-local");
        Assert.Equal(["folder-1"], store.Ids);
    }

    /// <summary>Coffres retirés (ou « se souvenir » décoché) pendant que le coffre local était verrouillé : oubliés ensuite.</summary>
    [Fact]
    public void RemoveAllExceptForgetsUnreferencedSecrets()
    {
        using (var store = new LocalSecretStore(StorePath, null, Fast))
        {
            store.Create("mot-de-passe-local");
            store.Set("folder-1", "a");
            store.Set("folder-2", "b");
            store.Set("folder-3", "c");
            Assert.Equal(2, store.RemoveAllExcept(["folder-2", "inconnu"]));
            Assert.Equal(0, store.RemoveAllExcept(["folder-2"]));
        }

        using var reopened = new LocalSecretStore(StorePath, null, Fast);
        reopened.Unlock("mot-de-passe-local");
        Assert.Equal(["folder-2"], reopened.Ids);
    }

    /// <summary>Verrouille le coffre pendant la lecture du fichier, comme un verrouillage de session au mauvais moment.</summary>
    private sealed class LockingProtector : ISecretProtector
    {
        public LocalSecretStore? Store { get; set; }

        public byte[] Protect(byte[] data) => [.. data];

        public byte[] Unprotect(byte[] data)
        {
            Store?.Lock();
            return [.. data];
        }
    }

    private sealed class XorProtector : ISecretProtector
    {
        public byte[] Protect(byte[] data) => data.Select(b => (byte)(b ^ 0x5A)).ToArray();

        public byte[] Unprotect(byte[] data) => Protect(data);
    }
}
