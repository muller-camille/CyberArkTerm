using System.Globalization;
using ZillaTerm.Core.Duty;
using ZillaTerm.Core.KeePass;
using ZillaTerm.Core.Terminal;

namespace ZillaTerm.Core.Tests.Duty;

public sealed class DutyJournalTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "zillaterm-duty-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static readonly DateTime T0 = new(2026, 10, 10, 21, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void EntriesAreReadBackInOrderAndTheFileIsEncrypted()
    {
        var protector = new XorProtector();
        string path;
        using (var journal = DutyJournal.Start(_dir, protector, T0))
        {
            path = journal.Path;
            journal.Write(new DutyEntry(T0, DutyKind.Started, "", "jdoe sur POSTE01"));
            journal.Write(new DutyEntry(T0.AddSeconds(1), DutyKind.Terminal, "root@srv01.corp.local", "[root@srv01 ~]# systemctl restart httpd"));
            journal.Flush();
            journal.Write(new DutyEntry(T0.AddSeconds(2), DutyKind.Screenshot, "srv02", "Capture", [0x89, 0x50, 0x4E, 0x47]));
            journal.Stop(new DutyEntry(T0.AddMinutes(5), DutyKind.Stopped, "", ""));
        }

        var bytes = File.ReadAllBytes(path);
        Assert.DoesNotContain("systemctl", System.Text.Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);

        var recording = DutyJournal.Read(path, protector);
        Assert.False(recording.Damaged);
        Assert.False(recording.Unfinished);
        Assert.Equal([DutyKind.Started, DutyKind.Terminal, DutyKind.Screenshot, DutyKind.Stopped], recording.Entries.Select(e => e.Kind));
        Assert.Equal("[root@srv01 ~]# systemctl restart httpd", recording.Entries[1].Text);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], recording.Entries[2].Image);
        Assert.Equal(T0, recording.Start);
        Assert.Equal(T0.AddMinutes(5), recording.End);
    }

    /// <summary>ZillaTerm arrêté pendant l'enregistrement : ce qui a été écrit se relit, la fin manque.</summary>
    [Fact]
    public void JournalWithoutItsEndIsUnfinished()
    {
        var protector = new XorProtector();
        var journal = DutyJournal.Start(_dir, protector, T0);
        journal.Write(new DutyEntry(T0, DutyKind.Started, "", ""));
        journal.Write(new DutyEntry(T0.AddSeconds(1), DutyKind.Note, "", "Incident INC0012345"));
        journal.Flush();

        var recording = DutyJournal.Read(journal.Path, protector);
        Assert.True(recording.Unfinished);
        Assert.False(recording.Damaged);
        Assert.Equal("Incident INC0012345", recording.Entries[1].Text);
        journal.Dispose();
    }

    /// <summary>Un bloc retiré, abîmé, ou tronqué à la fin : ce qui reste se relit, le journal est marqué abîmé.</summary>
    [Fact]
    public void RemovedOrDamagedBlocksAreReported()
    {
        var protector = new ChecksumProtector();
        var journal = DutyJournal.Start(_dir, protector, T0);
        for (int i = 0; i < 3; i++)
        {
            journal.Write(new DutyEntry(T0.AddSeconds(i), DutyKind.Note, "", $"note {i}"));
            journal.Flush();
        }

        journal.Stop(new DutyEntry(T0.AddSeconds(9), DutyKind.Stopped, "", ""));
        var original = File.ReadAllBytes(journal.Path);
        var blocks = Blocks(original);
        Assert.Equal(4, blocks.Count);

        // Deuxième bloc retiré.
        File.WriteAllBytes(journal.Path, [.. original[..8], .. blocks[0], .. blocks[2], .. blocks[3]]);
        var removed = DutyJournal.Read(journal.Path, protector);
        Assert.True(removed.Damaged);
        Assert.Equal(["note 0", "note 2", ""], removed.Entries.Select(e => e.Text));

        // Contenu d'un bloc modifié : refusé (DPAPI vérifie l'intégrité, comme ce faux protecteur).
        var changed = original.ToArray();
        changed[8 + 4 + 2] ^= 0xFF;
        File.WriteAllBytes(journal.Path, changed);
        Assert.True(DutyJournal.Read(journal.Path, protector).Damaged);

        // Fin coupée au milieu d'un bloc.
        File.WriteAllBytes(journal.Path, original[..^5]);
        var truncated = DutyJournal.Read(journal.Path, protector);
        Assert.True(truncated.Damaged);
        Assert.True(truncated.Unfinished);
        Assert.Equal(3, truncated.Entries.Count);

        File.WriteAllText(journal.Path, "pas un journal");
        Assert.Throws<InvalidDataException>(() => DutyJournal.Read(journal.Path, protector));
    }

    [Fact]
    public void TerminalTextIsCapped()
    {
        var protector = new XorProtector();
        using var journal = DutyJournal.Start(_dir, protector, T0, terminalTextLimit: 10);
        journal.Write(new DutyEntry(T0, DutyKind.Terminal, "s", "123456"));
        Assert.False(journal.TerminalTextCapped);
        journal.Write(new DutyEntry(T0, DutyKind.Terminal, "s", "7890123"));
        journal.Write(new DutyEntry(T0, DutyKind.Terminal, "s", "encore"));
        journal.Write(new DutyEntry(T0, DutyKind.Note, "", "les notes restent"));
        Assert.True(journal.TerminalTextCapped);
        journal.Stop(new DutyEntry(T0, DutyKind.Stopped, "", ""));

        var texts = DutyJournal.Read(journal.Path, protector).Entries.Select(e => e.Text).ToList();
        Assert.Equal(["123456", "…", "les notes restent", ""], texts);
    }

    [Fact]
    public void JournalsAreListedNewestFirst()
    {
        var protector = new XorProtector();
        DutyJournal.Start(_dir, protector, T0).Dispose();
        DutyJournal.Start(_dir, protector, T0).Dispose();
        DutyJournal.Start(_dir, protector, T0.AddDays(1)).Dispose();
        File.WriteAllText(Path.Combine(_dir, "autre.txt"), "");

        var list = DutyJournal.List(_dir);
        Assert.Equal(3, list.Count);
        Assert.Equal(T0.AddDays(1), list[0].Start);
        Assert.All(list.Skip(1), f => Assert.Equal(T0, f.Start));
        Assert.Empty(DutyJournal.List(Path.Combine(_dir, "absent")));
    }

    /// <summary>
    /// Texte des terminaux : lignes complètes, morceaux d'une ligne coupée réunis, rien de l'écran alternatif (vim), ni la
    /// commande que ZillaTerm tape puis efface pour suivre le dossier.
    /// </summary>
    [Fact]
    public void TranscriptKeepsWholeLinesOfTheMainScreenOnly()
    {
        var emulator = new TerminalEmulator(10, 5);
        var lines = new List<string>();
        using var transcript = new DutyTerminalTranscript(emulator, lines.Add);

        emulator.Feed("$ ls\r\na  b\r\n");
        emulator.Feed("0123456789abcdef\r\n");
        emulator.Feed("\x1b[?1049hvim screen\r\nmore\r\n\x1b[?1049l");
        emulator.Feed("$ ");
        Assert.Equal(["$ ls", "a  b", "0123456789abcdef"], lines);

        // Commande de ZillaTerm : marque, écho, puis marqueur d'effacement ; rien n'en reste.
        emulator.MarkEraseFromCursorLine();
        emulator.Feed("PROMPT_COMMAND=...\r\n" + WorkingDirectory.EraseMarker);
        emulator.Feed("$ uptime\r\n 21:30 up\r\n");
        Assert.Equal(["$ ls", "a  b", "0123456789abcdef", "$ uptime", " 21:30 up"], lines);

        // Marque retirée sans effacement (shell non Unix) : les lignes retenues étaient réelles.
        emulator.MarkEraseFromCursorLine();
        emulator.Feed("C:\\> dir\r\n");
        emulator.CancelEraseMark();
        emulator.Feed("x\r\n");
        Assert.Equal(["C:\\> dir", "x"], lines[^2..]);
    }

    [Fact]
    public void ReportEscapesTheServersTextAndForbidsScripts()
    {
        var png = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        var recording = new DutyRecording("x", [
            new DutyEntry(T0, DutyKind.Started, "", "jdoe"),
            new DutyEntry(T0.AddSeconds(1), DutyKind.Terminal, "root@srv01", "<script>alert(1)</script>"),
            new DutyEntry(T0.AddSeconds(2), DutyKind.Terminal, "root@srv01", "evil\u202Etxt.exe & \"quoted\""),
            new DutyEntry(T0.AddSeconds(3), DutyKind.Terminal, "root@srv02", "other"),
            new DutyEntry(T0.AddSeconds(4), DutyKind.Screenshot, "srv03", "Capture", png),
            new DutyEntry(T0.AddSeconds(5), DutyKind.Screenshot, "srv03", "Pas une image", "<svg>"u8.ToArray()),
            new DutyEntry(T0.AddMinutes(90), DutyKind.Stopped, "", ""),
        ], Damaged: false, Unfinished: false);

        string html = DutyReport.ToHtml(recording, new DutyReportText(), TimeZoneInfo.Utc, CultureInfo.InvariantCulture);

        Assert.Contains("default-src 'none'; img-src data:", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("\u202E", html, StringComparison.Ordinal);
        Assert.Contains("&amp; &quot;quoted&quot;", html, StringComparison.Ordinal);
        // Les deux lignes de srv01 forment un seul bloc ; srv02 a le sien.
        Assert.Equal(2, html.Split("<pre>").Length - 1);
        Assert.Contains("data:image/png;base64," + Convert.ToBase64String(png), html, StringComparison.Ordinal);
        Assert.Single(html.Split("data:image/").Skip(1), s => s.StartsWith("png", StringComparison.Ordinal));
        Assert.Equal(1, html.Split("<img").Length - 1);
    }

    private static List<byte[]> Blocks(byte[] file)
    {
        var blocks = new List<byte[]>();
        for (int at = 8; at < file.Length;)
        {
            int size = BitConverter.ToInt32(file, at);
            blocks.Add(file[at..(at + 4 + size)]);
            at += 4 + size;
        }

        return blocks;
    }

    /// <summary>Comme DPAPI : un bloc modifié est refusé.</summary>
    private sealed class ChecksumProtector : ISecretProtector
    {
        public byte[] Protect(byte[] data) => [.. System.Security.Cryptography.SHA256.HashData(data), .. data];

        public byte[] Unprotect(byte[] data)
        {
            var plain = data[32..];
            if (!System.Security.Cryptography.SHA256.HashData(plain).AsSpan().SequenceEqual(data.AsSpan(0, 32)))
            {
                throw new System.Security.Cryptography.CryptographicException("altered");
            }

            return plain;
        }
    }

    private sealed class XorProtector : ISecretProtector
    {
        public byte[] Protect(byte[] data) => data.Select(b => (byte)(b ^ 0x5A)).ToArray();

        public byte[] Unprotect(byte[] data) => Protect(data);
    }
}
