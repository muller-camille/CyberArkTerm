using System.Globalization;

namespace ZillaTerm.Core.Localization;

/// <summary>Langues de l'interface : français, anglais, italien ; par défaut celle de Windows, sinon l'anglais.</summary>
public static class UiLanguage
{
    /// <summary>Codes des langues traduites, dans l'ordre d'affichage.</summary>
    public static IReadOnlyList<string> Supported { get; } = ["fr", "en", "it"];

    /// <summary>Nom de chaque langue dans cette langue (identique quelle que soit la langue de l'interface).</summary>
    public static string NativeName(string code) => code switch
    {
        "fr" => "Français",
        "en" => "English",
        "it" => "Italiano",
        _ => code,
    };

    /// <summary>
    /// Langue à utiliser : celle choisie dans les préférences (« fr », « en », « it »), sinon celle de Windows
    /// si elle est traduite, sinon l'anglais.
    /// </summary>
    public static CultureInfo Resolve(string? setting, CultureInfo system)
    {
        var chosen = Normalize(setting);
        if (chosen.Length > 0)
        {
            // Même langue que Windows : on garde sa variante régionale (fr-CA, it-CH...).
            return system.TwoLetterISOLanguageName == chosen ? system : CultureInfo.GetCultureInfo(chosen);
        }

        return Supported.Contains(system.TwoLetterISOLanguageName) ? system : CultureInfo.GetCultureInfo("en");
    }

    /// <summary>Code de langue reconnu, ou chaîne vide pour « langue du système ».</summary>
    public static string Normalize(string? setting)
    {
        var code = (setting ?? "").Trim().ToLowerInvariant();
        return Supported.Contains(code) ? code : "";
    }

    /// <summary>Applique la langue à l'interface (thread courant et threads créés ensuite).</summary>
    public static void Apply(CultureInfo culture)
    {
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
