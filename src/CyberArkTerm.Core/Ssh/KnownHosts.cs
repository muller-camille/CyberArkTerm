namespace CyberArkTerm.Core.Ssh;

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

    public static void Remember(IDictionary<string, string> store, string host, int port, string algorithm, string sha256) =>
        store[Key(host, port)] = Format(algorithm, sha256);
}
