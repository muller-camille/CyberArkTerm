using System.Diagnostics;
using System.IO;

namespace ZillaTerm.App.Services;

/// <summary>
/// Ouvre l'Explorateur de Windows sur un fichier (sélectionné dans son dossier) ou sur un dossier. L'Explorateur lit
/// lui-même sa ligne de commande (virgules comprises), sans les règles habituelles des arguments : le chemin, complet,
/// sans guillemet ni caractère de contrôle, lui est donc passé entre guillemets, et l'exécutable est celui du dossier
/// de Windows (jamais un « explorer.exe » posé à côté de l'application).
/// </summary>
internal static class WindowsExplorer
{
    private static string ExecutablePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    /// <summary>Dossier du fichier, le fichier sélectionné.</summary>
    /// <exception cref="ArgumentException">Chemin relatif, ou contenant un guillemet ou un caractère de contrôle.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">L'Explorateur n'a pas pu être lancé.</exception>
    public static void ShowFile(string path) => Start(SelectArguments(path));

    /// <summary>Contenu du dossier.</summary>
    /// <exception cref="ArgumentException">Chemin relatif, ou contenant un guillemet ou un caractère de contrôle.</exception>
    /// <exception cref="System.ComponentModel.Win32Exception">L'Explorateur n'a pas pu être lancé.</exception>
    public static void OpenFolder(string folder) => Start(FolderArguments(folder));

    /// <summary>« /select,"chemin" ».</summary>
    internal static string SelectArguments(string path) => $"/select,\"{Checked(path)}\"";

    /// <summary>« "dossier" » (sans séparateur final, qui suivi d'un guillemet en ferait un caractère du chemin).</summary>
    internal static string FolderArguments(string folder) => $"\"{Path.TrimEndingDirectorySeparator(Checked(folder))}\"";

    private static string Checked(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(c => c == '"' || char.IsControl(c)) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"Chemin refusé pour l'Explorateur : {path}", nameof(path));
        }

        return Path.GetFullPath(path);
    }

    private static void Start(string arguments) =>
        Process.Start(new ProcessStartInfo(ExecutablePath, arguments) { UseShellExecute = false })?.Dispose();
}
