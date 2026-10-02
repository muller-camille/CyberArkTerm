using System.Security.Cryptography;

namespace CyberArkTerm.Core.Ssh;

/// <summary>
/// Fichier du serveur ouvert dans l'éditeur de texte de l'utilisateur : surveille la copie locale et signale
/// (<see cref="Saved"/>) chaque enregistrement dont le contenu diffère du dernier vu et de celui du serveur.
/// </summary>
public sealed class EditedFile : IDisposable
{
    private static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(400);
    private const int MaxReadAttempts = 10;

    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;
    private readonly TimeSpan _delay;
    private readonly object _lock = new();
    private string _seenHash;
    private string _sentHash;
    private int _readAttempts;
    private bool _disposed;

    /// <param name="remoteWriteTime">Date de modification du fichier sur le serveur à l'ouverture.</param>
    /// <param name="remoteLength">Taille du fichier sur le serveur à l'ouverture.</param>
    /// <param name="delay">Attente après la dernière écriture avant de lire le fichier (les éditeurs écrivent en plusieurs fois).</param>
    public EditedFile(string remotePath, string localPath, DateTime remoteWriteTime, long remoteLength, TimeSpan? delay = null)
    {
        RemotePath = remotePath;
        LocalPath = localPath;
        RemoteWriteTime = remoteWriteTime;
        RemoteLength = remoteLength;
        _delay = delay ?? DefaultDelay;
        _sentHash = _seenHash = Hash(ReadAllBytesShared(localPath));
        _timer = new Timer(_ => OnQuiet());
        // On surveille le dossier : beaucoup d'éditeurs enregistrent dans un fichier temporaire puis le renomment.
        _watcher = new FileSystemWatcher(Path.GetDirectoryName(localPath)!, Path.GetFileName(localPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Renamed += OnFileEvent;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Enregistrement dans l'éditeur avec un contenu nouveau (levé sur un thread du pool).</summary>
    public event Action<EditedFile>? Saved;

    /// <summary>Chemin complet du fichier sur le serveur.</summary>
    public string RemotePath { get; }

    /// <summary>Copie locale ouverte dans l'éditeur.</summary>
    public string LocalPath { get; }

    public string Name => Ssh.RemotePath.Name(RemotePath);

    /// <summary>Date de modification sur le serveur lors de l'ouverture ou du dernier renvoi.</summary>
    public DateTime RemoteWriteTime { get; private set; }

    /// <summary>Taille sur le serveur lors de l'ouverture ou du dernier renvoi.</summary>
    public long RemoteLength { get; private set; }

    /// <summary>Vrai si la copie locale diffère du contenu présent sur le serveur.</summary>
    public bool HasUnsentChanges
    {
        get
        {
            try
            {
                var hash = Hash(ReadAllBytesShared(LocalPath));
                lock (_lock)
                {
                    return hash != _sentHash;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Un renvoi a été coupé pendant l'écriture : le fichier du serveur est peut-être incomplet, et c'est nous qui
    /// l'avons modifié (le prochain renvoi ne doit pas le signaler comme changé par quelqu'un d'autre).
    /// </summary>
    public bool WriteInterrupted { get; private set; }

    /// <summary>Le contenu <paramref name="content"/> vient d'être écrit sur le serveur.</summary>
    public void MarkSent(byte[] content, DateTime remoteWriteTime, long remoteLength)
    {
        var hash = Hash(content);
        lock (_lock)
        {
            _sentHash = hash;
            _seenHash = hash;
            RemoteWriteTime = remoteWriteTime;
            RemoteLength = remoteLength;
            WriteInterrupted = false;
        }
    }

    public void MarkWriteInterrupted()
    {
        lock (_lock)
        {
            WriteInterrupted = true;
        }
    }

    /// <summary>
    /// Nom de la copie locale : celui du fichier distant, limité aux caractères sûrs pour Windows et pour la
    /// ligne de commande de l'éditeur (lettres, chiffres, espace, « . », « - », « _ »).
    /// </summary>
    public static string LocalCopyName(string remoteName)
    {
        var chars = remoteName.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or ' ' ? c : '_').ToArray();
        var name = new string(chars).Trim(' ', '.');
        if (name.Length == 0)
        {
            return "fichier";
        }

        return name.Length > 120 ? name[..120] : name;
    }

    /// <summary>Lit un fichier même s'il est encore ouvert par l'éditeur.</summary>
    public static byte[] ReadAllBytesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
        }

        _watcher.Dispose();
        _timer.Dispose();
    }

    private static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (e is RenamedEventArgs r && !string.Equals(r.FullPath, LocalPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_lock)
        {
            if (!_disposed)
            {
                _readAttempts = 0;
                _timer.Change(_delay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void OnQuiet()
    {
        byte[] content;
        try
        {
            content = ReadAllBytesShared(LocalPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Fichier encore verrouillé ou en cours de remplacement par l'éditeur : on réessaie un peu plus tard.
            lock (_lock)
            {
                if (!_disposed && ++_readAttempts < MaxReadAttempts)
                {
                    _timer.Change(_delay, Timeout.InfiniteTimeSpan);
                }
            }

            return;
        }

        var hash = Hash(content);
        lock (_lock)
        {
            if (_disposed || hash == _seenHash)
            {
                return;
            }

            _seenHash = hash;
            if (hash == _sentHash)
            {
                // Retour au contenu du serveur : rien à renvoyer.
                return;
            }
        }

        Saved?.Invoke(this);
    }
}
