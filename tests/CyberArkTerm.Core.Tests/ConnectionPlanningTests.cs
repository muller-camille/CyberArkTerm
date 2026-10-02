using System.Text.Json;

namespace CyberArkTerm.Core.Tests;

public class ConnectionPlanningTests
{
    private static PvwaAccount Account(string platform, string user = "admin", string address = "srv01.corp.local",
        string? domain = null, string? remoteMachines = null) => new()
    {
        Id = "1_1",
        PlatformId = platform,
        UserName = user,
        Address = address,
        PlatformAccountProperties = domain is null ? null : new() { ["LogonDomain"] = JsonSerializer.SerializeToElement(domain) },
        RemoteMachinesAccess = remoteMachines is null ? null : new() { RemoteMachines = remoteMachines },
    };

    [Theory]
    [InlineData("WinServerLocal", AccountKind.Windows, "PSM-RDP")]
    [InlineData("WinDomain", AccountKind.Windows, "PSM-RDP")]
    [InlineData("WinDesktopLocal", AccountKind.Windows, "PSM-RDP")]
    [InlineData("UnixSSH", AccountKind.Unix, "PSM-SSH")]
    [InlineData("UnixSSHKeys", AccountKind.Unix, "PSM-SSH")]
    [InlineData("RHEL-Prod", AccountKind.Unix, "PSM-SSH")]
    [InlineData("CiscoSSH", AccountKind.Network, "PSM-SSH")]
    [InlineData("Oracle", AccountKind.Database, "PSM-SQLPlus")]
    [InlineData("MSSql", AccountKind.Database, "PSM-SQLServerMgmtStudio")]
    [InlineData("MySQL", AccountKind.Database, "PSM-RDP")]
    [InlineData("AWSAccessKeys", AccountKind.Other, "PSM-RDP")]
    [InlineData("", AccountKind.Other, "PSM-RDP")]
    public void ClassifiesPlatformAndPicksDefaultComponent(string platform, AccountKind kind, string component)
    {
        var account = Account(platform);

        Assert.Equal(kind, AccountClassifier.Classify(account));
        Assert.Equal(component, AccountClassifier.DefaultComponent(account));
    }

    [Fact]
    public void DomainAndRestrictedAccountsNeedRemoteMachine()
    {
        Assert.True(AccountClassifier.NeedsRemoteMachine(Account("WinDomain", address: "corp.local")));
        Assert.True(AccountClassifier.NeedsRemoteMachine(Account("WinServerLocal", remoteMachines: "a;b")));
        Assert.False(AccountClassifier.NeedsRemoteMachine(Account("WinServerLocal")));
        Assert.Equal(["jump01", "jump02", "jump03"], AccountClassifier.RemoteMachineList(Account("X", remoteMachines: " jump01; jump02 ,jump03;")));
    }

    [Fact]
    public void PsmpLogin_UsesVaultUserTargetUserAndAddress()
    {
        Assert.Equal("jdupont@root@srv01.corp.local", PsmpTarget.BuildLogin("jdupont", Account("UnixSSH", "root")));
    }

    [Fact]
    public void PsmpLogin_AddsDomainAndRemoteMachine()
    {
        var account = Account("WinDomain", "adm-t0", "corp.local", domain: "CORP");

        Assert.Equal("jdupont@adm-t0#CORP@srv02", PsmpTarget.BuildLogin("jdupont", account, "srv02"));
    }

    [Theory]
    [InlineData("jdupont", "root;calc", "srv01")]
    [InlineData("jdupont", "root", "srv01 -oProxyCommand=x")]
    [InlineData("j\"dupont", "root", "srv01")]
    [InlineData("", "root", "srv01")]
    [InlineData("jdupont", "", "srv01")]
    public void PsmpLogin_RejectsUnsafeOrMissingValues(string vaultUser, string user, string address)
    {
        Assert.Throws<ArgumentException>(() => PsmpTarget.BuildLogin(vaultUser, Account("UnixSSH", user, address)));
    }

    [Fact]
    public void Grouping_SortsGroupsAndAccounts()
    {
        using var _ = UiCulture.Use("fr-FR");
        PvwaAccount[] accounts =
        [
            new() { Id = "1", SafeName = "b-safe", Address = "srv2", PlatformId = "UnixSSH" },
            new() { Id = "2", SafeName = "A-Safe", Address = "srv9", PlatformId = "WinDomain" },
            new() { Id = "3", SafeName = "B-SAFE", Address = "srv1", PlatformId = "UnixSSH" },
            new() { Id = "4", SafeName = null, Address = "srv3", PlatformId = "Oracle" },
        ];

        var bySafe = AccountGrouping.Group(accounts, GroupBy.Safe);
        Assert.Equal(["(non renseigné)", "A-Safe", "b-safe"], bySafe.Select(g => g.Name));
        Assert.Equal(["3", "1"], bySafe[2].Accounts.Select(a => a.Id));

        var byKind = AccountGrouping.Group(accounts, GroupBy.Kind);
        Assert.Equal(["Bases de données", "Unix / Linux", "Windows"], byKind.Select(g => g.Name));
    }

    [Theory]
    [InlineData("en-US", "(not set)", "Databases")]
    [InlineData("it-IT", "(non specificato)", "Database")]
    public void Grouping_NamesFollowInterfaceLanguage(string culture, string notSet, string databases)
    {
        using var _ = UiCulture.Use(culture);
        PvwaAccount[] accounts = [new() { Id = "1", SafeName = null, Address = "db1", PlatformId = "Oracle" }];

        Assert.Equal(notSet, AccountGrouping.Group(accounts, GroupBy.Safe).Single().Name);
        Assert.Equal(databases, AccountGrouping.Group(accounts, GroupBy.Kind).Single().Name);
    }

    [Fact]
    public void Settings_FavoritesRecentAndRememberedComponents()
    {
        var settings = new AppSettings();
        var account = Account("UnixSSH");

        settings.ToggleFavorite("1_1");
        Assert.True(settings.IsFavorite("1_1"));
        settings.ToggleFavorite("1_1");
        Assert.False(settings.IsFavorite("1_1"));

        Assert.Equal("PSM-SSH", settings.ResolveComponent(account));
        settings.RememberComponent("unixssh", "PSM-WinSCP");
        settings.RememberComponent("UNIXSSH", "PSM-Telnet");
        Assert.Equal("PSM-Telnet", settings.ResolveComponent(account));
        Assert.Single(settings.ComponentByPlatform);

        for (int i = 0; i < 20; i++)
        {
            settings.AddRecent(new RecentSession { AccountId = $"id{i}", Mode = "PSM-RDP" });
        }

        settings.AddRecent(new RecentSession { AccountId = "id10", Mode = "PSM-RDP" });
        Assert.Equal(AppSettings.MaxRecent, settings.Recent.Count);
        Assert.Equal("id10", settings.Recent[0].AccountId);
        Assert.Single(settings.Recent, r => r.AccountId == "id10");
    }
}
