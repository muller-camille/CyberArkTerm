using System.Text;
using ZillaTerm.Core.Ssh;

namespace ZillaTerm.Core.Tests.Ssh;

public sealed class FileTailTests
{
    /// <summary>Fichier en mémoire qui grossit, se tronque ou se remplace comme un journal.</summary>
    private sealed class FakeFile : ITailSource
    {
        public List<byte> Content { get; set; } = [];

        public int Reads { get; private set; }

        public void Append(string text) => Content.AddRange(Encoding.UTF8.GetBytes(text));

        public void Append(byte[] bytes) => Content.AddRange(bytes);

        public Task<long> GetSizeAsync(CancellationToken ct) => Task.FromResult((long)Content.Count);

        public Task<byte[]> ReadAsync(long offset, int count, CancellationToken ct)
        {
            Reads++;
            return Task.FromResult(Content.Skip((int)offset).Take(count).ToArray());
        }
    }

    [Fact]
    public async Task ShowsTheEndThenWhatIsAdded()
    {
        var file = new FakeFile();
        file.Append("ligne 1\r\nligne 2\n");
        var tail = new FileTail(file);

        Assert.Equal("ligne 1\nligne 2\n", (await tail.PollAsync(default)).Text);
        Assert.Equal("", (await tail.PollAsync(default)).Text);

        file.Append("ligne 3 é");
        file.Append([0xE2, 0x82]);   // « € » coupé entre deux relevés
        Assert.Equal("ligne 3 é", (await tail.PollAsync(default)).Text);
        file.Append([0xAC]);
        file.Append("\r");
        Assert.Equal("€", (await tail.PollAsync(default)).Text);
        file.Append("\nligne 4\n");
        Assert.Equal("\nligne 4\n", (await tail.PollAsync(default)).Text);
    }

    /// <summary>Gros fichier : le premier relevé ne montre que la fin, à partir d'une ligne entière.</summary>
    [Fact]
    public async Task StartsOnAWholeLineNearTheEnd()
    {
        var file = new FakeFile();
        for (int i = 0; i < 20_000; i++)
        {
            file.Append($"ligne numéro {i}\n");
        }

        var update = await new FileTail(file).PollAsync(default);

        Assert.True(update.Text.Length <= FileTail.InitialBytes);
        Assert.StartsWith("ligne numéro ", update.Text);
        Assert.EndsWith("ligne numéro 19999\n", update.Text);
    }

    /// <summary>Rotation ou troncature : relu depuis le début, signalé.</summary>
    [Fact]
    public async Task RestartsWhenTheFileShrinks()
    {
        var file = new FakeFile();
        file.Append("ancien contenu assez long\n");
        var tail = new FileTail(file);
        await tail.PollAsync(default);

        file.Content = [];
        file.Append("nouveau\n");
        var update = await tail.PollAsync(default);

        Assert.True(update.Restarted);
        Assert.Equal("nouveau\n", update.Text);
    }

    /// <summary>Rotation vers un fichier déjà plus grand que la position lue : reconnu à son début, relu en entier.</summary>
    [Fact]
    public async Task RestartsWhenTheFileIsReplacedByALargerOne()
    {
        var file = new FakeFile();
        file.Append("2026-10-05 ancien\n");
        var tail = new FileTail(file);
        await tail.PollAsync(default);

        file.Content = [];
        file.Append("2026-10-06 nouveau fichier, plus long que l'ancien\n");
        var update = await tail.PollAsync(default);

        Assert.True(update.Restarted);
        Assert.Equal("2026-10-06 nouveau fichier, plus long que l'ancien\n", update.Text);

        file.Append("suite\n");
        Assert.Equal("suite\n", (await tail.PollAsync(default)).Text);
    }

    /// <summary>Énorme ajout entre deux relevés : le début est sauté, seule la fin est lue.</summary>
    [Fact]
    public async Task SkipsAHugeBurst()
    {
        var file = new FakeFile();
        file.Append("début\n");
        var tail = new FileTail(file);
        await tail.PollAsync(default);
        var line = new string('x', 99) + "\n";
        for (int i = 0; i < (FileTail.MaxBytesPerPoll / 100) + 1000; i++)
        {
            file.Append(line);
        }

        var update = await tail.PollAsync(default);

        Assert.True(update.Skipped > 0);
        Assert.True(update.Text.Length <= FileTail.InitialBytes);
        Assert.StartsWith(new string('x', 99), update.Text);
        Assert.Equal(file.Content.Count, tail.Position);
    }
}
