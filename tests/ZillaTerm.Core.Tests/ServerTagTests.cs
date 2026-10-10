namespace ZillaTerm.Core.Tests;

public class ServerTagTests
{
    private static readonly List<ServerTag> Tags = ServerTagRules.Defaults();

    [Fact]
    public void DefaultsAreProdQaDevInDistinctColors()
    {
        Assert.Equal(["PROD", "QA", "DEV"], Tags.Select(t => t.Name));
        Assert.Equal(3, Tags.Select(t => t.Color).Distinct().Count());
        Assert.All(Tags, t => Assert.True(ServerTagRules.IsValidColor(t.Color)));
    }

    [Theory]
    [InlineData("PROD", true)]
    [InlineData(" Pré-prod ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("UNE-ETIQUETTE-TROP-LONGUE", false)]
    [InlineData("PROD‮", false)]
    [InlineData("PR\nOD", false)]
    public void NamesAreShortAndVisible(string name, bool valid) => Assert.Equal(valid, ServerTagRules.IsValidName(name));

    [Theory]
    [InlineData("#D32F2F", true)]
    [InlineData("#d32f2f", true)]
    [InlineData("D32F2F", false)]
    [InlineData("#D32F2", false)]
    [InlineData("red", false)]
    [InlineData(null, false)]
    public void ColorsAreSixDigitHex(string? color, bool valid) => Assert.Equal(valid, ServerTagRules.IsValidColor(color));

    /// <summary>Réglages anciens (sans étiquettes) : celles par défaut ; sinon les valides, sans doublon.</summary>
    [Fact]
    public void SanitizeKeepsValidUniqueTags()
    {
        Assert.Equal(["PROD", "QA", "DEV"], ServerTagRules.Sanitize(null).Select(t => t.Name));
        Assert.Empty(ServerTagRules.Sanitize([]));
        var tags = ServerTagRules.Sanitize(
        [
            new ServerTag(" PROD ", "#d32f2f"), new ServerTag("prod", "#000000"), new ServerTag("QA", "orange"), null,
            new ServerTag("", "#FFFFFF"), new ServerTag("SANDBOX", "#00838F"),
        ]);
        Assert.Equal(["PROD", "SANDBOX"], tags.Select(t => t.Name));
        Assert.Equal("#D32F2F", tags[0].Color);
        Assert.Equal(ServerTagRules.MaxTags,
            ServerTagRules.Sanitize(Enumerable.Range(0, 30).Select(i => new ServerTag($"T{i}", "#123456"))).Count);
    }

    [Theory]
    [InlineData("#F9A825", true)]
    [InlineData("#FFFFFF", true)]
    [InlineData("#D32F2F", false)]
    [InlineData("#2E7D32", false)]
    [InlineData("#000000", false)]
    public void TextOnTheColorStaysReadable(string color, bool dark) => Assert.Equal(dark, ServerTagRules.NeedsDarkText(color));

    [Theory]
    [InlineData("PROD", "root@prd-lnx01.corp.local")]
    [InlineData("PROD", "srvprod02")]
    [InlineData("PROD", "lnxprd01.corp.local")]
    [InlineData("PROD", "Production")]
    [InlineData("QA", "web-rec-01")]
    [InlineData("QA", "Recette")]
    [InlineData("QA", "app01.uat.corp.local")]
    [InlineData("QA", "PRE-PROD")]
    [InlineData("QA", "Pré-production")]
    [InlineData("DEV", "webdev01")]
    [InlineData("DEV", "Développement")]
    [InlineData(null, "non-prod")]
    [InlineData(null, "srv01.corp.local")]
    [InlineData(null, "product01")]
    [InlineData(null, "devprod-bridge")]
    [InlineData(null, "prod-dev-bridge")]
    public void GuessReadsCommonEnvironmentWords(string? expected, string text) =>
        Assert.Equal(expected, ServerTagGuess.Guess(Tags, text)?.Name);

    /// <summary>Le texte le plus précis l'emporte : le nom avant les dossiers, puis du dossier le plus proche au plus large.</summary>
    [Fact]
    public void TheMostPreciseTextWins()
    {
        Assert.Equal("DEV", ServerTagGuess.Guess(Tags, "webdev01", "Prod/Linux")?.Name);
        Assert.Equal("PROD", ServerTagGuess.Guess(Tags, "srv01", null, "Production/Linux")?.Name);
        Assert.Equal("QA", ServerTagGuess.Guess(Tags, "srv01", "", "SRV-LNX-REC", "Prod")?.Name);
        Assert.Null(ServerTagGuess.Guess([], "prd-lnx01"));
    }

    /// <summary>Une étiquette ajoutée par l'équipe se reconnaît à son nom, tirets et espaces ignorés.</summary>
    [Fact]
    public void CustomTagsAreRecognizedByName()
    {
        List<ServerTag> tags = [.. Tags, new ServerTag("SANDBOX", "#00838F"), new ServerTag("PRE PROD", "#7B1FA2")];
        Assert.Equal("SANDBOX", ServerTagGuess.Guess(tags, "sandbox-lnx01")?.Name);
        Assert.Equal("SANDBOX", ServerTagGuess.Guess(tags, "lnxsandbox01")?.Name);
        // « PRE PROD » et QA (préproduction) reconnaissent tous deux « preprod » : rien n'est proposé.
        Assert.Null(ServerTagGuess.Guess(tags, "preprod-web01"));
    }

    /// <summary>Paramètres : renommer suit dans « Mes serveurs », supprimer retire l'étiquette des serveurs.</summary>
    [Fact]
    public void RenamingOrRemovingATagUpdatesTheServers()
    {
        var settings = new AppSettings
        {
            Sessions =
            [
                new SavedSession { Name = "a", Tag = "QA" },
                new SavedSession { Name = "b", Tag = "dev" },
                new SavedSession { Name = "c", Tag = "PROD" },
                new SavedSession { Name = "d" },
            ],
        };
        settings.ReplaceServerTags([new ServerTag("PROD", "#D32F2F"), new ServerTag("REC", "#EF6C00")],
            new Dictionary<string, string> { ["qa"] = "REC" });
        Assert.Equal(["REC", null, "PROD", null], settings.Sessions.Select(s => s.Tag));
        Assert.Equal(["PROD", "REC"], settings.ServerTags.Select(t => t.Name));
    }

    /// <summary>
    /// Fichier d'environnement : les étiquettes de l'équipe remplacent celles du poste ; les serveurs gardent la leur ;
    /// une étiquette invalide ou en double fait refuser le fichier.
    /// </summary>
    [Fact]
    public void EnvironmentSharesTheTags()
    {
        var team = new AppSettings { ServerTags = [new ServerTag("PROD", "#D32F2F"), new ServerTag("REC", "#F9A825")] };
        var profile = EnvironmentProfile.FromSettings(team, null);
        Assert.Equal(["PROD", "REC"], profile.ServerTags!.Select(t => t.Name));

        var target = new AppSettings { Sessions = [new SavedSession { Tag = "QA" }] };
        var change = Assert.Single(profile.Diff(target), c => c.Setting == EnvironmentSetting.ServerTags);
        Assert.Equal("PROD (#D32F2F), QA (#1976D2), DEV (#2E7D32)", change.Current);
        Assert.Equal("PROD (#D32F2F), REC (#F9A825)", change.New);
        profile.ApplyTo(target);
        Assert.Equal(["PROD", "REC"], target.ServerTags.Select(t => t.Name));
        Assert.Equal("QA", target.Sessions[0].Tag);
        Assert.DoesNotContain(profile.Diff(target), c => c.Setting == EnvironmentSetting.ServerTags);

        foreach (var tags in new List<ServerTag>[]
                 {
                     [new ServerTag("PROD", "rouge")],
                     [new ServerTag("PROD‮", "#D32F2F")],
                     [new ServerTag("PROD", "#D32F2F"), new ServerTag("prod", "#2E7D32")],
                 })
        {
            var refused = Assert.Throws<EnvironmentFileException>(new EnvironmentProfile { ServerTags = tags }.Validate);
            Assert.Equal(EnvironmentProblem.InvalidServerTag, refused.Problem);
        }
    }

    /// <summary>Listes exportées ou partagées : l'étiquette suit le serveur ; illisible dans un fichier, elle est ignorée.</summary>
    [Fact]
    public void ServerListsCarryTheTag()
    {
        var session = new SavedSession { AccountId = "12_3", Name = "root@prd-lnx01", Tag = "PROD" };
        var entry = ServerEntry.From(session);
        Assert.Equal("PROD", entry.Tag);
        Assert.Equal("PROD", entry.ToSession("pvwa.corp.local").Tag);

        var file = ServerListFile.Parse("""
            {"format":"CyberArkTerm.Servers","version":1,"pvwa":"pvwa.corp.local","servers":[
              {"accountId":"1","name":"a","tag":"QA"},
              {"accountId":"2","name":"b","tag":"PROD‮"},
              {"accountId":"3","name":"c"}]}
            """);
        Assert.Equal(["QA", null, null], file.Servers.Select(s => s.Tag));
    }
}
