using System.Globalization;
using ZillaTerm.Core.Localization;

namespace ZillaTerm.Core;

/// <summary>
/// Recherche plein texte côté client : chaque mot de la requête doit apparaître
/// (sans tenir compte de la casse) dans au moins un des champs affichés du compte.
/// </summary>
public static class AccountFilter
{
    public static bool Matches(PvwaAccount account, string? query) => SearchQuery.Matches(
        query,
        account.Address,
        account.UserName,
        account.Name,
        account.SafeName,
        account.PlatformId,
        account.LogonDomain,
        account.RemoteMachines);

    /// <summary>
    /// Pourquoi le compte correspond à la recherche quand <paramref name="shown"/> (le texte affiché pour lui) ne le
    /// montre pas : pour chaque mot absent de ce texte, le champ qui le contient (« machine prd-jump01 », « safe
    /// PROD »…), une fois chacun. Vide sans recherche, ou si tout se voit déjà.
    /// </summary>
    public static IReadOnlyList<string> HiddenMatches(PvwaAccount account, string? query, string shown)
    {
        var found = new List<string>();
        foreach (var term in (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!shown.Contains(term, StringComparison.OrdinalIgnoreCase) && Describe(account, term) is { } field && !found.Contains(field))
            {
                found.Add(field);
            }
        }

        return found;
    }

    /// <summary>Machines autorisées du compte (séparées par « ; » dans CyberArk, parfois par « , »).</summary>
    public static IEnumerable<string> Machines(string remoteMachines) =>
        remoteMachines.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Premier champ caché qui contient le mot : une machine (la seule à citer), le safe, la plateforme, le domaine, le nom.</summary>
    private static string? Describe(PvwaAccount account, string term)
    {
        bool Has(string? value) => value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
        return Machines(account.RemoteMachines).FirstOrDefault(Has) is { } machine ? Field(CoreStrings.FieldMachine, machine)
            : Has(account.SafeName) ? Field(CoreStrings.FieldSafe, account.SafeName)
            : Has(account.PlatformId) ? Field(CoreStrings.FieldPlatform, account.PlatformId)
            : Has(account.LogonDomain) ? Field(CoreStrings.FieldDomain, account.LogonDomain)
            : Has(account.Name) ? Field(CoreStrings.FieldName, account.Name)
            : null;
    }

    internal static string Field(string format, string? value) => string.Format(CultureInfo.CurrentCulture, format, value);
}
