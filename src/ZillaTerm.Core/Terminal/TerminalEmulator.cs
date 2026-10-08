using System.Text;

namespace ZillaTerm.Core.Terminal;

[Flags]
public enum CellFlags : byte
{
    None = 0,
    Bold = 1,
    Dim = 2,
    Italic = 4,
    Underline = 8,
    Inverse = 16,
    Hidden = 32,
}

/// <summary>Une case de l'écran : caractère et attributs.</summary>
public struct Cell
{
    public char Char;
    public int Foreground;
    public int Background;
    public CellFlags Flags;

    public static Cell Blank(int background = TerminalColor.Default) =>
        new() { Char = ' ', Foreground = TerminalColor.Default, Background = background };
}

/// <summary>
/// Émulateur de terminal compatible xterm (sous-ensemble utilisé par bash, vim, less, top, mc...).
/// Reçoit le flux texte de la session SSH et maintient l'écran, l'historique et les modes.
/// Non thread-safe : à utiliser depuis un seul thread (celui de l'interface).
/// </summary>
public sealed class TerminalEmulator
{
    private const int MaxStringLength = 4096;

    // Paramètres d'une séquence CSI gardés au plus (xterm en garde 30) : « \e[1;1;1;…m » sans fin ne remplit pas la mémoire.
    private const int MaxParams = 32;

    private readonly LineRing _scrollback;
    private readonly List<int> _params = [];
    private readonly StringBuilder _osc = new();

    private Cell[][] _main;
    private Cell[][]? _alt;
    private Cell[][] _screen;
    private State _state;
    private int _param = -1;
    private char _private;
    private char _intermediate;
    private bool _charsetForG1;
    private bool _wrapPending;

    // Lignes de l'écran principal sorties par le haut depuis le début (historique plein ou non) : numéro absolu d'une
    // ligne = _scrolledOff + rang à l'écran, stable quand l'écran défile.
    private long _scrolledOff;
    private long? _eraseFrom;
    private long _eraseUntil;
    private int _scrollTop;
    private int _scrollBottom;
    private int _fg = TerminalColor.Default;
    private int _bg = TerminalColor.Default;
    private CellFlags _flags;
    private bool _g0Graphics;
    private bool _g1Graphics;
    private bool _shiftOut;
    private SavedCursor _saved;

    public TerminalEmulator(int columns = 80, int rows = 24, int maxScrollback = 5000)
    {
        Columns = Math.Max(columns, 2);
        Rows = Math.Max(rows, 2);
        _scrollback = new LineRing(maxScrollback);
        _main = NewScreen(Rows, Columns);
        _screen = _main;
        _scrollBottom = Rows - 1;
        _saved = InitialCursor;
    }

    private static SavedCursor InitialCursor =>
        new(0, 0, TerminalColor.Default, TerminalColor.Default, CellFlags.None, false, false, false, false);

    private enum State
    {
        Ground,
        Escape,
        EscapeCharset,
        EscapeIgnoreNext,
        Csi,
        Osc,
        OscEscape,
        IgnoreString,
        IgnoreStringEscape,
    }

    /// <summary>Durée pendant laquelle une marque attend le marqueur, en millisecondes.</summary>
    public const int EraseMarkLifetimeMs = 10_000;

    /// <summary>Horloge en millisecondes (remplacée par les tests).</summary>
    internal Func<long> Clock { get; set; } = () => Environment.TickCount64;

    /// <summary>
    /// Marque la ligne du curseur : le prochain <see cref="WorkingDirectory.EraseMarker"/> reçu efface l'écran depuis le
    /// début de cette ligne. Sert à masquer une commande tapée par ZillaTerm (suivi du dossier) et son écho, sur autant de
    /// lignes que le serveur en a affiché, quelle que soit la largeur qu'il suppose. Sans effet en écran alternatif.
    /// La marque ne vaut que <see cref="EraseMarkLifetimeMs"/> ms, et <see cref="CancelEraseMark"/> la retire avant : si
    /// la commande ne s'exécute pas (shell non Unix), aucune sortie ultérieure ne peut plus effacer l'écran.
    /// </summary>
    public void MarkEraseFromCursorLine()
    {
        _eraseFrom = IsAlternateScreen ? null : _scrolledOff + CursorRow;
        _eraseUntil = Clock() + EraseMarkLifetimeMs;
    }

