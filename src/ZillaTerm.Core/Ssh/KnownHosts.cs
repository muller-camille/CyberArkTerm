namespace ZillaTerm.Core.Ssh;

public enum HostKeyStatus
{
    Trusted,
    Unknown,
    Changed,
}

/// <summary>
/// Empreintes des clés d'hôte des PSMP déjà acceptées (« premier usage de confiance », comme known_hosts).
/// Clé : « hôte:port » ; valeur : « algorithme SHA256:empreinte ».
/// </summary>
public static class KnownHosts
{
    public static string Key(string host, int port) => $"{host.Trim().ToLowerInvariant()}:{port}";

    public static string Format(string algorithm, string sha256) => $"{algorithm} SHA256:{sha256}";

    public static HostKeyStatus Check(IReadOnlyDictionary<string, string> store, string host, int port, string algorithm, string sha256)
    {
        if (!store.TryGetValue(Key(host, port), out var known))
        {
            return HostKeyStatus.Unknown;
        }

        return string.Equals(known, Format(algorithm, sha256), StringComparison.Ordinal) ? HostKeyStatus.Trusted : HostKeyStatus.Changed;
    }

    /// <summary>Empreinte déjà acceptée pour ce serveur (algorithme, empreinte SHA256 sans préfixe), ou null.</summary>
    public static (string Algorithm, string Sha256)? Known(IReadOnlyDictionary<string, string> store, string host, int port)
    {
        if (!store.TryGetValue(Key(host, port), out var known))
        {
            return null;
        }

        int space = known.IndexOf(' ');
        var algorithm = space > 0 ? known[..space] : "";
        var fingerprint = space > 0 ? known[(space + 1)..] : known;
        return (algorithm, fingerprint.StartsWith("SHA256:", StringComparison.Ordinal) ? fingerprint["SHA256:".Length..] : fingerprint);
    }

    public static void Remember(IDictionary<string, string> store, string host, int port, string algorithm, string sha256) =>
        store[Key(host, port)] = Format(algorithm, sha256);
}
