using System.Text;

namespace ZillaTerm.Core.Tests;

public class DutyReminderFileTests
{
    [Theory]
    [InlineData(@"C:\Equipe\consignes.txt", true)]
    [InlineData(@"\\files.corp.local\astreinte\consignes.txt", true)]
    [InlineData("consignes.txt", false)]
    [InlineData(@"\\files.corp.local\astreinte", false)]
    [InlineData(@"\\.\C:\consignes.txt", false)]
    [InlineData(@"\\?\C:\consignes.txt", false)]
    [InlineData(@"\\srv01.corp.local\PIPE\consignes", false)]
    [InlineData(@"C:\Equipe\nul", false)]
    [InlineData(@"C:\Equipe\COM1.txt", false)]
    [InlineData(@"C:\Equipe\compte-rendu.txt", true)]
    [InlineData("C:\\Equipe\\consignes\u200B.txt", false)]
    [InlineData("", false)]
    public void OnlyFullPathsToOrdinaryFilesAreAccepted(string path, bool valid) =>
        Assert.Equal(valid, DutyReminderFile.IsValidPath(path));

    /// <summary>UTF-8 avec ou sans BOM, UTF-16, ou ancien Bloc-notes (Latin-1) : le texte est le même.</summary>
    [Fact]
    public void CommonEncodingsAreRead()
    {
        const string text = "Numéros d'urgence :\n• Responsable : 01 23 45 67 89";
        Assert.Equal(text, DutyReminderFile.Decode(Encoding.UTF8.GetBytes(text)));
        Assert.Equal(text, DutyReminderFile.Decode(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray()));
        Assert.Equal(text, DutyReminderFile.Decode(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray()));
        Assert.Equal(text, DutyReminderFile.Decode(Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes(text)).ToArray()));
        Assert.Equal("Numéros d'urgence", DutyReminderFile.Decode(Encoding.Latin1.GetBytes("Numéros d'urgence")));
    }

    /// <summary>
    /// Sauts de ligne Windows ramenés à « \n » ; caractères de contrôle et invisibles retirés (un inverseur de sens de
    /// lecture afficherait un autre numéro que celui écrit) ; lignes vides de la fin retirées.
    /// </summary>
    [Fact]
    public void InvisibleAndControlCharactersAreRemoved()
    {
        var bytes = Encoding.UTF8.GetBytes("Supervision :\t\u202E01 98 76 54 32\u202C\r\nBip\u0007 \u001B[31mrouge\r\r\n\n");
        Assert.Equal("Supervision :\t01 98 76 54 32\nBip [31mrouge", DutyReminderFile.Decode(bytes));
    }

    /// <summary>Fichier coupé au milieu d'un caractère : la fin coupée est ignorée, sans repli vers Latin-1.</summary>
    [Fact]
    public void ACutCharacterIsDropped()
    {
        var bytes = Encoding.UTF8.GetBytes("Appeler le responsable — 01 23");
        int dash = Array.IndexOf(bytes, (byte)0xE2);
        Assert.Equal("Appeler le responsable", DutyReminderFile.Decode(bytes.AsSpan(0, dash + 1), cut: true));
        Assert.Equal("Appeler le responsable", DutyReminderFile.Decode(bytes.AsSpan(0, dash + 2), cut: true));
        Assert.Equal("Appeler le responsable —", DutyReminderFile.Decode(bytes.AsSpan(0, dash + 3), cut: true));
    }

    [Fact]
    public async Task ALongFileIsReadUpToTheLimit()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zillaterm-reminder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "consignes.txt");
            await File.WriteAllTextAsync(path, "Responsable : 01 23 45 67 89\r\n");
            var reminder = await DutyReminderFile.ReadFileAsync(path, default);
            Assert.Equal(new DutyReminderText("Responsable : 01 23 45 67 89", false), reminder);

            await File.WriteAllTextAsync(path, new string('é', DutyReminderFile.MaxBytes));
            var longOne = await DutyReminderFile.ReadFileAsync(path, default);
            Assert.True(longOne.Truncated);
            Assert.Equal(new string('é', DutyReminderFile.MaxBytes / 2), longOne.Text);

            await Assert.ThrowsAsync<FileNotFoundException>(() => DutyReminderFile.ReadFileAsync(Path.Combine(dir, "absent.txt"), default));
            await Assert.ThrowsAsync<ArgumentException>(() => DutyReminderFile.ReadAsync("consignes.txt"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