    /// <summary>Retire la marque posée par <see cref="MarkEraseFromCursorLine"/> (commande validée par l'utilisateur).</summary>
    public void CancelEraseMark() => _eraseFrom = null;

    /// <summary>Dossier courant signalé par le shell (OSC 7), déjà décodé en chemin Unix.</summary>
    public event Action<string>? WorkingDirectoryChanged;

    /// <summary>Réponse à renvoyer au serveur (rapport de position du curseur, attributs du terminal...).</summary>
    public event Action<string>? Response;

    public int Columns { get; private set; }

    public int Rows { get; private set; }

    public int CursorRow { get; private set; }

    public int CursorColumn { get; private set; }

    public bool CursorVisible { get; private set; } = true;

    public bool ApplicationCursorKeys { get; private set; }

    public bool BracketedPaste { get; private set; }

    public bool AutoWrap { get; private set; } = true;

    public bool InsertMode { get; private set; }

    public bool OriginMode { get; private set; }

    public bool IsAlternateScreen => _alt is not null && ReferenceEquals(_screen, _alt);

    /// <summary>Nombre de lignes d'historique consultables (aucune en écran alternatif : vim, less...).</summary>
    public int ScrollbackCount => IsAlternateScreen ? 0 : _scrollback.Count;

    /// <summary>Incrémenté à chaque modification : permet au rendu de savoir s'il doit redessiner.</summary>
    public long Version { get; private set; }

    /// <summary>Ligne <paramref name="row"/> : de -ScrollbackCount (plus ancienne) à Rows - 1.</summary>
    public Cell[] GetLine(int row) =>
        row < 0 ? _scrollback[_scrollback.Count + row] : _screen[row];

    public void Feed(string text)
    {
        foreach (var c in text)
        {
            Process(c);
        }

        Version++;
    }

    /// <summary>Oublie l'historique (lignes sorties de l'écran) ; l'écran lui-même ne change pas.</summary>
    public void ClearScrollback()
    {
        _scrollback.Clear();
        Version++;
    }

    public void Resize(int columns, int rows)
    {
        columns = Math.Max(columns, 2);
        rows = Math.Max(rows, 2);
        if (columns == Columns && rows == Rows)
        {
            return;
        }

        // Si l'écran rétrécit sous le curseur, les lignes du haut partent dans l'historique.
        int shift = Math.Max(0, CursorRow - rows + 1);
        if (!IsAlternateScreen)
        {
            for (int i = 0; i < shift; i++)
            {
                PushScrollback(_main[i]);
            }
        }

        _main = ResizeScreen(_main, IsAlternateScreen ? 0 : shift, rows, columns);
        if (_alt is not null)
        {
            _alt = ResizeScreen(_alt, IsAlternateScreen ? shift : 0, rows, columns);
        }

        bool alt = IsAlternateScreen;
        _screen = alt ? _alt! : _main;
        Columns = columns;
        Rows = rows;
        CursorRow = Math.Clamp(CursorRow - shift, 0, rows - 1);
        CursorColumn = Math.Clamp(CursorColumn, 0, columns - 1);
        _saved.Row = Math.Clamp(_saved.Row, 0, rows - 1);
        _saved.Column = Math.Clamp(_saved.Column, 0, columns - 1);
        _scrollTop = 0;
        _scrollBottom = rows - 1;
        _wrapPending = false;
        Version++;
    }

