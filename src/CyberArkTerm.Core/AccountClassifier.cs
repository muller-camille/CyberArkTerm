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
    /// Vrai pour un compte de domaine ou un compte limité à des machines : PSM a besoin de savoir sur quelle machine
    /// ouvrir la session (paramètre <c>PSMRemoteMachine</c>). Une connexion vers l'adresse d'un compte de domaine
    /// viserait le domaine lui-même : le serveur est toujours demandé.
    /// </summary>
    /// <param name="domains">Domaines connus (voir <see cref="KnownDomains"/>) ; null : seulement d'après le compte.</param>
    public static bool NeedsRemoteMachine(PvwaAccount account, KnownDomains? domains = null) =>
        !string.IsNullOrWhiteSpace(account.RemoteMachines) || IsDomainAccount(account, domains);

    /// <summary>
    /// Compte de domaine : plateforme de domaine, ou adresse qui est un domaine et non un serveur. Elle l'est si c'est le
    /// domaine de connexion du compte (« corp.local » ou « CORP » pour le domaine CORP, « corp.example.com » dont la
    /// première partie est CORP) ou un domaine connu (des serveurs sont dessous, domaine du PVWA ou du poste). Jamais un
    /// compte local, ni un compte Unix, base de données ou réseau sans domaine de connexion : ils visent un serveur.
    /// </summary>
    public static bool IsDomainAccount(PvwaAccount account, KnownDomains? domains = null)
    {
        var platform = account.PlatformId ?? "";
        if (ContainsAny(platform, "Domain"))
        {
            return true;
        }

        var address = PsmpRouting.NormalizeHost(account.Address);
        var logonDomain = PsmpRouting.NormalizeHost(account.LogonDomain);
        if (address.Length == 0 || System.Net.IPAddress.TryParse(address, out _) || ContainsAny(platform, "Local")
            || (Classify(account) is not (AccountKind.Windows or AccountKind.Other) && logonDomain.Length == 0))
        {
            return false;
        }

        return address == logonDomain
               || domains?.Contains(address) == true
               || (logonDomain.Length > 0 && address.Split('.')[0] == logonDomain && address.Contains('.'));
    }

    public static IReadOnlyList<string> RemoteMachineList(PvwaAccount account) =>
        account.RemoteMachines.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Le compte ne peut ouvrir de session que sur ses machines autorisées.</summary>
    public static bool IsRestrictedToRemoteMachines(PvwaAccount account) =>
        account.RemoteMachinesAccess?.AccessRestrictedToRemoteMachines == true && RemoteMachineList(account).Count > 0;

    private static bool ContainsAny(string value, params string[] keywords) =>
        keywords.Any(k => value.Contains(k, StringComparison.OrdinalIgnoreCase));
}
