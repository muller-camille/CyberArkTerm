using System.Text.Json;

namespace CyberArkTerm.Core.Tests;

public sealed class AccountChangesTests
{
    private static PvwaAccount Account(string? domain = null, string? machines = null, bool automatic = true, string? reason = null) =>
        JsonSerializer.Deserialize<PvwaAccount>(JsonSerializer.Serialize(new
        {
            id = "1_2",
            name = "Op-srv01",
            address = "srv01.corp.local",
            userName = "svc_app",
            platformId = "WinDomain",
            safeName = "Prod",
            platformAccountProperties = domain is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["LogonDomain"] = domain },
            remoteMachinesAccess = new { remoteMachines = machines, accessRestrictedToRemoteMachines = machines is not null },
            secretManagement = new { automaticManagementEnabled = automatic, manualManagementReason = reason },
        }), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static AccountEdit Same(PvwaAccount a) => new(a.Name!, a.Address!, a.UserName!, a.PlatformId!, a.LogonDomain, a.RemoteMachines,
        a.SecretManagement!.AutomaticManagementEnabled, a.SecretManagement.ManualManagementReason ?? "");

    [Fact]
    public void NothingChangedMeansNoOperation()
    {
        var account = Account("CORP", "srv01;srv02");

        Assert.Empty(AccountChanges.Diff(account, Same(account) with { Address = " srv01.corp.local ", RemoteMachines = "srv01, srv02" }));
    }

    [Fact]
    public void ChangedFieldsAreReplaced()
    {
        var account = Account("CORP");

        var ops = AccountChanges.Diff(account, Same(account) with { Address = "srv02.corp.local", PlatformId = "WinServerLocal", LogonDomain = "PROD" });

        Assert.Equal(
            [new PatchOperation("replace", "/address", "srv02.corp.local"),
             new PatchOperation("replace", "/platformId", "WinServerLocal"),
             new PatchOperation("replace", "/platformAccountProperties/LogonDomain", "PROD")],
            ops);
    }

    /// <summary>Propriété jusque-là vide : « add » ; vidée : « remove » ; nom vidé : ignoré.</summary>
    [Fact]
    public void EmptyValuesAreAddedOrRemoved()
    {
        var withDomain = Account("CORP", "srv01");
        var bare = Account();

        var removed = AccountChanges.Diff(withDomain, Same(withDomain) with { LogonDomain = " ", RemoteMachines = "", Name = "" });
        var added = AccountChanges.Diff(bare, Same(bare) with { LogonDomain = "CORP", RemoteMachines = "srv01;srv02" });

        Assert.Equal(
            [new PatchOperation("remove", "/platformAccountProperties/LogonDomain"),
             new PatchOperation("remove", "/remoteMachinesAccess/remoteMachines"),
             new PatchOperation("replace", "/remoteMachinesAccess/accessRestrictedToRemoteMachines", false)],
            removed);
        Assert.Equal(
            [new PatchOperation("add", "/platformAccountProperties/LogonDomain", "CORP"),
             new PatchOperation("add", "/remoteMachinesAccess/remoteMachines", "srv01;srv02"),
             new PatchOperation("replace", "/remoteMachinesAccess/accessRestrictedToRemoteMachines", true)],
            added);
    }

    [Fact]
    public void ManualManagementSendsItsReason()
    {
        var account = Account();

        var ops = AccountChanges.Diff(account, Same(account) with { AutomaticManagement = false, ManualManagementReason = "Compte applicatif" });
        var back = AccountChanges.Diff(Account(automatic: false, reason: "x"), Same(account));

        Assert.Equal(
            [new PatchOperation("replace", "/secretManagement/automaticManagementEnabled", false),
             new PatchOperation("add", "/secretManagement/manualManagementReason", "Compte applicatif")],
            ops);
        Assert.Equal([new PatchOperation("replace", "/secretManagement/automaticManagementEnabled", true)], back);
    }

    [Fact]
    public void CpmStatusAndDatesAreRead()
    {
        var account = JsonSerializer.Deserialize<PvwaAccount>("""
            {"id":"1","secretManagement":{"automaticManagementEnabled":true,"status":"failure","lastModifiedTime":1767225600,"lastVerifiedTime":0}}
            """, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.True(account.SecretManagement!.Failed);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToLocalTime(), account.SecretManagement.LastModified);
        Assert.Null(account.SecretManagement.LastVerified);
        Assert.False(new SecretManagement { Status = "success" }.Failed);
    }
}
