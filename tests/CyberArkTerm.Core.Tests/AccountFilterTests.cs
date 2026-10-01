using System.Text.Json;

namespace CyberArkTerm.Core.Tests;

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
}
