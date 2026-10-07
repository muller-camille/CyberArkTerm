using System.Globalization;
using System.Text;
using ZillaTerm.Core.Localization;

namespace ZillaTerm.Core.Migration;

/// <summary>
/// Clé du registre Windows et ses valeurs utiles (texte ou nombre), lue dans le registre ou dans un export .reg. Seules
/// les valeurs demandées par le lecteur sont gardées : jamais un mot de passe.
/// </summary>
public sealed class RegKey(string path, IReadOnlyDictionary<string, object> values)
{
    public string Path { get; } = path;

    public IReadOnlyDictionary<string, object> Values { get; } = values;

    public string? GetString(string name) => Values.TryGetValue(name, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;

    public int? GetInt(string name) => Values.TryGetValue(name, out var value)
        ? value as int? ?? ImportText.Int(Convert.ToString(value, CultureInfo.InvariantCulture))
        : null;

    /// <summary>
    /// Sous-clés directes de <paramref name="parent"/> (« Software\SimonTatham\PuTTY\Sessions »), quelle que soit la
    /// ruche (« HKEY_CURRENT_USER\… », « HKEY_USERS\S-1-5-…\… »), avec leur nom.
    /// </summary>
    public static IEnumerable<(string Name, RegKey Key)> ChildrenOf(IEnumerable<RegKey> keys, string parent)
    {
        var marker = "\\" + parent.Trim('\\') + "\\";
        foreach (var key in keys)
        {
            var path = "\\" + key.Path.Trim('\\');
            int at = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                continue;
            }

            var name = path[(at + marker.Length)..];
            if (name.Length > 0 && !name.Contains('\\'))
            {
                yield return (name, key);
            }
        }
    }
}

/// <summary>Export du registre (.reg, « Windows Registry Editor Version 5.00 » ou « REGEDIT4 »).</summary>
public static class RegFile
{
    /// <summary>Clés du fichier avec les valeurs nommées dans <paramref name="valueNames"/> (chaînes et DWORD).</summary>
    public static List<RegKey> Parse(string text, IEnumerable<string> valueNames)
    {
        var wanted = new HashSet<string>(valueNames, StringComparer.OrdinalIgnoreCase);
        var lines = Join(text);
        var first = lines.FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
        if (!first.StartsWith("Windows Registry Editor", StringComparison.OrdinalIgnoreCase)
            && !first.Equals("REGEDIT4", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(CoreStrings.MigrationNotRegFile);
        }

        var keys = new List<RegKey>();
        Dictionary<string, object>? current = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                current = null;
                if (line.EndsWith(']') && !line.StartsWith("[-", StringComparison.Ordinal))
                {
                    current = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    keys.Add(new RegKey(line[1..^1], current));
                }

                continue;
            }

            if (current is null || !line.StartsWith('"') || !TryReadQuoted(line, 0, out var name, out int end))
            {
                continue;
            }

            var data = line[end..].TrimStart();
            if (!data.StartsWith('=') || !wanted.Contains(name))
            {
                continue;
            }

            data = data[1..].Trim();
            if (data.StartsWith('"') && TryReadQuoted(data, 0, out var value, out _))
            {
                current[name] = value;
            }
            else if (data.StartsWith("dword:", StringComparison.OrdinalIgnoreCase)
                     && uint.TryParse(data[6..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var dword))
            {
                current[name] = unchecked((int)dword);
            }
        }

        return keys;
    }

    /// <summary>Lignes du fichier, celles qui se poursuivent (« \ » en fin de ligne, valeurs hex) réunies.</summary>
    private static List<string> Join(string text)
    {
        var lines = new List<string>();
        var pending = new StringBuilder();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.TrimEnd();
            if (trimmed.EndsWith('\\') && !trimmed.StartsWith('['))
            {
                pending.Append(trimmed[..^1]);
                continue;
            }

            pending.Append(pending.Length > 0 ? line.TrimStart() : line);
            lines.Add(pending.ToString());
            pending.Clear();
        }

        if (pending.Length > 0)
        {
            lines.Add(pending.ToString());
        }

        return lines;
    }

    /// <summary>Chaîne entre guillemets à partir de <paramref name="start"/> (« \\ » et « \" » échappés).</summary>
    private static bool TryReadQuoted(string line, int start, out string value, out int end)
    {
        var sb = new StringBuilder();
        for (int i = start + 1; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\\' && i + 1 < line.Length)
            {
                sb.Append(line[++i]);
            }
            else if (c == '"')
            {
                value = sb.ToString();
                end = i + 1;
                return true;
            }
            else
            {
                sb.Append(c);
            }
        }

        value = "";
        end = line.Length;
        return false;
    }
}

/// <summary>Fichier .ini : sections et paires « clé=valeur », dans l'ordre du fichier.</summary>
internal static class IniFile
{
    /// <summary>Sections et valeurs gardées par <paramref name="keep"/> (section, clé).</summary>
    public static List<(string Section, List<KeyValuePair<string, string>> Values)> Parse(string text, Func<string, string, bool> keep)
    {
        var sections = new List<(string, List<KeyValuePair<string, string>>)>();
        List<KeyValuePair<string, string>>? current = null;
        string section = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                current = [];
                sections.Add((section, current));
                continue;
            }

            int eq = line.IndexOf('=');
            if (current is null || eq <= 0)
            {
                continue;
            }

            var key = line[..eq].Trim();
            if (keep(section, key))
            {
                current.Add(new KeyValuePair<string, string>(key, line[(eq + 1)..]));
            }
        }

        return sections;
    }
}
