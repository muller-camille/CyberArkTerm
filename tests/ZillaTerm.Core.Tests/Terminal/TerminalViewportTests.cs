using ZillaTerm.Core.Terminal;

namespace ZillaTerm.Core.Tests.Terminal;

/// <summary>
/// Vue remontée dans l'historique et sélection pendant que la sortie continue (tail -f, compilation) : elles restent
/// sur le même texte, et « Copier » prend les lignes sélectionnées.
/// </summary>
public class TerminalViewportTests
{
    private static string Lines(int from, int to) => string.Concat(Enumerable.Range(from, to - from + 1).Select(i => $"\r\nline {i}"));

    /// <summary>Lignes affichées, comme le terminal les dessine.</summary>
    private static string[] Visible(TerminalViewport view) =>
        Enumerable.Range(-view.ScrollOffset, view.Emulator.Rows)
            .Select(row => string.Concat(view.Emulator.GetLine(row).Select(view.Emulator.CellText)).TrimEnd())
            .ToArray();

    [Fact]
    public void ViewScrolledUpStaysOnTheSameLinesWhileOutputArrives()
    {
        var t = new TerminalEmulator(20, 3, maxScrollback: 1000);
        var view = new TerminalViewport(t);
        t.Feed("line 1" + Lines(2, 100));
        view.ScrollTo(50);
        var shown = Visible(view);
        Assert.Equal(["line 48", "line 49", "line 50"], shown);

        t.Feed(Lines(101, 300));

        Assert.Equal(shown, Visible(view));
        Assert.Equal(250, view.ScrollOffset);
        view.ScrollBy(-1);
        Assert.Equal(["line 49", "line 50", "line 51"], Visible(view));

        // En bas, la vue suit la sortie.
        view.ScrollTo(0);
        t.Feed(Lines(301, 310));
        Assert.Equal(0, view.ScrollOffset);
        Assert.Equal(["line 308", "line 309", "line 310"], Visible(view));
    }

    [Fact]
    public void ViewStaysOnItsLinesWhenTheFullHistoryDropsTheOldestOnes()
    {
        var t = new TerminalEmulator(20, 3, maxScrollback: 100);
        var view = new TerminalViewport(t);
        t.Feed("line 1" + Lines(2, 200));
        view.ScrollTo(20);
        var shown = Visible(view);

        t.Feed(Lines(201, 250));
        Assert.Equal(shown, Visible(view));
        Assert.Equal(100, t.ScrollbackCount);

        // Lignes affichées oubliées à leur tour : la vue reste en haut de l'historique, sans revenir à la fin.
        t.Feed(Lines(251, 400));
        Assert.Equal(100, view.ScrollOffset);
        Assert.Equal(["line 298", "line 299", "line 300"], Visible(view));
    }

    [Fact]
    public void SelectionKeepsItsTextWhileOutputArrivesAndLeavesWithItsLines()
    {
        var t = new TerminalEmulator(20, 3, maxScrollback: 100);
        var view = new TerminalViewport(t);
        t.Feed("line 1" + Lines(2, 60));
        view.ScrollTo(10);
        view.Select((-10, 5), (-9, 6));
        Assert.Equal("48\nline 49", view.SelectedText);

        // Sortie pendant que la souris glisse : la fin suit le pointeur sur les lignes affichées, le début ne bouge pas.
        t.Feed(Lines(61, 120));
        view.ExtendSelection((-view.ScrollOffset + 2, 6));
        Assert.Equal("48\nline 49\nline 50", view.SelectedText);
        Assert.True(view.HasSelection);

        // Ses lignes quittent l'historique plein : la sélection est retirée, rien n'est copié.
        t.Feed(Lines(121, 200));
        Assert.Null(view.Selection);
        Assert.False(view.HasSelection);
        Assert.Equal("", view.SelectedText);
    }

    [Fact]
    public void SelectionOnTheScreenFollowsItsTextWhenTheScreenScrollsOrShrinks()
    {
        var t = new TerminalEmulator(20, 4);
        var view = new TerminalViewport(t);
        t.Feed("$ make\r\nerror: x.c:12\r\nbuild");
        view.Select((1, 0), (1, 12));

        t.Feed("\r\nmore\r\noutput\r\nend");
        Assert.Equal(((-1, 0), (-1, 12)), view.Selection);
        Assert.Equal("error: x.c:12", view.SelectedText);

        t.Resize(20, 2);
        Assert.Equal("error: x.c:12", view.SelectedText);
    }

    [Fact]
    public void ClearedHistoryBringsTheViewBackAndDropsTheSelection()
    {
        var t = new TerminalEmulator(20, 3);
        var view = new TerminalViewport(t);
        t.Feed("line 1" + Lines(2, 50));
        view.ScrollTo(20);
        view.Select((-20, 0), (-19, 3));

        t.Feed("\x1b[3J");
        Assert.Equal(0, view.ScrollOffset);
        Assert.Null(view.Selection);

        // Une nouvelle sortie remplit l'historique : la vue reste à la fin.
        t.Feed(Lines(51, 80));
        Assert.Equal(0, view.ScrollOffset);
    }

    [Fact]
    public void AlternateScreenShowsItsOwnLinesOnly()
    {
        var t = new TerminalEmulator(20, 3);
        var view = new TerminalViewport(t);
        t.Feed("line 1" + Lines(2, 50));
        view.ScrollTo(5);

        t.Feed("\x1b[?1049h\x1b[Hvim");

        Assert.Equal(0, view.ScrollOffset);
        Assert.Equal(["vim", "", ""], Visible(view));
        view.ScrollTo(3);
        Assert.Equal(0, view.ScrollOffset);
    }
}
