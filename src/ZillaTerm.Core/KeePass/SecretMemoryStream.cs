using System.Security.Cryptography;

namespace ZillaTerm.Core.KeePass;

/// <summary>
/// Flux en mémoire pour des données déchiffrées : chaque tampon abandonné quand le flux grandit est effacé, et le
/// dernier l'est à la libération. <see cref="MemoryStream.ToArray"/> rend une copie que l'appelant efface lui-même.
/// </summary>
internal sealed class SecretMemoryStream(int capacity = 0) : MemoryStream(capacity)
{
    public override int Capacity
    {
        get => base.Capacity;
        set
        {
            var old = GetBuffer();
            base.Capacity = value;
            if (!ReferenceEquals(old, GetBuffer()))
            {
                CryptographicOperations.ZeroMemory(old);
            }
        }
    }

    /// <summary>Contenu, puis tampon effacé et flux libéré.</summary>
    public byte[] ToArrayAndClear()
    {
        var result = ToArray();
        Dispose();
        return result;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && CanRead)
        {
            CryptographicOperations.ZeroMemory(GetBuffer());
        }

        base.Dispose(disposing);
    }
}
