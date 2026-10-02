using CyberArkTerm.Core.Localization;

namespace CyberArkTerm.Core.Terminal;

/// <summary>
/// Dossier courant du shell distant, transmis par la séquence OSC 7
/// (<c>ESC ] 7 ; file://hôte/chemin BEL</c>) que CyberArkTerm installe via PROMPT_COMMAND.
/// </summary>
public static class WorkingDirectory
{
    /// <summary>
    /// Commande injectée dans le shell bash/zsh à l'ouverture de la session pour qu'il annonce son
    /// dossier courant à chaque invite. Préfixée d'une espace pour ne pas entrer dans l'historique.
    /// <paramref name="linesToErase"/> lignes sont ensuite effacées pour masquer la commande tapée.
    /// </summary>
    public static string InjectionCommand(int linesToErase, string? startDirectory = null)
    {
        var erase = linesToErase > 0 ? $"printf '\\033[{linesToErase}A\\r\\033[J'" : "true";
        var cd = startDirectory is null ? "" : $"cd -- {ShellQuote(startDirectory)} 2>/dev/null;";
        return " " + cd + "__catosc7(){ printf '\\033]7;%s\\007' \"$PWD\";};" +
               "PROMPT_COMMAND=\"__catosc7${PROMPT_COMMAND:+;$PROMPT_COMMAND}\";" +
               "[ -n \"$ZSH_VERSION\" ]&&eval 'precmd_functions+=(__catosc7)';" +
               erase + "\r";
    }

    /// <summary>
    /// Commande d'injection qui efface exactement les lignes occupées par son propre écho :
    /// l'invite occupe <paramref name="cursorColumn"/> colonnes avant la commande, sur un terminal
    /// de <paramref name="columns"/> colonnes.
    /// </summary>
    public static string InjectionFor(int cursorColumn, int columns, string? startDirectory = null)
    {
        columns = Math.Max(columns, 1);
        int lines = 1;
        for (int i = 0; i < 4; i++)
        {
            int length = InjectionCommand(lines, startDirectory).Length - 1; // sans le \r final
            int needed = (cursorColumn + length - 1) / columns + 1;
            if (needed == lines)
            {
                break;
            }

            lines = needed;
        }

        return InjectionCommand(lines, startDirectory);
    }

    /// <summary>« cd » visible vers le dossier de départ, quand le suivi du dossier n'est pas installé.</summary>
    public static string ChangeDirectoryCommand(string directory) => $" cd -- {ShellQuote(directory)}\r";

    /// <summary>Protège une valeur pour le shell POSIX : entre apostrophes, « ' » devenant « '\'' ».</summary>
    public static string ShellQuote(string value)
    {
        if (value.Any(char.IsControl))
        {
            throw new ArgumentException(CoreStrings.ControlCharacterInPath);
        }

        return "'" + value.Replace("'", "'\\''") + "'";
    }

    /// <summary>Extrait le chemin d'un OSC 7 : <c>file://hôte/chemin%20encodé</c> ou chemin absolu brut.</summary>
    public static string? Parse(string value)
    {
        value = value.Trim();
        if (value.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            int slash = value.IndexOf('/', "file://".Length);
            if (slash < 0)
            {
                return null;
            }

            value = Uri.UnescapeDataString(value[slash..]);
        }

        return value.StartsWith('/') && !value.Contains('\0') ? value : null;
    }
}
