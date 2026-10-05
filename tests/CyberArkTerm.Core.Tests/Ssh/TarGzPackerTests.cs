using System.Formats.Tar;
using System.IO.Compression;
using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.Core.Tests.Ssh;

public sealed class TarGzPackerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cat-targz-" + Guid.NewGuid().ToString("N"));

    public TarGzPackerTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Make(string relative, string content)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// Chaque élément déposé à la racine de l'archive, dossiers parcourus entièrement (fichiers cachés, noms accentués
    /// ou longs), droits fixes, contenu intact ; avancement en octets.
    /// </summary>
    [Fact]
    public async Task PacksDroppedItemsAtTheRoot()
    {
        var longName = new string('n', 150) + ".txt";
        Make("deploy/app.conf", "a=1");
        Make("deploy/.env", "secret=dans le fichier, pas dans le journal");
        Make($"deploy/sous dossier/été/{longName}", "long");
        var single = Make("notes.txt", "notes");
        Directory.CreateDirectory(Path.Combine(_dir, "deploy", "vide"));
        var archive = Path.Combine(_dir, "out.tar.gz");
        var reports = new List<TransferProgress>();

        int files = await TarGzPacker.CreateAsync([Path.Combine(_dir, "deploy") + Path.DirectorySeparatorChar, single], archive,
            new SyncProgress(reports.Add), default);

        Assert.Equal(4, files);
        Assert.Equal((4, 3L + 43 + 4 + 5), TarGzPacker.Measure([Path.Combine(_dir, "deploy"), single]));
        var entries = new Dictionary<string, (TarEntryType Type, UnixFileMode Mode, string Content)>();
        await using (var gzip = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress))
        using (var reader = new TarReader(gzip))
        {
            while (reader.GetNextEntry() is { } entry)
            {
                var content = entry.DataStream is null ? "" : new StreamReader(entry.DataStream).ReadToEnd();
                entries[entry.Name] = (entry.EntryType, entry.Mode, content);
            }
        }

        Assert.Equal(TarEntryType.Directory, entries["deploy/"].Type);
        Assert.Equal((UnixFileMode)0b111_101_101, entries["deploy/vide/"].Mode);
        Assert.Equal(("a=1", (UnixFileMode)0b110_100_100), (entries["deploy/app.conf"].Content, entries["deploy/app.conf"].Mode));
        Assert.Equal("long", entries[$"deploy/sous dossier/été/{longName}"].Content);
        Assert.Contains("deploy/.env", entries.Keys);
        Assert.Equal("notes", entries["notes.txt"].Content);
        Assert.All(reports, r => Assert.True(r.Packing));
        Assert.Equal(55, reports[^1].Transferred);
    }

    [Fact]
    public async Task CancellationStopsThePacking()
    {
        Make("a/1.txt", "1");
        Make("a/2.txt", "2");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TarGzPacker.CreateAsync([Path.Combine(_dir, "a")], Path.Combine(_dir, "x.tar.gz"), null, cts.Token));
    }

    /// <summary>Nom de l'archive et commande d'extraction protégée contre l'injection (apostrophes, nom commençant par « - »).</summary>
    [Fact]
    public void NamesTheArchiveAndQuotesTheCommand()
    {
        var now = new DateTime(2026, 10, 5, 18, 30, 0);
        Assert.Equal("deploy.tar.gz", TarGzPacker.ArchiveName([Path.Combine(_dir, "deploy") + Path.DirectorySeparatorChar], now));
        Assert.Equal("upload-20261005-183000.tar.gz", TarGzPacker.ArchiveName([Path.Combine(_dir, "a"), Path.Combine(_dir, "b")], now));
        Assert.Equal("cd '/opt/l'\\''app' && gzip -dc './-x.tar.gz' | tar -xf - && rm -f './-x.tar.gz'",
            TarGzPacker.ExtractCommand("/opt/l'app", "-x.tar.gz"));
        Assert.Null(TarGzPacker.ExtractCommand("/opt/a\nb", "x.tar.gz"));
    }

    private sealed class SyncProgress(Action<TransferProgress> report) : IProgress<TransferProgress>
    {
        public void Report(TransferProgress value) => report(value);
    }
}
