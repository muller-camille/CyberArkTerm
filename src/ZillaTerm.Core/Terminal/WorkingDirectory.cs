using ZillaTerm.Core.Localization;

namespace ZillaTerm.Core.Terminal;

/// <summary>
/// Dossier courant du shell distant, transmis par la séquence OSC 7
/// (<c>ESC ] 7 ; file://hôte/chemin BEL</c>) que ZillaTerm installe via PROMPT_COMMAND (bash, zsh) ou l'alias
/// <c>cwdcmd</c> (tcsh).
/// </summary>
public static class WorkingDirectory
{
    /// <summary>Code OSC privé du marqueur de fin de commande (voir <see cref="TerminalEmulator.MarkEraseFromCursorLine"/>).</summary>
    public const string EraseOscCode = "6973";

    /// <summary>Marqueur affiché par le shell à la fin de la commande injectée : ZillaTerm efface alors son écho.</summary>
    public const string EraseMarker = "\u001b]" + EraseOscCode + ";\u0007";

    /// <summary>
    /// Commande injectée dans le shell à l'ouverture de la session pour qu'il annonce son dossier courant :
    /// à chaque invite pour bash et zsh, à chaque changement de dossier pour tcsh (alias <c>cwdcmd</c>, seulement
    /// s'il n'est pas déjà défini). Préfixée d'une espace pour ne pas entrer dans l'historique (si HISTCONTROL l'ignore).
    /// Sans effet en double si elle est renvoyée dans le même shell. Elle se termine par le marqueur
    /// <see cref="EraseMarker"/> : ZillaTerm efface alors lui-même la commande tapée et son écho depuis la ligne marquée
    /// avant l'envoi, quel que soit le nombre de lignes que le serveur leur a données (largeur supposée, invite mal
    /// mesurée par le shell…).
    /// </summary>
    /// <remarks>
    /// La ligne doit être lisible par toutes les familles de shell, sinon csh refuse la ligne entière et l'affiche
    /// (« Bad : modifier in $ »). Chaque partie est donc passée entre apostrophes à <c>eval</c>, et seule la famille
    /// concernée l'exécute : <see cref="NotPosix"/> sépare les shells POSIX des autres, puis la variable <c>shell</c>,
    /// toujours définie par csh et tcsh, sépare csh de fish (csh ne remplace pas les variables d'une commande qu'il
    /// n'exécute pas). Un shell POSIX ne lit aucune variable non définie : un profil avec <c>set -u</c> interromprait
    /// la ligne avant le marqueur (« shell: parameter not set ») et elle resterait affichée.
    /// ksh, sh, csh et fish n'annoncent rien (fish change seulement de dossier), mais rien ne reste affiché.
    /// </remarks>
    public static string InjectionCommand(string? startDirectory = null)
    {
        var erase = $"printf '\\033]{EraseOscCode};\\007'";
        var posix = (startDirectory is null ? "" : $"cd -- {ShellQuote(startDirectory)} 2>/dev/null||:;") +
                    "__catosc7(){ printf '\\033]7;%s\\007' \"$PWD\";};" +
                    "case \";${PROMPT_COMMAND-};\" in *\";__catosc7;\"*);;*)PROMPT_COMMAND=\"__catosc7${PROMPT_COMMAND:+;$PROMPT_COMMAND}\";;esac;" +
                    "[ -z \"${ZSH_VERSION-}\" ]||eval '(( ${precmd_functions[(I)__catosc7]-0} ))||precmd_functions+=(__catosc7)'";
        var csh = "if ($?tcsh && ! $?__catosc7 && `alias cwdcmd | wc -c` == 0) set __catosc7;" +
                  "if ($?__catosc7) alias cwdcmd '" + CshAnnounce + "';" +
                  (startDirectory is null ? "" : CshChangeDirectory(startDirectory)) +
                  // L'alias défini sur cette ligne ne sert qu'à partir de la suivante : première annonce en direct.
                  "if ($?__catosc7) " + CshAnnounce;
        var fish = startDirectory is null ? "" :
            $"{NotPosix} && test -z \"$shell\" && cd {TypedQuote(NotAnOption(startDirectory))} 2>/dev/null;";
        return $" {NotPosix} || eval {TypedQuote(posix)} 2>/dev/null;" +
               $"{NotPosix} && test -n \"$shell\" && eval {TypedQuote(csh)};{fish}{erase}\r";
    }

    /// <summary>
    /// Faux seulement dans un shell POSIX, sans lire de variable : « \\ » entre guillemets y devient « \ », alors que
    /// csh le garde tel quel et que fish lit aussi « \ » entre apostrophes.
    /// </summary>
    private const string NotPosix = "test \"\\\\\" = '\\\\'";

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
    public static string TypedQuote(string value)
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
    /// « cd » visible vers le dossier de départ, quand le suivi du dossier n'est pas installé. Sans « -- », que csh
    /// refuse : un dossier commençant par « - » est précédé de « ./ ».
    /// </summary>
    public static string ChangeDirectoryCommand(string directory) => $" cd {TypedQuote(NotAnOption(directory))}\r";

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

    /// <summary>
    /// Extrait le chemin d'un OSC 7 : <c>file://hôte/chemin%20encodé</c> ou chemin absolu brut. Refusé s'il contient,
    /// une fois décodé, un caractère de contrôle ou de mise en forme invisible (retour à la ligne, échappement,
    /// inversion du sens d'écriture…) : n'importe quelle sortie affichée peut contenir un OSC 7, et ce chemin s'affiche
    /// dans l'onglet Fichiers.
    /// </summary>
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

        return value.StartsWith('/') && !value.Any(IsHidden) ? value : null;
    }

    private static bool IsHidden(char c) =>
        char.IsControl(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Format
            or System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator;
}
