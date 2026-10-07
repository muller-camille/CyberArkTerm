using System.Globalization;
using ZillaTerm.Core.Rdp;

namespace ZillaTerm.Core.KeePass;

public enum RemoteProtocol
{
    Unknown,
    Ssh,
    Rdp,

    /// <summary>Bureau VNC (RFB), dans un onglet.</summary>
    Vnc,

    /// <summary>Fichiers seulement, en SFTP (sans terminal).</summary>
    Sftp,

    /// <summary>FTP : TLS si le serveur le propose (AUTH TLS), sinon en clair après confirmation.</summary>
    Ftp,

    /// <summary>FTP avec TLS explicite exigé (ftpes://).</summary>
    Ftpes,

    /// <summary>FTP avec TLS implicite (ftps://, port 990).</summary>
    Ftps,
}

/// <summary>
/// Serveur à joindre d'après une entrée KeePass : protocole, hôte et port tirés de l'URL (<c>ssh://hôte:22</c>,
/// <c>rdp://hôte</c>, <c>vnc://hôte:1</c>, <c>sftp://</c>, <c>ftp://</c>, <c>ftpes://</c>, <c>ftps://</c>,
/// <c>hôte:3389</c>), de champs personnalisés (« Protocol », « Host », « Port »), des étiquettes (« ssh », « rdp »,
/// « vnc », « ftp »…) ou à défaut du titre, s'il ressemble à un nom de serveur.
/// </summary>
public sealed record KeePassTarget(RemoteProtocol Protocol, string Host, int Port, string UserName)
{
    public const int SshPort = 22;
    public const int VncPort = 5900;
    public const int FtpPort = 21;
    public const int FtpsPort = 990;

    public static KeePassTarget From(KeePassEntry entry)
    {
        var protocol = RemoteProtocol.Unknown;
        string host = "";
        int? port = null;

        var url = entry.Url.Trim();
        if (url.Length > 0)
        {
            var scheme = url.IndexOf("://", StringComparison.Ordinal) is var i and > 0 ? url[..i].ToLowerInvariant() : "";
            protocol = scheme switch
            {
                "ssh" or "scp" => RemoteProtocol.Ssh,
                "rdp" or "ms-rd" or "mstsc" => RemoteProtocol.Rdp,
                "vnc" => RemoteProtocol.Vnc,
                "sftp" => RemoteProtocol.Sftp,
                "ftp" => RemoteProtocol.Ftp,
                "ftpes" => RemoteProtocol.Ftpes,
                "ftps" => RemoteProtocol.Ftps,
                _ => RemoteProtocol.Unknown,
            };
            if (scheme.Length == 0 || protocol != RemoteProtocol.Unknown || scheme is "http" or "https")
            {
                (host, port) = HostAndPort(scheme.Length > 0 ? url[(scheme.Length + 3)..] : url);
            }
        }

        if (Field(entry, "Protocol", "Protocole", "Protocollo") is { } p)
        {
            protocol = ParseProtocol(p) ?? protocol;
        }

        if (Field(entry, "Host", "Hôte", "Hostname", "Server", "Serveur") is { Length: > 0 } h)
        {
            (host, var hostPort) = HostAndPort(h);
            port = hostPort ?? port;
        }

        if (Field(entry, "Port") is { } portText && int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var custom)
            && custom is > 0 and <= 65535)
        {
            port = custom;
        }

        if (protocol == RemoteProtocol.Unknown)
        {
            var tags = entry.Tags.Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            protocol = tags.Select(ParseProtocol).FirstOrDefault(t => t is not null) ?? protocol;
        }

        if (protocol == RemoteProtocol.Unknown)
        {
            protocol = port switch
            {
                SshPort => RemoteProtocol.Ssh,
                RdpConnectionSettings.DefaultPort => RemoteProtocol.Rdp,
                VncPort => RemoteProtocol.Vnc,
                FtpPort => RemoteProtocol.Ftp,
                FtpsPort => RemoteProtocol.Ftps,
                _ => RemoteProtocol.Unknown,
            };
        }

        // VNC : « hôte:1 » désigne l'écran 1, soit le port 5901.
        if (protocol == RemoteProtocol.Vnc && port is < 100)
        {
            port = VncPort + port;
        }

