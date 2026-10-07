using System.Text;

namespace ZillaTerm.Core.KeePass;

/// <summary>
/// Chemin d'un dossier dans un coffre KeePass : noms séparés par « / » (« Serveurs/Prod »). Un « / » ou un « \ »
/// dans un nom est précédé d'un « \ » (« Linux\/Unix » est un seul dossier), pour que tout nom reste représentable.
/// </summary>
public static class KeePassGroupPath
{
    public const char Separator = '/';
    private const char Escape = '\\';

    /// <summary>Chemin du dossier <paramref name="name"/> dans <paramref name="parent"/> (« » pour la racine).</summary>
    public static string Combine(string parent, string name) =>
        parent.Length == 0 ? EscapeName(name) : $"{parent}{Separator}{EscapeName(name)}";

    /// <summary>Noms des dossiers successifs, sans échappement (« » pour la racine donne un seul nom vide).</summary>
    public static IReadOnlyList<string> Split(string path)
    {
        var names = new List<string>();
        var current = new StringBuilder();
        for (int i = 0; i < path.Length; i++)
        {
            if (IsEscape(path, i))
            {
                current.Append(path[++i]);
            }
            else if (path[i] == Separator)
            {
                names.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(path[i]);
            }
        }

        names.Add(current.ToString());
        return names;
    }

    /// <summary>Chemin du dossier parent (« » pour un dossier de premier niveau).</summary>
    public static string Parent(string path)
    {
        int last = -1;
        for (int i = 0; i < path.Length; i++)
        {
            if (IsEscape(path, i))
            {
                i++;
            }
            else if (path[i] == Separator)
            {
                last = i;
            }
        }

        return last >= 0 ? path[..last] : "";
    }

    /// <summary>Nom du dossier lui-même, sans échappement.</summary>
    public static string Name(string path) => Split(path)[^1];

    /// <summary>Vrai si <paramref name="path"/> est <paramref name="ancestor"/> ou l'un de ses sous-dossiers.</summary>
    public static bool IsWithin(string path, string ancestor) =>
        path == ancestor || path.StartsWith(ancestor + Separator, StringComparison.Ordinal);

    /// <summary>Saisie de l'utilisateur mise en forme : espaces autour des noms et noms vides retirés.</summary>
    public static string Normalize(string text) =>
        string.Join(Separator, Split(text).Select(n => n.Trim()).Where(n => n.Length > 0).Select(EscapeName));

    private static bool IsEscape(string path, int i) =>
        path[i] == Escape && i + 1 < path.Length && path[i + 1] is Separator or Escape;

    private static string EscapeName(string name) =>
        name.Replace(Escape.ToString(), $"{Escape}{Escape}", StringComparison.Ordinal)
            .Replace(Separator.ToString(), $"{Escape}{Separator}", StringComparison.Ordinal);
}
