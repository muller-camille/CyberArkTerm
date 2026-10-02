using System.Globalization;
using System.Text;

namespace CyberArkTerm.Core.Rdp;

/// <summary>
/// Réglages d'une session Bureau à distance ouverte dans un onglet de l'application : lus dans le fichier .rdp
/// renvoyé par le PVWA (PSM), ou construits pour une connexion directe. Le mot de passe n'en fait pas partie.
/// </summary>
public sealed record RdpConnectionSettings
{
    public const int DefaultPort = 3389;

    // Bits de AdvancedSettings.PerformanceFlags (TS_PERF_*) activés par les clés du fichier .rdp.
    private static readonly (string Key, int Flag)[] PerformanceKeys =
    [
        ("disable wallpaper", 0x01),
        ("disable full window drag", 0x02),
        ("disable menu anims", 0x04),
        ("disable themes", 0x08),
        ("disable cursor setting", 0x40),
        ("allow font smoothing", 0x80),
        ("allow desktop composition", 0x100),
    ];

    /// <summary>Nom ou adresse du serveur (ou du PSM).</summary>
    public required string Server { get; init; }

    public int Port { get; init; } = DefaultPort;

    /// <summary>Utilisateur, éventuellement « DOMAINE\nom » ou « nom@domaine ».</summary>
    public string UserName { get; init; } = "";

    public string Domain { get; init; } = "";

    /// <summary>Programme lancé à l'ouverture de session (« alternate shell » : lancement de la session PSM).</summary>
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

    /// <summary>
    /// Vrai si le fichier demande une application distante (RemoteApp) : pas de bureau, les fenêtres de l'application
    /// s'ouvrent directement sur le bureau de ce poste. « alternate shell » ne sert pas dans ce mode.
    /// </summary>
    public bool IsRemoteApp { get; init; }

    /// <summary>Application distante : alias publié (« ||PSMInitSession ») ou chemin du programme sur le serveur.</summary>
    public string RemoteApplicationProgram { get; init; } = "";

    /// <summary>Nom affiché de l'application distante.</summary>
    public string RemoteApplicationName { get; init; } = "";

    /// <summary>Arguments de l'application distante (pour le PSM : la demande de session).</summary>
    public string RemoteApplicationArgs { get; init; } = "";

    /// <summary>Variables d'environnement des arguments développées sur le serveur (« remoteapplicationexpandcmdline »).</summary>
    public bool RemoteApplicationExpandArgs { get; init; } = true;

    /// <summary>Fichier à ouvrir avec l'application distante (« remoteapplicationfile »), le plus souvent vide.</summary>
    public string RemoteApplicationFile { get; init; } = "";

    /// <summary>Connexion en application distante même si le serveur ne l'annonce pas (« disableremoteappcapscheck »).</summary>
    public bool DisableRemoteAppCapsCheck { get; init; }

    /// <summary>Vrai pour une application distante ouverte comme un bureau (voir <see cref="RemoteAppAsDesktop"/>).</summary>
    public bool DesktopFromRemoteApp { get; init; }

    /// <summary>
    /// La même connexion ouverte comme un bureau au lieu d'une application distante, pour l'afficher dans l'onglet :
    /// le programme de démarrage (« alternate shell », pour le PSM la demande de session « PSM@… ») lance la session
    /// comme une connexion PSM classique. Null si ce n'est pas une application distante ou si le fichier n'a pas de
    /// programme de démarrage (le bureau n'afficherait rien d'utile).
    /// </summary>
    public RdpConnectionSettings? RemoteAppAsDesktop() =>
        IsRemoteApp && StartProgram.Trim().Length > 0 ? this with { IsRemoteApp = false, DesktopFromRemoteApp = true } : null;

    /// <summary>Nom à afficher pour l'application distante : son nom, sinon son programme sans « || » ni chemin.</summary>
    public string RemoteApplicationTitle =>
        RemoteApplicationName.Trim() is { Length: > 0 } name ? name
        : RemoteApplicationProgram.Trim().TrimStart('|').Split('\\', '/')[^1];

