using System.Security.Cryptography;
using ZillaTerm.Core.KeePass;

namespace ZillaTerm.App.Services.KeePass;

/// <summary>
/// DPAPI (compte Windows courant) : le fichier du coffre local est inutilisable sur un autre compte ou poste.
/// <see cref="ProtectedData"/> fait partie du framework Windows Desktop de .NET.
/// </summary>
internal sealed class DpapiProtector : ISecretProtector
{
    // Ancien nom de l'application gardé : les secrets déjà protégés doivent rester lisibles.
    private static readonly byte[] LocalStoreEntropy = "CyberArkTerm.LocalSecretStore.v1"u8.ToArray();

    private readonly byte[] _entropy;

    /// <summary>Protection du coffre local.</summary>
    public DpapiProtector()
        : this(LocalStoreEntropy)
    {
    }

    /// <param name="entropy">Distingue les usages : un bloc d'un usage ne se déchiffre pas avec un autre.</param>
    public DpapiProtector(byte[] entropy) => _entropy = entropy;

    public byte[] Protect(byte[] data) => ProtectedData.Protect(data, _entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, _entropy, DataProtectionScope.CurrentUser);
}
