using System.Globalization;
using System.Text;

namespace CyberArkTerm.Core.Rdp;

/// <summary>
/// Réglages d'une session Bureau à distance ouverte dans un onglet de l'application (connexion directe). Le mot de passe
/// n'en fait pas partie. Les sessions PSM, elles, s'ouvrent avec le fichier du PVWA dans Connexion Bureau à distance
/// (mstsc) ; ce fichier n'est lu ici que pour le journal de débogage (<see cref="ReadFile"/>).
/// </summary>
public sealed record RdpConnectionSettings
{
    public const int DefaultPort = 3389;

    /// <summary>Nom ou adresse du serveur.</summary>
    public required string Server { get; init; }

    public int Port { get; init; } = DefaultPort;

    /// <summary>Utilisateur, éventuellement « DOMAINE\nom » ou « nom@domaine ».</summary>
    public string UserName { get; init; } = "";

    public string Domain { get; init; } = "";

    /// <summary>Programme lancé à l'ouverture de session (« alternate shell »).</summary>
    public string StartProgram { get; init; } = "";

    public string WorkDir { get; init; } = "";

    /// <summary>0 : aucune vérification du serveur, 1 : connexion refusée si la vérification échoue, 2 : avertissement.</summary>
    public int AuthenticationLevel { get; init; } = 2;

    /// <summary>Authentification au niveau du réseau (NLA, CredSSP).</summary>
    public bool EnableCredSsp { get; init; } = true;

    public bool NegotiateSecurityLayer { get; init; } = true;

    public bool RedirectClipboard { get; init; } = true;

    public bool RedirectDrives { get; init; }

    public bool RedirectPrinters { get; init; }

    public bool RedirectPorts { get; init; }

    public bool RedirectSmartCards { get; init; }

    public bool RedirectPosDevices { get; init; }

    /// <summary>Son : 0 sur ce poste, 1 sur le serveur, 2 aucun.</summary>
    public int AudioMode { get; init; }

    public bool AudioCapture { get; init; }

    public int ColorDepth { get; init; } = 32;

    public string LoadBalanceInfo { get; init; } = "";

    public string GatewayHostname { get; init; } = "";

    public int GatewayUsageMethod { get; init; }

    public int GatewayCredsSource { get; init; }

    public int GatewayProfileUsageMethod { get; init; }

    /// <summary>Raccourcis Windows (Alt+Tab...) : 0 sur ce poste, 1 sur le serveur, 2 sur le serveur en plein écran seulement.</summary>
    public int KeyboardHookMode { get; init; } = 2;

    public bool AutoReconnect { get; init; } = true;

    public bool Compress { get; init; } = true;

    public bool BitmapPersistence { get; init; } = true;

    /// <summary>Effets désactivés ou activés (bits TS_PERF_*).</summary>
    public int PerformanceFlags { get; init; }

    /// <summary>Session d'administration (« console »).</summary>
    public bool ConnectToAdministerServer { get; init; }

    /// <summary>Mise à l'échelle de l'image au lieu d'adapter la résolution du bureau distant.</summary>
    public bool SmartSizing { get; init; }

    /// <summary>Réglages d'une connexion directe : authentification réseau, alerte si le serveur n'est pas reconnu.</summary>
    public static RdpConnectionSettings Direct(string server, int port, string userName) => new()
    {
        Server = server,
        Port = port is > 0 and <= 65535 ? port : DefaultPort,
        UserName = userName,
    };

    /// <summary>Réglages d'un fichier .rdp (voir <see cref="Parse"/>), quel que soit son encodage.</summary>
    public static IReadOnlyDictionary<string, string> ReadFile(byte[] content) => Parse(Decode(content));

    /// <summary>Lignes « nom:type:valeur » d'un fichier .rdp ; noms en minuscules, la dernière occurrence l'emporte.</summary>
    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            int first = line.IndexOf(':');
            int second = first < 0 ? -1 : line.IndexOf(':', first + 1);
            if (second != first + 2)
            {
                continue;
            }

            var name = line[..first].Trim().ToLowerInvariant();
            if (name.Length > 0 && line[first + 1] is 's' or 'i' or 'b' or 'S' or 'I' or 'B')
            {
                values[name] = line[(second + 1)..];
            }
        }

        return values;
    }

    /// <summary>« hôte », « hôte:port », « [IPv6]:port » ou IPv6 seule.</summary>
    public static (string Host, int? Port) SplitAddress(string address)
    {
        if (address.StartsWith('['))
        {
            int end = address.IndexOf(']');
            if (end > 0)
            {
                var host = address[1..end];
                return (host, ParsePort(address[(end + 1)..].TrimStart(':')));
            }
        }

        int colon = address.LastIndexOf(':');
        if (colon > 0 && address.IndexOf(':') == colon)
        {
            return (address[..colon], ParsePort(address[(colon + 1)..]));
        }

        return (address, null);
    }

    private static int? ParsePort(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is > 0 and <= 65535 ? port : null;

    private static string Decode(byte[] content)
    {
        // Fichier du PVWA : en général UTF-16 avec BOM ; on accepte aussi l'UTF-8 et l'UTF-16 sans BOM.
        if (content.Length >= 2 && content[0] != 0 && content[1] == 0 && !HasBom(content))
        {
            return Encoding.Unicode.GetString(content);
        }

        using var reader = new StreamReader(new MemoryStream(content), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static bool HasBom(byte[] content) =>
        (content[0] == 0xFF && content[1] == 0xFE) || (content[0] == 0xFE && content[1] == 0xFF)
        || (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF);
}
