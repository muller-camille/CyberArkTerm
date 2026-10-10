using System.Text;

namespace ZillaTerm.Core.Migration;

/// <summary>
/// Fichier de configuration du client OpenSSH (~/.ssh/config, %USERPROFILE%\.ssh\config) : chaque nom d'hôte sans joker
/// d'une ligne « Host » devient une session, avec « HostName », « User » et « Port » pris comme le fait ssh (première
/// valeur trouvée dans les blocs qui s'appliquent, blocs avec jokers compris). Les blocs « Match » ne sont pas évalués.
/// </summary>
public static class OpenSshConfig
{
    public static List<ImportedSession> Read(string text)
    {
        var blocks = new List<Block> { new(["*"]) };
        var aliases = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var (keyword, args) = Split(raw);
            if (keyword is null)
            {
                continue;
            }

            if (keyword.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                blocks.Add(new Block(args));
                aliases.AddRange(args.Where(a => !a.StartsWith('!') && a.IndexOfAny(['*', '?']) < 0));
            }
            else if (keyword.Equals("Match", StringComparison.OrdinalIgnoreCase))
            {
                blocks.Add(new Block([]));
            }
            else if (args.Count > 0 && blocks[^1].Options.All(o => !o.Key.Equals(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                blocks[^1].Options.Add(new KeyValuePair<string, string>(keyword, args[0]));
            }
        }

        var sessions = new List<ImportedSession>();
        foreach (var alias in aliases.Distinct(StringComparer.Ordinal))
        {
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var block in blocks.Where(b => b.Matches(alias)))
            {
                foreach (var (key, value) in block.Options)
                {
                    options.TryAdd(key, value);
                }
            }

            var host = options.GetValueOrDefault("HostName")?.Replace("%h", alias, StringComparison.Ordinal) ?? alias;
            sessions.Add(ImportedSession.Terminal("", alias, ImportProtocol.Ssh, host, ImportText.Port(options.GetValueOrDefault("Port")),
                options.GetValueOrDefault("User")));
        }

        return sessions;
    }

    private sealed class Block(IReadOnlyList<string> patterns)
    {
        public List<KeyValuePair<string, string>> Options { get; } = [];

        public bool Matches(string alias) =>
            patterns.Any(p => !p.StartsWith('!') && Glob(p, alias))
            && !patterns.Any(p => p.StartsWith('!') && Glob(p[1..], alias));
    }

    /// <summary>
    /// Motif « * » / « ? » d'OpenSSH, sans expression régulière : un motif plein d'étoiles ne peut pas faire
    /// exploser le temps de calcul (au plus longueur du motif × longueur du nom).
    /// </summary>
    internal static bool Glob(string pattern, string value)
    {
        int p = 0, v = 0, star = -1, resume = 0;
        while (v < value.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                resume = v;
            }
            else if (p < pattern.Length && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(value[v])))
            {
                p++;
                v++;
            }
            else if (star >= 0)
            {
                p = star + 1;
                v = ++resume;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

    /// <summary>« Host a b », « Port=2222 », « User "jean dupont" » → mot-clé et arguments ; commentaires ignorés.</summary>
    private static (string? Keyword, List<string> Args) Split(string line)
    {
        line = line.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
        {
            return (null, []);
        }

        int end = 0;
        while (end < line.Length && !char.IsWhiteSpace(line[end]) && line[end] != '=')
        {
            end++;
        }

        var keyword = line[..end];
        var rest = line[end..].TrimStart();
        if (rest.StartsWith('='))
        {
            rest = rest[1..];
        }

        var args = new List<string>();
        var current = new StringBuilder();
        bool quoted = false, any = false;
        foreach (char c in rest)
        {
            if (c == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any)
                {
                    args.Add(current.ToString());
                    current.Clear();
                    any = false;
                }
            }
            else if (c == '#' && !quoted && !any)
            {
                break;
            }
            else
            {
                current.Append(c);
                any = true;
            }
        }

        if (any)
        {
            args.Add(current.ToString());
        }

        return (keyword.Length > 0 ? keyword : null, args);
    }
}
