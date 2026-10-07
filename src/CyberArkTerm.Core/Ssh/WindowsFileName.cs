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
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM\u00B9", "COM\u00B2", "COM\u00B3",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT\u00B9", "LPT\u00B2", "LPT\u00B3",
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
}
