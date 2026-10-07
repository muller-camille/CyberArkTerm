namespace CyberArkTerm.Core.Terminal;

public enum TerminalInputKind
{
    /// <summary>Texte tapé, caractère de contrôle (Ctrl+C...), Alt+touche.</summary>
    Text,

    /// <summary>Touche spéciale (flèches, F1...).</summary>
    Key,

    /// <summary>Collage du presse-papiers.</summary>
    Paste,

    /// <summary>Molette dans un programme plein écran (vim, less) : envoyée comme des flèches.</summary>
    Scroll,
}

/// <summary>
/// Saisie de l'utilisateur avant son encodage. Une touche spéciale ou un collage s'encodent selon les modes du terminal
/// qui les reçoit (curseur « application », collage encadré) : la saisie simultanée envoie ainsi la bonne séquence à
/// des sessions dans des états différents (un shell dans l'une, vim dans l'autre).
/// </summary>
public sealed record TerminalInput(TerminalInputKind Kind, string Text = "", TerminalKey Key = default,
    bool Shift = false, bool Alt = false, bool Ctrl = false)
{
    public static TerminalInput Typed(string text) => new(TerminalInputKind.Text, text);

    public static TerminalInput Special(TerminalKey key, bool shift = false, bool alt = false, bool ctrl = false) =>
        new(TerminalInputKind.Key, "", key, shift, alt, ctrl);

    /// <summary>Texte collé, sans caractère de contrôle (voir <see cref="TerminalKeys.CleanPaste"/>).</summary>
    public static TerminalInput Pasted(string text) => new(TerminalInputKind.Paste, TerminalKeys.CleanPaste(text));

    public static TerminalInput Scrolled(TerminalKey key) => new(TerminalInputKind.Scroll, "", key);

    /// <summary>Rien à envoyer.</summary>
    public bool IsEmpty => Kind is TerminalInputKind.Text or TerminalInputKind.Paste && Text.Length == 0;

    /// <summary>Saisie au clavier (ou collage), recopiée par la saisie simultanée ; pas le défilement de la molette.</summary>
    public bool IsTyping => Kind != TerminalInputKind.Scroll;

    /// <summary>Collage de plusieurs lignes : il exécute des commandes.</summary>
    public bool IsMultiLinePaste => Kind == TerminalInputKind.Paste && Text.TrimEnd('\r', '\n').IndexOfAny(['\r', '\n']) >= 0;

    /// <summary>Séquence à envoyer au serveur, selon les modes actuels de <paramref name="emulator"/>.</summary>
    public string Encode(TerminalEmulator emulator) => Kind switch
    {
        TerminalInputKind.Text => Text,
        TerminalInputKind.Paste => TerminalKeys.Paste(Text, emulator.BracketedPaste),
        _ => TerminalKeys.Encode(Key, Shift, Alt, Ctrl, emulator.ApplicationCursorKeys),
    };
}

/// <summary>Vue parallèle : disposition des sessions et destinataires de la saisie simultanée.</summary>
public static class ParallelLayout
{
    public const int MaxSessions = 8;

    /// <summary>Grille pour <paramref name="count"/> sessions : côte à côte jusqu'à 3, puis sur deux lignes.</summary>
    public static (int Rows, int Columns) For(int count) => Math.Clamp(count, 1, MaxSessions) switch
    {
        1 => (1, 1),
        2 => (1, 2),
        3 => (1, 3),
        4 => (2, 2),
        5 or 6 => (2, 3),
        _ => (2, 4),
    };

    /// <summary>
    /// Sessions qui reçoivent une saisie faite dans <paramref name="source"/> : elle seule sans saisie simultanée, ou si
    /// elle en est exclue ; sinon elle et les autres sessions incluses et connectées, dans l'ordre de la vue.
    /// </summary>
    public static IReadOnlyList<T> Targets<T>(T source, IReadOnlyList<T> panes, bool broadcast, Func<T, bool> included, Func<T, bool> connected)
        where T : class
    {
        if (!broadcast || !included(source))
        {
            return [source];
        }

        return panes.Where(p => ReferenceEquals(p, source) || (included(p) && connected(p))).ToList();
    }
}