    /// <summary>Texte de la zone sélectionnée (coordonnées absolues, fin incluse), espaces de fin retirés.</summary>
    public string GetText(int startRow, int startColumn, int endRow, int endColumn)
    {
        if (startRow > endRow || (startRow == endRow && startColumn > endColumn))
        {
            (startRow, startColumn, endRow, endColumn) = (endRow, endColumn, startRow, startColumn);
        }

        var sb = new StringBuilder();
        for (int row = Math.Max(startRow, -ScrollbackCount); row <= Math.Min(endRow, Rows - 1); row++)
        {
            var line = GetLine(row);
            int from = row == startRow ? Math.Max(0, startColumn) : 0;
            int to = row == endRow ? Math.Min(endColumn, line.Length - 1) : line.Length - 1;
            var text = new StringBuilder();
            for (int col = from; col <= to; col++)
            {
                text.Append(line[col].Char);
            }

            if (row != startRow)
            {
                sb.Append('\n');
            }

            sb.Append(text.ToString().TrimEnd());
        }

        return sb.ToString();
    }

    // ===================== Analyseur =====================

    private void Process(char c)
    {
        switch (_state)
        {
            case State.Ground:
                if (c < 0x20 || c == 0x7F)
                {
                    Control(c);
                }
                else
                {
                    Print(c);
                }

                break;
            case State.Escape:
                Escape(c);
                break;
            case State.EscapeCharset:
                if (_charsetForG1)
                {
                    _g1Graphics = c == '0';
                }
                else
                {
                    _g0Graphics = c == '0';
                }

                _state = State.Ground;
                break;
            case State.EscapeIgnoreNext:
                _state = State.Ground;
                break;
            case State.Csi:
                Csi(c);
                break;
            case State.Osc:
                if (c == '\a')
                {
                    EndOsc();
                }
                else if (c == '\x1b')
                {
                    _state = State.OscEscape;
                }
                else if (_osc.Length < MaxStringLength)
                {
                    _osc.Append(c);
                }

                break;
            case State.OscEscape:
                // ESC \ (ST) termine l'OSC ; toute autre séquence l'abandonne.
                if (c == '\\')
                {
                    EndOsc();
                }
                else
                {
                    _osc.Clear();
                    _state = State.Escape;
                    Escape(c);
                }

                break;
            case State.IgnoreString:
                if (c == '\a')
                {
                    _state = State.Ground;
                }
                else if (c == '\x1b')
                {
                    _state = State.IgnoreStringEscape;
                }

                break;
            case State.IgnoreStringEscape:
                _state = c == '\\' ? State.Ground : State.IgnoreString;
                break;
        }
    }

    private void Control(char c)
    {
        switch (c)
        {
            case '\a':
                // Sonnerie : ignorée.
                break;
            case '\b':
                _wrapPending = false;
                if (CursorColumn > 0)
                {
                    CursorColumn--;
                }

                break;
            case '\t':
                CursorColumn = Math.Min(Columns - 1, (CursorColumn / 8 + 1) * 8);
                break;
            case '\n':
            case '\v':
            case '\f':
                LineFeed();
                break;
            case '\r':
                CursorColumn = 0;
                _wrapPending = false;
                break;
            case '\x0E':
                _shiftOut = true;
                break;
            case '\x0F':
                _shiftOut = false;
                break;
            case '\x1b':
                _state = State.Escape;
                break;
            case '\x18':
            case '\x1A':
                _state = State.Ground;
                break;
        }
    }

    private void Escape(char c)
    {
        _state = State.Ground;
        switch (c)
        {
            case '[':
                _params.Clear();
                _param = -1;
                _private = '\0';
                _intermediate = '\0';
                _state = State.Csi;
                break;
            case ']':
                _osc.Clear();
                _state = State.Osc;
                break;
            case 'P':
            case 'X':
            case '^':
            case '_':
                _state = State.IgnoreString;
                break;
            case '(':
            case ')':
                _charsetForG1 = c == ')';
                _state = State.EscapeCharset;
                break;
            case '*':
            case '+':
            case '#':
            case '%':
            case ' ':
                _state = State.EscapeIgnoreNext;
                break;
            case '7':
                SaveCursor();
                break;
            case '8':
                RestoreCursor();
                break;
            case 'D':
                LineFeed();
                break;
            case 'E':
                CursorColumn = 0;
                LineFeed();
                break;
            case 'M':
                ReverseIndex();
                break;
            case 'c':
                Reset();
                break;
        }
    }

