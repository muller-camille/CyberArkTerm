using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ZillaTerm.Core;

/// <summary>
/// Étiquette d'environnement d'un serveur de « Mes serveurs » (PROD, QA, DEV…) : sa couleur, sur l'onglet et autour de
/// la session, rappelle où l'on travaille.
/// </summary>
public sealed class ServerTag
{
    public ServerTag()
    {
    }

    public ServerTag(string name, string color)
    {
        Name = name;
        Color = color;
    }

    public string Name { get; set; } = "";

    /// <summary>Couleur « #RRGGBB ».</summary>
    public string Color { get; set; } = ServerTagRules.NeutralColor;
}

/// <summary>Étiquettes de serveurs : celles par défaut, leur vérification, et la couleur du texte posé dessus.</summary>
public static partial class ServerTagRules
{
    /// <summary>Étiquettes gardées au plus.</summary>
    public const int MaxTags = 20;

    /// <summary>Longueur maximale d'un nom d'étiquette (il s'affiche sur l'onglet).</summary>
    public const int MaxNameLength = 16;

    /// <summary>Couleur d'une étiquette inconnue (venue d'une liste partagée ou d'un fichier).</summary>
    public const string NeutralColor = "#757575";

    /// <summary>Couleurs proposées dans les Paramètres.</summary>
    public static IReadOnlyList<string> Palette { get; } =
    [
        "#D32F2F", "#C2185B", "#7B1FA2", "#3949AB", "#1976D2", "#00838F",
        "#2E7D32", "#689F38", "#F9A825", "#EF6C00", "#6D4C41", "#757575",
    ];

    /// <summary>PROD en rouge, QA en bleu, DEV en vert : trois couleurs bien distinctes, sans l'orange des alertes.</summary>
    public static List<ServerTag> Defaults() =>
    [
        new("PROD", "#D32F2F"),
        new("QA", "#1976D2"),
        new("DEV", "#2E7D32"),
    ];

    /// <summary>Nom acceptable : 1 à <see cref="MaxNameLength"/> caractères visibles (espaces permis à l'intérieur).</summary>
    public static bool IsValidName(string? name)
    {
        var trimmed = (name ?? "").Trim();
        return trimmed.Length is > 0 and <= MaxNameLength
               && trimmed.EnumerateRunes().All(r => !Rune.IsControl(r) && Rune.GetUnicodeCategory(r) != UnicodeCategory.Format);
    }

    /// <summary>Couleur « #RRGGBB ».</summary>
    public static bool IsValidColor(string? color) => color is not null && ColorPattern().IsMatch(color.Trim());

