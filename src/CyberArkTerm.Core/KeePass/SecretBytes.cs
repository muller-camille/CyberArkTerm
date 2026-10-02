using System.Security.Cryptography;

namespace CyberArkTerm.Core.KeePass;

/// <summary>
/// Octets secrets (clé, mot de passe) gardés masqués en mémoire par un masque aléatoire, et effacés à la libération.
/// Le masque évite qu'un secret apparaisse en clair dans un vidage mémoire ; il ne protège pas d'un débogueur.
/// </summary>
internal sealed class SecretBytes : IDisposable
{
    private readonly byte[] _mask;
    private readonly byte[] _masked;

    public SecretBytes(ReadOnlySpan<byte> value)
    {
        _mask = RandomNumberGenerator.GetBytes(value.Length);
        _masked = new byte[value.Length];
        for (int i = 0; i < value.Length; i++)
        {
            _masked[i] = (byte)(value[i] ^ _mask[i]);
        }
    }

    public int Length => _masked.Length;

    /// <summary>Valeur en clair : à effacer (<see cref="CryptographicOperations.ZeroMemory"/>) après usage.</summary>
    public byte[] Reveal()
    {
        var value = new byte[_masked.Length];
        for (int i = 0; i < value.Length; i++)
        {
            value[i] = (byte)(_masked[i] ^ _mask[i]);
        }

        return value;
    }

    public SecretBytes Clone()
    {
        var value = Reveal();
        try
        {
            return new SecretBytes(value);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(value);
        }
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_mask);
        CryptographicOperations.ZeroMemory(_masked);
    }
}
