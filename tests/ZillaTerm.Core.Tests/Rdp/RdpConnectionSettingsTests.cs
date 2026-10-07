using System.Text;
using ZillaTerm.Core.Rdp;

namespace ZillaTerm.Core.Tests.Rdp;

public class RdpConnectionSettingsTests
{
    [Fact]
    public void ReadsUtf8AndUtf16Files()
    {
        const string text = "full address:s:host.example:3390\nusername:s:CORP\\jdoe\n";

        foreach (var bytes in new[]
                 {
                     Encoding.UTF8.GetBytes(text), Encoding.Unicode.GetBytes(text),
                     [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)],
                 })
        {
            var values = RdpConnectionSettings.ReadFile(bytes);
            Assert.Equal("host.example:3390", values["full address"]);
            Assert.Equal("CORP\\jdoe", values["username"]);
        }
    }

    [Theory]
    [InlineData("srv01", "srv01", null)]
    [InlineData("srv01:3390", "srv01", 3390)]
    [InlineData("[fe80::1]:3390", "fe80::1", 3390)]
    [InlineData("[fe80::1]", "fe80::1", null)]
    [InlineData("fe80::1", "fe80::1", null)]
    [InlineData("srv01:abc", "srv01", null)]
    public void SplitsAddress(string address, string host, int? port)
    {
        Assert.Equal((host, port), RdpConnectionSettings.SplitAddress(address));
    }

    [Fact]
    public void ParseKeepsColonsInValuesAndIgnoresMalformedLines()
    {
        var values = RdpConnectionSettings.Parse("alternate shell:s:psm /a 10.0.0.1:22\r\nnot a setting\r\nfoo:xx:1\r\n");

        Assert.Equal("psm /a 10.0.0.1:22", values["alternate shell"]);
        Assert.Single(values);
    }

    [Fact]
    public void DirectConnectionUsesSecureDefaults()
    {
        var s = RdpConnectionSettings.Direct("srv", 0, "admin");

        Assert.Equal(3389, s.Port);
        Assert.Equal(2, s.AuthenticationLevel);
        Assert.True(s.EnableCredSsp);
        Assert.False(s.RedirectDrives);
        Assert.False(s.RedirectPrinters);
    }
}
