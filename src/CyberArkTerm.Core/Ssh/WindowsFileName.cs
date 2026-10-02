using System.Text;

namespace CyberArkTerm.Core.Ssh;

/// <summary>
/// Nom de fichier Unix rendu valide pour Windows, pour un téléchargement. Un nom Unix peut contenir « \ », « : »,
/// être « .. » ou un nom réservé (« CON ») : sans nettoyage, un serveur pourrait faire écrire hors du dossier choisi.
/// </summary>
public static class WindowsFileName
{
    public const int MaxLength = 255;

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Un seul élément de chemin, sans séparateur : caractères interdits et de contrôle remplacés par « _ », points et
    /// espaces finaux retirés, « . » / « .. » / vide remplacés par « _ », nom réservé préfixé par « _ », 255 caractères
    /// au plus.
    /// </summary>
    public static string Sanitize(string name)
    {
        var text = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            text.Append(char.IsControl(c) || c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c);
        }

        var clean = text.ToString().TrimEnd(' ', '.');
        if (clean.Length == 0)
        {
            return "_";
        }

        // « CON », « con.txt », « COM1.tar.gz » : réservés quelle que soit l'extension.
        var stem = clean.Split('.')[0].TrimEnd(' ');
        if (Reserved.Contains(stem))
        {
            clean = "_" + clean;
        }

        if (clean.Length > MaxLength)
        {
            // Coupe en gardant l'extension quand elle est courte.
            var extension = Path.GetExtension(clean);
            clean = extension.Length is > 0 and <= 16
                ? clean[..(MaxLength - extension.Length)].TrimEnd(' ', '.') + extension
                : clean[..MaxLength].TrimEnd(' ', '.');
        }

        return clean;
    }

    /// <summary>Chemin relatif Windows (« dossier\sous-dossier\fichier ») fait d'éléments nettoyés.</summary>
    public static string RelativePath(IEnumerable<string> components) => string.Join('\\', components.Select(Sanitize));
}
