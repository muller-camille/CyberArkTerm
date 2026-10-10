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

    /// <summary>
    /// Explorateur : chemin complet entre guillemets (virgules et espaces gardés), sans séparateur final ; chemins relatifs,
    /// guillemets et caractères de contrôle refusés (ils changeraient la ligne de commande).
    /// </summary>
    [Fact]
    public void ExplorerArgumentsAreBuiltFromCheckedPaths()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(@"/select,""C:\Logs\a, b\zillaterm.log""", Services.WindowsExplorer.SelectArguments(@"C:\Logs\a, b\.\zillaterm.log"));
        Assert.Equal(@"""\\srv01.corp.local\partage\listes""", Services.WindowsExplorer.FolderArguments(@"\\srv01.corp.local\partage\listes\"));
        Assert.Equal(@"""C:\Users\jdoe\AppData""", Services.WindowsExplorer.FolderArguments(@"C:\Users\jdoe\AppData\"));
        foreach (var bad in new[] { "zillaterm.log", @"C:\Logs\a"" /root,C:\x", "C:\\Logs\\a\nb", @"\Logs\x", "" })
        {
            Assert.Throws<ArgumentException>(() => Services.WindowsExplorer.SelectArguments(bad));
            Assert.Throws<ArgumentException>(() => Services.WindowsExplorer.FolderArguments(bad));
        }
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
