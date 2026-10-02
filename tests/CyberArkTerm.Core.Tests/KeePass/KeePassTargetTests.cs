using CyberArkTerm.Core.KeePass;

namespace CyberArkTerm.Core.Tests.KeePass;

public class KeePassTargetTests
{
    private static KeePassEntry Entry(string title = "x y", string url = "", string user = "root", string tags = "",
        Dictionary<string, string>? fields = null) =>
        new() { Id = "id", Title = title, Url = url, UserName = user, Tags = tags, CustomFields = fields ?? [] };

    [Theory]
    [InlineData("ssh://srv-lnx01.corp.local:2222", RemoteProtocol.Ssh, "srv-lnx01.corp.local", 2222)]
    [InlineData("ssh://root@srv-lnx01", RemoteProtocol.Ssh, "srv-lnx01", 22)]
    [InlineData("sftp://10.0.0.5/var/tmp", RemoteProtocol.Ssh, "10.0.0.5", 22)]
    [InlineData("rdp://srv-win01.corp.local", RemoteProtocol.Rdp, "srv-win01.corp.local", 3389)]
    [InlineData("rdp://[fe80::1]:3390", RemoteProtocol.Rdp, "fe80::1", 3390)]
    [InlineData("srv-win02:3389", RemoteProtocol.Rdp, "srv-win02", 3389)]
    [InlineData("srv-lnx02:22", RemoteProtocol.Ssh, "srv-lnx02", 22)]
    [InlineData("srv-x", RemoteProtocol.Unknown, "srv-x", 22)]
    public void ReadsProtocolHostAndPortFromUrl(string url, RemoteProtocol protocol, string host, int port)
    {
        var target = KeePassTarget.From(Entry(url: url));

        Assert.Equal((protocol, host, port), (target.Protocol, target.Host, target.Port));
    }

    [Fact]
    public void UsesCustomFieldsAndTags()
    {
        var fields = new Dictionary<string, string> { ["Host"] = "10.1.1.1", ["Port"] = "2200" };

        var target = KeePassTarget.From(Entry(tags: "prod;ssh", fields: fields));

        Assert.Equal((RemoteProtocol.Ssh, "10.1.1.1", 2200), (target.Protocol, target.Host, target.Port));
    }

    [Fact]
    public void ProtocolFieldWinsOverUrlScheme()
    {
        var target = KeePassTarget.From(Entry(url: "https://srv-win03", fields: new() { ["Protocol"] = "RDP" }));

        Assert.Equal((RemoteProtocol.Rdp, "srv-win03", 3389), (target.Protocol, target.Host, target.Port));
    }

    [Fact]
    public void FallsBackToTitleWhenItIsAHostName()
    {
        Assert.Equal("srv-db01.corp.local", KeePassTarget.From(Entry(title: "srv-db01.corp.local")).Host);
        Assert.Equal("", KeePassTarget.From(Entry(title: "Serveur de base de données")).Host);
    }

    [Fact]
    public void ChangingProtocolMovesImplicitPort()
    {
        var target = KeePassTarget.From(Entry(url: "srv-x"));

        Assert.Equal(3389, target.WithProtocol(RemoteProtocol.Rdp).Port);
        Assert.Equal(2222, (target with { Port = 2222 }).WithProtocol(RemoteProtocol.Rdp).Port);
    }

    [Fact]
    public void AddressShowsNonDefaultPort()
    {
        Assert.Equal("srv:2222", new KeePassTarget(RemoteProtocol.Ssh, "srv", 2222, "u").Address);
        Assert.Equal("srv", new KeePassTarget(RemoteProtocol.Rdp, "srv", 3389, "u").Address);
    }
}