    /// <summary>
    /// Étiquettes des réglages ou d'un fichier, nettoyées : noms et couleurs valides seulement, sans doublon (sans tenir
    /// compte des majuscules), <see cref="MaxTags"/> au plus. Absentes (anciens réglages) : celles par défaut.
    /// </summary>
    public static List<ServerTag> Sanitize(IEnumerable<ServerTag?>? tags)
    {
        if (tags is null)
        {
            return Defaults();
        }

        var result = new List<ServerTag>();
        foreach (var tag in tags)
        {
            if (tag is null || !IsValidName(tag.Name) || !IsValidColor(tag.Color) || Find(result, tag.Name) is not null)
            {
                continue;
            }

            result.Add(new ServerTag(tag.Name.Trim(), tag.Color.Trim().ToUpperInvariant()));
            if (result.Count == MaxTags)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>Étiquette nommée <paramref name="name"/> (sans tenir compte des majuscules), ou null.</summary>
    public static ServerTag? Find(IEnumerable<ServerTag> tags, string? name)
    {
        var trimmed = (name ?? "").Trim();
        return trimmed.Length == 0
            ? null
            : tags.FirstOrDefault(t => string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Nom d'étiquette d'un serveur tel qu'il est gardé : valide, sans espaces autour ; sinon null.</summary>
    public static string? NormalizeName(string? name) => IsValidName(name) ? name!.Trim() : null;

    /// <summary>Texte lisible sur la couleur : noir sur une couleur claire, blanc sur une couleur foncée.</summary>
    public static bool NeedsDarkText(string color)
    {
        if (!IsValidColor(color))
        {
            return false;
        }

        var hex = color.Trim();
        double Channel(int at)
        {
            double c = int.Parse(hex.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        double luminance = 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
        // Contraste avec le blanc (1,05 / (L + 0,05)) moindre qu'avec le noir ((L + 0,05) / 0,05) : texte noir.
        return 1.05 / (luminance + 0.05) < (luminance + 0.05) / 0.05;
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorPattern();
}

/// <summary>
/// Étiquette probable d'un serveur, d'après son nom, son adresse, son safe ou ses dossiers (import d'autres logiciels,
/// ajout dans « Mes serveurs ») : « prd », « production », « recette », « uat », « dev »… Proposée seulement, jamais
/// imposée ; en cas de doute (deux environnements dans un même texte), rien n'est proposé.
/// </summary>
public static class ServerTagGuess
{
    // Mots reconnus pour les étiquettes par défaut, en plus du nom de chaque étiquette (minuscules, sans accents).
    private static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PROD"] = ["prod", "prd", "production", "produzione"],
        ["QA"] =
        [
            "qa", "qual", "qualif", "qualification", "rec", "recette", "uat", "preprod", "pprod", "staging", "stg", "test",
            "tst", "homol", "homologation", "collaudo", "integration",
        ],
        ["DEV"] = ["dev", "devel", "develop", "development", "developpement", "sviluppo"],
    };

    // Formes composées qui contiennent un autre environnement : « pre-prod » n'est pas la production, « non-prod » non plus.
    private static readonly (string Pattern, string Replacement)[] Compounds =
    [
        (@"pre[\s_.-]*prod(uction)?", " preprod "),
        (@"(non|hors|no)[\s_.-]*prod(uction)?", " "),
    ];

    /// <summary>
    /// Étiquette de <paramref name="tags"/> reconnue dans le premier texte de <paramref name="sources"/> qui en nomme une
    /// (dans l'ordre : du plus précis, le nom du serveur, au plus large, les dossiers) ; null si aucune, ou si ce texte
    /// en nomme plusieurs.
    /// </summary>
    public static ServerTag? Guess(IReadOnlyList<ServerTag> tags, params string?[] sources)
    {
        if (tags.Count == 0)
        {
            return null;
        }

        var keywords = tags.Select(tag => (Tag: tag, Words: Words(tag))).ToList();
        foreach (var source in sources)
        {
            var tokens = Tokens(source);
            if (tokens.Count == 0)
            {
                continue;
            }

            var found = keywords.Where(k => tokens.Any(t => Matches(t, k.Words))).Select(k => k.Tag).ToList();
            if (found.Count > 0)
            {
                return found.Count == 1 ? found[0] : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Étiquette probable d'un serveur : d'après son nom, sa machine cible, son adresse, son safe, puis ses dossiers du
    /// plus proche au plus large (« Prod/Linux » : « Linux », puis « Prod »).
    /// </summary>
    public static ServerTag? ForServer(IReadOnlyList<ServerTag> tags, string? name, string? remoteMachine, string? address,
        string? safe, string? folder)
    {
        var folders = SessionFolders.Normalize(folder).Split('/', StringSplitOptions.RemoveEmptyEntries).Reverse();
        return Guess(tags, [name, remoteMachine, address, safe, .. folders]);
    }

    /// <summary>Mots d'une étiquette : son nom et, pour PROD, QA et DEV, leurs synonymes.</summary>
    private static string[] Words(ServerTag tag)
    {
        // « PRE-PROD » se cherche comme « preprod » : les mots du texte n'ont ni espace ni tiret.
        var name = new string(Fold(tag.Name).Where(char.IsAsciiLetterOrDigit).ToArray());
        var words = new List<string> { name };
        if (Synonyms.TryGetValue(tag.Name.Trim(), out var synonyms))
        {
            words.AddRange(synonyms);
        }

        return words.Where(w => w.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool Matches(Token token, string[] words) =>
        words.Any(w => token.Text == w
                       // « lnxprd01 », « webdev02 » : un mot de 3 lettres ou plus à la fin des lettres, juste avant des chiffres.
                       || (token.BeforeDigits && w.Length >= 3 && token.Text.Length > w.Length && token.Text.EndsWith(w, StringComparison.Ordinal)));

    private readonly record struct Token(string Text, bool BeforeDigits);

    /// <summary>Morceaux de lettres et de chiffres, en minuscules sans accents (« SRV-LNX-PRD/Web01 » → srv, lnx, prd, web, 01).</summary>
    private static List<Token> Tokens(string? source)
    {
        var text = Fold(source ?? "");
        foreach (var (pattern, replacement) in Compounds)
        {
            text = Regex.Replace(text, pattern, replacement, RegexOptions.CultureInvariant);
        }

        var tokens = new List<Token>();
        int i = 0;
        while (i < text.Length)
        {
            if (!char.IsAsciiLetterOrDigit(text[i]))
            {
                i++;
                continue;
            }

            int start = i;
            bool letters = char.IsAsciiLetter(text[i]);
            while (i < text.Length && char.IsAsciiLetterOrDigit(text[i]) && char.IsAsciiLetter(text[i]) == letters)
            {
                i++;
            }

            tokens.Add(new Token(text[start..i], letters && i < text.Length && char.IsAsciiDigit(text[i])));
        }

        return tokens;
    }

    /// <summary>Minuscules sans accents.</summary>
    private static string Fold(string text)
    {
        var folded = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                folded.Append(char.ToLowerInvariant(c));
            }
        }

        return folded.ToString();
    }
}
