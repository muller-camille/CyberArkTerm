using System.Net;

namespace ZillaTerm.Core;

/// <summary>Un PSMP de plus que celui par défaut : les serveurs de son domaine passent par lui.</summary>
public sealed class PsmpServer
{
    public string Address { get; set; } = "";

    public int Port { get; set; } = 22;

    /// <summary>
    /// Domaine des serveurs qui passent par ce PSMP ; vide : celui de son adresse (psmp.paris.corp.com → paris.corp.com).
    /// </summary>
    public string Domain { get; set; } = "";
}

/// <summary>PSMP retenu pour un serveur ; <see cref="Domain"/> : le domaine qui l'a désigné, null pour le PSMP de repli.</summary>
public sealed record PsmpEndpoint(string Host, int Port, string? Domain);

/// <summary>
/// Choix du PSMP d'un serveur parmi ceux configurés (le PSMP par défaut et la liste) : celui dont le domaine est le plus
/// proche du nom du serveur. srv01.zzz.xxx.corp.com passe par psmp.zzz.xxx.corp.com s'il est configuré, sinon par
/// psmp.xxx.corp.com. Seuls des PSMP configurés sont choisis, jamais une adresse déduite d'un nom de serveur : le PSMP
/// reçoit le mot de passe CyberArk de l'utilisateur.
/// </summary>
public static class PsmpRouting
{
    /// <summary>
    /// PSMP pour joindre <paramref name="server"/> : celui dont le domaine est la fin la plus longue de son nom (le premier
    /// configuré à égalité, le PSMP par défaut avant la liste) ; sinon le PSMP par défaut, ou à défaut le seul PSMP de la
    /// liste ; null si aucun ne convient (plusieurs PSMP sans PSMP par défaut, serveur d'aucun de leurs domaines).
    /// </summary>
    public static PsmpEndpoint? Resolve(AppSettings settings, string? server)
    {
        var host = NormalizeHost(server);
        if (host.Length > 0 && !IPAddress.TryParse(host, out _))
        {
            var best = Configured(settings)
                .Where(p => p.Domain is { Length: > 0 } domain && (host == domain || host.EndsWith("." + domain, StringComparison.Ordinal)))
                .OrderByDescending(p => p.Domain!.Length)
                .FirstOrDefault();
            if (best is not null)
            {
                return best;
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.PsmpAddress))
        {
            return new PsmpEndpoint(settings.PsmpAddress.Trim(), settings.PsmpPort, null);
        }

        var listed = settings.PsmpServers.Where(p => !string.IsNullOrWhiteSpace(p.Address)).ToList();
        return listed.Count == 1 ? new PsmpEndpoint(listed[0].Address.Trim(), listed[0].Port, null) : null;
    }

    /// <summary>Un PSMP est configuré : SSH et SFTP sont possibles.</summary>
    public static bool Any(AppSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.PsmpAddress) || settings.PsmpServers.Any(p => !string.IsNullOrWhiteSpace(p.Address));

    /// <summary>PSMP configurés avec le domaine qu'ils servent : le PSMP par défaut, puis la liste dans l'ordre.</summary>
    private static IEnumerable<PsmpEndpoint> Configured(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.PsmpAddress))
        {
            var address = settings.PsmpAddress.Trim();
            yield return new PsmpEndpoint(address, settings.PsmpPort, DomainOf(address));
        }

        foreach (var psmp in settings.PsmpServers.Where(p => !string.IsNullOrWhiteSpace(p.Address)))
        {
            var address = psmp.Address.Trim();
            var domain = NormalizeDomain(psmp.Domain);
            yield return new PsmpEndpoint(address, psmp.Port, domain.Length > 0 ? domain : DomainOf(address));
        }
    }

    /// <summary>Domaine d'une adresse, sans sa première partie (psmp.paris.corp.com → paris.corp.com) ; vide pour une IP ou un nom court.</summary>
    public static string DomainOf(string? address)
    {
        var host = NormalizeHost(address);
        int dot = host.IndexOf('.');
        return dot <= 0 || IPAddress.TryParse(host, out _) ? "" : host[(dot + 1)..];
    }

    /// <summary>« Srv01.Corp.com. » → « srv01.corp.com ».</summary>
    public static string NormalizeHost(string? host) => (host ?? "").Trim().TrimEnd('.').ToLowerInvariant();

    /// <summary>« *.Corp.com », « .corp.com » → « corp.com ».</summary>
    public static string NormalizeDomain(string? domain)
    {
        var value = NormalizeHost(domain);
        if (value.StartsWith("*.", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        return value.TrimStart('.');
    }

    /// <summary>Nom de domaine acceptable : lettres, chiffres, « - », « _ », points entre des parties non vides.</summary>
    public static bool IsValidDomain(string? domain)
    {
        var value = NormalizeDomain(domain);
        return value.Length is > 0 and <= 253
               && !IPAddress.TryParse(value, out _)
               && value.Split('.').All(part => part.Length is > 0 and <= 63
                                               && part.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));
    }
}

/// <summary>Problème du domaine d'un PSMP par domaine (voir <see cref="PsmpDomainCheck"/>).</summary>
public enum PsmpDomainProblem
{
    None,
    Missing,
    Invalid,
    Duplicate,
}

/// <summary>
/// Domaines des PSMP par domaine, vérifiés dans l'ordre : celui saisi, sinon celui de l'adresse ; valide, et pas déjà
/// servi par le PSMP par défaut ou un PSMP précédent (le second ne servirait jamais). Règle commune aux Paramètres et au
/// fichier d'environnement.
/// </summary>
public sealed class PsmpDomainCheck
{
    private readonly HashSet<string> _domains = new(StringComparer.Ordinal);

    /// <param name="defaultPsmpAddress">Adresse du PSMP par défaut : son domaine est déjà servi.</param>
    public PsmpDomainCheck(string? defaultPsmpAddress)
    {
        if (PsmpRouting.DomainOf(defaultPsmpAddress) is { Length: > 0 } domain)
        {
            _domains.Add(domain);
        }
    }

    /// <param name="domain">Domaine retenu : celui saisi (normalisé), sinon celui de l'adresse.</param>
    public PsmpDomainProblem Check(string? address, string? given, out string domain)
    {
        domain = PsmpRouting.NormalizeDomain(given) is { Length: > 0 } typed ? typed : PsmpRouting.DomainOf(address);
        return domain.Length == 0 ? PsmpDomainProblem.Missing
            : !PsmpRouting.IsValidDomain(domain) ? PsmpDomainProblem.Invalid
            : !_domains.Add(domain) ? PsmpDomainProblem.Duplicate
            : PsmpDomainProblem.None;
    }
}
