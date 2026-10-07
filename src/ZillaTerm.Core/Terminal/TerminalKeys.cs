using System.Text;

namespace ZillaTerm.Core.Terminal;

public enum TerminalKey
{
    Up,
    Down,
    Right,
    Left,
    Home,
    End,
    Insert,
    Delete,
    PageUp,
    PageDown,
    Enter,
    Backspace,
    Tab,
    Escape,
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,
}

/// <summary>Séquences envoyées au serveur pour les touches spéciales (conventions xterm).</summary>
public static class TerminalKeys
{
    public static string Encode(TerminalKey key, bool shift = false, bool alt = false, bool ctrl = false, bool applicationCursor = false)
    {
        int modifier = 1 + (shift ? 1 : 0) + (alt ? 2 : 0) + (ctrl ? 4 : 0);
        bool modified = modifier > 1;
        string mod = modified ? $"1;{modifier}" : "";

        string Cursor(char final) => modified
            ? $"\x1b[{mod}{final}"
            : applicationCursor ? $"\x1bO{final}" : $"\x1b[{final}";

        string Tilde(int code) => modified ? $"\x1b[{code};{modifier}~" : $"\x1b[{code}~";

        string Ss3(char final) => modified ? $"\x1b[{mod}{final}" : $"\x1bO{final}";

        return key switch
        {
            TerminalKey.Up => Cursor('A'),
            TerminalKey.Down => Cursor('B'),
            TerminalKey.Right => Cursor('C'),
            TerminalKey.Left => Cursor('D'),
            TerminalKey.Home => Cursor('H'),
            TerminalKey.End => Cursor('F'),
            TerminalKey.Insert => Tilde(2),
            TerminalKey.Delete => Tilde(3),
            TerminalKey.PageUp => Tilde(5),
            TerminalKey.PageDown => Tilde(6),
            TerminalKey.Enter => alt ? "\x1b\r" : "\r",
            TerminalKey.Backspace => ctrl ? "\b" : alt ? "\x1b\x7f" : "\x7f",
            TerminalKey.Tab => shift ? "\x1b[Z" : "\t",
            TerminalKey.Escape => "\x1b",
            TerminalKey.F1 => Ss3('P'),
            TerminalKey.F2 => Ss3('Q'),
            TerminalKey.F3 => Ss3('R'),
            TerminalKey.F4 => Ss3('S'),
            TerminalKey.F5 => Tilde(15),
            TerminalKey.F6 => Tilde(17),
            TerminalKey.F7 => Tilde(18),
            TerminalKey.F8 => Tilde(19),
            TerminalKey.F9 => Tilde(20),
            TerminalKey.F10 => Tilde(21),
            TerminalKey.F11 => Tilde(23),
            TerminalKey.F12 => Tilde(24),
            _ => "",
        };
    }

    /// <summary>Ctrl+lettre (et quelques symboles) : caractère de contrôle correspondant, sinon null.</summary>
    public static string? Control(char c) => char.ToUpperInvariant(c) switch
    {
        >= 'A' and <= 'Z' and var u => ((char)(u - 'A' + 1)).ToString(),
        '@' or ' ' or '2' => "\0",
        '[' or '3' => "\x1b",
        '\\' or '4' => "\x1c",
        ']' or '5' => "\x1d",
        '^' or '6' => "\x1e",
        '_' or '7' or '-' => "\x1f",
        '8' or '?' => "\x7f",
        _ => null,
    };

    /// <summary>Texte collé : retours à la ligne normalisés en CR, encadré en mode « bracketed paste ».</summary>
    public static string Paste(string text, bool bracketed)
    {
        text = CleanPaste(text).Replace("\r\n", "\r").Replace('\n', '\r');
        if (bracketed)
        {
            text = "\x1b[200~" + text + "\x1b[201~";
        }

        return text;
    }

    /// <summary>
    /// Texte collé débarrassé des caractères de contrôle, sauf tabulation et retours à la ligne : un texte piégé (page
    /// web, presse-papiers d'un serveur VNC) ne peut ni fermer le collage protégé (ÉCHAP), ni valider une commande par une
    /// touche Ctrl (Ctrl-O, Ctrl-J…), ni passer une séquence C1.
    /// </summary>
    public static string CleanPaste(string text)
    {
        if (!text.Any(IsUnsafe))
        {
            return text;
        }

        var clean = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!IsUnsafe(c))
            {
                clean.Append(c);
            }
        }

        return clean.ToString();

        static bool IsUnsafe(char c) => char.IsControl(c) && c is not ('\t' or '\r' or '\n');
    }
}
