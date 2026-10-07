using System.Globalization;
using System.Xml.Linq;
using ZillaTerm.Core.Localization;

namespace ZillaTerm.Core.Migration;

/// <summary>
/// Sessions SecureCRT : dossier « Sessions » de la configuration (un fichier .ini par session, sous-dossiers = dossiers,
/// lignes « S:"Hostname"=srv01 », « D:"[SSH2] Port"=00000016 ») ou export XML des réglages (clés « Sessions » imbriquées).
/// Seuls le serveur, le port, le protocole et l'utilisateur sont lus.
/// </summary>
public static class SecureCrtSessions
{
    private static readonly string[] ValueNames = ["Hostname", "Username", "Protocol Name", "[SSH2] Port", "[SSH1] Port", "Port"];

    /// <summary>Dossier « Sessions » (ou le dossier de configuration qui le contient).</summary>
    public static List<ImportedSession> FromFolder(string directory)
    {
        var root = Path.Combine(directory, "Sessions");
        root = Directory.Exists(root) ? root : directory;
        var sessions = new List<ImportedSession>();
        foreach (var (folder, path) in ImportText.Files(root, "*.ini"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.Equals("__FolderData__", StringComparison.OrdinalIgnoreCase)
                || (folder.Length == 0 && name.Equals("Default", StringComparison.OrdinalIgnoreCase))
                || ImportText.ReadSessionFile(path) is not { } text)
            {
                continue;
            }

            if (FromValues(folder, name, ParseIni(text)) is { } session)
            {
                sessions.Add(session);
            }
        }

        return sessions;
    }

    /// <summary>Export XML (« File > Export Settings ») : clé « Sessions », dossiers et sessions en clés imbriquées.</summary>
    public static List<ImportedSession> FromXml(string text)
    {
        var root = ImportText.ParseXml(text).Root;
        var top = root?.Descendants("key").FirstOrDefault(k => string.Equals((string?)k.Attribute("name"), "Sessions", StringComparison.OrdinalIgnoreCase));
        if (root is null || top is null)
        {
            throw new InvalidDataException(string.Format(CultureInfo.CurrentCulture, CoreStrings.MigrationWrongFile, "SecureCRT (XML)"));
        }

        var sessions = new List<ImportedSession>();
        Walk(top, "", sessions);
        return sessions;
    }

    private static void Walk(XElement parent, string folder, List<ImportedSession> sessions)
    {
        foreach (var key in parent.Elements("key"))
        {
            var name = ((string?)key.Attribute("name") ?? "").Trim();
            var values = key.Elements()
                .Where(e => e.Name.LocalName != "key" && ValueNames.Contains((string?)e.Attribute("name"), StringComparer.OrdinalIgnoreCase))
                .GroupBy(e => (string)e.Attribute("name")!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Value.Trim(), StringComparer.OrdinalIgnoreCase);
            if (values.ContainsKey("Hostname") || values.ContainsKey("Protocol Name"))
            {
                if (!(folder.Length == 0 && name.Equals("Default", StringComparison.OrdinalIgnoreCase))
                    && FromValues(folder, name, values) is { } session)
                {
                    sessions.Add(session);
                }
            }
            else if (!name.Equals("__FolderData__", StringComparison.OrdinalIgnoreCase))
            {
                Walk(key, SessionFolders.Combine(folder, name), sessions);
            }
        }
    }

    /// <summary>Lignes « S:"Nom"=valeur » (texte) et « D:"Nom"=0000002a » (nombre en hexadécimal, rendu en décimal).</summary>
    internal static Dictionary<string, string> ParseIni(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length < 5 || line[1] != ':' || line[2] != '"')
            {
                continue;
            }

            int close = line.IndexOf("\"=", 3, StringComparison.Ordinal);
            if (close < 0)
            {
                continue;
            }

            var name = line[3..close];
            var value = line[(close + 2)..];
            if (!ValueNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line[0] is 'S' or 's')
            {
                values[name] = value.Trim();
            }
            else if (line[0] is 'D' or 'd' && uint.TryParse(value.Trim(), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var number))
            {
                values[name] = number.ToString(CultureInfo.InvariantCulture);
            }
        }

        return values;
    }

    private static ImportedSession? FromValues(string folder, string name, Dictionary<string, string> values)
    {
        var host = values.GetValueOrDefault("Hostname")?.Trim() ?? "";
        if (host.Length == 0)
        {
            return null;
        }

        var protocol = values.GetValueOrDefault("Protocol Name")?.Trim() ?? "SSH2";
        var user = values.GetValueOrDefault("Username");
        return protocol.ToUpperInvariant() switch
        {
            "SSH2" or "" => ImportedSession.Terminal(folder, name, ImportProtocol.Ssh, host, ImportText.Port(values.GetValueOrDefault("[SSH2] Port")), user),
            "SSH1" => ImportedSession.Terminal(folder, name, ImportProtocol.Ssh, host, ImportText.Port(values.GetValueOrDefault("[SSH1] Port")), user),
            "TELNET" => ImportedSession.Terminal(folder, name, ImportProtocol.Telnet, host, ImportText.Port(values.GetValueOrDefault("Port")), user),
            _ => ImportedSession.Unsupported(folder, name, protocol, host),
        };
    }
}
