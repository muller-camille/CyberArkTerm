using System.IO;
using System.Security.Cryptography;
using CyberArkTerm.Core;
using CyberArkTerm.Core.KeePass;

namespace CyberArkTerm.App.Services.KeePass;

/// <summary>
/// Coffres KeePass ouverts (un par dossier de l'onglet « Courants »), coffre local des mots de passe maîtres et
/// journal des accès d'urgence. Vit le temps de l'application ; tout est verrouillé à la fermeture de la fenêtre
/// principale et au verrouillage de la session Windows.
/// </summary>
internal sealed class KeePassManager : IDisposable
{
    private readonly Dictionary<string, KeePassVault> _open = new(StringComparer.Ordinal);

    public KeePassManager()
    {
        var directory = Path.GetDirectoryName(AppSettings.DefaultPath)!;
        Store = new LocalSecretStore(Path.Combine(directory, "coffre-local.dat"), new DpapiProtector());
        Log = new EmergencyLog(Path.Combine(directory, "urgence.log"));
    }

    /// <summary>Un coffre a été déverrouillé ou verrouillé.</summary>
    public event Action? Changed;

    public LocalSecretStore Store { get; }

    public EmergencyLog Log { get; }

    public KeePassVault? Get(string folderId) => _open.GetValueOrDefault(folderId);

    public bool IsOpen(string folderId) => _open.ContainsKey(folderId);

    /// <summary>Mot de passe maître mémorisé pour ce dossier (UTF-8, à effacer), si le coffre local est déverrouillé.</summary>
    public byte[]? StoredPassword(KeePassFolder folder) =>
        folder.RememberPassword && Store.IsUnlocked ? Store.Get(folder.Id) : null;

    /// <summary>Ouvre le coffre avec ce mot de passe (UTF-8) et ce fichier clé ; l'action est journalisée.</summary>
    public async Task<KeePassVault> UnlockAsync(KeePassFolder folder, byte[]? password, string? keyFilePath, CancellationToken cancellation)
    {
        KeePassVault vault;
        try
        {
            var keyFile = KeePassKey.ReadKeyFile(keyFilePath);
            KeePassKey key;
            try
            {
                key = KeePassKey.CreateFromUtf8(folder.UsesPassword ? password ?? [] : null, keyFile);
            }
            finally
            {
                if (keyFile is not null)
                {
                    CryptographicOperations.ZeroMemory(keyFile);
                }
            }

            vault = await KeePassVault.OpenAsync(folder.FilePath, key, cancellation);
            if (cancellation.IsCancellationRequested)
            {
                // Abandon pendant la dérivation de clé (qui ne s'interrompt pas toujours) : on ne garde rien d'ouvert.
                vault.Dispose();
                cancellation.ThrowIfCancellationRequested();
            }
        }
        catch (Exception ex) when (ex is KeePassException or IOException or UnauthorizedAccessException)
        {
            Log.Write("keepass-open-failed", ("vault", folder.FilePath), ("reason", ex is KeePassException k ? k.Kind.ToString() : ex.GetType().Name));
            throw;
        }

        Lock(folder.Id, notify: false);
        _open[folder.Id] = vault;
        Log.Write("keepass-open", ("vault", folder.FilePath), ("format", vault.Database.FormatName),
            ("keyfile", string.IsNullOrEmpty(keyFilePath) ? "no" : "yes"), ("entries", vault.Database.Entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Changed?.Invoke();
        return vault;
    }

    public void Lock(string folderId) => Lock(folderId, notify: true);

    /// <summary>Verrouille tous les coffres KeePass (le coffre local aussi si <paramref name="localStore"/>).</summary>
    public void LockAll(bool localStore = false)
    {
        foreach (var id in _open.Keys.ToList())
        {
            Lock(id, notify: false);
        }

        if (localStore)
        {
            Store.Lock();
        }

        Changed?.Invoke();
    }

    public void Dispose()
    {
        LockAll(localStore: true);
        Store.Dispose();
    }

    private void Lock(string folderId, bool notify)
    {
        if (_open.Remove(folderId, out var vault))
        {
            Log.Write("keepass-lock", ("vault", vault.FilePath));
            vault.Dispose();
            if (notify)
            {
                Changed?.Invoke();
            }
        }
    }
}
