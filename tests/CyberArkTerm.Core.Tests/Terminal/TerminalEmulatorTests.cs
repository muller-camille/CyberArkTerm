using CyberArkTerm.Core.Terminal;

namespace CyberArkTerm.Core.Tests.Terminal;

public class TerminalEmulatorTests
{
    private static string Row(TerminalEmulator t, int row) => new string(t.GetLine(row).Select(c => c.Char).ToArray()).TrimEnd();

    private static string[] Screen(TerminalEmulator t) => Enumerable.Range(0, t.Rows).Select(r => Row(t, r)).ToArray();

    [Fact]
    public void PrintsTextAndHandlesCrLf()
    {
        var t = new TerminalEmulator(10, 3);

        t.Feed("ab\r\ncd");

        Assert.Equal(["ab", "cd", ""], Screen(t));
        Assert.Equal((1, 2), (t.CursorRow, t.CursorColumn));
    }

    [Fact]
    public void WrapsAtLastColumnOnlyWhenNextCharArrives()
    {
        var t = new TerminalEmulator(4, 3);

        t.Feed("abcd");
        Assert.Equal((0, 3), (t.CursorRow, t.CursorColumn));

        t.Feed("e");
        Assert.Equal(["abcd", "e", ""], Screen(t));
    }

    [Fact]
    public void CarriageReturnCancelsPendingWrap()
    {
        var t = new TerminalEmulator(4, 3);

        t.Feed("abcd\rX");

        Assert.Equal(["Xbcd", "", ""], Screen(t));
    }

    [Fact]
    public void ScrollsIntoScrollback()
    {
        var t = new TerminalEmulator(5, 2);

        t.Feed("1\r\n2\r\n3\r\n4");

        Assert.Equal(["3", "4"], Screen(t));
        Assert.Equal(2, t.ScrollbackCount);
        Assert.Equal("1", Row(t, -2));
        Assert.Equal("2", Row(t, -1));
    }

    [Fact]
    public void CursorPositionAndErase()
    {
        var t = new TerminalEmulator(6, 3);
        t.Feed("aaaaaa\r\nbbbbbb\r\ncccccc");

        t.Feed("\x1b[2;3H\x1b[K");
        Assert.Equal(["aaaaaa", "bb", "cccccc"], Screen(t));

        t.Feed("\x1b[1K");
        Assert.Equal(["aaaaaa", "", "cccccc"], Screen(t));

        t.Feed("\x1b[J");
        Assert.Equal(["aaaaaa", "", ""], Screen(t));

        t.Feed("\x1b[2J");
        Assert.Equal(["", "", ""], Screen(t));
        Assert.Equal((1, 2), (t.CursorRow, t.CursorColumn));
    }

    [Fact]
    public void RelativeMovesAreClamped()
    {
        var t = new TerminalEmulator(10, 5);

        t.Feed("\x1b[3;3H\x1b[10A\x1b[2C\x1b[B\x1b[20D");

        Assert.Equal((1, 0), (t.CursorRow, t.CursorColumn));
    }

    [Fact]
    public void BackspaceAndTab()
    {
        var t = new TerminalEmulator(20, 2);

        t.Feed("abc\bX\tY");

        Assert.Equal("abX     Y", Row(t, 0));
        Assert.Equal(9, t.CursorColumn);
    }

    [Fact]
    public void GraphicRendition()
    {
        var t = new TerminalEmulator(10, 2);

        t.Feed("\x1b[1;31mA\x1b[0;38;5;208;48;2;1;2;3mB\x1b[7mC\x1b[mD\x1b[94mE");

        var line = t.GetLine(0);
        Assert.Equal(1, line[0].Foreground);
        Assert.True(line[0].Flags.HasFlag(CellFlags.Bold));
        Assert.Equal(208, line[1].Foreground);
        Assert.Equal(TerminalColor.Rgb(1, 2, 3), line[1].Background);
        Assert.False(line[1].Flags.HasFlag(CellFlags.Bold));
        Assert.True(line[2].Flags.HasFlag(CellFlags.Inverse));
        Assert.Equal(TerminalColor.Default, line[3].Foreground);
        Assert.Equal(CellFlags.None, line[3].Flags);
        Assert.Equal(12, line[4].Foreground);
    }

    [Fact]
    public void ScrollRegionKeepsLinesOutsideAndSkipsScrollback()
    {
        var t = new TerminalEmulator(5, 4);
        t.Feed("top\r\na\r\nb\r\nbot");

        t.Feed("\x1b[2;3r\x1b[3;1H\nX");

        Assert.Equal(["top", "b", "X", "bot"], Screen(t));
        Assert.Equal(0, t.ScrollbackCount);
    }

