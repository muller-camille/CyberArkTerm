using CyberArkTerm.Core.Localization;

namespace CyberArkTerm.Core.Terminal;

/// <summary>
/// Dossier courant du shell distant, transmis par la séquence OSC 7
/// (<c>ESC ] 7 ; file://hôte/chemin BEL</c>) que CyberArkTerm installe via PROMPT_COMMAND (bash, zsh) ou l'alias
/// <c>cwdcmd</c> (tcsh).
/// </summary>
public static class WorkingDirectory
{
    /// <summary>
    /// Commande injectée dans le shell à l'ouverture de la session pour qu'il annonce son dossier courant :
    /// à chaque invite pour bash et zsh, à chaque changement de dossier pour tcsh (alias <c>cwdcmd</c>, seulement
    /// s'il n'est pas déjà défini). Préfixée d'une espace pour ne pas entrer dans l'historique. Sans effet en double
    /// si elle est renvoyée dans le même shell. <paramref name="linesToErase"/> lignes sont ensuite effacées pour
    /// masquer la commande tapée.
    /// </summary>
    /// <remarks>
    /// La ligne doit être lisible par toutes les familles de shell, sinon csh refuse la ligne entière et l'affiche
    /// (« Bad : modifier in $ »). Chaque partie est donc passée entre apostrophes à <c>eval</c>, et seule la famille
    /// concernée l'exécute : csh et tcsh définissent toujours la variable <c>shell</c>, fish <c>FISH_VERSION</c>, les
    /// shells POSIX aucune des deux (csh ne remplace pas les variables d'une commande qu'il n'exécute pas).
    /// ksh, sh, csh et fish n'annoncent rien (fish change seulement de dossier), mais rien ne reste affiché.
    /// </remarks>
    public static string InjectionCommand(int linesToErase, string? startDirectory = null)
    {
        var erase = linesToErase > 0 ? $"printf '\\033[{linesToErase}A\\r\\033[J'" : "true";
        var posix = (startDirectory is null ? "" : $"cd -- {ShellQuote(startDirectory)} 2>/dev/null;") +
                    "__catosc7(){ printf '\\033]7;%s\\007' \"$PWD\";};" +
                    "case \";$PROMPT_COMMAND;\" in *\";__catosc7;\"*);;*)PROMPT_COMMAND=\"__catosc7${PROMPT_COMMAND:+;$PROMPT_COMMAND}\";;esac;" +
                    "[ -n \"$ZSH_VERSION\" ]&&eval '(( ${precmd_functions[(I)__catosc7]} ))||precmd_functions+=(__catosc7)'";
        var csh = "if ($?tcsh && ! $?__catosc7 && `alias cwdcmd | wc -c` == 0) set __catosc7;" +
                  "if ($?__catosc7) alias cwdcmd '" + CshAnnounce + "';" +
                  (startDirectory is null ? "" : CshChangeDirectory(startDirectory)) +
                  // L'alias défini sur cette ligne ne sert qu'à partir de la suivante : première annonce en direct.
                  "if ($?__catosc7) " + CshAnnounce;
        // csh doit sauter ce « cd » sans lire FISH_VERSION, qu'il ne connaît pas : d'où le test de « shell » en tête.
        var fish = startDirectory is null ? "" :
            $"test -n \"$shell\" || test -z \"$FISH_VERSION\" || cd {InputQuote(NotAnOption(startDirectory))} 2>/dev/null;";
        return $" test -n \"$shell\" || test -n \"$FISH_VERSION\" || eval {InputQuote(posix)} 2>/dev/null;" +
               $"test -n \"$shell\" && eval {InputQuote(csh)};{fish}{erase}\r";
    }

    private const string CshAnnounce = "printf \"\\033]7;%s\\007\" \"$cwd\"";

    /// <summary>« cd » pour csh et tcsh, sans message si le dossier n'existe pas.</summary>
    private static string CshChangeDirectory(string directory)
    {
        // « cd -x » serait lu comme une option ; csh remplace « ! » (historique) même entre apostrophes.
        var quoted = ShellQuote(NotAnOption(directory)).Replace("!", "\\!");
        return $"if (-d {quoted}) cd {quoted};";
    }

    /// <summary>
    /// Protège une valeur sur la ligne tapée, lue de la même façon par les shells POSIX, csh et fish : entre
    /// apostrophes, sauf « ' », « ! » (historique de bash, zsh et csh) et « \ » (fish le lit entre apostrophes),
    /// placés hors des apostrophes derrière une barre oblique inverse. Un caractère de contrôle (retour à la ligne…)
    /// terminerait la commande : il est refusé.
    /// </summary>
    private static string InputQuote(string value)
    {
        if (value.Any(char.IsControl))
        {
            throw new ArgumentException(CoreStrings.ControlCharacterInPath);
        }

        return "'" + string.Concat(value.Select(c => c is '\'' or '!' or '\\' ? $"'\\{c}'" : c.ToString())) + "'";
    }

    /// <summary>
    /// Vrai si le texte de la ligne avant le curseur ressemble à une invite de shell en attente de saisie :
    /// il se termine par « $ », « # », « > » ou « % » (espaces de fin ignorés).
    /// </summary>
    public static bool LooksLikePrompt(string textBeforeCursor)
    {
        var text = textBeforeCursor.TrimEnd();
        return text.Length > 0 && text[^1] is '$' or '#' or '>' or '%';
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

    /// <summary>
    /// « cd » visible vers le dossier de départ, quand le suivi du dossier n'est pas installé. Sans « -- », que csh
    /// refuse : un dossier commençant par « - » est précédé de « ./ ».
    /// </summary>
    public static string ChangeDirectoryCommand(string directory) => $" cd {InputQuote(NotAnOption(directory))}\r";

    private static string NotAnOption(string directory) => directory.StartsWith('-') ? "./" + directory : directory;

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
