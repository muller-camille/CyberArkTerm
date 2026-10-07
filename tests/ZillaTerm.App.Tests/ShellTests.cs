using ZillaTerm.App.Views;

namespace ZillaTerm.App.Tests;

/// <summary>Coquille : noms d'onglets et dates des connexions récentes.</summary>
public sealed class ShellTests
{
    [Fact]
    public void DuplicateTabNamesAreNumbered()
    {
        Assert.Equal("root@srv01", SessionTabHeader.UniqueLabel("root@srv01", []));
        Assert.Equal("root@srv01 (2)", SessionTabHeader.UniqueLabel("root@srv01", ["root@srv01"]));
        Assert.Equal("root@srv01 (3)", SessionTabHeader.UniqueLabel("root@srv01", ["root@srv01", "root@srv01 (2)"]));
        Assert.Equal("root@srv01 (2)", SessionTabHeader.UniqueLabel("root@srv01", ["root@srv01", "root@srv01 (3)"]));
    }

    [Fact]
    public void RecentDatesSayTodayAndYesterday()
    {
        var now = new DateTime(2026, 10, 7, 15, 0, 0);
        Assert.StartsWith(Localization.Strings.RecentToday.Split('{')[0], RecentDateConverter.Describe(now.AddHours(-2), now));
        Assert.StartsWith(Localization.Strings.RecentYesterday.Split('{')[0], RecentDateConverter.Describe(now.AddDays(-1), now));
        Assert.Contains("2026", RecentDateConverter.Describe(new DateTime(2026, 3, 2, 9, 5, 0), now));
    }
}
