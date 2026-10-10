namespace ZillaTerm.Core.Tests;

/// <summary>Écritures qui résistent à une coupure : jamais de fichier vide, jamais plus de fichier du tout.</summary>
public sealed class DurableFileTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("zt-durable-").FullName;

    [Fact]
    public void WriteReplacesWithoutKeepingTheOldVersion()
    {
        var path = Path.Combine(_directory, "coffre.dat");
        DurableFile.Write(path, "ancien"u8);
        DurableFile.Write(path, "nouveau"u8);

        Assert.Equal("nouveau", File.ReadAllText(path));
        Assert.False(File.Exists(DurableFile.TempPath(path)));
        Assert.False(File.Exists(DurableFile.BackupPath(path)));
    }

    /// <summary>File.Replace peut renommer l'original en .bak puis échouer : le nouveau fichier est quand même mis en place.</summary>
    [Fact]
    public void ReplaceRecoversWhenTheOriginalWasMovedFirst()
    {
        var (path, temp) = Files();

        DurableFile.Replace(temp, path, (_, p, b) =>
        {
            File.Move(p, b, overwrite: true);
            throw new IOException("ERROR_UNABLE_TO_MOVE_REPLACEMENT_2");
        });

        Assert.Equal("nouveau", File.ReadAllText(path));
        Assert.Equal("ancien", File.ReadAllText(DurableFile.BackupPath(path)));
        Assert.False(File.Exists(temp));
    }

    [Fact]
    public void ReplaceCopiesWhenReplaceIsNotSupported()
    {
        var (path, temp) = Files();

        DurableFile.Replace(temp, path, (_, _, _) => throw new PlatformNotSupportedException());

        Assert.Equal("nouveau", File.ReadAllText(path));
        Assert.Equal("ancien", File.ReadAllText(DurableFile.BackupPath(path)));
    }

    /// <summary>
    /// Remplacement interrompu (settings.json renommé en .bak, nouveau resté en .tmp) : au démarrage suivant, les réglages
    /// de la sauvegarde reviennent, pas ceux par défaut qui écraseraient ensuite les vrais.
    /// </summary>
    [Fact]
    public void SettingsComeBackFromTheBackupWhenTheFileIsMissing()
    {
        var path = Path.Combine(_directory, "settings.json");
        new AppSettings { PvwaUrl = "pvwa.corp.local", UserName = "jdoe" }.Save(path);
        new AppSettings { PvwaUrl = "pvwa.corp.local", UserName = "jdoe2" }.Save(path);
        File.Delete(path);

        var loaded = AppSettings.Load(path);

        Assert.Equal("jdoe", loaded.UserName);
        Assert.True(loaded.RestoredFromBackup);
        Assert.Null(loaded.SetAsideFile);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private (string Path, string Temp) Files()
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "ancien");
        File.WriteAllText(DurableFile.TempPath(path), "nouveau");
        return (path, DurableFile.TempPath(path));
    }
}
