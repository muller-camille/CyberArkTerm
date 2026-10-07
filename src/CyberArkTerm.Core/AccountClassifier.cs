namespace CyberArkTerm.Core;

public enum AccountKind
{
    Windows,
    Unix,
    Database,
    Network,
    Other,
}

/// <summary>
/// Déduit le type de cible et le composant PSM par défaut à partir de l'ID de plateforme
/// (les plateformes CyberArk standard et leurs copies gardent en général ces mots-clés).
/// </summary>
public static class AccountClassifier
{
    public const string PsmRdp = "PSM-RDP";
    public const string PsmSsh = "PSM-SSH";

    /// <summary>Composants PSM livrés par CyberArk, proposés dans la fenêtre de connexion.</summary>
    public static readonly IReadOnlyList<string> CommonComponents =
        [PsmRdp, PsmSsh, "PSM-WinSCP", "PSM-SQLServerMgmtStudio", "PSM-SQLPlus", "PSM-Telnet"];

    public static AccountKind Classify(PvwaAccount account)
    {
        var p = account.PlatformId ?? "";
        if (ContainsAny(p, "Oracle", "SQL", "DB2", "Sybase", "Postgre", "Maria", "Mongo", "Database"))
        {
            return AccountKind.Database;
        }

        if (ContainsAny(p, "Win", "Domain", "ActiveDirectory"))
        {
            return AccountKind.Windows;
        }

        if (ContainsAny(p, "Cisco", "Juniper", "Fortinet", "FortiGate", "PaloAlto", "CheckPoint", "F5", "Network", "Router", "Switch"))
        {
            return AccountKind.Network;
        }

        if (ContainsAny(p, "Unix", "Linux", "SSH", "SFTP", "AIX", "Solaris", "HPUX", "RHEL"))
        {
            return AccountKind.Unix;
        }

        return AccountKind.Other;
    }

    /// <summary>
    /// Connexion par défaut (double-clic, ajout à « Mes serveurs ») : d'après le nom de la plateforme, « SFTP » donne les
    /// fichiers seuls via le PSMP, « SSH » une session SSH via le PSMP ; sinon SSH pour une cible Unix, PSM pour le reste.
    /// Sans PSMP renseigné, toujours PSM.
    /// </summary>
    public static ConnectMode DefaultMode(PvwaAccount account, bool hasPsmp)
    {
        var p = account.PlatformId ?? "";
        if (!hasPsmp)
        {
            return ConnectMode.Psm;
        }

        if (ContainsAny(p, "SFTP"))
        {
            return ConnectMode.Sftp;
        }

        return ContainsAny(p, "SSH") || Classify(account) == AccountKind.Unix ? ConnectMode.Ssh : ConnectMode.Psm;
    }

    public static string DefaultComponent(PvwaAccount account)
    {
        var p = account.PlatformId ?? "";
        return Classify(account) switch
        {
            AccountKind.Unix or AccountKind.Network => PsmSsh,
            AccountKind.Database when ContainsAny(p, "Oracle") => "PSM-SQLPlus",
            AccountKind.Database when ContainsAny(p, "MSSQL", "SQLServer") => "PSM-SQLServerMgmtStudio",
            _ => PsmRdp,
        };
    }

    /// <summary>
    /// Vrai pour un compte de domaine ou un compte limité à des machines : PSM a besoin de savoir
    /// sur quelle machine ouvrir la session (paramètre <c>PSMRemoteMachine</c>).
    /// </summary>
    public static bool NeedsRemoteMachine(PvwaAccount account) =>
        !string.IsNullOrWhiteSpace(account.RemoteMachines) || ContainsAny(account.PlatformId ?? "", "Domain")
        || IsRegisteredForDomain(account);

    /// <summary>
    /// Compte Windows enregistré pour son domaine et non pour un serveur : son adresse est le domaine de connexion
    /// (« corp.local » ou « CORP », domaine CORP). Jamais un compte local, dont le domaine est souvent le serveur lui-même.
    /// </summary>
    private static bool IsRegisteredForDomain(PvwaAccount account)
    {
        var domain = account.LogonDomain.Trim().TrimEnd('.');
        var address = (account.Address ?? "").Trim().TrimEnd('.');
        if (Classify(account) != AccountKind.Windows || ContainsAny(account.PlatformId ?? "", "Local")
            || domain.Length == 0 || address.Length == 0)
        {
            return false;
        }

        var labels = address.Split('.');
        return address.Equals(domain, StringComparison.OrdinalIgnoreCase)
               || (labels.Length == 2 && labels[0].Equals(domain, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string> RemoteMachineList(PvwaAccount account) =>
        account.RemoteMachines.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Le compte ne peut ouvrir de session que sur ses machines autorisées.</summary>
    public static bool IsRestrictedToRemoteMachines(PvwaAccount account) =>
        account.RemoteMachinesAccess?.AccessRestrictedToRemoteMachines == true && RemoteMachineList(account).Count > 0;

    private static bool ContainsAny(string value, params string[] keywords) =>
        keywords.Any(k => value.Contains(k, StringComparison.OrdinalIgnoreCase));
}
