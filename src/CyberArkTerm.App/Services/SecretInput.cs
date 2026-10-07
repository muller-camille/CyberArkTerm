using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Controls;

namespace CyberArkTerm.App.Services;

/// <summary>
/// Mot de passe saisi dans un <see cref="PasswordBox"/>, lu sans passer par une chaîne .NET (qui resterait en mémoire
/// jusqu'au ramasse-miettes) : tableau à effacer par l'appelant après usage.
/// </summary>
internal static class SecretInput
{
    public static char[] Read(PasswordBox box)
    {
        using var secure = box.SecurePassword;
        var chars = new char[secure.Length];
        var pointer = Marshal.SecureStringToGlobalAllocUnicode(secure);
        try
        {
            Marshal.Copy(pointer, chars, 0, chars.Length);
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(pointer);
        }

        return chars;
    }

    /// <summary>Mot de passe en UTF-8 (clé KeePass, coffre local).</summary>
    public static byte[] ReadUtf8(PasswordBox box)
    {
        var chars = Read(box);
        try
        {
            var bytes = new byte[Encoding.UTF8.GetByteCount(chars)];
            Encoding.UTF8.GetBytes(chars, bytes);
            return bytes;
        }
        finally
        {
            Clear(chars);
        }
    }

    public static void Clear(char[] chars) => CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(chars.AsSpan()));
}
