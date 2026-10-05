using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.Core.Tests.Ssh;

public sealed class TransferHistoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"cat-history-{Guid.NewGuid():N}", "transfers.json");

    public void Dispose()
    {
        if (Directory.Exists(Path.GetDirectoryName(_path)))
        {
            Directory.Delete(Path.GetDirectoryName(_path)!, recursive: true);
        }
    }

    private static TransferCheck Check(string name, bool same = true) =>
        new(name, $@"C:\Temp\{name}", $"/srv/{name}", 3, [1, 2, 3], 3, same ? [1, 2, 3] : [9, 9, 9]) { Upload = true };

    /// <summary>Un transfert relu après redémarrage : résultat, sens, et sommes de chaque fichier pour revérifier.</summary>
    [Fact]
    public void SurvivesSaveAndLoad()
    {
        var history = TransferHistory.Load(_path);
        Assert.Empty(history.Records);
        history.Add(new TransferRecord
        {
            Time = new DateTime(2026, 10, 5, 18, 0, 0, DateTimeKind.Utc),
            Upload = true,
            Server = "root@srv01",
            Label = "deploy/",
            Destination = "/opt/app",
            Protocol = "SCP",
            State = TransferState.Done,
            FileCount = 2,
            Files = [Check("a"), Check("b", same: false)],
        });
        history.Add(new TransferRecord { Label = "app.log", State = TransferState.Failed, Error = "permission refusée" });
        history.Save();

        var loaded = TransferHistory.Load(_path);

        Assert.Equal(["app.log", "deploy/"], loaded.Records.Select(r => r.Label));
        var deploy = loaded.Records[1];
        Assert.Equal((true, "root@srv01", "SCP", TransferState.Done, 1, 1), (deploy.Upload, deploy.Server, deploy.Protocol, deploy.State, deploy.Identical, deploy.Different));
        Assert.Equal("010203  /srv/a", deploy.Files[0].ToSha256SumLine());
        Assert.Equal("permission refusée", loaded.Records[0].Error);
        Assert.False(File.Exists(_path + ".tmp"));
    }

    /// <summary>Limites : 200 transferts, 500 fichiers par transfert (les fichiers à revoir gardés d'abord), 5 000 en tout.</summary>
    [Fact]
    public void KeepsOnlyTheLatestTransfers()
    {
        var history = new TransferHistory();
        for (int i = 0; i < TransferHistory.MaxRecords + 5; i++)
        {
            history.Add(new TransferRecord { Label = $"t{i}" });
        }

        Assert.Equal(TransferHistory.MaxRecords, history.Records.Count);
        Assert.Equal($"t{TransferHistory.MaxRecords + 4}", history.Records[0].Label);

        var big = Enumerable.Range(0, 600).Select(i => Check($"f{i}", same: i != 550)).ToList();
        history.Add(new TransferRecord { Label = "gros", FileCount = 600, Files = big });
        var kept = history.Records[0];
        Assert.Equal((TransferHistory.MaxFilesPerRecord, true, 600), (kept.Files.Count, kept.FilesTruncated, kept.FileCount));
        Assert.Equal("f550", kept.Files[0].Name);

        for (int i = 0; i < 12; i++)
        {
            history.Add(new TransferRecord { Label = $"g{i}", Files = big });
        }

        Assert.True(history.Records.Sum(r => r.Files.Count) <= TransferHistory.MaxFiles);
        Assert.Equal("g11", history.Records[0].Label);
    }

    [Fact]
    public void CorruptFileGivesAnEmptyHistory()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ pas du json");

        Assert.Empty(TransferHistory.Load(_path).Records);
    }
}
