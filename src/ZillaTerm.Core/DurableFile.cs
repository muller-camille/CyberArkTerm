namespace ZillaTerm.Core;

/// <summary>
/// Écriture de fichiers qui résiste à une coupure de courant ou à un arrêt brutal : le contenu est d'abord écrit en
/// entier jusqu'au disque dans une copie temporaire, puis mis à la place de l'ancien fichier. On retrouve donc toujours
/// un fichier complet, l'ancien ou le nouveau, jamais un fichier vide ou à moitié écrit.
/// </summary>
public static class DurableFile
{
    /// <summary>Copie de l'avant-dernière version, gardée par <see cref="Replace"/>.</summary>
    public static string BackupPath(string path) => path + ".bak";

    /// <summary>Copie temporaire, écrite jusqu'au disque avant d'être mise en place.</summary>
    public static string TempPath(string path) => path + ".tmp";

    /// <summary>
    /// Remplace <paramref name="path"/> par <paramref name="content"/>, sans garder l'ancienne version (fichier de
    /// secrets : un secret oublié ne doit pas survivre dans une copie).
    /// </summary>
    public static void Write(string path, ReadOnlySpan<byte> content)
    {
        var temp = WriteTemp(path, content);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Écrit <paramref name="content"/> dans la copie temporaire de <paramref name="path"/>, jusqu'au disque.</summary>
    public static string WriteTemp(string path, ReadOnlySpan<byte> content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = TempPath(path);
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(content);
            stream.Flush(flushToDisk: true);
        }

        return temp;
    }

    /// <summary>
    /// Met <paramref name="temp"/> à la place de <paramref name="path"/>, l'ancien devenant <see cref="BackupPath"/>. Sur un
    /// système de fichiers sans remplacement atomique (certains partages réseau, AppData redirigé), ou si le remplacement
    /// échoue à mi-chemin, le nouveau fichier est mis en place autrement, ou l'ancien remis : jamais plus de fichier du tout.
    /// </summary>
    /// <param name="replace">Remplacement atomique (File.Replace) ; remplaçable pour les tests.</param>
    public static void Replace(string temp, string path, Action<string, string, string>? replace = null)
    {
        var backup = BackupPath(path);
        if (!File.Exists(path))
        {
            File.Move(temp, path, overwrite: true);
            return;
        }

        try
        {
            (replace ?? ((t, p, b) => File.Replace(t, p, b, ignoreMetadataErrors: true)))(temp, path, backup);
        }
        catch (Exception e) when (e is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            if (File.Exists(path))
            {
                File.Copy(path, backup, overwrite: true);
                File.Move(temp, path, overwrite: true);
                return;
            }

            // Le remplacement a renommé l'original en sauvegarde avant d'échouer : il reste à mettre le nouveau en place,
            // ou, si c'est impossible, à remettre l'original.
            try
            {
                File.Move(temp, path);
            }
            catch (Exception) when (File.Exists(backup) && !File.Exists(path))
            {
                File.Copy(backup, path);
                throw;
            }
        }
    }
}
