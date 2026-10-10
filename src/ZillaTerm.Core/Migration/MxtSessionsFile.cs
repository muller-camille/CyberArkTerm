using System.Text.RegularExpressions;

namespace ZillaTerm.Core.Migration;

/// <summary>
/// Fichier de sessions .mxtsessions (ou fichier .ini de configuration au même format) : sections « [Bookmarks] »,
/// « [Bookmarks_1] »..., dont « SubRep » donne le dossier (« Prod\Web ») ; chaque autre ligne est une session
/// « nom=#icône#type%serveur%port%utilisateur%... ». Les autres sections (mots de passe compris) ne sont pas lues.
/// </summary>
public static partial class MxtSessionsFile
{
    public static List<ImportedSession> Read(string text)
    {
        var sessions = new List<ImportedSession>();
        foreach (var (section, values) in IniFile.Parse(text, (section, _) => BookmarksSection().IsMatch(section)))
        {
            if (!BookmarksSection().IsMatch(section))
            {
                continue;
            }

            var folder = SessionFolders.Normalize(values.LastOrDefault(v => v.Key.Equals("SubRep", StringComparison.OrdinalIgnoreCase)).Value);
            foreach (var (name, value) in values)
            {
                if (!name.Equals("SubRep", StringComparison.OrdinalIgnoreCase) && !name.Equals("ImgNum", StringComparison.OrdinalIgnoreCase)
                    && Parse(folder, name, value) is { } session)
                {
                    sessions.Add(session);
                }
            }
        }

        return sessions;
    }

    /// <summary>« #109#0%srv01%22%root%... » : type de session après l'icône, puis serveur, port et utilisateur.</summary>
    internal static ImportedSession? Parse(string folder, string name, string value)
    {
        var match = Definition().Match(value);
        if (!match.Success)
        {
            return null;
        }

        var fields = match.Groups["rest"].Value.Split('%');
        var host = fields.ElementAtOrDefault(0)?.Trim() ?? "";
        if (host.Length == 0)
        {
            return null;
        }

        var port = ImportText.Port(fields.ElementAtOrDefault(1));
        var user = fields.ElementAtOrDefault(2);
        int type = int.Parse(match.Groups["type"].Value, System.Globalization.CultureInfo.InvariantCulture);
        int icon = int.Parse(match.Groups["icon"].Value, System.Globalization.CultureInfo.InvariantCulture);
        return (type, icon) switch
        {
            (0, _) => ImportedSession.Terminal(folder, name, ImportProtocol.Ssh, host, port, user),
            (1, _) => ImportedSession.Terminal(folder, name, ImportProtocol.Telnet, host, port, user),
            (4, _) => ImportedSession.Rdp(folder, name, host, user).WithPort(port),
            (7, _) => ImportedSession.Terminal(folder, name, ImportProtocol.Sftp, host, port, user),
            (2, _) => ImportedSession.Unsupported(folder, name, "Rsh", host, port),
            (3, _) => ImportedSession.Unsupported(folder, name, "XDMCP", host, port),
            (5, _) => ImportedSession.Unsupported(folder, name, "VNC", host, port),
            (6, _) => ImportedSession.Unsupported(folder, name, "FTP", host, port),
            (8, _) => ImportedSession.Unsupported(folder, name, "Serial", host, port),
            (12, _) => ImportedSession.Unsupported(folder, name, "Mosh", host, port),
            (13, _) => ImportedSession.Unsupported(folder, name, "S3", host, port),

            // Type inconnu : l'icône par défaut du type (SSH, Bureau à distance, Telnet).
            (_, 109) => ImportedSession.Terminal(folder, name, ImportProtocol.Ssh, host, port, user),
            (_, 91) => ImportedSession.Rdp(folder, name, host, user).WithPort(port),
            (_, 98) => ImportedSession.Terminal(folder, name, ImportProtocol.Telnet, host, port, user),
            _ => ImportedSession.Unsupported(folder, name, $"type {type}", host, port),
        };
    }

    [GeneratedRegex(@"^Bookmarks(_\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BookmarksSection();

    // [0-9] et non \d, qui accepterait aussi des chiffres d'autres écritures, refusés ensuite par int.Parse.
    [GeneratedRegex(@"^\s*#(?<icon>[0-9]{1,6})#(?<type>[0-9]{1,6})%(?<rest>.*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex Definition();
}
