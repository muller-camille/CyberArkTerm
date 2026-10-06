using System.Text;
using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.Core.Tests.Ssh;

public sealed class TextDiffTests
{
    private static string Describe(DiffResult result) => string.Join(" ", result.Rows.Select(r => r.Kind switch
    {
        DiffKind.Same => "=" + r.Left,
        DiffKind.Removed => "-" + r.Left,
        DiffKind.Added => "+" + r.Right,
        DiffKind.Changed => $"{r.Left}>{r.Right}",
        _ => "…",
    }));

    [Fact]
    public void IdenticalTexts()
    {
        var result = TextDiff.Compare(["a", "b"], ["a", "b"]);
        Assert.True(result.Identical);
        Assert.Equal((0, 0, 0), (result.Blocks, result.Added, result.Removed));
        Assert.Equal("=a =b", Describe(result));
    }

    [Fact]
    public void ChangedRemovedAndAddedLines()
    {
        var result = TextDiff.Compare(["a", "b", "c", "d"], ["a", "B", "c", "d", "e"]);
        Assert.Equal("=a b>B =c =d +e", Describe(result));
        Assert.Equal((2, 2, 1), (result.Blocks, result.Added, result.Removed));

        result = TextDiff.Compare(["a", "b", "c"], ["a", "c"]);
        Assert.Equal("=a -b =c", Describe(result));
        Assert.Equal((2, null), (result.Rows[1].LeftNumber, result.Rows[1].RightNumber));
        Assert.Equal((3, 2), (result.Rows[2].LeftNumber, result.Rows[2].RightNumber));

        Assert.Equal("+x +y", Describe(TextDiff.Compare([], ["x", "y"])));
        Assert.Equal("-x", Describe(TextDiff.Compare(["x"], [])));
    }

    [Fact]
    public void FindsTheShortestChangesInTheMiddle()
    {
        var result = TextDiff.Compare(
            ["listen 80", "server_name a", "root /var/www", "index index.html", "}"],
            ["listen 443", "server_name a", "ssl on", "root /var/www", "}"]);
        Assert.Equal("listen 80>listen 443 =server_name a +ssl on =root /var/www -index index.html =}", Describe(result));
    }

    [Fact]
    public void CanIgnoreWhitespace()
    {
        Assert.False(TextDiff.Compare(["a  b", "c"], ["a b ", "c"]).Identical);
        var result = TextDiff.Compare(["a  b", "c"], ["a b ", "c"], ignoreWhitespace: true);
        Assert.True(result.Identical);
        Assert.Equal("a  b", result.Rows[0].Left);
        Assert.Equal("a b ", result.Rows[0].Right);
    }

    [Fact]
    public void WritesAUnifiedDiff()
    {
        var result = TextDiff.Compare(["a", "b", "c"], ["a", "B", "c"]);
        Assert.Equal("--- srv01:/etc/app.conf\n+++ srv02:/etc/app.conf\n@@ -1,3 +1,3 @@\n a\n-b\n+B\n c\n",
            TextDiff.Unified(result, "srv01:/etc/app.conf", "srv02:/etc/app.conf"));

        // Deux zones éloignées : deux blocs « @@ ».
        var left = Enumerable.Range(1, 20).Select(i => $"l{i}").ToList();
        var right = left.ToList();
        right[1] = "X";
        right[17] = "Y";
        var unified = TextDiff.Unified(TextDiff.Compare(left, right), "a", "b", context: 2);
        Assert.Equal(2, unified.Split("@@ -").Length - 1);
        Assert.Contains("@@ -1,4 +1,4 @@\n l1\n-l2\n+X\n l3\n l4\n", unified);
        Assert.Contains("@@ -16,5 +16,5 @@\n l16\n l17\n-l18\n+Y\n l19\n l20\n", unified);

        Assert.Equal("--- a\n+++ b\n", TextDiff.Unified(TextDiff.Compare(["x"], ["x"]), "a", "b"));
    }

    [Fact]
    public void ShowsOnlyTheDifferencesWithContext()
    {
        var left = Enumerable.Range(1, 30).Select(i => $"l{i}").ToList();
        var right = left.ToList();
        right[14] = "changed";
        var shown = TextDiff.Compare(left, right).OnlyDifferences(context: 2);
        Assert.Equal([DiffKind.Gap, DiffKind.Same, DiffKind.Same, DiffKind.Changed, DiffKind.Same, DiffKind.Same, DiffKind.Gap],
            shown.Select(r => r.Kind));
        Assert.Equal(12, shown[0].Hidden);
        Assert.Equal(13, shown[^1].Hidden);
        Assert.Empty(TextDiff.Compare(["a"], ["a"]).OnlyDifferences());
    }

    [Fact]
    public void RowsRebuildBothFiles()
    {
        var random = new Random(42);
        for (int round = 0; round < 200; round++)
        {
            var left = Enumerable.Range(0, random.Next(0, 40)).Select(_ => "l" + random.Next(0, 6)).ToList();
            var right = Enumerable.Range(0, random.Next(0, 40)).Select(_ => "l" + random.Next(0, 6)).ToList();
            var result = TextDiff.Compare(left, right);
            Assert.Equal(left, result.Rows.Where(r => r.Left is not null).Select(r => r.Left!));
            Assert.Equal(right, result.Rows.Where(r => r.Right is not null).Select(r => r.Right!));
            Assert.Equal(Enumerable.Range(1, left.Count).Cast<int?>(), result.Rows.Where(r => r.LeftNumber is not null).Select(r => r.LeftNumber));
            Assert.Equal(left.SequenceEqual(right), result.Identical);
        }
    }

    [Fact]
    public void VeryDifferentFilesAreShownAsReplaced()
    {
        var left = Enumerable.Range(0, TextDiff.MaxEdits + 10).Select(i => $"a{i}").ToList();
        var right = Enumerable.Range(0, TextDiff.MaxEdits + 10).Select(i => $"b{i}").ToList();
        var result = TextDiff.Compare(left, right);
        Assert.True(result.Approximate);
        Assert.Equal(left, result.Rows.Select(r => r.Left!));
        Assert.Equal(right, result.Rows.Select(r => r.Right!));
    }

    [Fact]
    public void ReadsTextAndBinaryFiles()
    {
        var text = DiffSide.FromBytes("a.conf", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("é=1\r\nb=2\r\n")).ToArray());
        Assert.True(text.IsText);
        Assert.True(text.UsesCrlf);
        Assert.Equal(["é=1", "b=2"], text.Lines);
        Assert.Equal(64, text.Sha256.Length);

        var unix = DiffSide.FromBytes("a.conf", Encoding.UTF8.GetBytes("é=1\nb=2"));
        Assert.False(unix.UsesCrlf);
        Assert.Equal(text.Lines, unix.Lines);

        var binary = DiffSide.FromBytes("app.bin", [1, 2, 0, 3]);
        Assert.False(binary.IsText);
        Assert.Null(binary.Lines);
        Assert.Equal(4, binary.Length);
        Assert.Equal(64, binary.Sha256.Length);
        Assert.NotEqual(text.Sha256, unix.Sha256);

        Assert.Empty(DiffSide.SplitLines(""));
        Assert.Equal(["", "a"], DiffSide.SplitLines("\na\n"));
    }
}
