namespace CyberArkTerm.Core;

/// <summary>
/// Recherche plein texte côté client : chaque mot de la requête doit apparaître
/// (sans tenir compte de la casse) dans au moins un des champs affichés du compte.
/// </summary>
public static class AccountFilter
{
    public static bool Matches(PvwaAccount account, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        string[] fields =
        [
            account.Address ?? "",
            account.UserName ?? "",
            account.Name ?? "",
            account.SafeName ?? "",
            account.PlatformId ?? "",
            account.LogonDomain,
            account.RemoteMachines,
        ];

        foreach (var term in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!fields.Any(f => f.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
    }
}
