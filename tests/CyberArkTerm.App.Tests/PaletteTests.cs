using System.IO;
using System.Windows;
using System.Xml.Linq;

namespace CyberArkTerm.App.Tests;

/// <summary>Contraste élevé : chaque couleur de la palette a son équivalent système.</summary>
public sealed class PaletteTests
{
    [Fact]
    public void EveryThemeColorHasAHighContrastValue()
    {
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var theme = XDocument.Load(Path.Combine(AccessKeysTests.RepositoryRoot(), "src", "CyberArkTerm.App", "Theme.xaml")).Root!;
        var colors = theme.Elements()
            .Where(e => e.Name == wpf + "SolidColorBrush" || e.Name == wpf + "LinearGradientBrush")
            .Select(e => (string?)e.Attribute(xaml + "Key"))
            .Where(k => k is not null && !k.StartsWith('{'))
            .ToHashSet();

        Assert.Empty(colors.Except(Palette.Keys));
        Assert.Empty(Palette.Keys.Except(colors!));
    }

    [Fact]
    public void HighContrastUsesSystemColors()
    {
        var dictionary = Palette.HighContrast();
        Assert.Same(SystemColors.WindowBrush, dictionary["ContentBrush"]);
        Assert.Same(SystemColors.WindowTextBrush, dictionary["MutedBrush"]);
        Assert.Same(SystemColors.HighlightBrush, dictionary["AccentBrush"]);
        Assert.Same(SystemColors.HighlightTextBrush, dictionary[SystemColors.InactiveSelectionHighlightTextBrushKey]);
    }
}
