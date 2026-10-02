using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CyberArkTerm.Core.Localization;
using Konscious.Security.Cryptography;

namespace CyberArkTerm.Core.KeePass;

/// <summary>Protection supplémentaire du fichier (DPAPI sous Windows : lié au compte Windows de l'utilisateur).</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] data);

    byte[] Unprotect(byte[] data);
}

/// <summary>
/// Coffre local des mots de passe maîtres KeePass : chiffré en AES-256-GCM avec une clé dérivée du mot de passe du
/// coffre local par Argon2id, puis protégé par <see cref="ISecretProtector"/> (DPAPI). Rien n'y est lisible sans le
/// mot de passe ; une fois déverrouillé, les secrets restent masqués en mémoire jusqu'au verrouillage.
/// </summary>
public sealed class LocalSecretStore : IDisposable
{
    public const int MinPasswordLength = 8;
    private const int FormatVersion = 1;

    private readonly string _path;
    private readonly ISecretProtector? _protector;
    private readonly KdfSettings _kdf;
    private readonly Dictionary<string, SecretBytes> _secrets = new(StringComparer.Ordinal);
    private SecretBytes? _key;
    private byte[] _salt = [];
    private KdfSettings _fileKdf;

    /// <param name="kdf">Coût d'Argon2id pour un nouveau mot de passe (par défaut 64 Mio, 3 passes, 2 voies).</param>
    public LocalSecretStore(string path, ISecretProtector? protector, KdfSettings? kdf = null)
    {
        _path = path;
        _protector = protector;
        _kdf = kdf ?? KdfSettings.Default;
        _fileKdf = _kdf;
    }

    public bool Exists => File.Exists(_path);

    public bool IsUnlocked => _key is not null;

    /// <summary>Identifiants des secrets mémorisés (coffre déverrouillé).</summary>
    public IReadOnlyCollection<string> Ids => _secrets.Keys;

    /// <summary>Crée un coffre local vide (remplace un éventuel coffre existant) et le laisse déverrouillé.</summary>
    public void Create(string password)
    {
        CheckPassword(password);
        Lock();
        SetKey(password, _kdf);
        Save();
    }

