using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CyberArkTerm.Core.Terminal;

namespace CyberArkTerm.App.Terminal;

/// <summary>
/// Affichage d'un <see cref="TerminalEmulator"/> et saisie clavier :
/// sélection = copie, clic droit = collage, molette = historique.
/// </summary>
public sealed class TerminalView : FrameworkElement
{
    private const double Padding = 4;

    private static readonly FontFamily MonoFamily = new("Cascadia Mono, Consolas, Lucida Console, Courier New");

    private readonly Dictionary<uint, SolidColorBrush> _brushes = [];
    private readonly Typeface _regular = new(MonoFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private readonly Typeface _bold = new(MonoFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private double _fontSize = TerminalAppearance.FontSize;
    private TerminalTheme _theme = TerminalAppearance.Theme;
    private IReadOnlyList<TerminalMatch> _matches = [];
    private int _currentMatch = -1;
    private TerminalEmulator? _emulator;
    private double _cellWidth;
    private double _cellHeight;
    private int _scrollOffset;
    private (int Row, int Col)? _selectionStart;
    private (int Row, int Col)? _selectionEnd;
    private bool _selecting;

    public TerminalView()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.IBeam;
        SnapsToDevicePixels = true;
        MeasureCell();
        // Palette et taille des Paramètres : suivies tant que le terminal est affiché (il peut changer de place).
        Loaded += (_, _) =>
        {
            TerminalAppearance.Changed += OnAppearanceChanged;
            if (!ReferenceEquals(_theme, TerminalAppearance.Theme))
            {
                _theme = TerminalAppearance.Theme;
                InvalidateVisual();
            }
        };
        Unloaded += (_, _) => TerminalAppearance.Changed -= OnAppearanceChanged;
    }

    /// <summary>Ctrl+Maj+F : recherche dans le terminal.</summary>
    public event Action? SearchRequested;

    /// <summary>Ctrl+Maj+S : enregistrer le contenu du terminal.</summary>
    public event Action? SaveRequested;

    public TerminalTheme Theme => _theme;

    public double TerminalFontSize => _fontSize;

    private void OnAppearanceChanged()
    {
        _theme = TerminalAppearance.Theme;
        SetFontSize(TerminalAppearance.FontSize);
        InvalidateVisual();
    }

    /// <summary>Taille de police de ce terminal (Ctrl+molette) ; le nombre de lignes et colonnes suit.</summary>
    public void SetFontSize(double size)
    {
        size = Math.Clamp(size, TerminalAppearance.MinFontSize, TerminalAppearance.MaxFontSize);
        if (size == _fontSize)
        {
            return;
        }

        _fontSize = size;
        MeasureCell();
        FitToSize();
        InvalidateVisual();
    }

    private void MeasureCell()
    {
        var probe = Format("M", _regular, Brushes.White);
        _cellWidth = probe.WidthIncludingTrailingWhitespace;
        _cellHeight = Math.Ceiling(MonoFamily.LineSpacing * _fontSize);
    }

    /// <summary>Occurrences de la recherche, surlignées ; la courante est amenée à l'écran.</summary>
    /// <param name="scroll">Faux quand les occurrences sont recalculées après de nouvelles lignes : la vue ne bouge pas.</param>
    public void ShowMatches(IReadOnlyList<TerminalMatch> matches, int current, bool scroll = true)
    {
        _matches = matches;
        _currentMatch = current;
        if (scroll && _emulator is not null && current >= 0 && current < matches.Count)
        {
            // Ligne visible : en haut de l'historique si besoin, vers le milieu de l'écran sinon.
            int row = matches[current].Row;
            int top = -_scrollOffset;
            if (row < top || row >= top + _emulator.Rows)
            {
                _scrollOffset = Math.Clamp(-row + (_emulator.Rows / 2), 0, _emulator.ScrollbackCount);
            }
        }

        InvalidateVisual();
    }

    public void ClearMatches()
    {
        _matches = [];
        _currentMatch = -1;
        InvalidateVisual();
    }

    /// <summary>Saisie à envoyer au serveur (frappe clavier, collage, molette), encodée par le destinataire.</summary>
    public event Action<TerminalInput>? Input;

    /// <summary>Nouvelle taille en caractères (colonnes, lignes) après redimensionnement.</summary>
    public event Action<int, int>? TerminalResized;

    public TerminalEmulator? Emulator
    {
        get => _emulator;
        set
        {
            _emulator = value;
            _scrollOffset = 0;
            ClearSelection();
            FitToSize();
            InvalidateVisual();
        }
    }

    public (int Columns, int Rows) SizeInCells => (
        Math.Max(2, (int)((ActualWidth - 2 * Padding) / _cellWidth)),
        Math.Max(2, (int)((ActualHeight - 2 * Padding) / _cellHeight)));

    /// <summary>À appeler après avoir alimenté l'émulateur.</summary>
    public void Refresh()
    {
        if (_emulator is not null)
        {
            _scrollOffset = Math.Min(_scrollOffset, _emulator.ScrollbackCount);
        }

        InvalidateVisual();
    }

    // ===================== Rendu =====================

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brush(_theme.Background), null, new Rect(RenderSize));
        var emulator = _emulator;
        if (emulator is null)
        {
            return;
        }

