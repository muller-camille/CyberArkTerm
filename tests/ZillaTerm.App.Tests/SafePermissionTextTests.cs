using System.Globalization;
using ZillaTerm.App.Localization;
using ZillaTerm.Core;

namespace ZillaTerm.App.Tests;

public sealed class SafePermissionTextTests
{
    /// <summary>Chaque droit renvoyé par le PVWA a un libellé dans les trois langues de l'interface.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("it")]
    public void EveryPermissionHasALabel(string language)
    {
        var previous = Strings.Culture;
        try
        {
            Strings.Culture = new CultureInfo(language);
            var labels = new SafePermissions().All().Select(p => SafePermissionText.Label(p.Name)).ToList();

            Assert.Equal(22, labels.Count);
            Assert.All(new SafePermissions().All(), p => Assert.NotEqual(p.Name, SafePermissionText.Label(p.Name)));
            Assert.Equal(labels.Count, labels.Distinct().Count());
        }
        finally
        {
            Strings.Culture = previous;
        }
    }
}
