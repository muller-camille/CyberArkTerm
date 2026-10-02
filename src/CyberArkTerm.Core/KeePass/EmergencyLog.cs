using System.Globalization;
using System.Text;

namespace CyberArkTerm.Core.KeePass;

/// <summary>
/// Journal local des accès d'urgence (hors CyberArk) : ouverture d'un coffre KeePass, connexions, modifications
/// d'entrées. Une ligne par action, sans aucun mot de passe : date UTC, compte Windows, poste, action, détails.
/// </summary>
public sealed class EmergencyLog(string path)
{
    /// <summary>Au-delà, le journal est renommé en « .1 » (une génération gardée).</summary>
    public const long MaxSize = 5 * 1024 * 1024;

    private const int WriteAttempts = 5;

    private static readonly object Lock = new();

    public string FilePath { get; } = path;

    /// <summary>
    /// Ajoute une ligne. Les détails ne doivent jamais contenir de secret ; tabulations et retours à la ligne y sont
    /// remplacés pour qu'une valeur ne puisse pas fabriquer de fausse ligne.
    /// </summary>
    public void Write(string action, params (string Name, string? Value)[] details)
    {
        var line = new StringBuilder()
            .Append(DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
            .Append('\t').Append(Clean(Environment.UserDomainName + "\\" + Environment.UserName))
            .Append('\t').Append(Clean(Environment.MachineName))
            .Append('\t').Append(Clean(action));
        foreach (var (name, value) in details)
        {
            line.Append('\t').Append(Clean(name)).Append('=').Append(Clean(value ?? ""));
        }

        line.Append(Environment.NewLine);
        var bytes = new UTF8Encoding(false).GetBytes(line.ToString());
        lock (Lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    Append(bytes);
                    return;
                }
                catch (IOException) when (attempt < WriteAttempts)
                {
                    // Une autre instance de l'application écrit au même moment.
                    Thread.Sleep(50 * attempt);
                }
            }
        }
    }

    /// <summary>Comme <see cref="Write"/>, sans erreur si le journal est inaccessible (renvoie faux).</summary>
    public bool TryWrite(string action, params (string Name, string? Value)[] details)
    {
        try
        {
            Write(action, details);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void Append(byte[] bytes)
    {
        var info = new FileInfo(FilePath);
        if (info.Exists && info.Length > MaxSize)
        {
            try
            {
                File.Move(FilePath, FilePath + ".1", overwrite: true);
            }
            catch (IOException)
            {
                // Une autre instance vient de le faire, ou le fichier est ouvert : on réessaiera à la ligne suivante.
            }
        }

        // Partagé en écriture : plusieurs instances peuvent ajouter leurs lignes ; chaque ligne part en une écriture.
        using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        stream.Write(bytes);
    }

    private static string Clean(string text) =>
        new(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
}
