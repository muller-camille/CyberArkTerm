using System.Diagnostics;
using ZillaTerm.Core.Ssh;

namespace ZillaTerm.Core.Tests.Ssh;

public class FileNameFilterTests
{
    [Theory]
    [InlineData("nginx", "nginx.conf", true)]
    [InlineData("NGINX", "nginx.conf", true)]
    [InlineData("conf", "app.conf.bak", true)]
    [InlineData("missing", "nginx.conf", false)]
    [InlineData("*.log", "messages.log", true)]
    [InlineData("*.LOG", "messages.log", true)]
    [InlineData("*.log", "messages.log.1", false)]
    [InlineData("app?.conf", "app1.conf", true)]
    [InlineData("app?.conf", "app12.conf", false)]
    [InlineData("*.log;*.gz", "old.tar.gz", true)]
    [InlineData("*.log; nginx ", "nginx", true)]
    [InlineData("a+b(c)", "x-a+b(c)-y", true)]
    [InlineData("[x]*", "[x]file", true)]
    [InlineData("[x]*", "xfile", false)]
    public void MatchesPartsAndMasksIgnoringCase(string filter, string name, bool expected) =>
        Assert.Equal(expected, FileNameFilter.Parse(filter)!.Matches(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" ; ;")]
    public void EmptyFilterShowsEverything(string? filter) => Assert.Null(FileNameFilter.Parse(filter));

    [Fact]
    public void PathologicalMaskStaysFast()
    {
        var filter = FileNameFilter.Parse("*a*a*a*a*a*a*a*a*a*a*b")!;
        var name = new string('a', 5000);
        var watch = Stopwatch.StartNew();

        Assert.False(filter.Matches(name));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
    }
}
