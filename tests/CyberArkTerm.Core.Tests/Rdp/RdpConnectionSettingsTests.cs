using System.Text;
using CyberArkTerm.Core.Rdp;

namespace CyberArkTerm.Core.Tests.Rdp;

public class RdpConnectionSettingsTests
{
    private const string PsmFile =
        "screen mode id:i:1\r\ndesktopwidth:i:1280\r\ndesktopheight:i:800\r\nsession bpp:i:24\r\n" +
        "full address:s:psm.corp.local\r\nusername:s:jdoe\r\n" +
        "alternate shell:s:psm /u admin /a srv01 /c PSM-RDP\r\n" +
        "authentication level:i:0\r\nenablecredsspsupport:i:0\r\nredirectclipboard:i:0\r\n" +
        "disable wallpaper:i:1\r\nallow font smoothing:i:1\r\nkeyboardhook:i:1\r\n";

    [Fact]
    public void ReadsPsmFileInUtf16WithBom()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(PsmFile)).ToArray();

        var s = RdpConnectionSettings.FromRdpFile(bytes);

        Assert.Equal("psm.corp.local", s.Server);
        Assert.Equal(3389, s.Port);
        Assert.Equal("jdoe", s.UserName);
        Assert.Equal("psm /u admin /a srv01 /c PSM-RDP", s.StartProgram);
        Assert.Equal(0, s.AuthenticationLevel);
        Assert.False(s.EnableCredSsp);
        Assert.False(s.RedirectClipboard);
        Assert.Equal(24, s.ColorDepth);
        Assert.Equal(0x01 | 0x80, s.PerformanceFlags);
        Assert.Equal(1, s.KeyboardHookMode);
        Assert.False(s.IsRemoteApp);
    }

    [Fact]
    public void ReadsUtf8AndUtf16WithoutBom()
    {
        const string text = "full address:s:host.example:3390\nusername:s:CORP\\jdoe\n";

        var utf8 = RdpConnectionSettings.FromRdpFile(Encoding.UTF8.GetBytes(text));
        var utf16 = RdpConnectionSettings.FromRdpFile(Encoding.Unicode.GetBytes(text));

        foreach (var s in new[] { utf8, utf16 })
        {
            Assert.Equal("host.example", s.Server);
            Assert.Equal(3390, s.Port);
            Assert.Equal("CORP\\jdoe", s.UserName);
        }
    }

    [Fact]
    public void MissingRedirectionsStayOffExceptClipboard()
    {
        var s = RdpConnectionSettings.FromRdpFile(Encoding.UTF8.GetBytes("full address:s:srv\r\n"));

        Assert.True(s.RedirectClipboard);
        Assert.False(s.RedirectDrives);
        Assert.False(s.RedirectPrinters);
        Assert.False(s.RedirectPorts);
        Assert.False(s.RedirectSmartCards);
        Assert.Equal(2, s.AuthenticationLevel);
        Assert.True(s.EnableCredSsp);
        Assert.Equal(32, s.ColorDepth);
        Assert.Equal(0, s.PerformanceFlags);
    }

    [Theory]
    [InlineData("*", true)]
    [InlineData("C:;D:;", false)]
    [InlineData("", false)]
    public void RedirectsDrivesOnlyWhenAllAreRequested(string drives, bool expected)
    {
        var s = RdpConnectionSettings.FromRdpFile(Encoding.UTF8.GetBytes($"full address:s:srv\r\ndrivestoredirect:s:{drives}\r\n"));

        Assert.Equal(expected, s.RedirectDrives);
    }

    [Fact]
    public void ReadsRemoteAppSettings()
    {
        var s = RdpConnectionSettings.FromRdpFile(Encoding.UTF8.GetBytes(
            "full address:s:psm\r\nremoteapplicationmode:i:1\r\nremoteapplicationprogram:s:||PSMInitSession\r\n" +
            "remoteapplicationname:s:PSM Session\r\nremoteapplicationcmdline:s:/u admin /a srv01 /c PSM-RDP\r\n" +
            "disableremoteappcapscheck:i:1\r\n"));

        Assert.True(s.IsRemoteApp);
        Assert.Equal("||PSMInitSession", s.RemoteApplicationProgram);
        Assert.Equal("/u admin /a srv01 /c PSM-RDP", s.RemoteApplicationArgs);
        Assert.True(s.DisableRemoteAppCapsCheck);
        Assert.True(s.RemoteApplicationExpandArgs);
        Assert.Equal("", s.RemoteApplicationFile);
        Assert.Equal("PSM Session", s.RemoteApplicationTitle);
    }

    /// <summary>
    /// Fichier RemoteApp tel que le renvoie un PVWA (valeurs masquées) : ouvert comme un bureau qui démarre le programme
    /// publié, avec le même utilisateur ; les réglages d'origine restent disponibles pour les fenêtres séparées.
    /// </summary>
    [Fact]
    public void PsmRemoteAppOpensAsDesktop()
    {
        var s = RdpConnectionSettings.FromRdpFile(Encoding.UTF8.GetBytes(
            "full address:s:psm01.corp.local\r\nserver port:i:3389\r\nusername:s:localhost\\PSM@0123abcd\r\n" +
            "alternate shell:s:PSM@0123abcd\r\nenablecredsspsupport:i:0\r\nremoteapplicationmode:i:1\r\n" +
            "remoteapplicationprogram:s:||PSMInitSession\r\nremoteapplicationname:s:PSM-RDP\r\ndisableconnectionsharing:i:1\r\n" +
            "signscope:s:Full Address,Alternate Shell,RemoteApplicationProgram\r\nsignature:s:AAAA\r\n"));

        var desktop = s.RemoteAppAsDesktop();

        Assert.NotNull(desktop);
        Assert.False(desktop.IsRemoteApp);
        Assert.True(desktop.DesktopFromRemoteApp);
        Assert.Equal("||PSMInitSession", desktop.StartProgram);
        Assert.Equal(@"localhost\PSM@0123abcd", desktop.UserName);
        Assert.Equal("psm01.corp.local", desktop.Server);
        Assert.False(desktop.EnableCredSsp);
        Assert.Same(s, desktop.RemoteAppSettings);
        Assert.True(s.IsRemoteApp);
        Assert.False(s.DesktopFromRemoteApp);
        Assert.Equal("PSM@0123abcd", s.StartProgram);
    }

    [Fact]
    public void RemoteAppArgumentsFollowThePublishedProgram()
    {
        var s = RdpConnectionSettings.FromRdpFile(Encoding.UTF8.GetBytes(
            "full address:s:psm\r\nalternate shell:s:PSM@0123abcd\r\nremoteapplicationmode:i:1\r\n" +
            "remoteapplicationprogram:s:||PSMInitSession \r\nremoteapplicationcmdline:s: /x 1\r\n"));

        Assert.Equal("||PSMInitSession /x 1", s.RemoteAppAsDesktop()?.StartProgram);
    }

    [Theory]
    [InlineData("full address:s:psm\r\nremoteapplicationmode:i:1\r\nremoteapplicationprogram:s:||PSMInitSession\r\n")]
    [InlineData("full address:s:psm\r\nalternate shell:s:psm /u admin /a srv01 /c PSM-RDP\r\n")]
    [InlineData("full address:s:psm\r\nremoteapplicationmode:i:1\r\nalternate shell:s:PSM@0123abcd\r\n")]
    public void OnlyRemoteAppsWithAStartProgramOpenAsDesktop(string file)
    {
        Assert.Null(RdpConnectionSettings.FromRdpFile(Encoding.UTF8.GetBytes(file)).RemoteAppAsDesktop());
    }

    [Theory]
    [InlineData("||PSMInitSession", "PSMInitSession")]
    [InlineData(@"C:\Windows\System32\notepad.exe", "notepad.exe")]
    public void RemoteAppTitleFallsBackToTheProgram(string program, string expected)
    {
        var s = RdpConnectionSettings.FromRdpFile(Encoding.UTF8.GetBytes(
            $"full address:s:psm\r\nremoteapplicationmode:i:1\r\nremoteapplicationprogram:s:{program}\r\n"));

        Assert.Equal(expected, s.RemoteApplicationTitle);
        Assert.False(s.DisableRemoteAppCapsCheck);
    }

    [Fact]
    public void UsesServerPortWhenAddressHasNone()
    {
        var s = RdpConnectionSettings.FromRdpFile(Encoding.UTF8.GetBytes("full address:s:srv\r\nserver port:i:4000\r\n"));

        Assert.Equal(4000, s.Port);
    }

    [Fact]
    public void RejectsFileWithoutAddress()
    {
        Assert.Throws<FormatException>(() => RdpConnectionSettings.FromRdpFile(Encoding.UTF8.GetBytes("username:s:jdoe\r\n")));
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
