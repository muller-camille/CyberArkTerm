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
    [InlineData("SFTP-Partners", AccountKind.Unix, "PSM-SSH")]
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

    /// <summary>
    /// Connexion par défaut d'après le nom de la plateforme : « SFTP » donne les fichiers seuls, « SSH » le terminal via
    /// le PSMP (même pour un équipement réseau), une autre cible Unix le terminal, le reste PSM ; sans PSMP, toujours PSM.
    /// </summary>
    [Theory]
    [InlineData("UnixSSH", ConnectMode.Ssh)]
    [InlineData("RHEL-Prod", ConnectMode.Ssh)]
    [InlineData("CiscoSSH", ConnectMode.Ssh)]
    [InlineData("UnixSFTP", ConnectMode.Sftp)]
    [InlineData("SFTP-Partners", ConnectMode.Sftp)]
    [InlineData("Unix-SSH-sftp-only", ConnectMode.Sftp)]
    [InlineData("WinServerLocal", ConnectMode.Psm)]
    [InlineData("Oracle", ConnectMode.Psm)]
    [InlineData("", ConnectMode.Psm)]
    public void DefaultModeFollowsThePlatformName(string platform, ConnectMode mode)
    {
        Assert.Equal(mode, AccountClassifier.DefaultMode(Account(platform), hasPsmp: true));
        Assert.Equal(ConnectMode.Psm, AccountClassifier.DefaultMode(Account(platform), hasPsmp: false));
    }

    [Fact]
    public void DomainAndRestrictedAccountsNeedRemoteMachine()
    {
        Assert.True(AccountClassifier.NeedsRemoteMachine(Account("WinDomain", address: "corp.local")));
        Assert.True(AccountClassifier.NeedsRemoteMachine(Account("WinServerLocal", remoteMachines: "a;b")));
        Assert.False(AccountClassifier.NeedsRemoteMachine(Account("WinServerLocal")));
        Assert.Equal(["jump01", "jump02", "jump03"], AccountClassifier.RemoteMachineList(Account("X", remoteMachines: " jump01; jump02 ,jump03;")));
    }

    /// <summary>
    /// Compte enregistré pour son domaine (adresse = le domaine de connexion) : il faut choisir le serveur, quel que soit
    /// le nom de la plateforme. Un compte local, ou un compte enregistré pour un serveur, s'ouvre directement.
    /// </summary>
    [Theory]
    [InlineData("WIN-ADMINS-T1", "corp.local", "CORP", true)]
    [InlineData("WIN-ADMINS-T1", "corp.example.com", "CORP", true)]
    [InlineData("WIN-ADMINS-T1", "corp.local", "corp.local", true)]
    [InlineData("WIN-ADMINS-T1", "CORP", "CORP", true)]
    [InlineData("AD-Admins-T1", "corp.example.com", "CORP", true)]
    [InlineData("UnixSSH", "corp.local", "CORP", true)]
    [InlineData("WIN-ADMINS-T1", "srv01.corp.local", "CORP", false)]
    [InlineData("WIN-ADMINS-T1", "corp.local", null, false)]
    [InlineData("WIN-ADMINS-T1", "10.0.0.1", "CORP", false)]
    [InlineData("WinServerLocal", "srv01", "SRV01", false)]
    [InlineData("WinServerLocal", "srv01.corp.local", "SRV01", false)]
    [InlineData("UnixSSH", "corp.local", null, false)]
    public void AccountRegisteredForItsDomainNeedsRemoteMachine(string platform, string address, string? domain, bool expected) =>
        Assert.Equal(expected, AccountClassifier.NeedsRemoteMachine(Account(platform, address: address, domain: domain)));

    /// <summary>
    /// Sans domaine de connexion renseigné : une adresse sous laquelle il y a des serveurs, ou le domaine du PVWA ou du
    /// poste, est un domaine. Un compte Unix ou base de données garde son adresse (il vise un serveur).
    /// </summary>
    [Fact]
    public void AddressWithServersBelowIsADomain()
    {
        var domains = KnownDomains.From(["srv01.paris.corp.example.com", "db01.corp.example.com", "10.1.2.3", "pvwa.vault.example.org"], ["LAB", "lab.example.net"]);

        Assert.True(AccountClassifier.NeedsRemoteMachine(Account("AD-Admins-T1", address: "corp.example.com"), domains));
        Assert.True(AccountClassifier.NeedsRemoteMachine(Account("Windows-Admins", address: "Paris.Corp.Example.com."), domains));
        Assert.True(AccountClassifier.NeedsRemoteMachine(Account("Windows-Admins", address: "vault.example.org"), domains));
        Assert.True(AccountClassifier.NeedsRemoteMachine(Account("Windows-Admins", address: "lab"), domains));
        Assert.True(AccountClassifier.NeedsRemoteMachine(Account("Windows-Admins", address: "lab.example.net"), domains));
        Assert.False(AccountClassifier.NeedsRemoteMachine(Account("Windows-Admins", address: "srv01.paris.corp.example.com"), domains));
        Assert.False(AccountClassifier.NeedsRemoteMachine(Account("Windows-Admins", address: "com"), domains));
        Assert.False(AccountClassifier.NeedsRemoteMachine(Account("UnixSSH", address: "corp.example.com"), domains));
        Assert.False(AccountClassifier.NeedsRemoteMachine(Account("Oracle", address: "corp.example.com"), domains));
        Assert.False(AccountClassifier.NeedsRemoteMachine(Account("WinServerLocal", address: "corp.example.com"), domains));
    }

    [Fact]
    public void RestrictedOnlyWithAllowedMachines()
    {
        var account = Account("WinDomain", remoteMachines: "jump01;jump02");
        Assert.False(AccountClassifier.IsRestrictedToRemoteMachines(account));
        account.RemoteMachinesAccess!.AccessRestrictedToRemoteMachines = true;
        Assert.True(AccountClassifier.IsRestrictedToRemoteMachines(account));

        // Restreint sans machine listée : rien à proposer, le choix reste libre.
        var none = Account("WinDomain", remoteMachines: " ; ");
        none.RemoteMachinesAccess!.AccessRestrictedToRemoteMachines = true;
        Assert.False(AccountClassifier.IsRestrictedToRemoteMachines(none));
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

    [Fact]
    public void PsmpLogin_RejectsSpacesInDomainAndRemoteMachine()
    {
        Assert.Throws<ArgumentException>(() => PsmpTarget.BuildLogin("jdupont", Account("WinDomain", "adm", "corp.local", domain: "MY CORP")));
        Assert.Throws<ArgumentException>(() => PsmpTarget.BuildLogin("jdupont", Account("WinDomain", "adm", "corp.local"), "srv 02"));
    }

    [Fact]
    public void Validate_RejectsSpacesUnlessAllowed()
    {
        Assert.Throws<ArgumentException>(() => PsmpTarget.Validate("psmp host", "x"));
        PsmpTarget.Validate("Jean Dupont@root@srv01", "x", allowSpaces: true);
        Assert.Throws<ArgumentException>(() => PsmpTarget.Validate("Jean\u00a0Dupont@root@srv01", "x", allowSpaces: true));
    }

    [Theory]
    [InlineData("Jean Dupont", "root", "Jean Dupont@root@srv01.corp.local")]
    [InlineData("jdupont", "Admin Local", "jdupont@Admin Local@srv01.corp.local")]
    [InlineData("  jdupont ", " root ", "jdupont@root@srv01.corp.local")]
    public void PsmpLogin_AcceptsSpacesInUserNames(string vaultUser, string user, string expected) =>
        Assert.Equal(expected, PsmpTarget.BuildLogin(vaultUser, Account("UnixSSH", user)));

    [Theory]
    [InlineData("jdupont", "root;calc", "srv01")]
    [InlineData("jdupont", "root\tadmin", "srv01")]
    [InlineData("jdupont", "root", "srv 01")]
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

    /// <summary>
    /// Composant des comptes Windows (paramètre) : pour les comptes Windows, domaine ou locaux, sans composant mémorisé
    /// pour leur plateforme ; les autres comptes gardent le leur, un composant mémorisé reste prioritaire.
    /// </summary>
    [Fact]
    public void WindowsComponentAppliesToWindowsAccountsWithoutRememberedComponent()
    {
        var settings = new AppSettings { WindowsComponent = " WIN-PSM " };

        Assert.Equal("WIN-PSM", settings.ResolveComponent(Account("WinDomain", address: "corp.local")));
        Assert.Equal("WIN-PSM", settings.ResolveComponent(Account("WinServerLocal")));
        Assert.Equal("PSM-SSH", settings.ResolveComponent(Account("UnixSSH")));
        Assert.Equal("PSM-SQLPlus", settings.ResolveComponent(Account("Oracle")));
        Assert.Equal("WIN-PSM", settings.KnownComponents("WinDomain")[0]);

        settings.RememberComponent("WinServerLocal", "PSM-RDP-Local");
        Assert.Equal("PSM-RDP-Local", settings.ResolveComponent(Account("WinServerLocal")));
        Assert.Equal("PSM-RDP", new AppSettings().ResolveComponent(Account("WinDomain")));
    }

    [Theory]
    [InlineData("WIN-PSM", true)]
    [InlineData("PSM_RDP.2", true)]
    [InlineData(" PSM-RDP ", true)]
    [InlineData("PSM RDP", false)]
    [InlineData("PSM;calc", false)]
    [InlineData("", false)]
    public void ComponentNames(string name, bool valid) => Assert.Equal(valid, AppSettings.IsValidComponentName(name));

    [Fact]
    public void Settings_RecentAndRememberedComponents()
    {
        var settings = new AppSettings();
        var account = Account("UnixSSH");

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
