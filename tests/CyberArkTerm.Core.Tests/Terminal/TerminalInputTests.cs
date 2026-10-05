using CyberArkTerm.Core.Terminal;

namespace CyberArkTerm.Core.Tests.Terminal;

public sealed class TerminalInputTests
{
    [Fact]
    public void EachTerminalEncodesTheKeyForItsOwnMode()
    {
        var shell = new TerminalEmulator();
        var vim = new TerminalEmulator();
        vim.Feed("\x1b[?1h\x1b[?2004h");   // curseur « application » et collage encadré, comme vim

        var up = TerminalInput.Special(TerminalKey.Up);
        Assert.Equal("\x1b[A", up.Encode(shell));
        Assert.Equal("\x1bOA", up.Encode(vim));
        Assert.Equal("\x1b[1;5A", TerminalInput.Special(TerminalKey.Up, ctrl: true).Encode(vim));

        var paste = TerminalInput.Pasted("ls\nuptime");
        Assert.Equal("ls\ruptime", paste.Encode(shell));
        Assert.Equal("\x1b[200~ls\ruptime\x1b[201~", paste.Encode(vim));

        Assert.Equal("\x03", TerminalInput.Typed("\x03").Encode(vim));
        Assert.Equal("\x1bOB", TerminalInput.Scrolled(TerminalKey.Down).Encode(vim));
    }

    [Fact]
    public void TellsWhatIsTypingAndWhatIsAMultiLinePaste()
    {
        Assert.True(TerminalInput.Typed("a").IsTyping);
        Assert.True(TerminalInput.Special(TerminalKey.Enter).IsTyping);
        Assert.True(TerminalInput.Pasted("x").IsTyping);
        Assert.False(TerminalInput.Scrolled(TerminalKey.Up).IsTyping);

        Assert.True(TerminalInput.Typed("").IsEmpty);
        Assert.True(TerminalInput.Pasted("").IsEmpty);
        Assert.False(TerminalInput.Special(TerminalKey.Up).IsEmpty);

        Assert.False(TerminalInput.Pasted("uptime").IsMultiLinePaste);
        Assert.False(TerminalInput.Pasted("uptime\r\n").IsMultiLinePaste);
        Assert.True(TerminalInput.Pasted("cd /tmp\r\nrm -rf x").IsMultiLinePaste);
        Assert.True(TerminalInput.Pasted("a\rb").IsMultiLinePaste);
        Assert.False(TerminalInput.Typed("a\nb").IsMultiLinePaste);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 2)]
    [InlineData(3, 1, 3)]
    [InlineData(4, 2, 2)]
    [InlineData(5, 2, 3)]
    [InlineData(6, 2, 3)]
    [InlineData(7, 2, 4)]
    [InlineData(8, 2, 4)]
    [InlineData(0, 1, 1)]
    [InlineData(12, 2, 4)]
    public void LaysOutUpToEightSessions(int count, int rows, int columns)
    {
        Assert.Equal((rows, columns), ParallelLayout.For(count));
        Assert.True(rows * columns >= Math.Min(Math.Max(count, 1), ParallelLayout.MaxSessions));
    }

    private sealed record Pane(string Name, bool Included, bool Connected);

    [Fact]
    public void SimultaneousTypingGoesToTheTickedConnectedSessions()
    {
        var a = new Pane("a", true, true);
        var b = new Pane("b", true, true);
        var c = new Pane("c", false, true);
        var d = new Pane("d", true, false);
        Pane[] panes = [a, b, c, d];

        IReadOnlyList<Pane> Targets(Pane source, bool broadcast) =>
            ParallelLayout.Targets(source, panes, broadcast, p => p.Included, p => p.Connected);

        // Sans saisie simultanée : la session où l'on tape, seule.
        Assert.Equal([a], Targets(a, broadcast: false));

        // Avec : les sessions cochées et connectées, dans l'ordre de la vue.
        Assert.Equal([a, b], Targets(a, broadcast: true));
        Assert.Equal([a, b], Targets(b, broadcast: true));

        // Une session décochée ne reçoit rien des autres, et ce qu'on y tape ne va qu'à elle.
        Assert.Equal([c], Targets(c, broadcast: true));

        // La session où l'on tape reçoit toujours sa saisie, même si elle n'est pas connectée.
        Assert.Equal([a, b, d], Targets(d, broadcast: true));
    }
}
