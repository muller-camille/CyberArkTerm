using ZillaTerm.Core.Terminal;

namespace ZillaTerm.Core.Tests.Terminal;

/// <summary>
/// Caractères hors BMP (emoji, icônes de l'invite), larges (chinois, japonais, coréen, emoji) et combinants (accents
/// décomposés) : largeur comme wcwidth (xterm, glibc), texte d'origine à la copie, à la recherche et à l'enregistrement.
/// </summary>
public class TerminalUnicodeTests
{
    private static string Row(TerminalEmulator t, int row) => string.Concat(t.GetLine(row).Select(t.CellText)).TrimEnd();

    [Theory]
    [InlineData('a', 1)]
    [InlineData('é', 1)]
    [InlineData(0x00AD, 1)]   // trait d'union conditionnel : 1, comme glibc
    [InlineData(0x0301, 0)]   // accent aigu combinant
    [InlineData(0x200D, 0)]   // liant sans chasse (emoji composés)
    [InlineData(0xFE0F, 0)]   // sélecteur de variante
    [InlineData(0x1160, 0)]   // voyelle médiane hangul
    [InlineData(0x1100, 2)]   // consonne initiale hangul
    [InlineData(0x3000, 2)]   // espace idéographique
    [InlineData(0x302A, 0)]   // marque combinante au milieu d'un bloc large
    [InlineData(0x4E2D, 2)]   // 中
    [InlineData(0x9FFF, 2)]
    [InlineData(0xAC00, 2)]   // 가
    [InlineData(0xFF21, 2)]   // Ａ pleine chasse
    [InlineData(0xFF61, 1)]   // ｡ demi-chasse
    [InlineData(0x2500, 1)]   // ─ cadre
    [InlineData(0x2764, 1)]   // ❤ sans présentation emoji
    [InlineData(0x231A, 2)]   // ⌚ présentation emoji
    [InlineData(0xE0A0, 1)]   // icône Powerline (zone privée)
    [InlineData(0x1F1EB, 1)]  // indicateur régional (drapeaux) : 1, comme glibc
    [InlineData(0x1F600, 2)]  // 😀
    [InlineData(0x1F3FB, 2)]  // modificateur de teinte de peau
    [InlineData(0x1FAE8, 2)]
    [InlineData(0xF0001, 1)]  // icône Nerd Font v3 (zone privée hors BMP)
    [InlineData(0x2A6DF, 2)]
    [InlineData(0x2EE5F, 2)]  // bloc CJK non encore attribué : large par défaut
    [InlineData(0xE0041, 0)]  // étiquette (drapeaux de région)
    public void WidthsFollowWcwidth(int codePoint, int width) => Assert.Equal(width, CharWidth.Of(codePoint));

    [Fact]
    public void SupplementaryCharactersStayWholeEvenWhenSplitAcrossFeeds()
    {
        var t = new TerminalEmulator(10, 3);

        t.Feed("a\U000F0001b\uD83D");
        t.Feed("\uDE00c");

        var line = t.GetLine(0);
        Assert.Equal(new int[] { 'a', 0xF0001, 'b', 0x1F600, Cell.WideTail, 'c' }, line.Take(6).Select(c => c.CodePoint));
        Assert.Equal("a\U000F0001b\U0001F600c", Row(t, 0));
        Assert.Equal(6, t.CursorColumn);
    }

    [Fact]
    public void LoneSurrogatesBecomeReplacementCharacters()
    {
        var t = new TerminalEmulator(10, 3);

        t.Feed("a\uD800b\uDC00c\uD83D\r\nd");

        Assert.Equal("a\uFFFDb\uFFFDc\uFFFD", Row(t, 0));
        Assert.Equal("d", Row(t, 1));
    }

    [Fact]
    public void WideCharactersTakeTwoColumnsAndMoveTheCursorByTwo()
    {
        var t = new TerminalEmulator(10, 3);

        t.Feed("日本語x");

        Assert.Equal("日本語x", Row(t, 0));
        Assert.Equal(7, t.CursorColumn);
        Assert.True(t.GetLine(0)[1].IsWideTail);
        Assert.Equal('x', t.GetLine(0)[6].CodePoint);

        // Retours arrière comme readline (deux pour un caractère large), puis effacement de la fin de ligne.
        t.Feed("\b\b\b\x1b[K!");
        Assert.Equal("日本!", Row(t, 0));
        Assert.Equal(5, t.CursorColumn);
    }

