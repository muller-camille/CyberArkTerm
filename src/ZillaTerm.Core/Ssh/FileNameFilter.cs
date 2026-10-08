using System.Text.RegularExpressions;

namespace ZillaTerm.Core.Ssh;

/// <summary>
/// Filtre de l'onglet Fichiers sur le nom des éléments du dossier affiché, sans tenir compte de la casse : une partie du
/// nom (« nginx »), ou un masque avec <c>*</c> et <c>?</c> qui doit correspondre au nom entier (« *.log »,
/// « app?.conf »). Plusieurs filtres séparés par « ; » : un élément est gardé s'il répond à l'un d'eux
/// (« *.log;*.gz »).
/// </summary>
public sealed class FileNameFilter
{
    private readonly string[] _parts;
    private readonly Regex[] _masks;

    private FileNameFilter(string[] parts, Regex[] masks)
    {
        _parts = parts;
        _masks = masks;
    }

    /// <summary>Filtre saisi ; null s'il est vide (tout est affiché).</summary>
    public static FileNameFilter? Parse(string? text)
    {
        var items = (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (items.Length == 0)
        {
            return null;
        }

        var parts = items.Where(i => i.IndexOfAny(['*', '?']) < 0).ToArray();
        // Sans retour arrière : un masque comme « *a*a*a*b » reste rapide sur un dossier de plusieurs milliers d'éléments.
        var masks = items.Where(i => i.IndexOfAny(['*', '?']) >= 0)
            .Select(i => new Regex("^" + Regex.Escape(i).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
            .ToArray();
        return new FileNameFilter(parts, masks);
    }

    public bool Matches(string name) =>
        _parts.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase)) || _masks.Any(m => m.IsMatch(name));
}