    /// <summary>Déverrouille le coffre ; <see cref="KeePassError.InvalidKey"/> si le mot de passe est faux.</summary>
    public void Unlock(string password)
    {
        Lock();
        var envelope = ReadEnvelope();
        _fileKdf = envelope.Kdf;
        _salt = envelope.Salt;
        var key = Derive(password, envelope.Salt, envelope.Kdf);
        var plain = new byte[envelope.Data.Length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(envelope.Nonce, envelope.Data, envelope.Tag, plain, AssociatedData(envelope.Kdf, envelope.Salt));
            ReadSecrets(plain);
            _key = new SecretBytes(key);
        }
        catch (AuthenticationTagMismatchException e)
        {
            throw new KeePassException(KeePassError.InvalidKey, CoreStrings.LocalStoreWrongPassword, e);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public void Lock()
    {
        _key?.Dispose();
        _key = null;
        foreach (var secret in _secrets.Values)
        {
            secret.Dispose();
        }

        _secrets.Clear();
    }

    /// <summary>Secret mémorisé, en UTF-8 : à effacer après usage. Null si absent.</summary>
    public byte[]? Get(string id)
    {
        EnsureUnlocked();
        return _secrets.TryGetValue(id, out var secret) ? secret.Reveal() : null;
    }

    public void Set(string id, ReadOnlySpan<byte> utf8Secret)
    {
        EnsureUnlocked();
        if (_secrets.Remove(id, out var old))
        {
            old.Dispose();
        }

        _secrets[id] = new SecretBytes(utf8Secret);
        Save();
    }

    public void Set(string id, string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(secret);
        try
        {
            Set(id, bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public void Remove(string id)
    {
        EnsureUnlocked();
        if (_secrets.Remove(id, out var old))
        {
            old.Dispose();
            Save();
        }
    }

    /// <summary>Nouveau mot de passe (et nouveau sel) ; le coffre doit être déverrouillé.</summary>
    public void ChangePassword(string newPassword)
    {
        CheckPassword(newPassword);
        EnsureUnlocked();
        _key!.Dispose();
        SetKey(newPassword, _kdf);
        Save();
    }

    /// <summary>Supprime le fichier du coffre local et oublie tout.</summary>
    public void Delete()
    {
        Lock();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    public void Dispose() => Lock();

    private static void CheckPassword(string password)
    {
        if (password.Length < MinPasswordLength)
        {
            throw new ArgumentException(string.Format(CultureInfo.CurrentCulture, CoreStrings.LocalStorePasswordTooShort, MinPasswordLength));
        }
    }

    private void EnsureUnlocked()
    {
        if (_key is null)
        {
            throw new InvalidOperationException(CoreStrings.LocalStoreLocked);
        }
    }

    private void SetKey(string password, KdfSettings kdf)
    {
        _salt = RandomNumberGenerator.GetBytes(32);
        _fileKdf = kdf;
        var key = Derive(password, _salt, kdf);
        _key = new SecretBytes(key);
        CryptographicOperations.ZeroMemory(key);
    }

    private static byte[] Derive(string password, byte[] salt, KdfSettings kdf)
    {
        var bytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon = new Argon2id(bytes)
            {
                Salt = salt,
                MemorySize = kdf.MemoryKiB,
                Iterations = kdf.Iterations,
                DegreeOfParallelism = kdf.Parallelism,
            };
            return argon.GetBytes(32);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>Les réglages de dérivation sont authentifiés avec les données : impossible de les affaiblir dans le fichier.</summary>
    private static byte[] AssociatedData(KdfSettings kdf, byte[] salt) =>
        Encoding.UTF8.GetBytes($"CyberArkTerm-local-store|{FormatVersion}|argon2id|{kdf.MemoryKiB}|{kdf.Iterations}|{kdf.Parallelism}|{Convert.ToBase64String(salt)}");

    private void Save()
    {
        var plain = WriteSecrets();
        var key = _key!.Reveal();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var data = new byte[plain.Length];
        try
        {
            using (var aes = new AesGcm(key, 16))
            {
                aes.Encrypt(nonce, plain, data, tag, AssociatedData(_fileKdf, _salt));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(new Envelope
        {
            Version = FormatVersion,
            Kdf = "argon2id",
            MemoryKiB = _fileKdf.MemoryKiB,
            Iterations = _fileKdf.Iterations,
            Parallelism = _fileKdf.Parallelism,
            Salt = Convert.ToBase64String(_salt),
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Data = Convert.ToBase64String(data),
        });
        var content = _protector?.Protect(json) ?? json;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temp = _path + ".tmp";
        File.WriteAllBytes(temp, content);
        File.Move(temp, _path, overwrite: true);
    }

    private (KdfSettings Kdf, byte[] Salt, byte[] Nonce, byte[] Tag, byte[] Data) ReadEnvelope()
    {
        if (!File.Exists(_path))
        {
            throw new FileNotFoundException(CoreStrings.LocalStoreMissing, _path);
        }

        try
        {
            var content = File.ReadAllBytes(_path);
            var json = _protector?.Unprotect(content) ?? content;
            var e = JsonSerializer.Deserialize<Envelope>(json) ?? throw new FormatException();
            if (e.Version != FormatVersion || e.Kdf != "argon2id" || e.MemoryKiB is < 8 * 1024 or > 4 * 1024 * 1024
                || e.Iterations is < 1 or > 100 || e.Parallelism is < 1 or > 64)
            {
                throw new FormatException();
            }

            return (new KdfSettings(e.MemoryKiB, e.Iterations, e.Parallelism), Convert.FromBase64String(e.Salt),
                Convert.FromBase64String(e.Nonce), Convert.FromBase64String(e.Tag), Convert.FromBase64String(e.Data));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException or ArgumentException)
        {
            throw new KeePassException(KeePassError.Corrupted, CoreStrings.LocalStoreDamaged, ex);
        }
    }

    /// <summary>{"id": "secret", ...} en UTF-8, sans passer par des chaînes .NET pour les secrets.</summary>
    private byte[] WriteSecrets()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (id, secret) in _secrets)
            {
                var bytes = secret.Reveal();
                writer.WriteString(id, bytes);
                CryptographicOperations.ZeroMemory(bytes);
            }

            writer.WriteEndObject();
        }

        var result = buffer.WrittenSpan.ToArray();
        // Clear() remet à zéro les octets écrits.
        buffer.Clear();
        return result;
    }

    private void ReadSecrets(byte[] plain)
    {
        var reader = new Utf8JsonReader(plain);
        reader.Read();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var id = reader.GetString()!;
            reader.Read();
            var bytes = new byte[reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length];
            int length = reader.CopyString(bytes);
            _secrets[id] = new SecretBytes(bytes.AsSpan(0, length));
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>Coût d'Argon2id.</summary>
    public sealed record KdfSettings(int MemoryKiB, int Iterations, int Parallelism)
    {
        public static KdfSettings Default { get; } = new(64 * 1024, 3, 2);
    }

    private sealed class Envelope
    {
        public int Version { get; set; }

        public string Kdf { get; set; } = "";

        public int MemoryKiB { get; set; }

        public int Iterations { get; set; }

        public int Parallelism { get; set; }

        public string Salt { get; set; } = "";

        public string Nonce { get; set; } = "";

        public string Tag { get; set; } = "";

        public string Data { get; set; } = "";
    }
}
