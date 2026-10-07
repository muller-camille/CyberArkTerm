using System.Text;
using ZillaTerm.Core.Diagnostics;
using ZillaTerm.Core.Rdp;

namespace ZillaTerm.Core.Tests.Diagnostics;

/// <summary>Le journal est global à l'application : ses tests ne tournent pas en même temps que les autres.</summary>
[CollectionDefinition(nameof(DebugLogCollection), DisableParallelization = true)]
public sealed class DebugLogCollection;

[Collection(nameof(DebugLogCollection))]
public sealed class DebugLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "cat-debuglog-" + Guid.NewGuid().ToString("N"));

    private string LogPath => Path.Combine(_directory, "debug.log");

    public void Dispose()
    {
        if (DebugLog.Enabled)
        {
            DebugLog.Stop();
        }

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(@"username:s:localhost\PSM@0123abcd", @"username:s:localhost\PSM@***")]
    [InlineData("alternate shell:s:PSM@Zm9v+YmFy/== suite", "alternate shell:s:PSM@*** suite")]
    [InlineData("« psm@abc » et PSM@def;", "« psm@*** » et PSM@***;")]
    [InlineData("password=hunter2 ok", "password=*** ok")]
    [InlineData("{\"password\":\"s3cr et\",\"user\":\"jdoe\"}", "{\"password\":***,\"user\":\"jdoe\"}")]
    [InlineData("Authorization: abcdef", "Authorization: ***")]
    [InlineData("signature:s:AAAABBBB", "signature:***")]
    [InlineData("Cookie = CA66=xyz; suite", "Cookie = ***; suite")]
    [InlineData("{\"password\":\"ab\\\"cdSECRET\",\"user\":\"jdoe\"}", "{\"password\":***,\"user\":\"jdoe\"}")]
    [InlineData("password 51:b:01000000D08C9DDF suite", "password 51:*** suite")]
    public void SecretsAreMasked(string text, string expected)
    {
        Assert.Equal(expected, DebugLog.Redact(text));
    }

    [Theory]
    [InlineData("full address:s:psm01.corp.local")]
    [InlineData("remoteapplicationprogram:s:||PSMInitSession")]
    [InlineData("POST https://pvwa/PasswordVault/API/Accounts/12_3/PSMConnect -> 200 OK")]
    [InlineData("jdoe@srv01.corp.local")]
    public void OrdinaryTextIsKept(string text)
    {
        Assert.Equal(text, DebugLog.Redact(text));
    }

    /// <summary>Fichier RemoteApp de PVWA (valeurs masquées) : structure gardée, jeton et signature masqués.</summary>
    [Fact]
    public void RdpFileIsDescribedWithoutSecrets()
    {
        var file = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(
            "full address:s:psm01.corp.local\r\nusername:s:localhost\\PSM@0123abcd\r\nalternate shell:s:PSM@0123abcd\r\n" +
            "remoteapplicationmode:i:1\r\nremoteapplicationprogram:s:||PSMInitSession\r\npassword 51:b:01000000D08C\r\n" +
            "loadbalanceinfo:s:tsv://route\r\nremoteapplicationcmdline:s:/ticket 98765\r\nsignature:s:AAAABBBBCCCC\r\n")).ToArray();

        var text = DebugLog.DescribeRdpFile(RdpConnectionSettings.ReadFile(file));

        Assert.Contains("full address = psm01.corp.local", text, StringComparison.Ordinal);
        Assert.Contains(@"username = localhost\PSM@***", text, StringComparison.Ordinal);
        Assert.Contains("alternate shell = PSM@***", text, StringComparison.Ordinal);
        Assert.Contains("remoteapplicationprogram = ||PSMInitSession", text, StringComparison.Ordinal);
        Assert.Contains("signature = *** (12 car.)", text, StringComparison.Ordinal);
        Assert.Contains("password 51 = *** (", text, StringComparison.Ordinal);
        Assert.Contains("loadbalanceinfo = *** (", text, StringComparison.Ordinal);
        Assert.DoesNotContain("0123abcd", text, StringComparison.Ordinal);
        Assert.DoesNotContain("AAAABBBB", text, StringComparison.Ordinal);
        Assert.DoesNotContain("D08C", text, StringComparison.Ordinal);
        Assert.DoesNotContain("route", text, StringComparison.Ordinal);
        Assert.Contains("remoteapplicationcmdline = *** (13 car.)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("98765", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("/ticket 98765", "*** (13 car.)")]
    public void HiddenValuesShowOnlyTheirLength(string value, string expected)
    {
        Assert.Equal(expected, DebugLog.Hidden(value));
    }

    [Fact]
    public void NothingIsWrittenWhenDisabled()
    {
        DebugLog.Write("test", "rien");

        Assert.False(DebugLog.Enabled);
        Assert.False(File.Exists(LogPath));
    }

    [Fact]
    public void LinesAreWrittenUntilStopped()
    {
        DebugLog.Start(LogPath, "en-tête");
        DebugLog.Write("rdp", "première\nseconde\u001b[31m");
        DebugLog.Stop();
        DebugLog.Write("rdp", "après l'arrêt");

        var lines = File.ReadAllLines(LogPath);
        Assert.Contains(lines, l => l.EndsWith("en-tête", StringComparison.Ordinal) && l.Contains(" log ", StringComparison.Ordinal));
        int first = Array.FindIndex(lines, l => l.EndsWith("rdp    première", StringComparison.Ordinal));
        Assert.True(first >= 0);
        // Suite d'un message : décalée sous le début du message, caractère de contrôle remplacé.
        Assert.Equal(lines[first].IndexOf("rdp", StringComparison.Ordinal), lines[first + 1].IndexOf("seconde", StringComparison.Ordinal) - 7);
        Assert.EndsWith("seconde [31m", lines[first + 1], StringComparison.Ordinal);
        Assert.StartsWith(" ", lines[first + 1], StringComparison.Ordinal);
        Assert.DoesNotContain(lines, l => l.Contains("après l'arrêt", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("désactivé", StringComparison.Ordinal));
        Assert.False(DebugLog.Enabled);
    }

    [Fact]
    public void ExceptionsAreWrittenWithTheirInnerExceptions()
    {
        DebugLog.Start(LogPath, "en-tête");
        try
        {
            throw new InvalidOperationException("externe", new IOException("interne password=abc"));
        }
        catch (InvalidOperationException e)
        {
            DebugLog.Write("app", "échec", e);
        }

        var text = File.ReadAllText(LogPath);
        Assert.Contains("System.InvalidOperationException (0x80131509) : externe", text, StringComparison.Ordinal);
        Assert.Contains("--- interne : System.IO.IOException", text, StringComparison.Ordinal);
        Assert.Contains("password=***", text, StringComparison.Ordinal);
        Assert.Contains(nameof(ExceptionsAreWrittenWithTheirInnerExceptions), text, StringComparison.Ordinal);
    }

    [Fact]
    public void LargeLogIsRolledOver()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(LogPath, new byte[DebugLog.MaxSize + 1]);

        DebugLog.Start(LogPath, "en-tête");

        Assert.Equal(DebugLog.MaxSize + 1, new FileInfo(LogPath + ".1").Length);
        Assert.Contains("en-tête", File.ReadAllText(LogPath), StringComparison.Ordinal);
    }

    /// <summary>Requêtes au PVWA : adresse, statut et erreur notés ; ni mot de passe ni jeton de session.</summary>
    [Fact]
    public async Task PvwaRequestsAreLoggedWithoutCredentials()
    {
        var pvwa = new FakePvwa(r => FakePvwa.IsLogon(r)
            ? FakePvwa.Json("\"tok-Secret-99\"")
            : FakePvwa.Json("""{"ErrorCode":"PASWS013E","ErrorMessage":"Session expired"}""", System.Net.HttpStatusCode.Unauthorized));
        using var client = pvwa.CreateClient();
        DebugLog.Start(LogPath, "en-tête");

        await client.LogonAsync(AuthMethod.CyberArk, "jdoe", "p@ss-Secret-42");
        await Assert.ThrowsAsync<PvwaException>(() => client.KeepAliveAsync());

        var text = File.ReadAllText(LogPath);
        Assert.Contains("POST https://pvwa.test/PasswordVault/API/auth/CyberArk/Logon -> 200 OK", text, StringComparison.Ordinal);
        Assert.Contains("GET https://pvwa.test/PasswordVault/API/Accounts?offset=0&limit=1 -> 401 Unauthorized", text, StringComparison.Ordinal);
        Assert.Contains("Session expired (PASWS013E)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret", text, StringComparison.Ordinal);
        Assert.Single(pvwa.Requests, r => r.Authorization == "tok-Secret-99");
    }
}