    private void Csi(char c)
    {
        if (c >= '0' && c <= '9')
        {
            _param = (_param < 0 ? 0 : _param) * 10 + (c - '0');
            if (_param > 99999)
            {
                _param = 99999;
            }

            return;
        }

        switch (c)
        {
            case ';':
            case ':':
                if (_params.Count < MaxParams)
                {
                    _params.Add(_param);
                }

                _param = -1;
                return;
            case '?':
            case '>':
            case '<':
            case '=':
                _private = c;
                return;
            case '\x1b':
                _state = State.Escape;
                return;
        }

        if (c < 0x20)
        {
            Control(c);
            return;
        }

        if (c >= 0x20 && c <= 0x2F)
        {
            _intermediate = c;
            return;
        }

        if (_params.Count < MaxParams)
        {
            _params.Add(_param);
        }

        _state = State.Ground;
        if (c >= 0x40 && c <= 0x7E)
        {
            ExecuteCsi(c);
        }
    }

    private int Param(int index, int fallback)
    {
        int value = index < _params.Count ? _params[index] : -1;
        return value <= 0 ? fallback : value;
    }

    private void ExecuteCsi(char final)
    {
        if (_intermediate != '\0')
        {
            // DECSCUSR (forme du curseur), DECSTR... : sans effet ici.
            if (_intermediate == '!' && final == 'p')
            {
                SoftReset();
            }

            return;
        }

        switch (final)
        {
            case '@':
                InsertChars(Param(0, 1));
                break;
            case 'A':
                MoveCursorRow(-Param(0, 1));
                break;
            case 'B':
            case 'e':
                MoveCursorRow(Param(0, 1));
                break;
            case 'C':
            case 'a':
                SetCursor(CursorRow, CursorColumn + Param(0, 1));
                break;
            case 'D':
                SetCursor(CursorRow, CursorColumn - Param(0, 1));
                break;
            case 'E':
                MoveCursorRow(Param(0, 1));
                CursorColumn = 0;
                break;
            case 'F':
                MoveCursorRow(-Param(0, 1));
                CursorColumn = 0;
                break;
            case 'G':
            case '`':
                SetCursor(CursorRow, Param(0, 1) - 1);
                break;
            case 'H':
            case 'f':
                CursorPosition(Param(0, 1) - 1, Param(1, 1) - 1);
                break;
            case 'J':
                EraseInDisplay(_params.Count > 0 ? Math.Max(_params[0], 0) : 0);
                break;
            case 'K':
                EraseInLine(_params.Count > 0 ? Math.Max(_params[0], 0) : 0);
                break;
            case 'L':
                InsertLines(Param(0, 1));
                break;
            case 'M':
                DeleteLines(Param(0, 1));
                break;
            case 'P':
                DeleteChars(Param(0, 1));
                break;
            case 'S':
                if (_private == '\0')
                {
                    ScrollUp(_scrollTop, _scrollBottom, Param(0, 1));
                }

                break;
            case 'T':
                if (_private == '\0')
                {
                    ScrollDown(_scrollTop, _scrollBottom, Param(0, 1));
                }

                break;
            case 'X':
                EraseChars(Param(0, 1));
                break;
            case 'd':
                // Ligne absolue, comptée depuis le haut de la zone de défilement en mode origine (CursorPosition).
                CursorPosition(Param(0, 1) - 1, CursorColumn);
                break;
            case 'h':
            case 'l':
                SetModes(final == 'h');
                break;
            case 'm':
                if (_private == '\0')
                {
                    SelectGraphicRendition();
                }

                break;
            case 'n':
                DeviceStatusReport();
                break;
            case 'c':
                if (_private == '>')
                {
                    Response?.Invoke("\x1b[>0;10;1c");
                }
                else if (_private == '\0' && Param(0, 0) == 0)
                {
                    Response?.Invoke("\x1b[?1;2c");
                }

                break;
            case 'r':
                if (_private == '\0')
                {
                    SetScrollRegion(Param(0, 1) - 1, Param(1, Rows) - 1);
                }

                break;
            case 's':
                if (_private == '\0')
                {
                    SaveCursor();
                }

                break;
            case 'u':
                if (_private == '\0')
                {
                    RestoreCursor();
                }

                break;
        }
    }

