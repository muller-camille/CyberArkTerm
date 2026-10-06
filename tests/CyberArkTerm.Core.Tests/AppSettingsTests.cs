namespace CyberArkTerm.Core.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cyberarkterm-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = Path.Combine(_dir, "sub", "settings.json");
        new AppSettings { PvwaUrl = "https://pvwa", UserName = "jdoe", AuthMethod = AuthMethod.LDAP, Language = "it" }.Save(path);

        var loaded = AppSettings.Load(path);

        Assert.Equal("it", loaded.Language);

        Assert.Equal("https://pvwa", loaded.PvwaUrl);
        Assert.Equal("jdoe", loaded.UserName);
        Assert.Equal(AuthMethod.LDAP, loaded.AuthMethod);
        Assert.DoesNotContain("password", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Envois : SFTP d'abord, y compris pour une installation existante dont l'ancienne clé valait SCP (défaut de
    /// l'époque) ; un choix fait avec la nouvelle clé est gardé.
    /// </summary>
    [Fact]
    public void Load_UploadsPreferSftp_ExceptWhenScpChosenWithTheNewKey()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "PvwaUrl": "https://pvwa", "UploadProtocol": 0 }""");
        Assert.Equal(Core.Ssh.TransferProtocol.Sftp, AppSettings.Load(path).PreferredUploadProtocol);
        Assert.Equal(Core.Ssh.TransferProtocol.Sftp, new AppSettings().PreferredUploadProtocol);

        new AppSettings { PreferredUploadProtocol = Core.Ssh.TransferProtocol.Scp }.Save(path);
        Assert.Equal(Core.Ssh.TransferProtocol.Scp, AppSettings.Load(path).PreferredUploadProtocol);
    }

    [Fact]
    public void Load_MissingOrCorruptFile_ReturnsDefaults()
    {
        Assert.Equal("", AppSettings.Load(Path.Combine(_dir, "missing.json")).PvwaUrl);

        Directory.CreateDirectory(_dir);
        var corrupt = Path.Combine(_dir, "corrupt.json");
        File.WriteAllText(corrupt, "{ not json");
        Assert.Equal(AuthMethod.CyberArk, AppSettings.Load(corrupt).AuthMethod);
    }

    /// <summary>
    /// Composants proposés : celui mémorisé pour la plateforme d'abord, puis ceux déjà utilisés (même absents de la liste
    /// usuelle, comme WIN-PSM), puis les usuels, sans doublon ni connexion SSH.
    /// </summary>
    [Fact]
    public void KnownComponentsPutThePlatformAndUsedOnesFirst()
    {
        var settings = new AppSettings();
        settings.RememberComponent("WinDomain", "WIN-PSM");
        settings.RememberComponent("UnixSSH", "PSM-SSH");
        settings.Sessions.Add(new SavedSession { Mode = ConnectMode.Psm, Component = " PSM-RDP-Custom " });
        settings.Sessions.Add(new SavedSession { Mode = ConnectMode.Ssh, Component = "Ignored" });
        settings.AddRecent(new RecentSession { AccountId = "1", Mode = "SSH" });
        settings.AddRecent(new RecentSession { AccountId = "2", Mode = "win-psm" });
        settings.AddRecent(new RecentSession { AccountId = "3", Mode = "PSM-WebApp" });

        var components = settings.KnownComponents("windomain");

        Assert.Equal(["WIN-PSM", "PSM-SSH", "PSM-RDP-Custom", "PSM-WebApp"], components.Take(4));
        Assert.Contains("PSM-RDP", components);
        Assert.DoesNotContain("SSH", components);
        Assert.DoesNotContain("Ignored", components);
        Assert.Equal(components.Count, components.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(AccountClassifier.CommonComponents, new AppSettings().KnownComponents("WinDomain"));
    }
}
