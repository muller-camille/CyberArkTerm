namespace ZillaTerm.Core.Migration;

/// <summary>
/// Sites WinSCP : registre (HKCU\Software\Martin Prikryl\WinSCP 2\Sessions, lu directement ou exporté en .reg) ou fichier
/// WinSCP.ini (sections « [Sessions\nom] »). Le nom « Prod/Web/srv01 » range le site dans des dossiers. Seuls le serveur,
/// le port, l'utilisateur et le protocole sont lus, jamais le mot de passe.
/// </summary>
public static class WinScpSites
{
    public const string RegistryKey = @"Software\Martin Prikryl\WinSCP 2\Sessions";

    public static readonly IReadOnlyList<string> ValueNames = ["HostName", "PortNumber", "UserName", "FSProtocol", "Ftps", "IsWorkspace"];

    public static List<ImportedSession> FromRegistry(IEnumerable<RegKey> keys) =>
        RegKey.ChildrenOf(keys, RegistryKey)
            .Select(k => FromValues(k.Name, k.Key.GetString, k.Key.GetInt))
            .OfType<ImportedSession>()
            .ToList();

    public static List<ImportedSession> FromIni(string text)
    {
        var sessions = new List<ImportedSession>();
        foreach (var (section, values) in IniFile.Parse(text, (section, key) => ValueNames.Contains(key, StringComparer.OrdinalIgnoreCase)))
        {
            if (!section.StartsWith(@"Sessions\", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var map = values.GroupBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);
            if (FromValues(section[9..], n => map.GetValueOrDefault(n), n => ImportText.Int(map.GetValueOrDefault(n))) is { } session)
            {
                sessions.Add(session);
            }
        }

        return sessions;
    }

    private static ImportedSession? FromValues(string encodedName, Func<string, string?> text, Func<string, int?> number)
    {
        var path = ImportText.Unescape(encodedName).Trim();
        var host = text("HostName")?.Trim() ?? "";
        if (path.Length == 0 || path.Equals("Default Settings", StringComparison.OrdinalIgnoreCase) || host.Length == 0
            || number("IsWorkspace") is > 0)
        {
            return null;
        }

        var folder = SessionFolders.Parent(path);
        var name = SessionFolders.Name(path);
        var port = ImportText.Port(number("PortNumber"));

        // FSProtocol : 0 SCP, 1 SFTP (avec repli SCP, défaut), 2 SFTP, 5 FTP, 6 WebDAV, 7 S3.
        return number("FSProtocol") switch
        {
            null or 0 or 1 or 2 => ImportedSession.Terminal(folder, name, ImportProtocol.Sftp, host, port, text("UserName")),
            5 => ImportedSession.Unsupported(folder, name, number("Ftps") is > 0 ? "FTPS" : "FTP", host, port),
            6 => ImportedSession.Unsupported(folder, name, "WebDAV", host, port),
            7 => ImportedSession.Unsupported(folder, name, "S3", host, port),
            var other => ImportedSession.Unsupported(folder, name, $"FSProtocol {other}", host, port),
        };
    }
}