    private void EndOsc()
    {
        _state = State.Ground;
        var text = _osc.ToString();
        _osc.Clear();
        int sep = text.IndexOf(';');
        if (sep <= 0)
        {
            return;
        }

        var code = text[..sep];
        var value = text[(sep + 1)..];
        // Titre de la fenêtre (OSC 0 / 2) et autres codes : ignorés.
        switch (code)
        {
            case "7":
                if (WorkingDirectory.Parse(value) is { } path)
                {
                    WorkingDirectoryChanged?.Invoke(path);
                }

                break;
            case WorkingDirectory.EraseOscCode:
                EraseFromMark();
                break;
        }
    }

    /// <summary>
    /// Fin d'une commande tapée par ZillaTerm : écran effacé depuis la ligne marquée par <see cref="MarkEraseFromCursorLine"/>
    /// (une seule fois ; ignoré sans marque ou marque expirée, ce qu'un programme du serveur ne peut donc pas déclencher
    /// de lui-même).
    /// </summary>
    private void EraseFromMark()
    {
        var mark = _eraseFrom;
        _eraseFrom = null;
        if (mark is not long line || IsAlternateScreen || Clock() > _eraseUntil)
        {
            return;
        }

        SetCursor((int)Math.Clamp(line - _scrolledOff, 0, Rows - 1), 0);
        EraseInDisplay(0);
    }

    // ===================== Écriture et déplacements =====================

    private void Print(char c)
    {
        if (_shiftOut ? _g1Graphics : _g0Graphics)
        {
            c = DecSpecialGraphics(c);
        }

        if (_wrapPending)
        {
            _wrapPending = false;
            if (AutoWrap)
            {
                CursorColumn = 0;
                LineFeed();
            }
        }

        if (InsertMode)
        {
            InsertChars(1);
        }

        _screen[CursorRow][CursorColumn] = new Cell { Char = c, Foreground = _fg, Background = _bg, Flags = _flags };
        if (CursorColumn == Columns - 1)
        {
            _wrapPending = AutoWrap;
        }
        else
        {
            CursorColumn++;
        }
    }

    private void LineFeed()
    {
        _wrapPending = false;
        if (CursorRow == _scrollBottom)
        {
            ScrollUp(_scrollTop, _scrollBottom, 1);
        }
        else if (CursorRow < Rows - 1)
        {
            CursorRow++;
        }
    }

    private void ReverseIndex()
    {
        _wrapPending = false;
        if (CursorRow == _scrollTop)
        {
            ScrollDown(_scrollTop, _scrollBottom, 1);
        }
        else if (CursorRow > 0)
        {
            CursorRow--;
        }
    }

    private void SetCursor(int row, int column)
    {
        CursorRow = Math.Clamp(row, 0, Rows - 1);
        CursorColumn = Math.Clamp(column, 0, Columns - 1);
        _wrapPending = false;
    }

    private void CursorPosition(int row, int column)
    {
        if (OriginMode)
        {
            row = Math.Clamp(row + _scrollTop, _scrollTop, _scrollBottom);
        }

        SetCursor(row, column);
    }

    private void MoveCursorRow(int delta)
    {
        // Les déplacements verticaux s'arrêtent aux marges de la zone de défilement quand le curseur y est.
        int top = CursorRow >= _scrollTop ? _scrollTop : 0;
        int bottom = CursorRow <= _scrollBottom ? _scrollBottom : Rows - 1;
        SetCursor(Math.Clamp(CursorRow + delta, top, bottom), CursorColumn);
    }

    private void SetScrollRegion(int top, int bottom)
    {
        bottom = Math.Min(bottom, Rows - 1);
        if (top < bottom)
        {
            _scrollTop = top;
            _scrollBottom = bottom;
            CursorPosition(0, 0);
        }
    }