    [Fact]
    public void InsertAndDeleteLinesAndChars()
    {
        var t = new TerminalEmulator(6, 3);
        t.Feed("abcdef\r\n2\r\n3");

        t.Feed("\x1b[1;3H\x1b[2@");
        Assert.Equal("ab  cd", Row(t, 0));

        t.Feed("\x1b[3P");
        Assert.Equal("abd", Row(t, 0));

        t.Feed("\x1b[2;1H\x1b[L");
        Assert.Equal(["abd", "", "2"], Screen(t));

        t.Feed("\x1b[M");
        Assert.Equal(["abd", "2", ""], Screen(t));

        t.Feed("\x1b[1;1H\x1b[2X");
        Assert.Equal("  d", Row(t, 0));
    }

    [Fact]
    public void AlternateScreenRestoresMainScreenAndCursor()
    {
        var t = new TerminalEmulator(10, 3);
        t.Feed("shell$ vi");

        t.Feed("\x1b[?1049h\x1b[Hvim content");
        Assert.True(t.IsAlternateScreen);
        Assert.Equal("vim conten", Row(t, 0));
        Assert.Equal(0, t.ScrollbackCount);

        t.Feed("\x1b[?1049l");
        Assert.False(t.IsAlternateScreen);
        Assert.Equal("shell$ vi", Row(t, 0));
        Assert.Equal((0, 9), (t.CursorRow, t.CursorColumn));
    }

    [Fact]
    public void ReportsTitleAndWorkingDirectory()
    {
        var t = new TerminalEmulator();
        string? title = null;
        var dirs = new List<string>();
        t.TitleChanged += s => title = s;
        t.WorkingDirectoryChanged += dirs.Add;

        t.Feed("\x1b]0;root@srv: ~\a");
        t.Feed("\x1b]7;file://srv01/var/log/my%20app\x1b\\");
        t.Feed("\x1b]7;/opt/app\a");
        t.Feed("\x1b]7;relative\a");

        Assert.Equal("root@srv: ~", title);
        Assert.Equal(["/var/log/my app", "/opt/app"], dirs);
        Assert.Equal("", Row(t, 0));
    }

    [Fact]
    public void SequencesSplitAcrossFeedsAreParsed()
    {
        var t = new TerminalEmulator(10, 2);
        var dirs = new List<string>();
        t.WorkingDirectoryChanged += dirs.Add;

        foreach (var chunk in new[] { "\x1b", "[3", "1mR", "\x1b]7;/t", "mp\x1b", "\\ok" })
        {
            t.Feed(chunk);
        }

        Assert.Equal("Rok", Row(t, 0));
        Assert.Equal(1, t.GetLine(0)[0].Foreground);
        Assert.Equal(["/tmp"], dirs);
    }

    [Fact]
    public void AnswersDeviceQueries()
    {
        var t = new TerminalEmulator(10, 5);
        var responses = new List<string>();
        t.Response += responses.Add;

        t.Feed("\x1b[3;4H\x1b[6n\x1b[5n\x1b[c");

        Assert.Equal(["\x1b[3;4R", "\x1b[0n", "\x1b[?1;2c"], responses);
    }

    [Fact]
    public void ModesAreTracked()
    {
        var t = new TerminalEmulator();

        t.Feed("\x1b[?1h\x1b[?2004h\x1b[?25l");
        Assert.True(t.ApplicationCursorKeys);
        Assert.True(t.BracketedPaste);
        Assert.False(t.CursorVisible);

        t.Feed("\x1b[?1l\x1b[?2004l\x1b[?25h");
        Assert.False(t.ApplicationCursorKeys);
        Assert.False(t.BracketedPaste);
        Assert.True(t.CursorVisible);
    }

    [Fact]
    public void DecLineDrawing()
    {
        var t = new TerminalEmulator(10, 2);

        t.Feed("\x1b(0lqk\x1b(Bq");

        Assert.Equal("┌─┐q", Row(t, 0));
    }

    [Fact]
    public void SaveAndRestoreCursorKeepsAttributes()
    {
        var t = new TerminalEmulator(10, 3);

        t.Feed("\x1b[32m\u001b7\x1b[0m\x1b[3;5HZ\u001b8A");

        Assert.Equal(2, t.GetLine(0)[0].Foreground);
        Assert.Equal('A', t.GetLine(0)[0].Char);
    }

    [Fact]
    public void RestoreWithoutSaveUsesDefaults()
    {
        var t = new TerminalEmulator(10, 3);

        t.Feed("\x1b[31m\u001b8X");

        Assert.Equal(TerminalColor.Default, t.GetLine(0)[0].Foreground);
    }

