using System.Globalization;
using System.Text;

namespace CyberArkTerm.Core.Tests;

public sealed class AccountCsvTests
{
    /// <summary>Fichier fictif façon Excel français : « ; », guillemets, en-têtes en français, colonne inconnue ignorée.</summary>
    [Fact]
    public void ReadsAnExcelStyleFile()
    {
        const string csv = "Safe;Plateforme;Adresse;Utilisateur;Domaine;Mot de passe;Machines autorisées;Gestion automatique;Motif;Commentaire\r\n"
            + "Prod;WinDomain;srv01.corp.example;svc_app;CORP;\"p;a\"\"ss\";srv01, srv02;oui;;à ignorer\r\n"
            + "\r\n"
            + "Prod;UnixSSH;lnx01;root;;;;non;\"Compte\nde secours\";\r\n";

        var import = AccountCsv.Parse(csv, null, null);

        Assert.Null(import.Error);
        Assert.Equal(2, import.Ready);
        var first = import.Rows[0];
        Assert.Equal((2, "Prod", "WinDomain", "srv01.corp.example", "svc_app", "CORP", true), (first.Line, first.Safe, first.Platform, first.Address, first.UserName, first.LogonDomain, first.HasPassword));
        Assert.Equal("p;a\"ss", new string(first.Account!.Secret));
        Assert.Equal("srv01, srv02", first.Account.RemoteMachines);
        Assert.True(first.Account.AutomaticManagement);
        var second = import.Rows[1];
        Assert.Equal(4, second.Line);
        Assert.False(second.Account!.AutomaticManagement);
        Assert.Equal("Compte\nde secours", second.Account.ManualManagementReason);
        Assert.Null(second.Account.Secret);

        import.Clear();
        Assert.All(first.Account.Secret!, c => Assert.Equal('\0', c));
    }

    /// <summary>Un fichier produit par « Exporter » (en-têtes de l'interface, « , » ici) se réimporte ; safe et plateforme par défaut.</summary>
    [Fact]
    public void ReimportsAnExportedFileAndUsesDefaults()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            var writer = new StringWriter();
            CsvExporter.Write(writer, [new PvwaAccount { Id = "1_2", Address = "srv01", UserName = "admin", SafeName = "", PlatformId = "" }], ',');

            var import = AccountCsv.Parse(writer.ToString(), "Default safe", "WinServerLocal");

            var row = Assert.Single(import.Rows);
            Assert.Equal(("Default safe", "WinServerLocal", "srv01", "admin"), (row.Safe, row.Platform, row.Address, row.UserName));
            Assert.NotNull(row.Account);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void IncompleteRowsKeepTheirError()
    {
        const string csv = "safe,platform,address,userName,cpm\nProd,P,,u,\nProd,P,srv,u,peut-être\nProd,P,srv,u,\n";

        var import = AccountCsv.Parse(csv, null, null);

        Assert.Equal(3, import.Rows.Count);
        Assert.Equal(1, import.Ready);
        Assert.NotNull(import.Rows[0].Error);
        Assert.Contains("peut-être", import.Rows[1].Error);
        Assert.Null(import.Rows[2].Error);
    }

    [Theory]
    [InlineData("safe;platform;userName\nS;P;u\n")]
    [InlineData("platform;address;userName\nP;a;u\n")]
    [InlineData("safe;address;userName\nS;a;u\n")]
    [InlineData("safe;platform;address;userName\n")]
    [InlineData("safe;platform;address;userName\n\"S;P;a;u\n")]
    [InlineData("")]
    public void RefusesFilesWithoutTheNeededColumnsOrRows(string csv)
    {
        Assert.NotNull(AccountCsv.Parse(csv, null, null).Error);
    }

    [Fact]
    public void TemplateIsReadable()
    {
        var import = AccountCsv.Parse(AccountCsv.Template(';'), null, null);

        Assert.Equal(1, import.Ready);
        Assert.Equal("Prod-Windows", import.Rows[0].Safe);
    }

    [Fact]
    public void DecodesUtf8BomUtf16AndAnsi()
    {
        Assert.Equal("é;x", new string(AccountCsv.Decode([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("é;x")])));
        Assert.Equal("é;x", new string(AccountCsv.Decode([0xFF, 0xFE, .. Encoding.Unicode.GetBytes("é;x")])));
        Assert.Equal("é;x", new string(AccountCsv.Decode(Encoding.UTF8.GetBytes("é;x"))));
        Assert.Equal("é;x", new string(AccountCsv.Decode(Encoding.Latin1.GetBytes("é;x"))));
    }
}