        int top = -_scrollOffset;
        for (int screenRow = 0; screenRow < emulator.Rows; screenRow++)
        {
            int row = top + screenRow;
            if (row < -emulator.ScrollbackCount || row >= emulator.Rows)
            {
                continue;
            }

            DrawMatches(dc, row, Padding + screenRow * _cellHeight);
            DrawLine(dc, emulator, row, Padding + screenRow * _cellHeight);
        }

        // Curseur : pavé plein si le terminal a le focus, contour sinon.
        if (emulator.CursorVisible && _scrollOffset == 0)
        {
            var rect = new Rect(Padding + emulator.CursorColumn * _cellWidth, Padding + emulator.CursorRow * _cellHeight, _cellWidth, _cellHeight);
            if (IsKeyboardFocused)
            {
                dc.DrawRectangle(Brush(_theme.Cursor), null, rect);
                var cell = emulator.GetLine(emulator.CursorRow)[Math.Min(emulator.CursorColumn, emulator.Columns - 1)];
                if (cell.Char != ' ')
                {
                    dc.DrawText(Format(cell.Char.ToString(), _regular, Brush(_theme.Background)), rect.TopLeft);
                }
            }
            else
            {
                dc.DrawRectangle(null, new Pen(Brush(_theme.Cursor), 1), new Rect(rect.X + 0.5, rect.Y + 0.5, rect.Width - 1, rect.Height - 1));
            }
        }
    }

    private void DrawLine(DrawingContext dc, TerminalEmulator emulator, int row, double y)
    {
        var line = emulator.GetLine(row);
        int columns = Math.Min(line.Length, emulator.Columns);
        int col = 0;
        while (col < columns)
        {
            var (fg, bg, bold, underline) = Colors(line[col], IsSelected(row, col));
            int start = col;
            bool ascii = true;
            while (col < columns && Colors(line[col], IsSelected(row, col)) == (fg, bg, bold, underline))
            {
                ascii &= line[col].Char < 0x80;
                col++;
            }

            double x = Padding + start * _cellWidth;
            double width = (col - start) * _cellWidth;
            if (bg != _theme.Background)
            {
                dc.DrawRectangle(Brush(bg), null, new Rect(x, y, width, _cellHeight));
            }

            var typeface = bold ? _bold : _regular;
            var brush = Brush(fg);
            if (ascii)
            {
                var text = new string(line.AsSpan(start, col - start).ToArray().Select(c => c.Char).ToArray());
                if (!string.IsNullOrWhiteSpace(text))
                {
                    dc.DrawText(Format(text, typeface, brush), new Point(x, y));
                }
            }
            else
            {
                // Caractères hors ASCII (cadres, accents...) : placés case par case pour garder l'alignement
                // même si la police de secours a une autre largeur.
                for (int c = start; c < col; c++)
                {
                    if (line[c].Char != ' ')
                    {
                        dc.DrawText(Format(line[c].Char.ToString(), typeface, brush), new Point(Padding + c * _cellWidth, y));
                    }
                }
            }

            if (underline)
            {
                dc.DrawRectangle(brush, null, new Rect(x, y + _cellHeight - 2, width, 1));
            }
        }
    }

    /// <summary>Fond des occurrences de la recherche (jaune ; orange pour la courante).</summary>
    private void DrawMatches(DrawingContext dc, int row, double y)
    {
        for (int i = 0; i < _matches.Count; i++)
        {
            var match = _matches[i];
            if (match.Row == row)
            {
                dc.DrawRectangle(Brush(i == _currentMatch ? 0xFF9632u : 0xFFE066u), null,
                    new Rect(Padding + match.Column * _cellWidth, y, match.Length * _cellWidth, _cellHeight));
            }
        }
    }

    private (uint Fg, uint Bg, bool Bold, bool Underline) Colors(Cell cell, bool selected)
    {
        bool bold = cell.Flags.HasFlag(CellFlags.Bold);
        uint fg = _theme.ToRgb(cell.Foreground, foreground: true, bold);
        uint bg = _theme.ToRgb(cell.Background, foreground: false);
        if (cell.Flags.HasFlag(CellFlags.Dim))
        {
            fg = Blend(fg, bg);
        }

        if (cell.Flags.HasFlag(CellFlags.Inverse))
        {
            (fg, bg) = (bg, fg);
        }

        if (cell.Flags.HasFlag(CellFlags.Hidden))
        {
            fg = bg;
        }

        if (selected)
        {
            (fg, bg) = (0x0C0C0C, 0x9CC7F2);
        }

        return (fg, bg, bold, cell.Flags.HasFlag(CellFlags.Underline));
    }

    private static uint Blend(uint a, uint b) =>
        (((a >> 16 & 0xFF) + (b >> 16 & 0xFF)) / 2 << 16) | (((a >> 8 & 0xFF) + (b >> 8 & 0xFF)) / 2 << 8) | ((a & 0xFF) + (b & 0xFF)) / 2;

    private SolidColorBrush Brush(uint rgb)
    {
        if (!_brushes.TryGetValue(rgb, out var brush))
        {
            brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            brush.Freeze();
            _brushes[rgb] = brush;
        }

        return brush;
    }

    private FormattedText Format(string text, Typeface typeface, Brush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, _fontSize, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    // ===================== Taille =====================

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        FitToSize();
    }

    /// <summary>
    /// Réajuste l'émulateur à la taille affichée (et le signale) : utile quand la session a imposé une autre taille, par
    /// exemple celle mesurée avant que le terminal ne change de place pendant la connexion.
    /// </summary>
    public void Refit() => FitToSize();

    private void FitToSize()
    {
        if (_emulator is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var (columns, rows) = SizeInCells;
        if (columns != _emulator.Columns || rows != _emulator.Rows)
        {
            _emulator.Resize(columns, rows);
            TerminalResized?.Invoke(columns, rows);
            InvalidateVisual();
        }
    }

    // ===================== Clavier =====================

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (_emulator is null)
        {
            return;
        }

        var mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control);
        bool shift = mods.HasFlag(ModifierKeys.Shift);
        bool alt = mods.HasFlag(ModifierKeys.Alt);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Raccourcis locaux (non envoyés au serveur).
        if (ctrl && shift && key == Key.F)
        {
            SearchRequested?.Invoke();
            e.Handled = true;
            return;
        }

        if (ctrl && shift && key == Key.S)
        {
            SaveRequested?.Invoke();
            e.Handled = true;
            return;
        }

        if (ctrl && !shift && !alt && key is Key.D0 or Key.NumPad0)
        {
            // Taille de police par défaut (Ctrl+molette la change).
            SetFontSize(TerminalAppearance.FontSize);
            e.Handled = true;
            return;
        }

        if (ctrl && shift && key == Key.C)
        {
            CopySelection();
            e.Handled = true;
            return;
        }

        if ((ctrl && shift && key == Key.V) || (shift && key == Key.Insert))
        {
            Paste();
            e.Handled = true;
            return;
        }

        if (shift && key is Key.PageUp or Key.PageDown)
        {
            ScrollBy(key == Key.PageUp ? _emulator.Rows - 1 : -(_emulator.Rows - 1));
            e.Handled = true;
            return;
        }

        if (MapKey(key) is { } terminalKey)
        {
            // Alt+F4 & co. restent gérés par Windows.
            if (alt && terminalKey is >= TerminalKey.F1 and <= TerminalKey.F12)
            {
                return;
            }

            Send(TerminalInput.Special(terminalKey, shift, alt, ctrl));
            e.Handled = true;
            return;
        }

        // Ctrl+lettre → caractère de contrôle. Ctrl+Alt = AltGr sur clavier français : laissé à la saisie de texte.
        if (ctrl && !alt && KeyChar(key) is { } c && TerminalKeys.Control(c) is { } control)
        {
            Send(TerminalInput.Typed(control));
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        if (!string.IsNullOrEmpty(e.Text))
        {
            Send(TerminalInput.Typed(e.Text));
        }
        else if (!string.IsNullOrEmpty(e.SystemText))
        {
            Send(TerminalInput.Typed("\x1b" + e.SystemText)); // Alt+touche : préfixe Échap (« Meta »).
        }
        else if (!string.IsNullOrEmpty(e.ControlText))
        {
            Send(TerminalInput.Typed(e.ControlText));
        }

        e.Handled = true;
    }

    private static TerminalKey? MapKey(Key key) => key switch
    {
        Key.Up => TerminalKey.Up,
        Key.Down => TerminalKey.Down,
        Key.Left => TerminalKey.Left,
        Key.Right => TerminalKey.Right,
        Key.Home => TerminalKey.Home,
        Key.End => TerminalKey.End,
        Key.Insert => TerminalKey.Insert,
        Key.Delete => TerminalKey.Delete,
        Key.PageUp => TerminalKey.PageUp,
        Key.PageDown => TerminalKey.PageDown,
        Key.Enter => TerminalKey.Enter,
        Key.Back => TerminalKey.Backspace,
        Key.Tab => TerminalKey.Tab,
        Key.Escape => TerminalKey.Escape,
        >= Key.F1 and <= Key.F12 => TerminalKey.F1 + (key - Key.F1),
        _ => null,
    };

    private static char? KeyChar(Key key) => key switch
    {
        >= Key.A and <= Key.Z => (char)('A' + (key - Key.A)),
        Key.Space => ' ',
        Key.OemOpenBrackets => '[',
        Key.OemCloseBrackets => ']',
        Key.Oem5 => '\\',
        _ => null,
    };

    private void Send(TerminalInput input)
    {
        if (input.IsEmpty)
        {
            return;
        }

        if (_scrollOffset != 0 || _selectionStart is not null)
        {
            _scrollOffset = 0;
            ClearSelection();
            InvalidateVisual();
        }

        Input?.Invoke(input);
    }

    // ===================== Souris : focus, historique, sélection, collage =====================

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_emulator is null)
        {
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // Ctrl+molette : taille de police de ce terminal.
            SetFontSize(_fontSize + Math.Sign(e.Delta));
            e.Handled = true;
            return;
        }

        int lines = e.Delta / 40;
        if (_emulator.IsAlternateScreen)
        {
            // vim, less... : la molette déplace dans le document comme les flèches.
            var key = lines > 0 ? TerminalKey.Up : TerminalKey.Down;
            for (int i = 0; i < Math.Abs(lines); i++)
            {
                Input?.Invoke(TerminalInput.Scrolled(key));
            }
        }
        else
        {
            ScrollBy(lines);
        }

        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (_emulator is null)
        {
            return;
        }

        var cell = CellAt(e.GetPosition(this));
        if (e.ClickCount == 2)
        {
            SelectWord(cell);
            CopySelection();
        }
        else
        {
            _selectionStart = cell;
            _selectionEnd = cell;
            _selecting = true;
            CaptureMouse();
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_selecting)
        {
            _selectionEnd = CellAt(e.GetPosition(this));
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_selecting)
        {
            return;
        }

        _selecting = false;
        ReleaseMouseCapture();
        if (_selectionStart == _selectionEnd)
        {
            ClearSelection();
        }
        else
        {
            CopySelection();
        }

        InvalidateVisual();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        Focus();
        Paste();
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    private void ScrollBy(int lines)
    {
        if (_emulator is null)
        {
            return;
        }

        _scrollOffset = Math.Clamp(_scrollOffset + lines, 0, _emulator.ScrollbackCount);
        InvalidateVisual();
    }

    private (int Row, int Col) CellAt(Point p)
    {
        var emulator = _emulator!;
        int row = (int)Math.Floor((p.Y - Padding) / _cellHeight);
        int col = (int)Math.Floor((p.X - Padding) / _cellWidth);
        row = Math.Clamp(row, 0, emulator.Rows - 1) - _scrollOffset;
        return (row, Math.Clamp(col, 0, emulator.Columns - 1));
    }

    private void SelectWord((int Row, int Col) cell)
    {
        var line = _emulator!.GetLine(cell.Row);
        static bool IsWordChar(char c) => !char.IsWhiteSpace(c) && "\"'`()[]{}<>|;,".IndexOf(c) < 0;
        if (cell.Col >= line.Length || !IsWordChar(line[cell.Col].Char))
        {
            ClearSelection();
            return;
        }

        int start = cell.Col;
        int end = cell.Col;
        while (start > 0 && IsWordChar(line[start - 1].Char))
        {
            start--;
        }

        while (end < line.Length - 1 && IsWordChar(line[end + 1].Char))
        {
            end++;
        }

        _selectionStart = (cell.Row, start);
        _selectionEnd = (cell.Row, end);
    }

    private bool IsSelected(int row, int col)
    {
        if (_selectionStart is not { } a || _selectionEnd is not { } b || a == b && !_selecting)
        {
            return false;
        }

        if (a.Row > b.Row || (a.Row == b.Row && a.Col > b.Col))
        {
            (a, b) = (b, a);
        }

        return (row > a.Row || (row == a.Row && col >= a.Col)) && (row < b.Row || (row == b.Row && col <= b.Col));
    }

    private void ClearSelection()
    {
        _selectionStart = null;
        _selectionEnd = null;
    }

    private void CopySelection()
    {
        if (_emulator is null || _selectionStart is not { } a || _selectionEnd is not { } b)
        {
            return;
        }

        var text = _emulator.GetText(a.Row, a.Col, b.Row, b.Col).Replace("\n", Environment.NewLine);
        if (text.Length > 0)
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Presse-papiers occupé par une autre application : on ignore.
            }
        }
    }

    private void Paste()
    {
        if (_emulator is null)
        {
            return;
        }

        try
        {
            if (Clipboard.ContainsText())
            {
                Send(TerminalInput.Pasted(Clipboard.GetText()));
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }
    }
}