    [Fact]
    public void ResizeKeepsCursorLineAndPushesTopToScrollback()
    {
        var t = new TerminalEmulator(10, 5);
        t.Feed("1\r\n2\r\n3\r\n4\r\n5");

        t.Resize(4, 3);

        Assert.Equal(["3", "4", "5"], Screen(t));
        Assert.Equal(2, t.ScrollbackCount);
        Assert.Equal((2, 1), (t.CursorRow, t.CursorColumn));

        t.Resize(12, 4);
        Assert.Equal(["3", "4", "5", ""], Screen(t));
        Assert.Equal(12, t.GetLine(0).Length);
    }

    [Fact]
    public void GetTextJoinsLinesAndTrims()
    {
        var t = new TerminalEmulator(10, 3);
        t.Feed("hello   \r\nworld");

        Assert.Equal("llo\nwor", t.GetText(0, 2, 1, 2));
        Assert.Equal("llo\nwor", t.GetText(1, 2, 0, 2));
    }

    [Fact]
    public void ControlCharactersInsideCsiAreExecuted()
    {
        var t = new TerminalEmulator(10, 3);

        t.Feed("ab\x1b[\r1Cc");

        Assert.Equal("ac", Row(t, 0));
    }

    [Fact]
    public void IgnoresDcsAndUnknownSequences()
    {
        var t = new TerminalEmulator(10, 2);

        t.Feed("\x1bP1$r0m\x1b\\a\x1b[>4;1mb\x1b[2 qc\x1b=d");

        Assert.Equal("abcd", Row(t, 0));
    }

    [Fact]
    public void FullResetClearsEverything()
    {
        var t = new TerminalEmulator(5, 2);
        t.Feed("1\r\n2\r\n3\x1b[31m\x1b[?1h");

        t.Feed("\u001bcX");

        Assert.Equal(["X", ""], Screen(t));
        Assert.Equal(0, t.ScrollbackCount);
        Assert.False(t.ApplicationCursorKeys);
        Assert.Equal(TerminalColor.Default, t.GetLine(0)[0].Foreground);
    }
}

public class TerminalSupportTests
{
    [Theory]
    [InlineData(TerminalKey.Up, false, false, false, false, "\x1b[A")]
    [InlineData(TerminalKey.Up, false, false, false, true, "\x1bOA")]
    [InlineData(TerminalKey.Right, false, false, true, false, "\x1b[1;5C")]
    [InlineData(TerminalKey.Delete, false, false, false, false, "\x1b[3~")]
    [InlineData(TerminalKey.PageUp, true, false, false, false, "\x1b[5;2~")]
    [InlineData(TerminalKey.F1, false, false, false, false, "\x1bOP")]
    [InlineData(TerminalKey.F5, false, false, false, false, "\x1b[15~")]
    [InlineData(TerminalKey.Backspace, false, false, false, false, "\x7f")]
    [InlineData(TerminalKey.Tab, true, false, false, false, "\x1b[Z")]
    [InlineData(TerminalKey.Enter, false, false, false, false, "\r")]
    public void EncodesKeys(TerminalKey key, bool shift, bool alt, bool ctrl, bool app, string expected) =>
        Assert.Equal(expected, TerminalKeys.Encode(key, shift, alt, ctrl, app));

    [Theory]
    [InlineData('c', "\x03")]
    [InlineData('D', "\x04")]
    [InlineData('[', "\x1b")]
    [InlineData(' ', "\0")]
    [InlineData('é', null)]
    public void EncodesControlCharacters(char c, string? expected) => Assert.Equal(expected, TerminalKeys.Control(c));

    [Fact]
    public void PasteNormalizesNewlinesAndBrackets()
    {
        Assert.Equal("a\rb\rc", TerminalKeys.Paste("a\r\nb\nc", bracketed: false));
        Assert.Equal("\x1b[200~ls\r\x1b[201~", TerminalKeys.Paste("ls\n\x1b[201~", bracketed: true));
    }

    [Theory]
    [InlineData(-1, true, false, 0xCCCCCCu)]
    [InlineData(-1, false, false, 0x0C0C0Cu)]
    [InlineData(1, true, true, 0xE74856u)]
    [InlineData(1, false, true, 0xC50F1Fu)]
    [InlineData(196, true, false, 0xFF0000u)]
    [InlineData(232, true, false, 0x080808u)]
    public void MapsColors(int color, bool foreground, bool bold, uint expected) =>
        Assert.Equal(expected, TerminalColor.ToRgb(color, foreground, bold));

    [Fact]
    public void InjectionCommandIsSingleLineStartingWithSpace()
    {
        var command = WorkingDirectory.InjectionCommand(2);

        Assert.StartsWith(" ", command);
        Assert.EndsWith("\r", command);
        Assert.DoesNotContain('\n', command);
        Assert.Contains("\\033[2A", command);
        Assert.Contains("PROMPT_COMMAND=", command);
    }
}
