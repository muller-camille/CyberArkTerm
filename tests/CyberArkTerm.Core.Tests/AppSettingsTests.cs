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
    /// Applications distantes : dans l'onglet par défaut, et pas en bureau (un PSM l'a refusé) ; un choix enregistré
    /// est gardé.
    /// </summary>
    [Fact]
    public void PsmRemoteAppsOpenInSeparateWindowsByDefault()
    {
        Assert.True(new AppSettings().RemoteAppInTab);
        Assert.False(new AppSettings().PsmRemoteAppAsDesktop);
        Assert.False(AppSettings.Load(Path.Combine(_dir, "missing.json")).PsmRemoteAppAsDesktop);

        var path = Path.Combine(_dir, "settings.json");
        new AppSettings { PsmRemoteAppAsDesktop = true }.Save(path);
        Assert.True(AppSettings.Load(path).PsmRemoteAppAsDesktop);
    }
}