    [Fact]
    public void WideCharacterThatDoesNotFitWrapsToTheNextLine()
    {
        var t = new TerminalEmulator(5, 3);

        t.Feed("abcd日e");

        Assert.Equal(["abcd", "日e", ""], Enumerable.Range(0, 3).Select(r => Row(t, r)));
        Assert.Equal((1, 3), (t.CursorRow, t.CursorColumn));

        // À la dernière colonne exactement : le retour à la ligne attend le caractère suivant, comme pour les autres.
        t.Feed("\x1b[2J\x1b[Habc日");
        Assert.Equal((0, 4), (t.CursorRow, t.CursorColumn));
        t.Feed("f");
        Assert.Equal(["abc日", "f", ""], Enumerable.Range(0, 3).Select(r => Row(t, r)));

        // Sans retour automatique à la ligne : écrit sur les deux dernières colonnes.
        t.Feed("\x1b[2J\x1b[H\x1b[?7labcd日");
        Assert.Equal("abc日", Row(t, 0));
        Assert.Equal((0, 4), (t.CursorRow, t.CursorColumn));
    }

    [Fact]
    public void CombiningMarksJoinThePreviousCharacterWithoutMovingTheCursor()
    {
        var t = new TerminalEmulator(10, 3);

        t.Feed("e\u0301te\u0301 日\u0301x");

        Assert.Equal("e\u0301te\u0301 日\u0301x", Row(t, 0));
        Assert.Equal(7, t.CursorColumn);
        var line = t.GetLine(0);
        Assert.Equal(line[0].CodePoint, line[2].CodePoint);   // même texte : même numéro
        Assert.True(line[5].IsWideTail);
        Assert.Equal("e\u0301", t.CellText(line[0]));

        // Emoji composés (liant sans chasse, sélecteur de variante) : comme wcwidth, 2 + 0 + 2, et 1 + 0.
        t.Feed("\r\n👨\u200D👩❤\uFE0F");
        Assert.Equal("👨\u200D👩❤\uFE0F", Row(t, 1));
        Assert.Equal(5, t.CursorColumn);

        // En début de ligne, sans caractère où la joindre : ignorée.
        t.Feed("\r\n\u0301a");
        Assert.Equal("a", Row(t, 2));
        Assert.Equal(1, t.CursorColumn);
    }

    [Fact]
    public void CombiningMarkAtTheLastColumnJoinsItsCharacterBeforeTheWrap()
    {
        var t = new TerminalEmulator(3, 3);

        t.Feed("abe\u0301c");

        Assert.Equal(["abe\u0301", "c", ""], Enumerable.Range(0, 3).Select(r => Row(t, r)));
    }

    [Fact]
    public void EndlessCombiningMarksAreBounded()
    {
        var t = new TerminalEmulator(10, 3);

        t.Feed("a" + string.Concat(Enumerable.Repeat("\u0301", 1000)) + "b");

        Assert.InRange(t.CellText(t.GetLine(0)[0]).Length, 2, 32);
        Assert.Equal("b", t.CellText(t.GetLine(0)[1]));
        Assert.Equal(2, t.CursorColumn);

        // Un flot de combinaisons toutes différentes ne remplit pas la mémoire : au-delà de la limite, les nouvelles
        // marques sont ignorées, celles déjà connues restent.
        t.Feed(string.Concat(Enumerable.Range(0, 20_000).Select(i => $"{(char)(0x4E00 + i)}\u0301\r")));
        Assert.Equal("\u9C1F", t.CellText(t.GetLine(0)[0]));
        Assert.Equal(0, t.CursorColumn);
        t.Feed("\r\na\u0301");
        Assert.Equal("a\u0301", Row(t, 1));
    }

