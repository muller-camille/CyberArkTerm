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

    /// <summary>
    /// Un fichier produit par « Exporter » (en-têtes de l'interface, « , » ici) se réimporte ; safe et plateforme par défaut ;
    /// l'apostrophe ajoutée par l'export devant « - » (protection contre les formules) est retirée.
    /// </summary>
    [Fact]
    public void ReimportsAnExportedFileAndUsesDefaults()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            var writer = new StringWriter();
            CsvExporter.Write(writer, [new PvwaAccount { Id = "1_2", Address = "srv01", UserName = "-svc", SafeName = "", PlatformId = "" }], ',');

            var import = AccountCsv.Parse(writer.ToString(), "Default safe", "WinServerLocal");

            var row = Assert.Single(import.Rows);
            Assert.Contains("'-svc", writer.ToString());
            Assert.Equal(("Default safe", "WinServerLocal", "srv01", "-svc"), (row.Safe, row.Platform, row.Address, row.UserName));
            Assert.NotNull(row.Account);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>
    /// Résultat d'un import : une ligne par ligne du fichier avec état, détail et identifiant, jamais le mot de passe ;
    /// une valeur qui ressemble à une formule est neutralisée comme dans l'export.
    /// </summary>
    [Fact]
    public void WritesTheResultWithoutPasswords()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            var import = AccountCsv.Parse("safe;platform;address;userName;password\nProd;P;srv01;adm;Secret-1\nProd;P;;u;\nProd;P;srv03;=cmd;\nProd;P;srv04;v;\n", null, null);
            import.Rows[0].Outcome = ImportOutcome.Created;
            import.Rows[0].AccountId = "12_3";
            import.Rows[2].Outcome = ImportOutcome.Refused;
            import.Rows[2].Detail = "Account already exists";
            import.Rows[3].Outcome = ImportOutcome.NotSent;
            var writer = new StringWriter();

            AccountCsv.WriteResults(writer, import.Rows, ';');

            Assert.Equal(
            [
                "Line;Safe;Platform;Server;User;Domain;Name;Result;Detail;ID",
                "2;Prod;P;srv01;adm;;;Created;;12_3",
                "3;Prod;P;;u;;;Not imported;address missing;",
                "4;Prod;P;srv03;'=cmd;;;Refused;Account already exists;",
                "5;Prod;P;srv04;v;;;Not sent;;",
            ], writer.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
            Assert.DoesNotContain("Secret", writer.ToString());
            Assert.Equal(ImportOutcome.Pending, AccountCsv.Parse("safe,platform,address,userName\nS,P,a,u\n", null, null).Rows[0].Outcome);
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

        // CSV « ANSI » d'Excel en français : Windows-1252, où « € » (0x80) et « ’ » (0x92) ne sont pas du Latin-1.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            Assert.Equal("é;€’x", new string(AccountCsv.Decode([0xE9, (byte)';', 0x80, 0x92, (byte)'x'])));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
