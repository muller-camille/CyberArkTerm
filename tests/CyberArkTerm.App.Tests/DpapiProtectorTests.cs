using System.IO;
using System.Text;
using CyberArkTerm.App.Services.KeePass;
using CyberArkTerm.Core.KeePass;

namespace CyberArkTerm.App.Tests;

public sealed class DpapiProtectorTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("cat-dpapi-").FullName;

    [Fact]
    public void LocalStoreIsBoundToTheWindowsAccount()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(_directory, "coffre-local.dat");
        var fast = new LocalSecretStore.KdfSettings(8 * 1024, 1, 1);
        using (var store = new LocalSecretStore(path, new DpapiProtector(), fast))
        {
            store.Create("mot-de-passe-local");
            store.Set("vault", Encoding.UTF8.GetBytes("Maître-1"));
        }

        // Sans DPAPI le fichier est illisible (ni JSON, ni secrets) ; avec, il se rouvre.
        var raw = File.ReadAllBytes(path);
        Assert.DoesNotContain("Salt", Encoding.UTF8.GetString(raw), StringComparison.Ordinal);
        Assert.Throws<KeePassException>(() =>
        {
            using var withoutDpapi = new LocalSecretStore(path, null, fast);
            withoutDpapi.Unlock("mot-de-passe-local");
        });
        using var reopened = new LocalSecretStore(path, new DpapiProtector(), fast);
        reopened.Unlock("mot-de-passe-local");
        Assert.Equal("Maître-1", Encoding.UTF8.GetString(reopened.Get("vault")!));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
