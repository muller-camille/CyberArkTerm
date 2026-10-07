using System.Globalization;
using System.Xml.Linq;
using ZillaTerm.Core.Localization;

namespace ZillaTerm.Core.Migration;

/// <summary>
/// Fichier Remote Desktop Connection Manager (.rdg) : groupes = dossiers, serveurs = sessions Bureau à distance.
/// L'utilisateur et le domaine viennent des « logonCredentials » du serveur ou de son groupe (héritage), ou d'un profil
/// d'identifiants nommé ; le port et le programme de démarrage (connexion via PSM) des « connectionSettings ». Les mots de
/// passe ne sont jamais lus. Formats 2.2 (nom directement sous le serveur) et 2.7 et suivants (sous « properties »).
/// </summary>
public static class RdcManFile
{
    public static List<ImportedSession> Read(string text)
    {
        var root = ImportText.ParseXml(text).Root;
        var file = root?.Element("file");
        if (root is null || root.Name.LocalName != "RDCMan" || file is null)
        {
            throw new InvalidDataException(string.Format(CultureInfo.CurrentCulture, CoreStrings.MigrationWrongFile, "Remote Desktop Connection Manager (.rdg)"));
        }

        var profiles = root.Descendants("credentialsProfile")
            .Select(p => (Name: Value(p, "profileName"), Credentials: new Logon(Value(p, "userName"), Value(p, "domain"))))
            .Where(p => p.Name is not null)
            .GroupBy(p => p.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Credentials, StringComparer.OrdinalIgnoreCase);
        var sessions = new List<ImportedSession>();
        var top = new Settings(Credentials(file, new Logon(null, null), profiles), Connection(file, new Link(null, null)));
        Walk(file, "", top, profiles, sessions);
        return sessions;
    }

    private sealed record Logon(string? User, string? Domain);

    private sealed record Link(int? Port, string? StartProgram);

    private sealed record Settings(Logon Credentials, Link Connection);

    private static void Walk(XElement parent, string folder, Settings settings, Dictionary<string, Logon> profiles, List<ImportedSession> sessions)
    {
        foreach (var element in parent.Elements())
        {
            if (element.Name.LocalName is not ("group" or "server"))
            {
                continue;
            }

            var own = new Settings(Credentials(element, settings.Credentials, profiles), Connection(element, settings.Connection));
            var properties = element.Element("properties") ?? element;
            if (element.Name.LocalName == "group")
            {
                Walk(element, SessionFolders.Combine(folder, Value(properties, "name") ?? ""), own, profiles, sessions);
                continue;
            }

            var host = Value(properties, "name");
            if (host is null)
            {
                continue;
            }

            var name = Value(properties, "displayName") ?? host;
            sessions.Add(ImportedSession.Rdp(folder, name, host, own.Credentials.User, own.Credentials.Domain, own.Connection.StartProgram)
                .WithPort(own.Connection.Port));
        }
    }

    /// <summary>Identifiants du nœud : les siens (« inherit="None" »), ceux d'un profil nommé, ou ceux du parent.</summary>
    private static Logon Credentials(XElement node, Logon parent, Dictionary<string, Logon> profiles)
    {
        var logon = node.Element("logonCredentials");
        if (logon is null || !string.Equals((string?)logon.Attribute("inherit"), "None", StringComparison.OrdinalIgnoreCase))
        {
            return parent;
        }

        var profile = Value(logon, "profileName");
        if (profile is not null && !profile.Equals("Custom", StringComparison.OrdinalIgnoreCase)
            && profiles.TryGetValue(profile, out var named))
        {
            return named;
        }

        return new Logon(Value(logon, "userName"), Value(logon, "domain"));
    }

    private static Link Connection(XElement node, Link parent)
    {
        var settings = node.Element("connectionSettings");
        if (settings is null || !string.Equals((string?)settings.Attribute("inherit"), "None", StringComparison.OrdinalIgnoreCase))
        {
            return parent;
        }

        return new Link(ImportText.Port(Value(settings, "port")), Value(settings, "startProgram"));
    }

    private static string? Value(XElement parent, string name) =>
        parent.Element(name)?.Value.Trim() is { Length: > 0 } value ? value : null;
}
