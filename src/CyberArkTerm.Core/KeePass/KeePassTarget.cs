using System.Globalization;
using CyberArkTerm.Core.Rdp;

namespace CyberArkTerm.Core.KeePass;

public enum RemoteProtocol
{
    Unknown,
    Ssh,
    Rdp,
}

/// <summary>
/// Serveur à joindre d'après une entrée KeePass : protocole, hôte et port tirés de l'URL (<c>ssh://hôte:22</c>,
/// <c>rdp://hôte</c>, <c>hôte:3389</c>), de champs personnalisés (« Protocol », « Host », « Port »), des étiquettes
/// (« ssh », « rdp ») ou à défaut du titre, s'il ressemble à un nom de serveur.
/// </summary>
public sealed record KeePassTarget(RemoteProtocol Protocol, string Host, int Port, string UserName)
{
    public const int SshPort = 22;

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
                "ssh" or "sftp" or "scp" => RemoteProtocol.Ssh,
                "rdp" or "ms-rd" or "mstsc" => RemoteProtocol.Rdp,
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
                _ => RemoteProtocol.Unknown,
            };
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

    public static int DefaultPort(RemoteProtocol protocol) => protocol == RemoteProtocol.Rdp ? RdpConnectionSettings.DefaultPort : SshPort;

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
        "ssh" or "sftp" or "scp" or "linux" or "unix" => RemoteProtocol.Ssh,
        "rdp" or "windows" or "bureau à distance" or "remote desktop" => RemoteProtocol.Rdp,
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
