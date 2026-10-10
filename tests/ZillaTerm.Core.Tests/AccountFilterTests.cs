using System.Text.Json;

namespace ZillaTerm.Core.Tests;

public class AccountFilterTests
{
    private static readonly PvwaAccount Account = new()
    {
        Address = "srv-sql01.corp.local",
        UserName = "svc_sql",
        Name = "Operating System-WinServerLocal-srv-sql01-svc_sql",
        SafeName = "PROD-SQL",
        PlatformId = "WinServerLocal",
        PlatformAccountProperties = new() { ["LogonDomain"] = JsonSerializer.SerializeToElement("CORP") },
        RemoteMachinesAccess = new() { RemoteMachines = "jump01;jump02" },
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sql01")]
    [InlineData("SVC_SQL")]
    [InlineData("prod-sql")]
    [InlineData("corp")]
    [InlineData("jump02")]
    [InlineData("WinServer sql01")]
    [InlineData("  sql01   prod  ")]
    public void Matches(string? query) => Assert.True(AccountFilter.Matches(Account, query));

    [Theory]
    [InlineData("oracle")]
    [InlineData("sql01 oracle")]
    public void DoesNotMatch(string query) => Assert.False(AccountFilter.Matches(Account, query));

    [Fact]
    public void HandlesAccountWithOnlyId() => Assert.False(AccountFilter.Matches(new PvwaAccount { Id = "1_1" }, "x"));

    /// <summary>« Disponibles » : le champ caché qui fait correspondre le compte est dit, pas ce qui se voit déjà.</summary>
    [Theory]
    [InlineData("jump02", "machine jump02")]
    [InlineData("JUMP", "machine jump01")]
    [InlineData("prod-sql", "safe PROD-SQL")]
    [InlineData("winserver", "plateforme WinServerLocal")]
    [InlineData("operating", "nom Operating System-WinServerLocal-srv-sql01-svc_sql")]
    [InlineData("sql01 jump02 prod-sql", "machine jump02|safe PROD-SQL")]
    [InlineData("svc_sql corp", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void HiddenMatches_SayWhichHiddenFieldMatched(string? query, string expected)
    {
        using var _ = UiCulture.Use("fr-FR");

        var found = AccountFilter.HiddenMatches(Account, query, "svc_sql@srv-sql01.corp.local");

        Assert.Equal(expected, string.Join("|", found));
    }

    [Fact]
    public void HiddenMatches_IgnoreWhatTheFolderAlreadyShows()
    {
        using var _ = UiCulture.Use("fr-FR");

        Assert.Empty(AccountFilter.HiddenMatches(Account, "prod-sql", "svc_sql@srv-sql01.corp.local PROD-SQL"));
    }

    /// <summary>Comptes affichés sous le même nom dans un dossier : ce qui les distingue, et rien pour les autres.</summary>
    [Fact]
    public void Distinctions_TellAccountsWithTheSameNameApart()
    {
        using var _ = UiCulture.Use("fr-FR");
        PvwaAccount Admin(string id, string machines, string platform = "WinDomain", string name = "") =>
            new() { Id = id, UserName = "adm-t0", Address = "corp.local", PlatformId = platform, SafeName = "T0-ADMINS", Name = name,
                RemoteMachinesAccess = new() { RemoteMachines = machines } };
        var jump = Admin("1", "srv01;srv02;srv03;srv04");
        var app = Admin("2", "srv05");
        var any = Admin("3", "");
        var alone = new PvwaAccount { Id = "4", UserName = "root", Address = "srv01.corp.local", PlatformId = "UnixSSH" };

        var byMachines = AccountGrouping.Distinctions([jump, app, any, alone]);

        Assert.Equal("→ srv01, srv02 (+2)", byMachines[jump]);
        Assert.Equal("→ srv05", byMachines[app]);
        Assert.Equal("→ toutes machines", byMachines[any]);
        Assert.False(byMachines.ContainsKey(alone));

        var byPlatform = AccountGrouping.Distinctions([Admin("5", "", "WinDomain"), Admin("6", "", "WinDomainT0")]);
        Assert.Equal(["plateforme WinDomain", "plateforme WinDomainT0"], byPlatform.Values.Order(StringComparer.Ordinal));

        var byName = AccountGrouping.Distinctions([Admin("7", "", name: "a"), Admin("8", "", name: "b")]);
        Assert.Equal(["nom a", "nom b"], byName.Values.Order(StringComparer.Ordinal));

        // Identiques sur tout ce qui s'affiche : rien à dire.
        Assert.Empty(AccountGrouping.Distinctions([Admin("9", ""), Admin("10", "")]));
    }
}