    /// <summary>Réglages d'une connexion directe : authentification réseau, alerte si le serveur n'est pas reconnu.</summary>
    public static RdpConnectionSettings Direct(string server, int port, string userName) => new()
    {
        Server = server,
        Port = port is > 0 and <= 65535 ? port : DefaultPort,
        UserName = userName,
    };

    /// <summary>
    /// Lit un fichier .rdp. Les redirections absentes du fichier restent désactivées (sauf le presse-papiers) ;
    /// une liste de lecteurs précise n'est pas reprise, seul « * » (tous les lecteurs) l'est.
    /// </summary>
    public static RdpConnectionSettings FromRdpFile(byte[] content)
    {
        var values = Parse(Decode(content));
        string Text(string key, string fallback = "") => values.TryGetValue(key, out var v) ? v : fallback;
        int Int(string key, int fallback) =>
            values.TryGetValue(key, out var v) && int.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;
        bool Bool(string key, bool fallback) => Int(key, fallback ? 1 : 0) != 0;

        var (server, port) = SplitAddress(Text("full address").Trim());
        if (server.Length == 0)
        {
            throw new FormatException("full address");
        }

        int performance = PerformanceKeys.Where(k => Bool(k.Key, false)).Sum(k => k.Flag);

        return new RdpConnectionSettings
        {
            Server = server,
            Port = port ?? (Int("server port", DefaultPort) is var p and > 0 and <= 65535 ? p : DefaultPort),
            UserName = Text("username"),
            Domain = Text("domain"),
            StartProgram = Text("alternate shell"),
            WorkDir = Text("shell working directory"),
            AuthenticationLevel = Math.Clamp(Int("authentication level", 2), 0, 3),
            EnableCredSsp = Bool("enablecredsspsupport", true),
            NegotiateSecurityLayer = Bool("negotiate security layer", true),
            RedirectClipboard = Bool("redirectclipboard", true),
            RedirectDrives = Text("drivestoredirect").Trim() == "*",
            RedirectPrinters = Bool("redirectprinters", false),
            RedirectPorts = Bool("redirectcomports", false),
            RedirectSmartCards = Bool("redirectsmartcards", false),
            RedirectPosDevices = Bool("redirectposdevices", false),
            AudioMode = Math.Clamp(Int("audiomode", 0), 0, 2),
            AudioCapture = Bool("audiocapturemode", false),
            ColorDepth = Int("session bpp", 32) is var bpp and (8 or 15 or 16 or 24 or 32) ? bpp : 32,
            LoadBalanceInfo = Text("loadbalanceinfo"),
            GatewayHostname = Text("gatewayhostname"),
            GatewayUsageMethod = Int("gatewayusagemethod", 0),
            GatewayCredsSource = Int("gatewaycredentialssource", 0),
            GatewayProfileUsageMethod = Int("gatewayprofileusagemethod", 0),
            KeyboardHookMode = Math.Clamp(Int("keyboardhook", 2), 0, 2),
            AutoReconnect = Bool("autoreconnection enabled", true),
            Compress = Bool("compression", true),
            BitmapPersistence = Bool("bitmapcachepersistenable", true),
            PerformanceFlags = performance,
            ConnectToAdministerServer = Bool("administrative session", false) || Bool("connect to console", false),
            SmartSizing = Bool("smart sizing", false),
            IsRemoteApp = Bool("remoteapplicationmode", false),
            RemoteApplicationProgram = Text("remoteapplicationprogram"),
            RemoteApplicationName = Text("remoteapplicationname"),
            RemoteApplicationArgs = Text("remoteapplicationcmdline"),
            RemoteApplicationExpandArgs = Bool("remoteapplicationexpandcmdline", true),
            RemoteApplicationFile = Text("remoteapplicationfile"),
            DisableRemoteAppCapsCheck = Bool("disableremoteappcapscheck", false),
        };
    }

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
