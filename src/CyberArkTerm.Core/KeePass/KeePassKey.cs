using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using CyberArkTerm.Core.Localization;

namespace CyberArkTerm.Core.KeePass;

/// <summary>
/// Clé maîtresse d'un coffre KeePass : mot de passe et/ou fichier clé. Seule l'empreinte composite (SHA-256)
/// est gardée, masquée en mémoire ; ni le mot de passe ni le fichier clé ne sont conservés.
/// </summary>
public sealed class KeePassKey : IDisposable
{
    /// <summary>Taille maximale d'un fichier clé (n'importe quel fichier peut servir de clé).</summary>
    public const int MaxKeyFileSize = 64 * 1024 * 1024;

    private readonly SecretBytes _composite;

    private KeePassKey(SecretBytes composite) => _composite = composite;

    /// <summary>
    /// Clé à partir d'un mot de passe et/ou du contenu d'un fichier clé. Un mot de passe vide accompagné d'un fichier
    /// clé est ignoré (coffre protégé par le seul fichier clé).
    /// </summary>
    public static KeePassKey Create(string? password, byte[]? keyFile)
    {
        bool usePassword = password is not null && (password.Length > 0 || keyFile is null);
        if (!usePassword && keyFile is null)
        {
            throw new ArgumentException("Mot de passe ou fichier clé requis.");
        }

        var parts = new List<byte[]>();
        try
        {
            if (usePassword)
            {
                var bytes = Encoding.UTF8.GetBytes(password!);
                parts.Add(SHA256.HashData(bytes));
                CryptographicOperations.ZeroMemory(bytes);
            }

            if (keyFile is not null)
            {
                parts.Add(KeyFileData(keyFile));
            }

            var all = parts.SelectMany(p => p).ToArray();
            var composite = SHA256.HashData(all);
            CryptographicOperations.ZeroMemory(all);
            try
            {
                return new KeePassKey(new SecretBytes(composite));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(composite);
            }
        }
        finally
        {
            foreach (var part in parts)
            {
                CryptographicOperations.ZeroMemory(part);
            }
        }
    }

    /// <summary>Clé à partir d'un mot de passe et/ou d'un chemin de fichier clé.</summary>
    public static KeePassKey Create(string? password, string? keyFilePath)
    {
        byte[]? keyFile = null;
        if (!string.IsNullOrEmpty(keyFilePath))
        {
            var info = new FileInfo(keyFilePath);
            if (info.Length > MaxKeyFileSize)
            {
                throw new KeePassException(KeePassError.InvalidKeyFile, CoreStrings.KeePassKeyFileTooLarge);
            }

            keyFile = File.ReadAllBytes(keyFilePath);
        }

        try
        {
            return Create(password, keyFile);
        }
        finally
        {
            if (keyFile is not null)
            {
                CryptographicOperations.ZeroMemory(keyFile);
            }
        }
    }

    public void Dispose() => _composite.Dispose();

    /// <summary>Empreinte composite en clair (32 octets) : à effacer après usage.</summary>
    internal byte[] RevealComposite() => _composite.Reveal();

    /// <summary>
    /// Données d'un fichier clé, comme KeePass : fichier XML (version 1.0 en base64, 2.0 en hexadécimal avec somme
    /// de contrôle), 32 octets bruts, 64 caractères hexadécimaux, ou sinon l'empreinte SHA-256 du fichier.
    /// </summary>
    internal static byte[] KeyFileData(byte[] content)
    {
        if (TryXmlKeyFile(content) is { } xml)
        {
            return xml;
        }

        if (content.Length == 32)
        {
            return (byte[])content.Clone();
        }

        if (content.Length == 64 && content.All(b => Uri.IsHexDigit((char)b)))
        {
            return Convert.FromHexString(Encoding.ASCII.GetString(content));
        }

        return SHA256.HashData(content);
    }

    private static byte[]? TryXmlKeyFile(byte[] content)
    {
        // Rapide : un fichier XML commence par « < » (après un éventuel BOM et des espaces).
        var start = content.AsSpan();
        if (start.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            start = start[3..];
        }

        start = start.TrimStart(" \t\r\n"u8);
        if (start.IsEmpty || start[0] != (byte)'<')
        {
            return null;
        }

        string? version = null;
        string? data = null;
        string? hash = null;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 };
            using var reader = XmlReader.Create(new MemoryStream(content), settings);
            var path = new List<string>();
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    path.RemoveRange(reader.Depth, path.Count - reader.Depth);
                    path.Add(reader.Name);
                    var where = string.Join('/', path);
                    if (where == "KeyFile/Meta/Version")
                    {
                        version = reader.ReadElementContentAsString().Trim();
                    }
                    else if (where == "KeyFile/Key/Data")
                    {
                        hash = reader.GetAttribute("Hash");
                        data = reader.ReadElementContentAsString();
                    }
                }
            }
        }
        catch (XmlException)
        {
            return null;
        }

        if (data is null || version is null)
        {
            return null;
        }

        if (version.StartsWith("1.", StringComparison.Ordinal))
        {
            try
            {
                return Convert.FromBase64String(data.Trim());
            }
            catch (FormatException ex)
            {
                throw new KeePassException(KeePassError.InvalidKeyFile, CoreStrings.KeePassKeyFileDamaged, ex);
            }
        }

        if (version.StartsWith("2.", StringComparison.Ordinal))
        {
            var hex = new string(data.Where(c => !char.IsWhiteSpace(c)).ToArray());
            byte[] key;
            try
            {
                key = Convert.FromHexString(hex);
            }
            catch (FormatException ex)
            {
                throw new KeePassException(KeePassError.InvalidKeyFile, CoreStrings.KeePassKeyFileDamaged, ex);
            }

            if (hash is not null && !Convert.ToHexString(SHA256.HashData(key).AsSpan(0, 4)).Equals(hash.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new KeePassException(KeePassError.InvalidKeyFile, CoreStrings.KeePassKeyFileDamaged);
            }

            return key;
        }

        throw new KeePassException(KeePassError.InvalidKeyFile,
            string.Format(CultureInfo.CurrentCulture, CoreStrings.KeePassKeyFileVersion, version));
    }
}
