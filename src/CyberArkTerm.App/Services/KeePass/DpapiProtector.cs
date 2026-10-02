using System.Security.Cryptography;
using CyberArkTerm.Core.KeePass;

namespace CyberArkTerm.App.Services.KeePass;

/// <summary>
/// DPAPI (compte Windows courant) : le fichier du coffre local est inutilisable sur un autre compte ou poste.
/// <see cref="ProtectedData"/> fait partie du framework Windows Desktop de .NET.
/// </summary>
internal sealed class DpapiProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "CyberArkTerm.LocalSecretStore.v1"u8.ToArray();

    public byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}
