using System.Net;

namespace ZillaTerm.Core.Migration;

/// <summary>Type de connexion d'une session lue dans un autre logiciel.</summary>
public enum ImportProtocol
{
    Ssh,

    /// <summary>Transfert de fichiers SFTP ou SCP.</summary>
    Sftp,

    Rdp,

    Telnet,

    /// <summary>Non repris (VNC, FTP, port série...) : voir <see cref="ImportedSession.ProtocolName"/>.</summary>
    Other,
}

/// <summary>
/// Session lue dans la configuration d'un autre logiciel, sans aucun mot de passe : son dossier, son nom, le serveur et le
/// compte visés. Une session qui passait déjà par PSM ou par le PSMP est décodée : ce sont le serveur et le compte cibles
/// qui sont gardés, pas l'adresse du PSM.
/// </summary>
public sealed record ImportedSession
{
    /// <summary>Dossier dans l'autre logiciel, sous la forme « Prod/Web » ; vide = racine.</summary>
    public string Folder { get; init; } = "";

    public string Name { get; init; } = "";

    public ImportProtocol Protocol { get; init; }

    /// <summary>Nom du protocole dans l'autre logiciel (« VNC », « Serial »...), pour un protocole non repris.</summary>
    public string? ProtocolName { get; init; }

    /// <summary>Serveur cible.</summary>
    public string Host { get; init; } = "";

    public int? Port { get; init; }

    /// <summary>Compte cible, sans son domaine ; null s'il n'est pas indiqué (l'autre logiciel le demandait).</summary>
    public string? User { get; init; }

    /// <summary>Domaine du compte : « CORP » de « CORP\admin », « corp.local » de « admin@corp.local ».</summary>
    public string? Domain { get; init; }

    /// <summary>Composant PSM d'une session qui passait par PSM.</summary>
    public string? Component { get; init; }

    /// <summary>La session passait déjà par PSM ou par le PSMP.</summary>
    public bool ViaPsm { get; init; }

    /// <summary>Compte affiché : « CORP\admin », « root » ou vide.</summary>
    public string DisplayUser => User is null ? "" : Domain is { Length: > 0 } d ? $"{d}\\{User}" : User;

    /// <summary>
    /// Session SSH, SFTP ou Telnet : le serveur et l'utilisateur sont décodés. Un serveur « utilisateur@serveur » donne
    /// l'utilisateur s'il n'est pas indiqué à part ; une connexion via le PSMP
    /// (« coffre@cible[#domaine]@serveur@psmp ») donne le compte et le serveur cibles.
    /// </summary>
    public static ImportedSession Terminal(string folder, string name, ImportProtocol protocol, string? host, int? port, string? user)
    {
        var login = (user ?? "").Trim();
        var address = (host ?? "").Trim();
        var full = login.Length > 0 ? $"{login}@{address}" : address;
        var parts = full.Split('@');
        if (parts.Length >= 4 && parts.All(p => p.Trim().Length > 0))
        {
            // PSMP : <coffre>@<cible>[#domaine]@<serveur>@<psmp> ; l'utilisateur du coffre peut lui-même contenir un « @ ».
            var target = parts[^3].Trim();
            int hash = target.IndexOf('#');
            return new ImportedSession
            {
                Folder = SessionFolders.Normalize(folder),
                Name = name.Trim(),
                Protocol = protocol,
                Host = CleanHost(parts[^2]),
                Port = port,
                User = hash < 0 ? target : target[..hash].Trim(),
                Domain = hash < 0 ? null : NullIfEmpty(target[(hash + 1)..]),
                ViaPsm = true,
            };
        }

        int at = full.LastIndexOf('@');
        var (account, domain) = SplitUser(at < 0 ? null : full[..at]);
        return new ImportedSession
        {
            Folder = SessionFolders.Normalize(folder),
            Name = name.Trim(),
            Protocol = protocol,
            Host = CleanHost(at < 0 ? full : full[(at + 1)..]),
            Port = port,
            User = account,
            Domain = domain,
        };
    }

    /// <summary>
    /// Session Bureau à distance. Un programme de démarrage PSM (« psm /u compte /a serveur /c composant », fichier .rdp
    /// du PVWA) donne le compte, le serveur et le composant cibles ; l'utilisateur et l'adresse du PSM sont ignorés.
    /// </summary>
    public static ImportedSession Rdp(string folder, string name, string? address, string? user, string? domain = null,
        string? startProgram = null)
    {
        var (host, port) = SplitPort(address);
        if (PsmShell.TryParse(startProgram, out var psmUser, out var psmAddress, out var component))
        {
            var (target, targetDomain) = SplitUser(psmUser);
            return new ImportedSession
            {
                Folder = SessionFolders.Normalize(folder),
                Name = name.Trim(),
                Protocol = ImportProtocol.Rdp,
                Host = CleanHost(psmAddress),
                User = target,
                Domain = targetDomain,
                Component = component,
                ViaPsm = true,
            };
        }

        var (account, userDomain) = SplitUser(user);
        return new ImportedSession
        {
            Folder = SessionFolders.Normalize(folder),
            Name = name.Trim(),
            Protocol = ImportProtocol.Rdp,
            Host = host,
            Port = port,
            User = account,
            Domain = userDomain ?? NullIfEmpty(domain),
        };
    }

