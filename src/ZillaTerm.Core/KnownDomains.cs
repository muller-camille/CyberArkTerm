using System.Net;

namespace ZillaTerm.Core;

/// <summary>
/// Noms de domaine connus, pour reconnaître un compte enregistré pour son domaine plutôt que pour un serveur : un nom sous
/// lequel il y a des serveurs (corp.local, pour srv01.corp.local) n'est pas un serveur.
/// </summary>
public sealed class KnownDomains
{
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    /// <param name="hosts">Serveurs connus (adresses des comptes, PVWA) : leurs domaines parents sont des domaines.</param>
    /// <param name="domains">Domaines eux-mêmes (domaine du poste de travail, nom DNS ou NetBIOS).</param>
    public static KnownDomains From(IEnumerable<string?> hosts, IEnumerable<string?> domains)
    {
        var known = new KnownDomains();
        foreach (var host in hosts)
        {
            known.AddParentsOf(host);
        }

        foreach (var domain in domains)
        {
            known.Add(domain);
        }

        return known;
    }

    /// <summary>Domaines au-dessus d'un serveur : srv01.paris.corp.local → paris.corp.local et corp.local (pas « local » seul).</summary>
    public void AddParentsOf(string? host)
    {
        var name = PsmpRouting.NormalizeHost(host);
        if (IPAddress.TryParse(name, out _))
        {
            return;
        }

        for (int dot = name.IndexOf('.'); dot > 0 && name.IndexOf('.', dot + 1) > dot + 1; dot = name.IndexOf('.', dot + 1))
        {
            _names.Add(name[(dot + 1)..]);
        }
    }

    public void Add(string? domain)
    {
        var name = PsmpRouting.NormalizeDomain(domain);
        if (name.Length > 0 && !IPAddress.TryParse(name, out _))
        {
            _names.Add(name);
        }
    }

    public bool Contains(string? name) => _names.Contains(PsmpRouting.NormalizeHost(name));
}
