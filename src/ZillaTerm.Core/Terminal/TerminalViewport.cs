namespace ZillaTerm.Core.Terminal;

/// <summary>
/// Lignes affichées et sélection d'un terminal, ancrées sur le texte de l'émulateur (numéros de ligne absolus) : pendant
/// que la sortie continue (tail -f, compilation), une vue remontée dans l'historique reste sur les mêmes lignes, même
/// quand l'historique plein oublie les plus anciennes, et la sélection désigne toujours le même texte. Une sélection dont
/// une ligne quitte l'historique ou l'écran est retirée. Lignes et colonnes comme <see cref="TerminalEmulator.GetLine"/>.
/// Non thread-safe, comme l'émulateur.
/// </summary>
public sealed class TerminalViewport(TerminalEmulator emulator)
{
    // Numéro de la première ligne affichée quand la vue est remontée ; null : la vue suit la fin.
    private long? _top;

    // Début (là où la sélection a commencé) et fin de la sélection : numéro de ligne et colonne.
    private (long Line, int Column)? _start;
    private (long Line, int Column)? _end;

    public TerminalEmulator Emulator => emulator;

    /// <summary>Lignes remontées dans l'historique (0 : la fin, comme à la saisie).</summary>
    public int ScrollOffset
    {
        get
        {
            if (_top is not long top)
            {
                return 0;
            }

            long offset = emulator.FirstScreenLine - top;
            if (offset > emulator.ScrollbackCount)
            {
                // Première ligne affichée sortie de l'historique (plein, ou effacé) : la vue reste en haut de ce qu'il
                // en reste, ou revient à la fin s'il est vide (écran alternatif compris).
                ScrollTo(emulator.ScrollbackCount);
                return emulator.ScrollbackCount;
            }

            return (int)Math.Max(offset, 0);
        }
    }

    /// <summary>Affiche l'historique à <paramref name="offset"/> lignes de la fin ; la vue reste ensuite sur ces lignes.</summary>
    public void ScrollTo(int offset)
    {
        offset = Math.Clamp(offset, 0, emulator.ScrollbackCount);
        _top = offset == 0 ? null : emulator.FirstScreenLine - offset;
    }

    public void ScrollBy(int lines) => ScrollTo(ScrollOffset + lines);

    /// <summary>Sélection : début (là où elle a commencé) et fin, aux lignes actuelles ; null sans sélection.</summary>
    public ((int Row, int Column) Start, (int Row, int Column) End)? Selection
    {
        get
        {
            if (_start is not { } start || _end is not { } end)
            {
                return null;
            }

            long first = emulator.FirstScreenLine - emulator.ScrollbackCount;
            long last = emulator.FirstScreenLine + emulator.Rows - 1;
            if (Math.Min(start.Line, end.Line) < first || Math.Max(start.Line, end.Line) > last)
            {
                ClearSelection();
                return null;
            }

            return ((Row(start.Line), start.Column), (Row(end.Line), end.Column));
        }
    }

    /// <summary>Sélection non vide (fin distincte du début).</summary>
    public bool HasSelection => Selection is { } selection && selection.Start != selection.End;

    /// <summary>Texte sélectionné, comme <see cref="TerminalEmulator.GetText"/> ; vide sans sélection.</summary>
    public string SelectedText =>
        Selection is { } s ? emulator.GetText(s.Start.Row, s.Start.Column, s.End.Row, s.End.Column) : "";

    public void Select((int Row, int Column) start, (int Row, int Column) end)
    {
        _start = (Line(start.Row), start.Column);
        _end = (Line(end.Row), end.Column);
    }

    /// <summary>Nouvelle fin de la sélection en cours (la souris glisse).</summary>
    public void ExtendSelection((int Row, int Column) end)
    {
        if (_start is not null)
        {
            _end = (Line(end.Row), end.Column);
        }
    }

    public void ClearSelection()
    {
        _start = null;
        _end = null;
    }

    private long Line(int row) => emulator.FirstScreenLine + row;

    private int Row(long line) => (int)(line - emulator.FirstScreenLine);
}
