using CyberArkTerm.Core.KeePass;

namespace CyberArkTerm.Core.Tests.KeePass;

public sealed class EmergencyLogTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("cat-log-").FullName;

    [Fact]
    public void WritesOneLinePerActionWithoutLineBreakInjection()
    {
        var log = new EmergencyLog(Path.Combine(_directory, "urgence.log"));

        log.Write("ssh", ("vault", @"C:\coffres\urgence.kdbx"), ("entry", "srv-lnx01\nFAUSSE LIGNE"), ("target", "srv-lnx01:22"));
        log.Write("open", ("vault", "x"));

        var lines = File.ReadAllLines(log.FilePath);
        Assert.Equal(2, lines.Length);
        var fields = lines[0].Split('\t');
        Assert.Equal("ssh", fields[3]);
        Assert.Equal("entry=srv-lnx01 FAUSSE LIGNE", fields[5]);
        Assert.Equal("target=srv-lnx01:22", fields[6]);
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", fields[0]);
    }

    [Fact]
    public void RotatesWhenTooLarge()
    {
        var path = Path.Combine(_directory, "urgence.log");
        File.WriteAllText(path, new string('x', (int)EmergencyLog.MaxSize + 1));

        new EmergencyLog(path).Write("open");

        Assert.True(File.Exists(path + ".1"));
        Assert.Single(File.ReadAllLines(path));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