    /// <summary>Session d'un protocole non repris : elle figure dans le résultat, sans être importée.</summary>
    public static ImportedSession Unsupported(string folder, string name, string protocolName, string? host, int? port = null) => new()
    {
        Folder = SessionFolders.Normalize(folder),
        Name = name.Trim(),
        Protocol = ImportProtocol.Other,
        ProtocolName = protocolName.Trim(),
        Host = CleanHost(host),
        Port = port,
    };

    /// <summary>Port indiqué à part (sauf s'il figure déjà dans l'adresse, ou pour une session via PSM).</summary>
    public ImportedSession WithPort(int? port) => ViaPsm || Port is not null ? this : this with { Port = port };

    /// <summary>« CORP\admin » → (admin, CORP) ; « admin@corp.local » → (admin, corp.local) ; « root » → (root, null).</summary>
    public static (string? User, string? Domain) SplitUser(string? user)
    {
        var value = (user ?? "").Trim();
        if (value.Length == 0)
        {
            return (null, null);
        }

        int slash = value.LastIndexOf('\\');
        if (slash >= 0)
        {
            return (NullIfEmpty(value[(slash + 1)..]), NullIfEmpty(value[..slash]));
        }

        int at = value.LastIndexOf('@');
        if (at > 0 && at < value.Length - 1)
        {
            return (value[..at].Trim(), value[(at + 1)..].Trim());
        }

        return (NullIfEmpty(value.Trim('@')), null);
    }

    /// <summary>« srv01:3390 » → (srv01, 3390) ; « [fe80::1]:3390 » → (fe80::1, 3390) ; une IPv6 seule reste entière.</summary>
    public static (string Host, int? Port) SplitPort(string? address)
    {
        var value = (address ?? "").Trim();
        if (value.StartsWith('['))
        {
            int end = value.IndexOf(']');
            if (end > 0)
            {
                var rest = value[(end + 1)..];
                return (value[1..end], rest.StartsWith(':') && int.TryParse(rest[1..], out var p) && p is > 0 and < 65536 ? p : null);
            }
        }

        int colon = value.LastIndexOf(':');
        if (colon > 0 && value.IndexOf(':') == colon && int.TryParse(value[(colon + 1)..], out var port) && port is > 0 and < 65536)
        {
            return (CleanHost(value[..colon]), port);
        }

        return (CleanHost(value), null);
    }

    private static string CleanHost(string? host)
    {
        var value = (host ?? "").Trim();
        if (value.StartsWith('[') && value.EndsWith(']') && IPAddress.TryParse(value[1..^1], out _))
        {
            value = value[1..^1];
        }

        return value;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Programme de démarrage d'un fichier .rdp de connexion via PSM : « psm /u compte /a serveur /c composant » (valeurs
/// éventuellement entre guillemets).
/// </summary>
public static class PsmShell
{
    public static bool TryParse(string? shell, out string? user, out string address, out string? component)
    {
        user = null;
        address = "";
        component = null;
        var tokens = Tokens(shell ?? "");
        if (tokens.Count == 0 || !string.Equals(tokens[0], "psm", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (int i = 1; i + 1 < tokens.Count; i++)
        {
            switch (tokens[i].ToLowerInvariant())
            {
                case "/u":
                    user = tokens[++i];
                    break;
                case "/a":
                    address = tokens[++i];
                    break;
                case "/c":
                    component = tokens[++i];
                    break;
            }
        }

        return address.Trim().Length > 0;
    }

    private static List<string> Tokens(string value)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < value.Length)
        {
            while (i < value.Length && char.IsWhiteSpace(value[i]))
            {
                i++;
            }

            if (i >= value.Length)
            {
                break;
            }

            if (value[i] == '"')
            {
                int end = value.IndexOf('"', i + 1);
                end = end < 0 ? value.Length : end;
                tokens.Add(value[(i + 1)..end]);
                i = end + 1;
            }
            else
            {
                int start = i;
                while (i < value.Length && !char.IsWhiteSpace(value[i]))
                {
                    i++;
                }

                tokens.Add(value[start..i]);
            }
        }

        return tokens;
    }
}
