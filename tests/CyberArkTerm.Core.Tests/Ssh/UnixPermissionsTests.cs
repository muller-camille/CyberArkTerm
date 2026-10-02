using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.Core.Tests.Ssh;

public class UnixPermissionsTests
{
    [Theory]
    [InlineData("-rw-r--r--", 0b110_100_100)]
    [InlineData("drwxr-xr-x", 0b111_101_101)]
    [InlineData("rwx------", 0b111_000_000)]
    [InlineData("-rwsr-sr-t", 0b111_111_101_101)]
    [InlineData("drwxrwxrwt", 0b001_111_111_111)]
    [InlineData("-rwSr--r--", 0b100_110_100_100)]
    [InlineData("drwxr-sr-x", 0b010_111_101_101)]
    [InlineData("-rw-r--r-T", 0b001_110_100_100)]
    [InlineData("----------", 0)]
    public void FromSymbolic(string text, int expected) => Assert.Equal(expected, UnixPermissions.FromSymbolic(text));

    [Theory]
    [InlineData("644", 0b110_100_100)]
    [InlineData("0755", 0b111_101_101)]
    [InlineData(" 600 ", 0b110_000_000)]
    [InlineData("1777", 0b001_111_111_111)]
    [InlineData("4755", 0b100_111_101_101)]
    [InlineData("2750", 0b010_111_101_000)]
    [InlineData("000", 0)]
    public void TryParseOctal_Accepts(string text, int expected)
    {
        Assert.True(UnixPermissions.TryParseOctal(text, out var mode));
        Assert.Equal(expected, mode);
        Assert.Equal(expected, UnixPermissions.FromSymbolic(UnixPermissions.ToSymbolic(mode)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("64")]
    [InlineData("648")]
    [InlineData("17777")]
    [InlineData("8755")]
    [InlineData("rwx")]
    [InlineData("-644")]
    public void TryParseOctal_Rejects(string text) => Assert.False(UnixPermissions.TryParseOctal(text, out _));

    [Fact]
    public void Formats()
    {
        Assert.Equal("755", UnixPermissions.ToOctal(0b111_101_101));
        Assert.Equal("040", UnixPermissions.ToOctal(0b000_100_000));
        Assert.Equal("rw-r-----", UnixPermissions.ToSymbolic(0b110_100_000));
        Assert.Equal("1777", UnixPermissions.ToOctal(0b001_111_111_111));
        Assert.Equal("rwxrwxrwt", UnixPermissions.ToSymbolic(0b001_111_111_111));
        Assert.Equal("rwsr-xr-x", UnixPermissions.ToSymbolic(0b100_111_101_101));
        Assert.Equal("rw-r-Sr-T", UnixPermissions.ToSymbolic(0b011_110_100_100));
        Assert.Equal("-rwxrwxrwt", "-" + UnixPermissions.ToSymbolic(UnixPermissions.FromSymbolic("drwxrwxrwt")));
    }
}
