namespace CyberArkTerm.Core;

public enum GroupBy
{
    Safe,
    Platform,
    Kind,
}

/// <summary>Regroupement des comptes en dossiers pour l'arbre des sessions.</summary>
public static class AccountGrouping
{
    public static List<(string Name, List<PvwaAccount> Accounts)> Group(IEnumerable<PvwaAccount> accounts, GroupBy groupBy) =>
        accounts
            .GroupBy(a => KeyOf(a, groupBy), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.OrderBy(a => a.Address, StringComparer.OrdinalIgnoreCase)
                                  .ThenBy(a => a.UserName, StringComparer.OrdinalIgnoreCase)
                                  .ToList()))
            .ToList();

    private static string KeyOf(PvwaAccount a, GroupBy groupBy)
    {
        var key = groupBy switch
        {
            GroupBy.Platform => a.PlatformId,
            GroupBy.Kind => AccountClassifier.Classify(a) switch
            {
                AccountKind.Windows => "Windows",
                AccountKind.Unix => "Unix / Linux",
                AccountKind.Database => "Bases de données",
                AccountKind.Network => "Réseau",
                _ => "Autres",
            },
            _ => a.SafeName,
        };
        return string.IsNullOrWhiteSpace(key) ? "(non renseigné)" : key;
    }
}
