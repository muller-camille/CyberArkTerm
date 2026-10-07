namespace ZillaTerm.Core.Migration;

/// <summary>
/// Fichiers Bureau à distance (.rdp) d'un dossier et de ses sous-dossiers : une session par fichier, nommée comme lui.
/// « full address », « username » et « domain » donnent le serveur et le compte ; un programme de démarrage PSM
/// (« alternate shell:s:psm /u compte /a serveur /c composant ») donne le compte, le serveur et le composant cibles.
/// </summary>
public static class RdpFiles
{
    public static List<ImportedSession> FromFolder(string directory)
    {
        var sessions = new List<ImportedSession>();
        foreach (var (folder, path) in ImportText.Files(directory, "*.rdp"))
        {
            if (ImportText.ReadSessionFile(path) is { } text && Parse(folder, Path.GetFileNameWithoutExtension(path), text) is { } session)
            {
                sessions.Add(session);
            }
        }

        return sessions;
    }

    /// <summary>Session d'un fichier .rdp (lignes « nom:type:valeur ») ; null sans serveur.</summary>
    public static ImportedSession? Parse(string folder, string name, string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            int first = line.IndexOf(':');
            int second = first < 0 ? -1 : line.IndexOf(':', first + 1);
            if (second < 0)
            {
                continue;
            }

            var key = line[..first].Trim().ToLowerInvariant();
            if (key is "full address" or "username" or "domain" or "alternate shell" or "server port")
            {
                values.TryAdd(key, line[(second + 1)..].Trim());
            }
        }

        var address = values.GetValueOrDefault("full address");
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        return ImportedSession.Rdp(folder, name, address, values.GetValueOrDefault("username"), values.GetValueOrDefault("domain"),
                values.GetValueOrDefault("alternate shell"))
            .WithPort(ImportText.Port(values.GetValueOrDefault("server port")));
    }
}
