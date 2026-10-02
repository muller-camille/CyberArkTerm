using Renci.SshNet.Sftp;

namespace CyberArkTerm.Core.Ssh;

/// <summary>
/// Droits Unix : 9 bits rwx (propriétaire, groupe, autres) et 3 bits spéciaux (setuid, setgid, sticky),
/// en notation symbolique (<c>rwsr-xr-t</c>) et octale (<c>644</c>, <c>1777</c>).
/// </summary>
public static class UnixPermissions
{
    /// <summary>Bits rwx seuls.</summary>
    public const int RwxMask = 0x1FF;

    /// <summary>Bits spéciaux seuls (setuid, setgid, sticky).</summary>
    public const int SpecialMask = 0xE00;

    public const int SetUid = 0x800;
    public const int SetGid = 0x400;
    public const int Sticky = 0x200;

    /// <summary>Bits rwx dans l'ordre d'affichage : propriétaire r w x, groupe r w x, autres r w x.</summary>
    public static IReadOnlyList<int> Bits { get; } = [0x100, 0x80, 0x40, 0x20, 0x10, 0x08, 0x04, 0x02, 0x01];

    /// <summary>
    /// <c>rwxr-xr-x</c> ou <c>drwxr-xr-x</c> (type en tête) → 0755. En position x : « s » = setuid ou setgid et x,
    /// « S » = setuid ou setgid sans x, « t » = sticky et x, « T » = sticky sans x (comme <c>ls -l</c>).
    /// </summary>
    public static int FromSymbolic(string text)
    {
        var rwx = text.Length >= 10 ? text[^9..] : text;
        int mode = 0;
        for (int i = 0; i < Math.Min(rwx.Length, 9); i++)
        {
            char c = rwx[i];
            if (c is 'r' or 'w' or 'x' or 's' or 't')
            {
                mode |= Bits[i];
            }

            if (c is 's' or 'S' && i == 2)
            {
                mode |= SetUid;
            }
            else if (c is 's' or 'S' && i == 5)
            {
                mode |= SetGid;
            }
            else if (c is 't' or 'T' && i == 8)
            {
                mode |= Sticky;
            }
        }

        return mode;
    }

    public static string ToSymbolic(int mode)
    {
        var chars = Enumerable.Range(0, 9).Select(i => (mode & Bits[i]) != 0 ? "rwx"[i % 3] : '-').ToArray();
        chars[2] = Special(chars[2], (mode & SetUid) != 0, 's');
        chars[5] = Special(chars[5], (mode & SetGid) != 0, 's');
        chars[8] = Special(chars[8], (mode & Sticky) != 0, 't');
        return new string(chars);
    }

    /// <summary>Trois chiffres (<c>644</c>), ou quatre si un bit spécial est présent (<c>1777</c>, <c>4755</c>).</summary>
    public static string ToOctal(int mode)
    {
        mode &= RwxMask | SpecialMask;
        return Convert.ToString(mode, 8).PadLeft((mode & SpecialMask) != 0 ? 4 : 3, '0');
    }

    /// <summary>Accepte trois chiffres (<c>644</c>) ou quatre, le premier pour les bits spéciaux (<c>1777</c>, <c>0644</c>).</summary>
    public static bool TryParseOctal(string text, out int mode)
    {
        mode = 0;
        var digits = text.Trim();
        if (digits.Length is not (3 or 4) || digits.Any(c => c is < '0' or > '7'))
        {
            return false;
        }

        foreach (var c in digits)
        {
            mode = (mode << 3) | (c - '0');
        }

        return true;
    }

    /// <summary>Droits (12 bits) d'un fichier distant.</summary>
    public static int FromAttributes(SftpFileAttributes a)
    {
        bool[] rwx =
        [
            a.OwnerCanRead, a.OwnerCanWrite, a.OwnerCanExecute,
            a.GroupCanRead, a.GroupCanWrite, a.GroupCanExecute,
            a.OthersCanRead, a.OthersCanWrite, a.OthersCanExecute,
        ];
        int mode = Enumerable.Range(0, 9).Where(i => rwx[i]).Sum(i => Bits[i]);
        return mode | (a.IsUIDBitSet ? SetUid : 0) | (a.IsGroupIDBitSet ? SetGid : 0) | (a.IsStickyBitSet ? Sticky : 0);
    }

    private static char Special(char current, bool set, char letter) =>
        !set ? current : current == 'x' ? letter : char.ToUpperInvariant(letter);
}
