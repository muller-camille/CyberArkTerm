using System.Globalization;
using System.Xml.Linq;
using ZillaTerm.Core.Localization;

namespace ZillaTerm.Core.Migration;

/// <summary>
/// Connexions mRemoteNG (confCons.xml) : nœuds « Container » = dossiers, nœuds « Connection » = sessions, avec
/// l'utilisateur et le domaine hérités du dossier parent si demandé. Un fichier chiffré en entier est refusé (il faut
/// l'exporter sans ce chiffrement) ; les mots de passe ne sont jamais lus.
/// </summary>
public static class MRemoteNgFile
{
    public static List<ImportedSession> Read(string text)
    {
        var root = ImportText.ParseXml(text).Root;
        if (root is null || root.Name.LocalName != "Connections")
        {
            throw new InvalidDataException(string.Format(CultureInfo.CurrentCulture, CoreStrings.MigrationWrongFile, "mRemoteNG (confCons.xml)"));
        }

        if (string.Equals((string?)root.Attribute("FullFileEncryption"), "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(CoreStrings.MigrationEncryptedFile);
        }

        var sessions = new List<ImportedSession>();
        Walk(root, "", new Inherited(null, null), sessions);
        return sessions;
    }

    private sealed record Inherited(string? User, string? Domain);

    private static void Walk(XElement parent, string folder, Inherited inherited, List<ImportedSession> sessions)
    {
        foreach (var node in parent.Elements().Where(e => e.Name.LocalName == "Node"))
        {
            var name = ((string?)node.Attribute("Name") ?? "").Trim();
            var user = Inherit(node, "Username", inherited.User);
            var domain = Inherit(node, "Domain", inherited.Domain);
            if (string.Equals((string?)node.Attribute("Type"), "Container", StringComparison.OrdinalIgnoreCase))
            {
                Walk(node, SessionFolders.Combine(folder, name), new Inherited(user, domain), sessions);
                continue;
            }

            var host = ((string?)node.Attribute("Hostname") ?? "").Trim();
            if (host.Length == 0)
            {
                continue;
            }

            var port = ImportText.Port((string?)node.Attribute("Port"));
            var protocol = ((string?)node.Attribute("Protocol") ?? "").Trim();
            sessions.Add(protocol.ToUpperInvariant() switch
            {
                "RDP" => ImportedSession.Rdp(folder, name, host, user, domain, (string?)node.Attribute("RDPStartProgram")).WithPort(port),
                "SSH1" or "SSH2" => ImportedSession.Terminal(folder, name, ImportProtocol.Ssh, host, port, WithDomain(user, domain)),
                "TELNET" => ImportedSession.Terminal(folder, name, ImportProtocol.Telnet, host, port, WithDomain(user, domain)),
                _ => ImportedSession.Unsupported(folder, name, protocol.Length > 0 ? protocol : "?", host, port),
            });
        }
    }

    /// <summary>Valeur du nœud, ou celle du dossier parent si le nœud en hérite (« InheritUsername="true" »).</summary>
    private static string? Inherit(XElement node, string attribute, string? parent) =>
        string.Equals((string?)node.Attribute("Inherit" + attribute), "true", StringComparison.OrdinalIgnoreCase)
            ? parent
            : ((string?)node.Attribute(attribute))?.Trim() is { Length: > 0 } value ? value : null;

    private static string? WithDomain(string? user, string? domain) =>
        user is null ? null : domain is null || user.Contains('\\') || user.Contains('@') ? user : $"{domain}\\{user}";
}
