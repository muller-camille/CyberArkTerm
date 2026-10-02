namespace CyberArkTerm.Core.Tests;

public class CsvExporterTests
{
    [Theory]
    [InlineData("srv01", "srv01")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("=cmd|' /C calc'!A0", "'=cmd|' /C calc'!A0")]
    [InlineData("@SUM(1)", "'@SUM(1)")]
    [InlineData("", "")]
    public void Escape(string value, string expected) => Assert.Equal(expected, CsvExporter.Escape(value));

    [Fact]
    public void Write_ProducesHeaderAndOneLinePerAccount()
    {
        using var _ = UiCulture.Use("fr-FR");
        var writer = new StringWriter();

        CsvExporter.Write(writer,
        [
            new PvwaAccount { Id = "1_1", Address = "srv01", UserName = "admin", SafeName = "S1", PlatformId = "P" },
            new PvwaAccount { Id = "1_2", Address = "srv02", UserName = "root" },
        ]);

        var lines = writer.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("Serveur;Utilisateur;", lines[0]);
        Assert.Equal("srv01;admin;;P;S1;;;;1_1", lines[1]);
        Assert.Equal("srv02;root;;;;;;;1_2", lines[2]);
    }

    [Fact]
    public void Write_UsesListSeparatorAndHeadersOfTheCulture()
    {
        using var _ = UiCulture.Use("en-US");
        var writer = new StringWriter();

        CsvExporter.Write(writer, [new PvwaAccount { Id = "1_1", Address = "srv01", UserName = "admin" }]);

        var lines = writer.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Server,User,Domain,Platform,", lines[0]);
        Assert.Equal("srv01,admin,,,,,,,1_1", lines[1]);
    }

    [Theory]
    [InlineData("fr-FR", ';')]
    [InlineData("it-IT", ';')]
    [InlineData("en-US", ',')]
    public void DefaultSeparator_IsTheExcelListSeparator(string culture, char expected) =>
        Assert.Equal(expected, CsvExporter.DefaultSeparator(System.Globalization.CultureInfo.GetCultureInfo(culture)));
}
