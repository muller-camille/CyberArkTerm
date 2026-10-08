using ZillaTerm.Core.Terminal;

namespace ZillaTerm.Core.Tests.Terminal;

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

    /// <summary>
    /// Historique plein : les plus anciennes lignes partent une à une, sans ralentir (une sortie abondante ou piégée
    /// figeait l'interface) ; une séquence aux paramètres sans fin ne remplit pas la mémoire.
    /// </summary>
    [Fact]
    public void FullScrollbackKeepsTheLatestLinesQuickly()
    {
        var t = new TerminalEmulator(10, 3, maxScrollback: 100);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        t.Feed(string.Concat(Enumerable.Range(1, 200_000).Select(i => $"{i}\r\n")));
        t.Feed(string.Concat(Enumerable.Repeat("\x1b[999S", 20_000)));
        t.Feed("\x1b[" + new string(';', 1_000_000) + "m");
        watch.Stop();

        Assert.Equal(100, t.ScrollbackCount);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), watch.Elapsed.ToString());
        t.ClearScrollback();
        t.Feed("\x1b[2J\x1b[Ha\r\nb\r\nc\r\nd");
        Assert.Equal(1, t.ScrollbackCount);
        Assert.Equal("a", Row(t, -1));
    }

    [Fact]
    public void ClearScrollbackKeepsTheScreen()
    {
        var t = new TerminalEmulator(5, 2);
        t.Feed("1\r\n2\r\n3\r\n4");
        long version = t.Version;

        t.ClearScrollback();

        Assert.Equal(0, t.ScrollbackCount);
        Assert.Equal(["3", "4"], Screen(t));
        Assert.True(t.Version > version);
        t.Feed("\r\n5");
        Assert.Equal(1, t.ScrollbackCount);
        Assert.Equal("3", Row(t, -1));
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
    public void ReportsTheWorkingDirectoryAndIgnoresTheTitle()
    {
        var t = new TerminalEmulator();
        var dirs = new List<string>();
        t.WorkingDirectoryChanged += dirs.Add;

        t.Feed("\x1b]0;root@srv: ~\a");
        t.Feed("\x1b]7;file://srv01/var/log/my%20app\x1b\\");
        t.Feed("\x1b]7;/opt/app\a");
        t.Feed("\x1b]7;relative\a");

        Assert.Equal(["/var/log/my app", "/opt/app"], dirs);
        Assert.Equal("", Row(t, 0));
    }

    [Theory]
    [InlineData("file://h/tmp/a%0Ab")]
    [InlineData("file://h/tmp/a%1B[31m")]
    [InlineData("file://h/tmp/%E2%80%AEtxt.exe")]
    [InlineData("file://h/tmp/a%E2%80%8Bb")]
    [InlineData("/tmp/a\u0085b")]
    public void WorkingDirectoryWithHiddenCharactersIsIgnored(string value)
    {
        var t = new TerminalEmulator();
        var dirs = new List<string>();
        t.WorkingDirectoryChanged += dirs.Add;

        t.Feed($"\x1b]7;{value}\a");

        Assert.Empty(dirs);
    }

    [Fact]
    public void LinePositionCountsFromTheScrollRegionInOriginMode()
    {
        var t = new TerminalEmulator(10, 10);

        t.Feed("\x1b[3;8r\x1b[?6h\x1b[2dX");

        Assert.Equal("X", Row(t, 3));
        t.Feed("\x1b[?6l\x1b[2dY");
        Assert.Equal("Y", Row(t, 1).Trim());
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

    [Theory]
    [InlineData(25)]
    [InlineData(28)]
    public void EraseMarkerErasesTheTypedCommandFromTheMarkedLine(int length)
    {
        // Le serveur peut couper l'écho sur plus de lignes que ZillaTerm ne le prévoirait : tout part quand même.
        var t = new TerminalEmulator(10, 6);
        t.Feed("out1\r\nout2\r\n$ ");
        t.MarkEraseFromCursorLine();

        t.Feed(new string('x', length) + "\r\n" + WorkingDirectory.EraseMarker + "$ ");

        Assert.Equal(["out1", "out2", "$", "", "", ""], Screen(t));
        Assert.Equal((2, 2), (t.CursorRow, t.CursorColumn));
    }

    [Fact]
    public void EraseMarkerFollowsTheMarkedLineWhenTheEchoScrolls()
    {
        var t = new TerminalEmulator(10, 4);
        t.Feed("out1\r\nout2\r\nout3\r\n$ ");
        t.MarkEraseFromCursorLine();

        t.Feed(new string('y', 15) + "\r\n");
        Assert.Equal(2, t.ScrollbackCount);
        t.Feed(WorkingDirectory.EraseMarker + "$ ");

        Assert.Equal(["out3", "$", "", ""], Screen(t));
        Assert.Equal("out2", Row(t, -1));
    }

    [Fact]
    public void EraseMarkerActsOnlyOnceAfterAMarkOnTheMainScreen()
    {
        var t = new TerminalEmulator(10, 3);

        t.Feed("abc" + WorkingDirectory.EraseMarker);
        Assert.Equal(["abc", "", ""], Screen(t));

        t.MarkEraseFromCursorLine();
        t.Feed("\r\n" + WorkingDirectory.EraseMarker + "def\r\n" + WorkingDirectory.EraseMarker);
        Assert.Equal(["def", "", ""], Screen(t));

        t.MarkEraseFromCursorLine();
        t.Feed("\x1b[?1049hvim" + WorkingDirectory.EraseMarker);
        Assert.Equal(["", "vim", ""], Screen(t));
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
        Assert.Equal("\x1b[200~ls\r[201~\x1b[201~", TerminalKeys.Paste("ls\n\x1b[201~", bracketed: true));
    }

    [Fact]
    public void PastedTextCannotLeaveTheBracketsNorRunACommand()
    {
        // Marqueur de fin imbriqué (« \e[20\e[201~1~ ») puis Ctrl-O : avant, le collage se fermait et « id » s'exécutait.
        Assert.Equal("\x1b[200~[20[201~1~ id\x1b[201~", TerminalKeys.Paste("\x1b[20\x1b[201~1~ id\x0f", bracketed: true));
        Assert.Equal("a\tb\rc", TerminalKeys.Paste("a\tb\u009b\u007f\nc\0", bracketed: false));
        Assert.Equal("echo ok", TerminalKeys.CleanPaste("echo ok"));
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
        var command = WorkingDirectory.InjectionCommand();

        Assert.StartsWith(" ", command);
        Assert.EndsWith("\r", command);
        Assert.DoesNotContain('\n', command);
        Assert.EndsWith(WorkingDirectory.EraseMarker.Replace("\x1b", "\\033").Replace("\a", "\\007") + "'\r", command);
        Assert.Contains("PROMPT_COMMAND=", command);
    }

    [Fact]
    public void InjectionCommandIsIdempotent()
    {
        var command = WorkingDirectory.InjectionCommand();

        Assert.Contains("case \";$PROMPT_COMMAND;\" in *\";__catosc7;\"*)", command);
        Assert.Contains("precmd_functions[(I)__catosc7]", command);
    }

    [Fact]
    public void InjectionCommandRunsEachPartOnlyInItsShellFamily()
    {
        // csh refuse toute la ligne s'il y lit du code bash : chaque partie passe entre apostrophes à eval.
        var command = WorkingDirectory.InjectionCommand();

        Assert.StartsWith(" test -n \"$shell\" || test -n \"$FISH_VERSION\" || eval '", command);
        Assert.Contains(";test -n \"$shell\" && eval '", command);
        Assert.EndsWith(";printf '\\033]6973;\\007'\r", command);

        var words = ShellWords(command, fish: false);
        var posix = words[words.IndexOf("eval") + 1];
        var csh = words[words.LastIndexOf("eval") + 1];
        Assert.StartsWith("__catosc7(){ printf '\\033]7;%s\\007' \"$PWD\";};", posix);
        Assert.Contains("PROMPT_COMMAND=\"__catosc7${PROMPT_COMMAND:+;$PROMPT_COMMAND}\"", posix);
        Assert.StartsWith("if ($?tcsh && ! $?__catosc7 && `alias cwdcmd | wc -c` == 0) set __catosc7;", csh);
        Assert.Contains("alias cwdcmd 'printf \"\\033]7;%s\\007\" \"$cwd\"'", csh);
    }

    [Theory]
    [InlineData("/var/log")]
    [InlineData("it's a!b\\c")]
    [InlineData("-dash")]
    public void InjectionCommandQuotesTheStartDirectoryForEveryShell(string directory)
    {
        var command = WorkingDirectory.InjectionCommand(directory);

        // Même découpage pour les shells POSIX et csh que pour fish (« \ » entre apostrophes) ; chaque « ! » est
        // précédé de « \ », sinon bash, zsh et csh y verraient l'historique.
        var words = ShellWords(command, fish: false);
        Assert.Equal(words, ShellWords(command, fish: true));
        Assert.DoesNotMatch(@"(?<!\\)!", command);

        var posix = words[words.IndexOf("eval") + 1];
        Assert.StartsWith($"cd -- {WorkingDirectory.ShellQuote(directory)} 2>/dev/null;", posix);
        var csh = words[words.LastIndexOf("eval") + 1];
        var cshPath = WorkingDirectory.ShellQuote(directory.StartsWith('-') ? "./" + directory : directory).Replace("!", "\\!");
        Assert.Contains($"if (-d {cshPath}) cd {cshPath};", csh);

        // fish : « cd » seul, après le test de « shell » pour que csh ne lise jamais FISH_VERSION.
        var fish = words.LastIndexOf("\"$FISH_VERSION\"");
        Assert.Equal(["test", "-n", "\"$shell\"", "||", "test", "-z", "\"$FISH_VERSION\"", "||", "cd"], words[(fish - 6)..(fish + 3)]);
        Assert.Equal(directory.StartsWith('-') ? "./" + directory : directory, words[fish + 3]);
    }

    /// <summary>Mots d'une ligne de commande : apostrophes, et « \ » hors apostrophes (fish : « \' » et « \\ » aussi dedans).</summary>
    private static List<string> ShellWords(string line, bool fish)
    {
        var words = new List<string>();
        var word = new System.Text.StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '\'')
                {
                    quoted = false;
                }
                else if (fish && c == '\\' && i + 1 < line.Length && line[i + 1] is '\'' or '\\')
                {
                    word.Append(line[++i]);
                }
                else
                {
                    word.Append(c);
                }
            }
            else if (c == '\'')
            {
                quoted = any = true;
            }
            else if (c == '\\' && i + 1 < line.Length)
            {
                word.Append(line[++i]);
                any = true;
            }
            else if (c is ' ' or ';' or '\r')
            {
                if (any)
                {
                    words.Add(word.ToString());
                }

                word.Clear();
                any = false;
            }
            else
            {
                word.Append(c);
                any = true;
            }
        }

        Assert.False(quoted);
        if (any)
        {
            words.Add(word.ToString());
        }

        return words;
    }

    [Theory]
    [InlineData("[root@srv01 log]# ", true)]
    [InlineData("jdupont@srv01:~$", true)]
    [InlineData("srv01% ", true)]
    [InlineData("> ", true)]
    [InlineData("[root@srv01 log]# ls", false)]
    [InlineData("Password:", false)]
    [InlineData("PSM for SSH (demo): this session is recorded.", false)]
    [InlineData("   ", false)]
    [InlineData("", false)]
    public void LooksLikePrompt(string text, bool expected) => Assert.Equal(expected, WorkingDirectory.LooksLikePrompt(text));
}
