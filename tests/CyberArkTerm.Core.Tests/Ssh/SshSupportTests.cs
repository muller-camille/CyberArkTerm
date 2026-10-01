using CyberArkTerm.Core.Ssh;
using CyberArkTerm.Core.Terminal;

namespace CyberArkTerm.Core.Tests.Ssh;

public class SshSupportTests
{
    [Theory]
    [InlineData("/opt/appli", "logs", "/opt/appli/logs")]
    [InlineData("/opt/appli/", "../etc//./x", "/opt/etc/x")]
    [InlineData("/opt", "/var/log", "/var/log")]
    [InlineData("/", "..", "/")]
    public void CombinesRemotePaths(string dir, string name, string expected) => Assert.Equal(expected, RemotePath.Combine(dir, name));

    [Fact]
    public void ParentAndName()
    {
        Assert.Equal("/opt", RemotePath.Parent("/opt/appli/"));
        Assert.Equal("/", RemotePath.Parent("/opt"));
        Assert.Equal("/", RemotePath.Parent("/"));
        Assert.Equal("appli", RemotePath.Name("/opt/appli"));
    }

    [Theory]
    [InlineData(512, "512 o")]
    [InlineData(2048, "2 Ko")]
    [InlineData(2_400_000, "2,3 Mo")]
    [InlineData(5L * 1024 * 1024 * 1024, "5 Go")]
    public void FormatsSizes(long bytes, string expected) => Assert.Equal(expected, RemotePath.FormatSize(bytes));

    [Fact]
    public void SortsDirectoriesFirstAndFormatsPermissions()
    {
        RemoteEntry E(string name, bool dir) => new(name, "/" + name, dir, false, 0, DateTime.MinValue, "");

        var sorted = RemoteEntry.Sort([E("b.txt", false), E("Zeta", true), E("a.txt", false), E("alpha", true)]);

        Assert.Equal(["alpha", "Zeta", "a.txt", "b.txt"], sorted.Select(e => e.Name));
        Assert.Equal("drwxr-x---", RemoteEntry.FormatPermissions(true, false, true, true, true, true, false, true, false, false, false));
        Assert.Equal("lrw-r--r--", RemoteEntry.FormatPermissions(false, true, true, true, false, true, false, false, true, false, false));
    }

    [Fact]
    public void KnownHostsTrustOnFirstUseAndDetectChanges()
    {
        var store = new Dictionary<string, string>();

        Assert.Equal(HostKeyStatus.Unknown, KnownHosts.Check(store, "PSMP.corp", 22, "ssh-ed25519", "AAA"));
        KnownHosts.Remember(store, "PSMP.corp", 22, "ssh-ed25519", "AAA");
        Assert.Equal(HostKeyStatus.Trusted, KnownHosts.Check(store, "psmp.corp", 22, "ssh-ed25519", "AAA"));
        Assert.Equal(HostKeyStatus.Changed, KnownHosts.Check(store, "psmp.corp", 22, "ssh-ed25519", "BBB"));
        Assert.Equal(HostKeyStatus.Unknown, KnownHosts.Check(store, "psmp.corp", 2222, "ssh-ed25519", "AAA"));
    }

    [Fact]
    public void ParsesMfaKeyResponsesAndPrefersOpenSsh()
    {
        const string json = """
            {"count":2,"value":[
              {"format":"PPK","privateKey":"PuTTY-User-Key-File-3: ssh-rsa\nAAA","keyAlias":""},
              {"format":"OpenSSH","privateKey":"-----BEGIN OPENSSH PRIVATE KEY-----\nBBB\n-----END OPENSSH PRIVATE KEY-----"}
            ],"creationTime":1790000000,"expirationTime":1790003600}
            """;

        var key = MfaSshKey.Parse(json);

        Assert.NotNull(key);
        Assert.Equal("OpenSSH", key.Format);
        Assert.StartsWith("-----BEGIN OPENSSH", key.PrivateKey);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790003600), key.ExpiresAt);
        Assert.True(key.IsExpired(DateTimeOffset.FromUnixTimeSeconds(1790003590)));
        Assert.False(key.IsExpired(DateTimeOffset.FromUnixTimeSeconds(1790000000)));
    }

    [Theory]
    [InlineData("""{"value":"-----BEGIN RSA PRIVATE KEY-----\nX\n-----END RSA PRIVATE KEY-----"}""", true)]
    [InlineData("""{"value":[]}""", false)]
    [InlineData("not json", false)]
    public void ParsesOtherShapes(string json, bool found) => Assert.Equal(found, MfaSshKey.Parse(json) is not null);

    [Theory]
    [InlineData(20, 100)]
    [InlineData(0, 80)]
    [InlineData(95, 100)]
    [InlineData(10, 40)]
    public void InjectionErasesExactlyItsOwnEcho(int column, int columns)
    {
        var command = WorkingDirectory.InjectionFor(column, columns);
        int occupied = (column + command.Length - 1 - 1) / columns + 1;

        Assert.Contains($"\\033[{occupied}A", command);
    }

    [Theory]
    [InlineData("/opt/appli", "'/opt/appli'")]
    [InlineData("/home/o'brien/my dir", "'/home/o'\\''brien/my dir'")]
    [InlineData("/tmp/$(reboot)", "'/tmp/$(reboot)'")]
    public void ShellQuotesPaths(string path, string expected) => Assert.Equal(expected, WorkingDirectory.ShellQuote(path));

    [Fact]
    public void ShellQuoteRejectsControlCharacters() =>
        Assert.Throws<ArgumentException>(() => WorkingDirectory.ShellQuote("/tmp/a\nreboot"));

    [Fact]
    public void InjectionWithStartDirectoryChangesDirectoryFirstAndStillErasesItsEcho()
    {
        var command = WorkingDirectory.InjectionFor(20, 80, "/opt/appli/logs");
        int occupied = (20 + command.Length - 1 - 1) / 80 + 1;

        Assert.StartsWith(" cd -- '/opt/appli/logs' 2>/dev/null;", command);
        Assert.Contains($"\\033[{occupied}A", command);
        Assert.Equal(" cd -- '/srv/x y'\r", WorkingDirectory.ChangeDirectoryCommand("/srv/x y"));
    }

    [Theory]
    [InlineData("Password: ", true)]
    [InlineData("Mot de passe :", true)]
    [InlineData("Enter your one-time passcode:", false)]
    [InlineData("Verification code: ", false)]
    public void OnlyPasswordPromptsAreCached(string prompt, bool cached) => Assert.Equal(cached, PsmpConnector.IsPasswordPrompt(prompt));
}
