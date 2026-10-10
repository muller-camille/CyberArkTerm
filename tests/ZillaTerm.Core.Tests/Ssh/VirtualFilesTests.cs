using System.Buffers.Binary;
using System.Text;
using ZillaTerm.Core.Ssh;

namespace ZillaTerm.Core.Tests.Ssh;

public class VirtualFilesTests
{
    /// <summary>Un serveur ne doit pas pouvoir faire écrire hors du dossier où l'on dépose les fichiers.</summary>
    [Theory]
    [InlineData("rapport.pdf", "rapport.pdf")]
    [InlineData(@"..\..\Windows\evil.exe", ".._.._Windows_evil.exe")]
    [InlineData("../../etc", ".._.._etc")]
    [InlineData("..", "_")]
    [InlineData(".", "_")]
    [InlineData("", "_")]
    [InlineData("a:b*c?d\"e<f>g|h", "a_b_c_d_e_f_g_h")]
    [InlineData("tab\there\u0001", "tab_here_")]
    [InlineData("facture\u202Efdp.exe", "facture_fdp.exe")]
    [InlineData("a\u200Bb\u2028c\u2066d", "a_b_c_d")]
    [InlineData("fin. . ", "fin")]
    [InlineData("CON", "_CON")]
    [InlineData("con.txt", "_con.txt")]
    [InlineData("COM1.tar.gz", "_COM1.tar.gz")]
    [InlineData("console.log", "console.log")]
    [InlineData(".bashrc", ".bashrc")]
    public void NamesAreMadeSafeForWindows(string unix, string windows)
    {
        Assert.Equal(windows, WindowsFileName.Sanitize(unix));
    }

    [Fact]
    public void LongNamesKeepTheirExtension()
    {
        var name = WindowsFileName.Sanitize(new string('a', 300) + ".log");

        Assert.Equal(WindowsFileName.MaxLength, name.Length);
        Assert.EndsWith(".log", name, StringComparison.Ordinal);
    }

    [Fact]
    public void NamesAreUniqueInEachFolderIgnoringCase()
    {
        string[][] paths =
        [
            ["logs"],
            ["logs", "Rapport.txt"],
            ["logs", "rapport.txt"],
            ["logs", "a:b"],
            ["logs", "a_b"],
            ["Logs"],
            ["Logs", "Rapport.txt"],
            ["CON"],
        ];

        var names = VirtualFiles.AssignNames(paths);

        Assert.Equal(
            [@"logs", @"logs\Rapport.txt", @"logs\rapport (2).txt", @"logs\a_b", @"logs\a_b (2)", @"Logs (2)", @"Logs (2)\Rapport.txt", "_CON"],
            names);
    }

    [Fact]
    public void ContentMustFollowItsFolder()
    {
        Assert.Throws<ArgumentException>(() => VirtualFiles.AssignNames([["logs", "a.txt"]]));
    }

    [Fact]
    public void DescriptorFollowsTheWindowsLayout()
    {
        var date = new DateTime(2026, 10, 2, 12, 30, 0, DateTimeKind.Utc);
        var data = VirtualFiles.BuildDescriptor(
        [
            new VirtualFile("logs", IsDirectory: true, 0, date),
            new VirtualFile(@"logs\big.bin", IsDirectory: false, 5_000_000_000, date),
        ]);

        Assert.Equal(4 + (2 * VirtualFiles.DescriptorSize), data.Length);
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(data));

        var folder = data.AsSpan(4, VirtualFiles.DescriptorSize);
        Assert.Equal(0x10u, BinaryPrimitives.ReadUInt32LittleEndian(folder[36..]));
        Assert.Equal("logs", Name(folder));

        var file = data.AsSpan(4 + VirtualFiles.DescriptorSize, VirtualFiles.DescriptorSize);
        // Attributs, date d'écriture, taille, Unicode, progression de l'Explorateur.
        Assert.Equal(0x80004064u, BinaryPrimitives.ReadUInt32LittleEndian(file));
        Assert.Equal(0x80u, BinaryPrimitives.ReadUInt32LittleEndian(file[36..]));
        Assert.Equal(date.ToFileTimeUtc(), BinaryPrimitives.ReadInt64LittleEndian(file[56..]));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(file[64..]));
        Assert.Equal(705_032_704u, BinaryPrimitives.ReadUInt32LittleEndian(file[68..]));
        Assert.Equal(@"logs\big.bin", Name(file));
    }

    [Fact]
    public void PathsLongerThanMaxPathAreRefused()
    {
        var path = string.Join('\\', Enumerable.Repeat(new string('d', 50), 6));

        Assert.Throws<PathTooLongException>(() => VirtualFiles.BuildDescriptor([new VirtualFile(path, false, 1, default)]));
    }

    private static string Name(ReadOnlySpan<byte> descriptor)
    {
        var text = Encoding.Unicode.GetString(descriptor.Slice(72, 520));
        return text[..text.IndexOf('\0', StringComparison.Ordinal)];
    }
}
