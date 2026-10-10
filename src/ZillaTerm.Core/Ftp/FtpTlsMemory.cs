using ZillaTerm.Core.Ssh;

namespace ZillaTerm.Core.Ftp;

/// <summary>
/// Serveurs FTP déjà vus avec TLS : une connexion ftp:// vers l'un d'eux qui ne propose plus TLS est signalée comme
/// une interception possible (un intermédiaire n'a qu'à effacer AUTH TLS), au lieu de la simple question « continuer
/// en clair ».
/// </summary>
public static class FtpTlsMemory
{
    /// <summary>Vrai si ce serveur a déjà chiffré une connexion, ou si son certificat FTPS a été épinglé.</summary>
    public static bool Seen(AppSettings settings, string host, int port) =>
        settings.FtpTlsServers.Contains(KnownHosts.Key(host, port), StringComparer.Ordinal)
        || settings.KnownHosts.ContainsKey(CertificateKey(host, port));

    /// <summary>Note une connexion chiffrée ; vrai si c'est nouveau (réglages à enregistrer).</summary>
    public static bool Remember(AppSettings settings, string host, int port)
    {
        var key = KnownHosts.Key(host, port);
        if (settings.FtpTlsServers.Contains(key, StringComparer.Ordinal))
        {
            return false;
        }

        settings.FtpTlsServers.Add(key);
        return true;
    }

    /// <summary>
    /// Retrait de TLS confirmé par l'utilisateur : le serveur n'est plus noté comme chiffré, ni son certificat épinglé
    /// (s'il revient à TLS, son certificat sera vérifié comme un nouveau).
    /// </summary>
    public static void Forget(AppSettings settings, string host, int port)
    {
        settings.FtpTlsServers.Remove(KnownHosts.Key(host, port));
        settings.KnownHosts.Remove(CertificateKey(host, port));
    }

    /// <summary>Clé du certificat FTPS épinglé de ce serveur dans <see cref="AppSettings.KnownHosts"/>.</summary>
    public static string CertificateKey(string host, int port) => KnownHosts.Key("ftps://" + host, port);
}
