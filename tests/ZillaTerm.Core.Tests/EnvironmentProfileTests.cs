using ZillaTerm.Core.Ssh;

namespace ZillaTerm.Core.Tests;

public sealed class EnvironmentProfileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "zillaterm-env-" + Guid.NewGuid().ToString("N"));

    public EnvironmentProfileTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static AppSettings Team()
    {
        var settings = new AppSettings
        {
            PvwaUrl = "pvwa.corp.com",
            UserName = "jdupont",
            AuthMethod = AuthMethod.LDAP,
            PsmpAddress = "psmp.corp.com",
            PsmpPort = 2222,
            WindowsComponent = "WIN-PSM",
            EnvironmentFile = @"\\srv\partage\zillaterm\prod.env.json",
        };
        settings.PsmpServers.Add(new PsmpServer { Address = "psmp.paris.corp.com", Port = 22 });
        settings.ComponentByPlatform["UnixSSH"] = "PSM-SSH";
        settings.SharedLists.Add(@"\\srv\partage\listes\prod.json");
        KnownHosts.Remember(settings.KnownHosts, "psmp.corp.com", 2222, "ssh-ed25519", "AAAA");
        KnownHosts.Remember(settings.KnownHosts, "ftp.perso.local", 990, "X.509", "BBBB");
        settings.Recent.Add(new RecentSession { AccountId = "1", PvwaHost = "pvwa.corp.com", Label = "root@srv" });
        settings.Sessions.Add(new SavedSession { AccountId = "1", Name = "root@srv" });
        return settings;
    }

    /// <summary>
    /// Export : réglages d'équipe seulement. Ni identifiant, ni « Mes serveurs », ni récents, ni clé d'un serveur qui n'est
    /// pas un PSMP configuré.
    /// </summary>
    [Fact]
    public void ExportKeepsTeamSettingsOnly()
    {
        var path = Path.Combine(_dir, EnvironmentProfile.FileName);
        EnvironmentProfile.FromSettings(Team(), "Production").Save(path);
        var json = File.ReadAllText(path);

        Assert.Contains("psmp.paris.corp.com", json);
        Assert.Contains("WIN-PSM", json);
        Assert.Contains("\"LDAP\"", json);
        Assert.DoesNotContain("jdupont", json);
        Assert.DoesNotContain("root@srv", json);
        Assert.DoesNotContain("ftp.perso.local", json);

        var loaded = EnvironmentProfile.Load(path);
        Assert.Equal("Production", loaded.Profile.Name);
        Assert.Equal(["psmp.corp.com:2222"], loaded.Profile.HostKeys!.Keys);
        Assert.Equal(64, loaded.Sha256.Length);
    }

    /// <summary>
    /// Nouveau poste : tout ce que porte l'environnement est montré puis appliqué ; adresses et clés marquées sensibles. Un
    /// second passage ne trouve plus rien à changer.
    /// </summary>
    [Fact]
    public void FreshInstallGetsTheWholeEnvironment()
    {
        var profile = EnvironmentProfile.FromSettings(Team(), "Production");
        var fresh = new AppSettings();

        var changes = profile.Diff(fresh);
        Assert.Contains(changes, c => c is { Setting: EnvironmentSetting.PvwaUrl, New: "pvwa.corp.com", Sensitive: true });
        Assert.Contains(changes, c => c is { Setting: EnvironmentSetting.DefaultPsmp, New: "psmp.corp.com:2222", Sensitive: true });
        Assert.Contains(changes, c => c is { Setting: EnvironmentSetting.PsmpServers, New: "psmp.paris.corp.com:22" });
        Assert.Contains(changes, c => c is { Setting: EnvironmentSetting.HostKey, Detail: "psmp.corp.com:2222", Ignored: false });
        Assert.Contains(changes, c => c is { Setting: EnvironmentSetting.PlatformComponent, Detail: "UnixSSH", New: "PSM-SSH" });
        Assert.Contains(changes, c => c is { Setting: EnvironmentSetting.CentralFile, Sensitive: true });
        Assert.DoesNotContain(changes, c => c.Setting == EnvironmentSetting.KeepPvwaSessionAlive);

        profile.ApplyTo(fresh);
        Assert.Equal(("pvwa.corp.com", AuthMethod.LDAP, "psmp.corp.com", 2222, "WIN-PSM"),
            (fresh.PvwaUrl, fresh.AuthMethod, fresh.PsmpAddress, fresh.PsmpPort, fresh.WindowsComponent));
        Assert.Equal("", fresh.UserName);
        Assert.Empty(fresh.Sessions);
        Assert.Equal("PSM-SSH", fresh.ComponentByPlatform["UnixSSH"]);
        Assert.Single(fresh.SharedLists);
        Assert.Empty(profile.Diff(fresh));
    }

    /// <summary>
    /// Une clé de serveur déjà acceptée n'est jamais remplacée par un fichier (le changement se vérifie à la connexion) ;
    /// les listes partagées s'ajoutent sans retirer celles de l'utilisateur.
    /// </summary>
    [Fact]
    public void KnownHostKeysAreNeverReplaced()
    {
        var user = Team();
        user.SharedLists.Add(@"\\srv\perso\mes-serveurs.json");
        var profile = new EnvironmentProfile
        {
            HostKeys = new() { ["PSMP.corp.com:2222"] = "ssh-ed25519 SHA256:ZZZZ", ["psmp.lyon.corp.com:22"] = "ssh-rsa SHA256:CCCC" },
            SharedLists = [@"\\srv\partage\listes\recette.json"],
        };

        var changes = profile.Diff(user);
        Assert.Contains(changes, c => c is { Setting: EnvironmentSetting.HostKey, Detail: "psmp.corp.com:2222", Ignored: true });
        Assert.Contains(changes, c => c is { Setting: EnvironmentSetting.HostKey, Detail: "psmp.lyon.corp.com:22", Ignored: false });

        profile.ApplyTo(user);
        Assert.Equal("ssh-ed25519 SHA256:AAAA", user.KnownHosts["psmp.corp.com:2222"]);
        Assert.Equal("ssh-rsa SHA256:CCCC", user.KnownHosts["psmp.lyon.corp.com:22"]);
        Assert.Equal(3, user.SharedLists.Count);
    }

    /// <summary>Exporté d'un poste sans PSMP ni composant Windows : n'efface pas ceux du poste qui l'importe.</summary>
    [Fact]
    public void EmptySettingsAreNotExported()
    {
        var profile = EnvironmentProfile.FromSettings(new AppSettings { PvwaUrl = "pvwa.corp.com", AuthMethod = AuthMethod.LDAP }, null);
        Assert.Equal((null, null, null, null), (profile.PsmpAddress, profile.PsmpPort, profile.PsmpServers, profile.WindowsComponent));

        var changes = profile.Diff(Team());
        Assert.DoesNotContain(changes, c => c.Setting is EnvironmentSetting.DefaultPsmp or EnvironmentSetting.PsmpServers
            or EnvironmentSetting.WindowsComponent or EnvironmentSetting.CentralFile);
    }

    /// <summary>Même PVWA écrit autrement : pas de changement.</summary>
    [Fact]
    public void SamePvwaWrittenDifferentlyIsNoChange()
    {
        var profile = new EnvironmentProfile { PvwaUrl = "https://PVWA.corp.com/PasswordVault/v10/logon" };
        Assert.Empty(profile.Diff(new AppSettings { PvwaUrl = "pvwa.corp.com" }));
    }

    [Theory]
    [InlineData("""{ "Format": 2 }""", EnvironmentProblem.UnsupportedFormat)]
    [InlineData("""{ "PvwaUrl": "http://pvwa.corp.com" }""", EnvironmentProblem.InvalidPvwa)]
    [InlineData("""{ "PsmpAddress": "psmp corp" }""", EnvironmentProblem.InvalidPsmpAddress)]
    [InlineData("""{ "PsmpPort": 70000 }""", EnvironmentProblem.InvalidPort)]
    [InlineData("""{ "PsmpServers": [ { "Address": "psmp" } ] }""", EnvironmentProblem.InvalidDomain)]
    [InlineData("""{ "PsmpAddress": "psmp.corp.com", "PsmpServers": [ { "Address": "psmp2.corp.com" } ] }""", EnvironmentProblem.DuplicateDomain)]
    [InlineData("""{ "WindowsComponent": "WIN PSM" }""", EnvironmentProblem.InvalidComponent)]
    [InlineData("""{ "ComponentByPlatform": { "WinDomain": "a;b" } }""", EnvironmentProblem.InvalidComponent)]
    [InlineData("""{ "HostKeys": { "psmp.corp.com": "ssh-ed25519 SHA256:AAAA" } }""", EnvironmentProblem.InvalidHostKey)]
    [InlineData("""{ "HostKeys": { "psmp.corp.com:22": "ssh-ed25519 AAAA" } }""", EnvironmentProblem.InvalidHostKey)]
    [InlineData("""{ "SharedLists": [ "listes\\prod.json" ] }""", EnvironmentProblem.InvalidPath)]
    [InlineData("""{ "CentralFile": "env.json" }""", EnvironmentProblem.InvalidPath)]
    [InlineData("""{ "CentralFile": "C:env.json" }""", EnvironmentProblem.InvalidPath)]
    [InlineData("""{ "SharedLists": [ null ] }""", EnvironmentProblem.InvalidPath)]
    [InlineData("""{ "AuthMethod": 1 }""", EnvironmentProblem.InvalidJson)]
    [InlineData("""{ "AuthMethod": "7" }""", EnvironmentProblem.InvalidJson)]
    [InlineData("""{ "AuthMethod": "LDAP, RADIUS" }""", EnvironmentProblem.InvalidJson)]
    [InlineData("""{ "UploadProtocol": "Ftp" }""", EnvironmentProblem.InvalidJson)]
    [InlineData("""{ "PvwaUrl": """, EnvironmentProblem.InvalidJson)]
    public void InvalidFilesAreRefused(string json, EnvironmentProblem problem)
    {
        var path = Path.Combine(_dir, "bad.env.json");
        File.WriteAllText(path, json);
        Assert.Equal(problem, Assert.Throws<EnvironmentFileException>(() => EnvironmentProfile.Load(path)).Problem);
    }

    /// <summary>Champs inconnus (un mot de passe ajouté à la main…) : ignorés, jamais repris dans les réglages.</summary>
    [Fact]
    public void UnknownFieldsAreIgnored()
    {
        var path = Path.Combine(_dir, "extra.env.json");
        File.WriteAllText(path, """{ "PvwaUrl": "pvwa.corp.com", "Password": "secret", "UserName": "admin" }""");
        var settings = new AppSettings { UserName = "jdupont" };

        EnvironmentProfile.Load(path).Profile.ApplyTo(settings);

        Assert.Equal(("pvwa.corp.com", "jdupont"), (settings.PvwaUrl, settings.UserName));
    }
}