    private void SaveCursor() =>
        _saved = new SavedCursor(CursorRow, CursorColumn, _fg, _bg, _flags, _g0Graphics, _g1Graphics, _shiftOut, OriginMode);

    private void RestoreCursor()
    {
        SetCursor(_saved.Row, _saved.Column);
        (_fg, _bg, _flags, _g0Graphics, _g1Graphics, _shiftOut, OriginMode) =
            (_saved.Foreground, _saved.Background, _saved.Flags, _saved.G0Graphics, _saved.G1Graphics, _saved.ShiftOut, _saved.OriginMode);
    }

    // ===================== Effacement, insertion, défilement =====================

    private Cell Erased => Cell.Blank(_bg);

    private void EraseInDisplay(int mode)
    {
        switch (mode)
        {
            case 0:
                EraseInLine(0);
                for (int r = CursorRow + 1; r < Rows; r++)
                {
                    Fill(_screen[r], 0, Columns, Erased);
                }

                break;
            case 1:
                EraseInLine(1);
                for (int r = 0; r < CursorRow; r++)
                {
                    Fill(_screen[r], 0, Columns, Erased);
                }

                break;
            case 2:
            case 3:
                for (int r = 0; r < Rows; r++)
                {
                    Fill(_screen[r], 0, Columns, Erased);
                }

                if (mode == 3)
                {
                    _scrollback.Clear();
                }

                break;
        }
    }

    private void EraseInLine(int mode)
    {
        var line = _screen[CursorRow];
        switch (mode)
        {
            case 0:
                Fill(line, CursorColumn, Columns, Erased);
                break;
            case 1:
                Fill(line, 0, CursorColumn + 1, Erased);
                break;
            case 2:
                Fill(line, 0, Columns, Erased);
                break;
        }

        _wrapPending = false;
    }

    private void EraseChars(int count) =>
        Fill(_screen[CursorRow], CursorColumn, Math.Min(Columns, CursorColumn + count), Erased);

    private void InsertChars(int count)
    {
        var line = _screen[CursorRow];
        count = Math.Min(count, Columns - CursorColumn);
        Array.Copy(line, CursorColumn, line, CursorColumn + count, Columns - CursorColumn - count);
        Fill(line, CursorColumn, CursorColumn + count, Erased);
        _wrapPending = false;
    }

    private void DeleteChars(int count)
    {
        var line = _screen[CursorRow];
        count = Math.Min(count, Columns - CursorColumn);
        Array.Copy(line, CursorColumn + count, line, CursorColumn, Columns - CursorColumn - count);
        Fill(line, Columns - count, Columns, Erased);
        _wrapPending = false;
    }

    private void InsertLines(int count)
    {
        if (CursorRow < _scrollTop || CursorRow > _scrollBottom)
        {
            return;
        }

        ScrollDown(CursorRow, _scrollBottom, count);
        CursorColumn = 0;
    }

    private void DeleteLines(int count)
    {
        if (CursorRow < _scrollTop || CursorRow > _scrollBottom)
        {
            return;
        }

        ScrollUp(CursorRow, _scrollBottom, count);
        CursorColumn = 0;
    }

    private void ScrollUp(int top, int bottom, int count)
    {
        count = Math.Min(count, bottom - top + 1);
        for (int i = 0; i < count; i++)
        {
            var removed = _screen[top];
            if (top == 0 && !IsAlternateScreen)
            {
                PushScrollback(removed);
            }

            Array.Copy(_screen, top + 1, _screen, top, bottom - top);
            _screen[bottom] = NewLine(Columns, Erased);
        }
    }

    private void ScrollDown(int top, int bottom, int count)
    {
        count = Math.Min(count, bottom - top + 1);
        for (int i = 0; i < count; i++)
        {
            Array.Copy(_screen, top, _screen, top + 1, bottom - top);
            _screen[top] = NewLine(Columns, Erased);
        }
    }

    private void PushScrollback(Cell[] line)
    {
        _scrollback.Add(line);
        _scrolledOff++;
    }

