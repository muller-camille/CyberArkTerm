using System.Net;
using System.Text;
using System.Text.Json;

namespace CyberArkTerm.Core.Tests;

public class PsmConnectTests
{
    private const string Rdp = "full address:s:psm.corp.local\r\nusername:s:jdoe\r\nalternate shell:s:psm /u admin /a srv01 /c PSM-RDP\r\n";

    private static FakePvwa Pvwa(Func<HttpRequestMessage, HttpResponseMessage> psmConnect) =>
        new(req => FakePvwa.IsLogon(req) ? FakePvwa.Json("\"tok\"") : psmConnect(req));

    private static HttpResponseMessage RdpResponse(byte[] body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) { Headers = { { "Content-Type", "application/octet-stream" } } } };

    /// <summary>
    /// Composants de la plateforme : la plateforme cible est retrouvée par son nom, puis ses composants activés sont lus
    /// dans leur ordre (ceux du bouton « Connect » du PVWA) ; une plateforme inconnue n'en a pas.
    /// </summary>
    [Fact]
    public async Task PlatformComponents_ListsEnabledConnectorsOfThePlatform()
    {
        var pvwa = Pvwa(req => req.RequestUri!.AbsolutePath switch
        {
            "/PasswordVault/API/Platforms/Targets" => FakePvwa.Json("""
                {"Total":2,"Platforms":[{"ID":6,"PlatformID":"WinDomain","Name":"Windows Domain"},
                                        {"ID":40,"PlatformID":"WIN-ADM-T1","Name":"Admins T1","PrivilegedSessionManagement":{"PSMServerId":"PSM01"}}]}
                """),
            "/PasswordVault/API/Platforms/Targets/40/PrivilegedSessionManagement" => FakePvwa.Json("""
                {"PSMServerId":"PSM01","PSMConnectors":[{"PSMConnectorID":"PSM-RDP","Enabled":false},
                  {"PSMConnectorID":"WIN-PSM","Enabled":true},{"PSMConnectorID":"PSM-WinSCP"},{"PSMConnectorID":" ","Enabled":true}]}
                """),
            _ => FakePvwa.Json("{}", HttpStatusCode.NotFound),
        });
        using var client = await pvwa.CreateLoggedOnClientAsync();

        Assert.Equal(["WIN-PSM", "PSM-WinSCP"], await client.GetPlatformConnectionComponentsAsync("win-adm-t1"));
        Assert.Equal("/PasswordVault/API/Platforms/Targets", pvwa.Requests[1].PathAndQuery);
        Assert.Equal("/PasswordVault/API/Platforms/Targets/40/PrivilegedSessionManagement", pvwa.Requests[2].PathAndQuery);
        Assert.Empty(await client.GetPlatformConnectionComponentsAsync("UnixSSH"));
    }

    /// <summary>Lecture des plateformes réservée (droit de gestion des plateformes) : refus du PVWA, à traiter par l'appelant.</summary>
    [Fact]
    public async Task PlatformComponents_RefusalIsReported()
    {
        var pvwa = Pvwa(_ => FakePvwa.Json("""{"ErrorCode":"PASWS041E","ErrorMessage":"You are not authorized to perform this action."}""",
            HttpStatusCode.Forbidden));
        using var client = await pvwa.CreateLoggedOnClientAsync();

        var ex = await Assert.ThrowsAsync<PvwaException>(() => client.GetPlatformConnectionComponentsAsync("WinDomain"));
        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    [Fact]
    public async Task PsmConnect_PostsComponentAndAsksForRdpFile()
    {
        var pvwa = Pvwa(_ => RdpResponse(Encoding.UTF8.GetBytes(Rdp)));
        using var client = await pvwa.CreateLoggedOnClientAsync();

        var file = await client.PsmConnectAsync("12_34", new PsmConnectOptions { ConnectionComponent = "PSM-RDP" });

        Assert.Equal(Rdp, Encoding.UTF8.GetString(file));
        var (method, path, auth, body) = pvwa.Requests[1];
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("/PasswordVault/API/Accounts/12_34/PSMConnect", path);
        Assert.Equal("tok", auth);
        Assert.Equal("application/octet-stream", pvwa.Accepts[1]);
        Assert.NotNull(pvwa.ContentLengths[1]);
        Assert.Equal("""{"ConnectionComponent":"PSM-RDP"}""", body);
    }

    [Fact]
    public async Task PsmConnect_SendsReasonTicketAndRemoteMachine()
    {
        var pvwa = Pvwa(_ => RdpResponse(Encoding.UTF8.GetBytes(Rdp)));
        using var client = await pvwa.CreateLoggedOnClientAsync();

        await client.PsmConnectAsync("1_1", new PsmConnectOptions
        {
            ConnectionComponent = " PSM-RDP ",
            Reason = "Incident INC42",
            TicketingSystemName = "ServiceNow",
            TicketId = "INC42",
            RemoteMachine = "srv01.corp.local",
        });

        using var json = JsonDocument.Parse(pvwa.Requests[1].Body);
        var root = json.RootElement;
        Assert.Equal("PSM-RDP", root.GetProperty("ConnectionComponent").GetString());
        Assert.Equal("Incident INC42", root.GetProperty("reason").GetString());
        Assert.Equal("ServiceNow", root.GetProperty("TicketingSystemName").GetString());
        Assert.Equal("INC42", root.GetProperty("TicketId").GetString());
        var remote = root.GetProperty("ConnectionParams").GetProperty("PSMRemoteMachine");
        Assert.Equal("srv01.corp.local", remote.GetProperty("value").GetString());
        Assert.False(remote.GetProperty("ShouldSave").GetBoolean());
    }

    [Fact]
    public async Task PsmConnect_EscapesAccountId()
    {
        var pvwa = Pvwa(_ => RdpResponse(Encoding.UTF8.GetBytes(Rdp)));
        using var client = await pvwa.CreateLoggedOnClientAsync();

        await client.PsmConnectAsync("../Users", new PsmConnectOptions { ConnectionComponent = "PSM-RDP" });

        Assert.Equal("/PasswordVault/API/Accounts/..%2FUsers/PSMConnect", pvwa.Requests[1].PathAndQuery);
    }

    [Fact]
    public async Task PsmConnect_RefusalIsReportedWithPvwaMessage()
    {
        var pvwa = Pvwa(_ => FakePvwa.Json(
            """{"ErrorCode":"PASWS204E","ErrorMessage":"You must specify a reason for this operation."}""",
            HttpStatusCode.BadRequest));
        using var client = await pvwa.CreateLoggedOnClientAsync();

        var ex = await Assert.ThrowsAsync<PvwaException>(
            () => client.PsmConnectAsync("1_1", new PsmConnectOptions { ConnectionComponent = "PSM-RDP" }));

        Assert.Equal("You must specify a reason for this operation. (PASWS204E)", ex.Message);
        Assert.False(ex.IsUnknownComponent);
    }

    /// <summary>
    /// Composant absent de la plateforme du compte (réponse constatée : 500 et EPVWA093E) : reconnu, pour inviter à
    /// saisir celui de la plateforme (par ex. WIN-PSM au lieu de PSM-RDP).
    /// </summary>
    [Fact]
    public async Task PsmConnect_RecognizesAnUnknownComponent()
    {
        var pvwa = Pvwa(_ => FakePvwa.Json(
            """{"ErrorCode":"EPVWA093E","ErrorMessage":"Failed to get the relevant connection component (CAWS00001E)"}""",
            HttpStatusCode.InternalServerError));
        using var client = await pvwa.CreateLoggedOnClientAsync();

        var ex = await Assert.ThrowsAsync<PvwaException>(() => client.PsmConnectAsync("2302_3", new PsmConnectOptions
        {
            ConnectionComponent = "PSM-RDP",
            RemoteMachine = "srv01.corp.local",
        }));

        Assert.True(ex.IsUnknownComponent);
    }

    [Fact]
    public void RdpFile_KeepsRawFileBytes()
    {
        var utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Rdp)).ToArray();

        Assert.Same(utf16, RdpFile.FromPsmConnectResponse(utf16, "application/octet-stream"));
    }

    [Fact]
    public void RdpFile_UnwrapsJsonString()
    {
        var wrapped = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Rdp));

        var file = RdpFile.FromPsmConnectResponse(wrapped, "application/json");

        Assert.Equal(Rdp, Encoding.Unicode.GetString(file, 2, file.Length - 2));
        Assert.Equal(Encoding.Unicode.GetPreamble(), file[..2]);
    }

    [Theory]
    [InlineData("""{"PSMGWURL":"https://gw/guac","PSMGWRequest":"abc"}""", "PSM Gateway")]
    [InlineData("<html>Error</html>", "pas de fichier RDP")]
    [InlineData("", "pas de fichier RDP")]
    public void RdpFile_RejectsAnythingElse(string body, string expected)
    {
        using var _ = UiCulture.Use("fr-FR");
        var ex = Assert.Throws<PvwaException>(() => RdpFile.FromPsmConnectResponse(Encoding.UTF8.GetBytes(body), null));

        Assert.Contains(expected, ex.Message);
    }
}
