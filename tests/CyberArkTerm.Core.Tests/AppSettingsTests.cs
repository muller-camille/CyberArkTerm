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
    /// Connexions récentes : propres à chaque PVWA (le même ID de compte y désigne un autre compte) ; celles des versions
    /// qui ne notaient pas le PVWA sont oubliées au chargement.
    /// </summary>
    [Fact]
    public void RecentSessionsBelongToTheirPvwa()
    {
        var settings = new AppSettings();
        settings.AddRecent(new RecentSession { AccountId = "12_3", PvwaHost = "pvwa-a", Mode = "PSM-RDP" });
        settings.AddRecent(new RecentSession { AccountId = "12_3", PvwaHost = "pvwa-b", Mode = "PSM-RDP" });
        settings.AddRecent(new RecentSession { AccountId = "12_3", PvwaHost = "PVWA-A", Mode = "PSM-RDP" });
        Assert.Equal(["PVWA-A", "pvwa-b"], settings.Recent.Select(r => r.PvwaHost));
        Assert.True(settings.Recent[0].IsForHost("pvwa-a"));
        Assert.False(settings.Recent[1].IsForHost("pvwa-a"));

        var path = Path.Combine(Path.GetTempPath(), $"cat-recent-{Guid.NewGuid():N}.json");
        try
        {
            settings.Recent.Add(new RecentSession { AccountId = "7_1", Mode = "SSH" });
            settings.Save(path);
            Assert.Equal(2, AppSettings.Load(path).Recent.Count);
        }
        finally
        {
            File.Delete(path);
        }
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

    /// <summary>
    /// Fenêtre principale remise où elle était, sauf si elle tomberait hors des écrans actuels (écran débranché) ; une
    /// taille plus grande que l'écran est ramenée à l'écran, une taille trop petite au minimum.
    /// </summary>
    [Fact]
    public void WindowPlacementIsKeptOnlyWhenVisible()
    {
        var screen = (0d, 0d, 1920d, 1080d);
        var kept = new WindowPlacement(100, 80, 1280, 780, false).FitIn(screen, 820, 480);
        Assert.Equal(new WindowPlacement(100, 80, 1280, 780, false), kept);

        Assert.Null(new WindowPlacement(2500, 80, 1280, 780, false).FitIn(screen, 820, 480));
        Assert.Null(new WindowPlacement(100, 1200, 1280, 780, true).FitIn(screen, 820, 480));
        Assert.Null(new WindowPlacement(double.NaN, 0, 1280, 780, false).FitIn(screen, 820, 480));

        var resized = new WindowPlacement(0, 0, 4000, 100, true).FitIn(screen, 820, 480);
        Assert.Equal(new WindowPlacement(0, 0, 1920, 480, true), resized);
    }

    [Fact]
    public void LayoutIsSavedWithTheSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cat-layout-{Guid.NewGuid():N}.json");
        try
        {
            new AppSettings
            {
                SidePanelWidth = 455,
                SidePanelCollapsed = true,
                MainWindowPlacement = new WindowPlacement(10, 20, 1300, 800, true),
            }.Save(path);

            var loaded = AppSettings.Load(path);
            Assert.Equal(455, loaded.SidePanelWidth);
            Assert.True(loaded.SidePanelCollapsed);
            Assert.Equal(new WindowPlacement(10, 20, 1300, 800, true), loaded.MainWindowPlacement);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }
}
