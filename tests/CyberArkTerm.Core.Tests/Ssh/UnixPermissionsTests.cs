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

    [Fact]
    public void AbsoluteChangeSetsEveryRwxBitAndKeepsSpecialBitsUnlessAsked()
    {
        var change = PermissionChange.Absolute(0b111_101_000, includeSpecial: false);

        Assert.True(change.CoversAllRwx);
        Assert.Equal(0b111_101_000, change.Apply(0b110_110_110));
        // setuid déjà présent : gardé tant que les bits spéciaux ne font pas partie du changement.
        Assert.Equal(UnixPermissions.SetUid | 0b111_101_000, change.Apply(UnixPermissions.SetUid | 0b100_100_100));
        Assert.Equal(0b111_101_000, PermissionChange.Absolute(0b111_101_000, includeSpecial: true).Apply(UnixPermissions.SetUid));
    }

    /// <summary>Sélection de fichiers aux droits différents : « g+w » ne leur donne pas les droits du premier.</summary>
    [Fact]
    public void PartialChangeOnlyTouchesTheChangedBits()
    {
        var groupWrite = new PermissionChange(Set: 0b000_010_000, Clear: 0);

        Assert.False(groupWrite.CoversAllRwx);
        Assert.Equal(0b111_111_101, groupWrite.Apply(0b111_101_101));
        Assert.Equal(0b110_110_100, groupWrite.Apply(0b110_100_100));
        Assert.Equal("g+w", groupWrite.ToSymbolic());
        Assert.Equal("u+x,o-rw,+sticky", new PermissionChange(0b001_000_000 | UnixPermissions.Sticky, 0b000_000_110).ToSymbolic());
    }

    [Fact]
    public void ContentKeepsSpecialBitsAndOnlyExecutableFilesStayExecutable()
    {
        var change = PermissionChange.Absolute(UnixPermissions.SetGid | 0b111_101_101, includeSpecial: true);

        // Dossier : x gardé ; fichier non exécutable : pas de x ; fichier déjà exécutable : x.
        Assert.Equal(0b111_101_101, change.ApplyToContent(0b111_000_000, isDirectory: true, executeOnlyIfAlready: true));
        Assert.Equal(0b110_100_100, change.ApplyToContent(0b110_000_000, isDirectory: false, executeOnlyIfAlready: true));
        Assert.Equal(0b111_101_101, change.ApplyToContent(0b100_000_000 | 0b001_000_000, isDirectory: false, executeOnlyIfAlready: true));
        Assert.Equal(0b111_101_101, change.ApplyToContent(0b110_000_000, isDirectory: false, executeOnlyIfAlready: false));
        // Le setgid du changement n'est pas propagé au contenu ; le setuid d'un fichier du contenu y reste.
        Assert.Equal(UnixPermissions.SetUid | 0b111_101_101, change.ApplyToContent(UnixPermissions.SetUid | 0b111_000_000, isDirectory: false, executeOnlyIfAlready: false));
    }
}