    /// <summary>
    /// Historique en anneau : une fois plein, chaque nouvelle ligne remplace la plus ancienne sans déplacer les autres (une
    /// sortie abondante ne fige plus l'interface).
    /// </summary>
    private sealed class LineRing(int capacity)
    {
        private readonly Cell[][] _lines = new Cell[Math.Max(capacity, 0)][];
        private int _start;

        public int Count { get; private set; }

        public Cell[] this[int index] => _lines[(_start + index) % _lines.Length];

        public void Add(Cell[] line)
        {
            if (_lines.Length == 0)
            {
                return;
            }

            if (Count < _lines.Length)
            {
                _lines[(_start + Count) % _lines.Length] = line;
                Count++;
            }
            else
            {
                _lines[_start] = line;
                _start = (_start + 1) % _lines.Length;
            }
        }

        public void Clear()
        {
            Array.Clear(_lines);
            _start = 0;
            Count = 0;
        }
    }

    // ===================== Modes et attributs =====================

    private void SetModes(bool enable)
    {
        foreach (var raw in _params)
        {
            int mode = Math.Max(raw, 0);
            if (_private == '?')
            {
                switch (mode)
                {
                    case 1:
                        ApplicationCursorKeys = enable;
                        break;
                    case 6:
                        OriginMode = enable;
                        CursorPosition(0, 0);
                        break;
                    case 7:
                        AutoWrap = enable;
                        break;
                    case 25:
                        CursorVisible = enable;
                        break;
                    case 47:
                    case 1047:
                        SwitchScreen(enable, clear: enable && mode == 1047);
                        break;
                    case 1049:
                        if (enable)
                        {
                            SaveCursor();
                            SwitchScreen(true, clear: true);
                        }
                        else
                        {
                            SwitchScreen(false, clear: false);
                            RestoreCursor();
                        }

                        break;
                    case 2004:
                        BracketedPaste = enable;
                        break;
                }
            }
            else if (_private == '\0' && mode == 4)
            {
                InsertMode = enable;
            }
        }
    }

    private void SwitchScreen(bool alternate, bool clear)
    {
        if (alternate)
        {
            if (_alt is null || clear)
            {
                _alt = NewScreen(Rows, Columns);
            }

            _screen = _alt;
        }
        else
        {
            _screen = _main;
        }

        _wrapPending = false;
    }

    private void SelectGraphicRendition()
    {
        for (int i = 0; i < _params.Count; i++)
        {
            int p = Math.Max(_params[i], 0);
            switch (p)
            {
                case 0:
                    _fg = TerminalColor.Default;
                    _bg = TerminalColor.Default;
                    _flags = CellFlags.None;
                    break;
                case 1:
                    _flags |= CellFlags.Bold;
                    break;
                case 2:
                    _flags |= CellFlags.Dim;
                    break;
                case 3:
                    _flags |= CellFlags.Italic;
                    break;
                case 4:
                    _flags |= CellFlags.Underline;
                    break;
                case 7:
                    _flags |= CellFlags.Inverse;
                    break;
                case 8:
                    _flags |= CellFlags.Hidden;
                    break;
                case 21:
                case 22:
                    _flags &= ~(CellFlags.Bold | CellFlags.Dim);
                    break;
                case 23:
                    _flags &= ~CellFlags.Italic;
                    break;
                case 24:
                    _flags &= ~CellFlags.Underline;
                    break;
                case 27:
                    _flags &= ~CellFlags.Inverse;
                    break;
                case 28:
                    _flags &= ~CellFlags.Hidden;
                    break;
                case >= 30 and <= 37:
                    _fg = p - 30;
                    break;
                case 38:
                    _fg = ExtendedColor(ref i, _fg);
                    break;
                case 39:
                    _fg = TerminalColor.Default;
                    break;
                case >= 40 and <= 47:
                    _bg = p - 40;
                    break;
                case 48:
                    _bg = ExtendedColor(ref i, _bg);
                    break;
                case 49:
                    _bg = TerminalColor.Default;
                    break;
                case >= 90 and <= 97:
                    _fg = p - 90 + 8;
                    break;
                case >= 100 and <= 107:
                    _bg = p - 100 + 8;
                    break;
            }
        }
    }

