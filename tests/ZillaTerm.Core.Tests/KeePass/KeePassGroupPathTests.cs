using ZillaTerm.Core.KeePass;

namespace ZillaTerm.Core.Tests.KeePass;

public class KeePassGroupPathTests
{
    [Fact]
    public void SlashesAndBackslashesInNamesAreEscaped()
    {
        var path = KeePassGroupPath.Combine(KeePassGroupPath.Combine("Urgence", "Linux/Unix"), @"C:\Prod");

        Assert.Equal(@"Urgence/Linux\/Unix/C:\\Prod", path);
        Assert.Equal(["Urgence", "Linux/Unix", @"C:\Prod"], KeePassGroupPath.Split(path));
        Assert.Equal(@"C:\Prod", KeePassGroupPath.Name(path));
        Assert.Equal(@"Urgence/Linux\/Unix", KeePassGroupPath.Parent(path));
        Assert.Equal("Urgence", KeePassGroupPath.Parent(KeePassGroupPath.Parent(path)));
        Assert.Equal("", KeePassGroupPath.Parent("Urgence"));
    }

    [Fact]
    public void IsWithinDoesNotConfuseSimilarNames()
    {
        Assert.True(KeePassGroupPath.IsWithin("Prod/Linux", "Prod"));
        Assert.True(KeePassGroupPath.IsWithin("Prod", "Prod"));
        Assert.False(KeePassGroupPath.IsWithin("Production", "Prod"));
        Assert.False(KeePassGroupPath.IsWithin(@"Prod\/DR", "Prod"));
    }

    [Theory]
    [InlineData(" Serveurs / Prod ", "Serveurs/Prod")]
    [InlineData("/Serveurs//Prod/", "Serveurs/Prod")]
    [InlineData(@"Linux\/Unix", @"Linux\/Unix")]
    [InlineData("", "")]
    public void NormalizeCleansUserInput(string text, string expected)
    {
        Assert.Equal(expected, KeePassGroupPath.Normalize(text));
    }
}
