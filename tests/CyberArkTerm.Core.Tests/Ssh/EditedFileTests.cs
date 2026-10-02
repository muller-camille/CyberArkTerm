using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.Core.Tests.Ssh;

public sealed class EditedFileTests : IDisposable
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cyberarkterm-edit-" + Guid.NewGuid().ToString("N"));

    public EditedFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task Save_WithNewContent_RaisesSavedOnce()
    {
        var path = Write("app.conf", "port=80\n");
        using var file = new EditedFile("/etc/app/app.conf", path, new DateTime(2026, 1, 1), 8, Delay);
        var saves = Count(file);

        Assert.False(file.HasUnsentChanges);
        File.WriteAllText(path, "port=8080\n");

        await saves.WaitForAsync(1, Wait);
        Assert.True(file.HasUnsentChanges);
        Assert.Equal("app.conf", file.Name);

        // Même contenu réenregistré : pas de nouvelle demande.
        File.WriteAllText(path, "port=8080\n");
        await Task.Delay(Delay * 6);
        Assert.Equal(1, saves.Count);
    }

    [Fact]
    public async Task Save_BackToServerContent_IsIgnored_AndMarkSentResetsTheBaseline()
    {
        var path = Write("a.txt", "v1");
        using var file = new EditedFile("/tmp/a.txt", path, default, 2, Delay);
        var saves = Count(file);

        File.WriteAllText(path, "v2");
        await saves.WaitForAsync(1, Wait);
        File.WriteAllText(path, "v1");
        await Task.Delay(Delay * 6);
        Assert.Equal(1, saves.Count);
        Assert.False(file.HasUnsentChanges);

        File.WriteAllText(path, "v3");
        await saves.WaitForAsync(2, Wait);
        file.MarkSent("v3"u8.ToArray(), new DateTime(2026, 2, 2), 2);
        Assert.False(file.HasUnsentChanges);
        Assert.Equal(new DateTime(2026, 2, 2), file.RemoteWriteTime);

        File.WriteAllText(path, "v3");
        await Task.Delay(Delay * 6);
        Assert.Equal(2, saves.Count);
    }

    [Fact]
    public async Task Save_ByReplacingTheFile_IsDetected()
    {
        // Enregistrement « atomique » : écriture dans un fichier temporaire puis remplacement.
        var path = Write("b.sh", "echo 1");
        using var file = new EditedFile("/opt/b.sh", path, default, 6, Delay);
        var saves = Count(file);

        var temp = Write("b.sh.tmp", "echo 2");
        File.Move(temp, path, overwrite: true);

        await saves.WaitForAsync(1, Wait);
    }

    [Fact]
    public async Task Dispose_StopsWatching()
    {
        var path = Write("c.txt", "1");
        var file = new EditedFile("/c.txt", path, default, 1, Delay);
        var saves = Count(file);
        file.Dispose();

        File.WriteAllText(path, "2");
        await Task.Delay(Delay * 6);
        Assert.Equal(0, saves.Count);
    }

    [Theory]
    [InlineData("nginx.conf", "nginx.conf")]
    [InlineData("rapport final.txt", "rapport final.txt")]
    [InlineData("a&b|c>d.sh", "a_b_c_d.sh")]
    [InlineData("x:y*z?.log", "x_y_z_.log")]
    [InlineData("...", "fichier")]
    [InlineData("été.txt", "été.txt")]
    public void LocalCopyName_KeepsOnlySafeCharacters(string remote, string expected) =>
        Assert.Equal(expected, EditedFile.LocalCopyName(remote));

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static SaveCounter Count(EditedFile file)
    {
        var counter = new SaveCounter();
        file.Saved += _ => counter.Increment();
        return counter;
    }

    private sealed class SaveCounter
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Increment() => Interlocked.Increment(ref _count);

        public async Task WaitForAsync(int expected, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (Count < expected && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.Equal(expected, Count);
        }
    }
}
