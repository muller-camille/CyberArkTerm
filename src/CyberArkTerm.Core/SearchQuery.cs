namespace CyberArkTerm.Core;

/// <summary>
/// Recherche plein texte côté client : chaque mot de la requête doit apparaître (sans tenir compte de la casse) dans au
/// moins un des champs.
/// </summary>
public static class SearchQuery
{
    public static bool Matches(string? query, params string?[] fields)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        foreach (var term in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!fields.Any(f => f is not null && f.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
    }
}