    /// <summary>38;5;n (palette 256) ou 38;2;r;g;b (couleur vraie).</summary>
    private int ExtendedColor(ref int i, int current)
    {
        int kind = i + 1 < _params.Count ? _params[i + 1] : -1;
        if (kind == 5 && i + 2 < _params.Count)
        {
            i += 2;
            return Math.Clamp(Math.Max(_params[i], 0), 0, 255);
        }

        if (kind == 2 && i + 4 < _params.Count)
        {
            int r = Math.Clamp(Math.Max(_params[i + 2], 0), 0, 255);
            int g = Math.Clamp(Math.Max(_params[i + 3], 0), 0, 255);
            int b = Math.Clamp(Math.Max(_params[i + 4], 0), 0, 255);
            i += 4;
            return TerminalColor.Rgb((byte)r, (byte)g, (byte)b);
        }

        return current;
    }

    private void DeviceStatusReport()
    {
        switch (Param(0, 0))
        {
            case 5:
                Response?.Invoke("\x1b[0n");
                break;
            case 6:
                int row = OriginMode ? CursorRow - _scrollTop : CursorRow;
                Response?.Invoke($"\x1b[{row + 1};{CursorColumn + 1}R");
                break;
        }
    }

    private void SoftReset()
    {
        CursorVisible = true;
        InsertMode = false;
        OriginMode = false;
        AutoWrap = true;
        ApplicationCursorKeys = false;
        _fg = TerminalColor.Default;
        _bg = TerminalColor.Default;
        _flags = CellFlags.None;
        _scrollTop = 0;
        _scrollBottom = Rows - 1;
        _g0Graphics = _g1Graphics = _shiftOut = false;
        _saved = InitialCursor;
    }

    private void Reset()
    {
        SoftReset();
        BracketedPaste = false;
        _main = NewScreen(Rows, Columns);
        _alt = null;
        _screen = _main;
        _scrollback.Clear();
        CursorRow = CursorColumn = 0;
        _wrapPending = false;
        _eraseFrom = null;
    }

    // ===================== Utilitaires =====================

    private static char DecSpecialGraphics(char c) => c switch
    {
        '`' => '◆',
        'a' => '▒',
        'f' => '°',
        'g' => '±',
        'j' => '┘',
        'k' => '┐',
        'l' => '┌',
        'm' => '└',
        'n' => '┼',
        'q' => '─',
        't' => '├',
        'u' => '┤',
        'v' => '┴',
        'w' => '┬',
        'x' => '│',
        'y' => '≤',
        'z' => '≥',
        '~' => '·',
        _ => c,
    };

    private static void Fill(Cell[] line, int from, int to, Cell value)
    {
        for (int i = Math.Max(from, 0); i < Math.Min(to, line.Length); i++)
        {
            line[i] = value;
        }
    }

    private static Cell[] NewLine(int columns, Cell value)
    {
        var line = new Cell[columns];
        Array.Fill(line, value);
        return line;
    }

    private static Cell[][] NewScreen(int rows, int columns)
    {
        var screen = new Cell[rows][];
        for (int r = 0; r < rows; r++)
        {
            screen[r] = NewLine(columns, Cell.Blank());
        }

        return screen;
    }

    private static Cell[][] ResizeScreen(Cell[][] screen, int skip, int rows, int columns)
    {
        var result = new Cell[rows][];
        for (int r = 0; r < rows; r++)
        {
            var line = NewLine(columns, Cell.Blank());
            int source = r + skip;
            if (source < screen.Length)
            {
                Array.Copy(screen[source], line, Math.Min(columns, screen[source].Length));
            }

            result[r] = line;
        }

        return result;
    }

    private record struct SavedCursor(
        int Row, int Column, int Foreground, int Background, CellFlags Flags,
        bool G0Graphics, bool G1Graphics, bool ShiftOut, bool OriginMode);
}