    [Theory]
    [InlineData("\x1b[1Gy", "y 本x")]         // sur la moitié gauche : la droite s'efface
    [InlineData("\x1b[2Gy", " y本x")]         // sur la moitié droite : la gauche s'efface
    [InlineData("\x1b[2G\x1b[K", "")]         // effacement de ligne depuis la moitié droite
    [InlineData("\x1b[1G\x1b[X", "  本x")]    // effacement d'une case
    [InlineData("\x1b[1G\x1b[P", " 本x")]     // suppression d'une case
    [InlineData("\x1b[2G\x1b[@", "   本x")]   // insertion au milieu d'un caractère large
    [InlineData("\x1b[3G\x1b[1K", "    x")]   // effacement jusqu'au curseur, sur la moitié gauche de 本
    public void OverwritingHalfOfAWideCharacterErasesTheOtherHalf(string sequence, string expected)
    {
        var t = new TerminalEmulator(10, 3);
        t.Feed("日本x");

        t.Feed(sequence);

        Assert.Equal(expected, Row(t, 0));
        var line = t.GetLine(0);
        for (int col = 0; col < line.Length; col++)
        {
            // Jamais de moitié droite sans sa moitié gauche.
            Assert.False(line[col].IsWideTail && (col == 0 || line[col - 1].CodePoint == ' '), $"colonne {col}");
        }
    }

    [Fact]
    public void WideCharacterPushedOutOfTheLineOrCutByAResizeIsErased()
    {
        var t = new TerminalEmulator(4, 3);
        t.Feed("ab日\x1b[1G\x1b[@");
        Assert.Equal(" ab", Row(t, 0));
        Assert.False(t.GetLine(0)[3].IsWideTail);

        t.Feed("\x1b[2J\x1b[Hab日");
        t.Resize(3, 3);
        Assert.Equal("ab", Row(t, 0));
        Assert.Equal(' ', t.GetLine(0)[2].CodePoint);
    }

    [Fact]
    public void CopyGivesTheOriginalTextAndTakesWideCharactersWhole()
    {
        var t = new TerminalEmulator(20, 3);
        t.Feed("日本語 e\u0301té 😀!\r\n✓ ok");

        Assert.Equal("日本語 e\u0301té 😀!\n✓ ok", t.GetText(0, 0, 1, 19));
        Assert.Equal("日本", t.GetText(0, 1, 0, 2));   // de la moitié droite de 日 à la gauche de 本
        Assert.Equal("本", t.GetText(0, 3, 0, 3));
        Assert.Equal("😀", t.GetText(0, 12, 0, 12));
        Assert.Equal("日本語 e\u0301té 😀!" + Environment.NewLine + "✓ ok", TerminalSearch.AllText(t));
    }

    [Fact]
    public void SearchReportsColumnsAndWidthsOnTheScreen()
    {
        var t = new TerminalEmulator(30, 3);
        t.Feed("日本語 error é\u0301 😀x Ｅrror");

        var matches = TerminalSearch.Find(t, "error");
        Assert.Equal([new TerminalMatch(0, 7, 5)], matches);
        Assert.Equal([new TerminalMatch(0, 2, 4)], TerminalSearch.Find(t, "本語"));
        Assert.Equal([new TerminalMatch(0, 15, 3)], TerminalSearch.Find(t, "😀x"));
        Assert.Equal([new TerminalMatch(0, 13, 1)], TerminalSearch.Find(t, "é"));   // la case du caractère et de son accent
        Assert.Equal([new TerminalMatch(0, 19, 6)], TerminalSearch.Find(t, "Ｅrror"));
    }

    [Fact]
    public void FullResetForgetsTheCombinedCharacters()
    {
        var t = new TerminalEmulator(10, 3);
        t.Feed("a\u0301");
        int first = t.GetLine(0)[0].CodePoint;

        t.Feed("\u001bcb\u0302");

        Assert.Equal(first, t.GetLine(0)[0].CodePoint);
        Assert.Equal("b\u0302", Row(t, 0));
    }

    [Fact]
    public void PlainTextIsWrittenWithoutAllocating()
    {
        // Le cas courant (ASCII, accents précomposés, cadres, chinois) n'alloue rien par caractère.
        var t = new TerminalEmulator(120, 30);
        var text = string.Concat(Enumerable.Repeat("plain ASCII text, \x1b[1;31mred\x1b[0m, accentué, ─│┌, 日本語\r", 2000));
        t.Feed(text);

        long before = GC.GetAllocatedBytesForCurrentThread();
        t.Feed(text);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 16 * 1024, $"{allocated} octets pour {text.Length} caractères");
    }
}
