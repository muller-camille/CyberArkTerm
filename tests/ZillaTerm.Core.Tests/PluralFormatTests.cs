using System.Globalization;
using ZillaTerm.Core.Localization;

namespace ZillaTerm.Core.Tests;

/// <summary>Accord des mots avec un nombre dans les textes traduits (« 1 fichier », « 0 élément » en français).</summary>
public class PluralFormatTests
{
    [Theory]
    [InlineData("fr-FR", 0, "0 élément affiché")]
    [InlineData("fr-FR", 1, "1 élément affiché")]
    [InlineData("fr-FR", 2, "2 éléments affichés")]
    [InlineData("fr-FR", 1500, "1 500 éléments affichés")]
    [InlineData("en-US", 0, "0 éléments affichés")]
    [InlineData("en-US", 1, "1 élément affiché")]
    [InlineData("it-IT", 0, "0 éléments affichés")]
    [InlineData("it-IT", 1, "1 élément affiché")]
    public void Format_ChoosesTheFormOfTheInterfaceLanguage(string culture, int count, string expected)
    {
        using var _ = UiCulture.Use(culture);

        var text = PluralFormat.Format("{0:# élément affiché|# éléments affichés}", count);

        // Le séparateur de milliers dépend des réglages régionaux : comparé sans lui.
        Assert.Equal(expected.Replace(" ", ""), text.Replace(" ", "").Replace(" ", "").Replace(",", ""));
    }

    [Fact]
    public void Format_HandlesSeveralCountsSuffixesAndOtherParameters()
    {
        using var _ = UiCulture.Use("fr-FR");

        Assert.Equal("1 fichier sur 3 diffère : transférez-le à nouveau (srv01).",
            PluralFormat.Format("{0:# fichier|# fichiers} sur {1} {0:diffère|diffèrent} : transférez-{0:le|les} à nouveau ({2}).", 1, 3, "srv01"));
        Assert.Equal("2 fichiers sur 3 diffèrent : transférez-les à nouveau (srv01).",
            PluralFormat.Format("{0:# fichier|# fichiers} sur {1} {0:diffère|diffèrent} : transférez-{0:le|les} à nouveau ({2}).", 2, 3, "srv01"));
        Assert.Equal("3 créés", PluralFormat.Format("{0} créé{0:|s}", 3L));
        Assert.Equal("1,5", PluralFormat.Format("{0:0.0}", 1.5));
        Assert.Equal("01/02/2026", PluralFormat.Format("{0:dd/MM/yyyy}", new DateTime(2026, 2, 1)));
        Assert.Equal("[] x", PluralFormat.Format("[{0}] {1}", null, "x"));
    }

    [Theory]
    [InlineData("fr", 0, true)]
    [InlineData("fr", 1, true)]
    [InlineData("fr", 2, false)]
    [InlineData("en", 0, false)]
    [InlineData("en", 1, true)]
    [InlineData("it", 0, false)]
    [InlineData("it", 1, true)]
    [InlineData("it", 11, false)]
    public void IsSingular_FollowsEachLanguage(string language, int count, bool singular) =>
        Assert.Equal(singular, PluralFormat.IsSingular(count, CultureInfo.GetCultureInfo(language)));

    /// <summary>
    /// Textes à nombre : chaque forme « singulier|pluriel » des .resx en a exactement deux, et la mise en forme ne lève
    /// pas d'exception dans les trois langues (accolades, numéros de paramètres).
    /// </summary>
    [Fact]
    public void PluralTexts_AreWellFormedInEveryLanguage()
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), "src"), "*.resx", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            foreach (var data in System.Xml.Linq.XDocument.Load(file).Root!.Elements("data"))
            {
                var text = (string?)data.Element("value") ?? "";
                if (!text.Contains('|', StringComparison.Ordinal) || !text.Contains('{', StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"\{\d+:([^}]*)\}"))
                {
                    Assert.True(m.Groups[1].Value.Count(c => c == '|') == 1, $"{Path.GetFileName(file)}/{(string?)data.Attribute("name")} : {m.Value}");
                }

                var args = Enumerable.Repeat<object?>(2, 10).ToArray();
                _ = PluralFormat.Format(text, args);
                args = Enumerable.Repeat<object?>(1, 10).ToArray();
                _ = PluralFormat.Format(text, args);
            }
        }
    }

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ZillaTerm.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Racine du dépôt introuvable.");
    }
}