        if (host.Length == 0 && LooksLikeHost(entry.Title))
        {
            host = entry.Title.Trim();
        }

        return new KeePassTarget(protocol, host, port ?? DefaultPort(protocol), entry.UserName.Trim());
    }

    /// <summary>Même serveur avec un autre protocole (choisi par l'utilisateur) : le port suit s'il était implicite.</summary>
    public KeePassTarget WithProtocol(RemoteProtocol protocol) =>
        this with { Protocol = protocol, Port = Port == DefaultPort(Protocol) ? DefaultPort(protocol) : Port };

    public static int DefaultPort(RemoteProtocol protocol) => protocol switch
    {
        RemoteProtocol.Rdp => RdpConnectionSettings.DefaultPort,
        RemoteProtocol.Vnc => VncPort,
        RemoteProtocol.Ftp or RemoteProtocol.Ftpes => FtpPort,
        RemoteProtocol.Ftps => FtpsPort,
        _ => SshPort,
    };

    /// <summary>Nom court du protocole (« SSH », « VNC »…) ; vide s'il n'est pas connu.</summary>
    public static string Name(RemoteProtocol protocol) => protocol switch
    {
        RemoteProtocol.Ssh => "SSH",
        RemoteProtocol.Rdp => "RDP",
        RemoteProtocol.Vnc => "VNC",
        RemoteProtocol.Sftp => "SFTP",
        RemoteProtocol.Ftp => "FTP",
        RemoteProtocol.Ftpes => "FTPES",
        RemoteProtocol.Ftps => "FTPS",
        _ => "",
    };

    /// <summary>Protocole de transfert de fichiers seulement (onglet Fichiers, sans terminal).</summary>
    public static bool IsFileTransfer(RemoteProtocol protocol) =>
        protocol is RemoteProtocol.Sftp or RemoteProtocol.Ftp or RemoteProtocol.Ftpes or RemoteProtocol.Ftps;

    /// <summary>
    /// Recherche dans « Mes serveurs » : titre, utilisateur, URL, dossier, étiquettes, serveur et protocole de
    /// l'entrée (pas les notes ni les champs personnalisés).
    /// </summary>
    public static bool Matches(KeePassEntry entry, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var target = From(entry);
        var protocol = Name(target.Protocol) is { Length: > 0 } name ? name : null;
        return SearchQuery.Matches(query, entry.Title, entry.UserName, entry.Url, entry.Group, entry.Tags, target.Host, target.UserName, protocol);
    }

    /// <summary>« hôte » ou « hôte:port » pour l'affichage.</summary>
    public string Address => Port == DefaultPort(Protocol) ? Host : Host.Contains(':') ? $"[{Host}]:{Port}" : $"{Host}:{Port}";

    private static (string Host, int? Port) HostAndPort(string text)
    {
        // Retire « utilisateur@ », le chemin et les paramètres : ssh://root@srv:22/chemin?x
        var rest = text.Trim();
        int end = rest.IndexOfAny(['/', '?', '#']);
        if (end >= 0)
        {
            rest = rest[..end];
        }

        int at = rest.LastIndexOf('@');
        if (at >= 0)
        {
            rest = rest[(at + 1)..];
        }

        return RdpConnectionSettings.SplitAddress(rest);
    }

    private static RemoteProtocol? ParseProtocol(string text) => text.Trim().ToLowerInvariant() switch
    {
        "ssh" or "scp" or "linux" or "unix" => RemoteProtocol.Ssh,
        "rdp" or "windows" or "bureau à distance" or "remote desktop" => RemoteProtocol.Rdp,
        "vnc" or "rfb" => RemoteProtocol.Vnc,
        "sftp" => RemoteProtocol.Sftp,
        "ftp" => RemoteProtocol.Ftp,
        "ftpes" => RemoteProtocol.Ftpes,
        "ftps" => RemoteProtocol.Ftps,
        _ => null,
    };

    private static string? Field(KeePassEntry entry, params string[] names) =>
        names.Select(n => entry.CustomFields.FirstOrDefault(f => string.Equals(f.Key, n, StringComparison.OrdinalIgnoreCase)).Value)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static bool LooksLikeHost(string title)
    {
        var t = title.Trim();
        return t.Length is > 0 and <= 253 && t.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or ':');
    }
}
