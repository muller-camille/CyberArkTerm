using ZillaTerm.Core.Localization;

namespace ZillaTerm.Core;

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
                AccountKind.Database => CoreStrings.KindDatabase,
                AccountKind.Network => CoreStrings.KindNetwork,
                _ => CoreStrings.KindOther,
            },
            _ => a.SafeName,
        };
        return string.IsNullOrWhiteSpace(key) ? CoreStrings.GroupNotSet : key;
    }

    /// <summary>
    /// Ce qui distingue les comptes d'un dossier affichés sous le même nom (utilisateur@adresse) : leurs machines
    /// autorisées si elles diffèrent, sinon leur plateforme, leur safe ou leur nom dans CyberArk. Les comptes au nom
    /// unique, ou identiques sur tous ces champs, n'ont rien.
    /// </summary>
    public static Dictionary<PvwaAccount, string> Distinctions(IEnumerable<PvwaAccount> accounts)
    {
        var result = new Dictionary<PvwaAccount, string>(ReferenceEqualityComparer.Instance);
        foreach (var same in accounts.GroupBy(a => $"{a.UserName}@{a.Address}", StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            var list = same.ToList();
            Func<PvwaAccount, string>? describe =
                Differ(list, a => a.RemoteMachines) ? a => MachinesText(a.RemoteMachines)
                : Differ(list, a => a.PlatformId) ? a => AccountFilter.Field(CoreStrings.FieldPlatform, a.PlatformId)
                : Differ(list, a => a.SafeName) ? a => AccountFilter.Field(CoreStrings.FieldSafe, a.SafeName)
                : Differ(list, a => a.Name) ? a => AccountFilter.Field(CoreStrings.FieldName, a.Name)
                : null;
            if (describe is null)
            {
                continue;
            }

            foreach (var account in list)
            {
                result[account] = describe(account);
            }
        }

        return result;
    }

    /// <summary>« → srv01, srv02 (+2) » ; « → toutes machines » sans restriction.</summary>
    public static string MachinesText(string remoteMachines)
    {
        var machines = AccountFilter.Machines(remoteMachines).ToList();
        return "→ " + (machines.Count == 0 ? CoreStrings.AnyMachine
            : string.Join(", ", machines.Take(2)) + (machines.Count > 2 ? $" (+{machines.Count - 2})" : ""));
    }

    private static bool Differ(List<PvwaAccount> accounts, Func<PvwaAccount, string?> field) =>
        accounts.Select(a => (field(a) ?? "").Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any();
}
