namespace ZillaTerm.Core.Ssh;

/// <summary>
/// Changement de droits : bits à ajouter et bits à retirer, les autres restent tels quels (comme <c>chmod g+w,o-x</c>).
/// Une sélection de plusieurs éléments aux droits différents ne reçoit ainsi que ce qui a été changé, pas les droits
/// du premier élément.
/// </summary>
public readonly record struct PermissionChange(int Set, int Clear)
{
    private const int ExecuteBits = 0x49;

    /// <summary>Droits absolus (<c>chmod 755</c>) ; les bits spéciaux seulement si <paramref name="includeSpecial"/>.</summary>
    public static PermissionChange Absolute(int mode, bool includeSpecial)
    {
        int scope = UnixPermissions.RwxMask | (includeSpecial ? UnixPermissions.SpecialMask : 0);
        return new PermissionChange(mode & scope, ~mode & scope);
    }

    /// <summary>Les 9 bits rwx sont tous fixés : le résultat ne dépend pas des droits actuels.</summary>
    public bool CoversAllRwx => ((Set | Clear) & UnixPermissions.RwxMask) == UnixPermissions.RwxMask;

    /// <summary>Le même changement sans les bits spéciaux : contenu d'un dossier (comme <c>chmod -R</c>, setuid n'y est pas propagé).</summary>
    public PermissionChange RwxOnly => new(Set & UnixPermissions.RwxMask, Clear & UnixPermissions.RwxMask);

    public int Apply(int mode) => (mode & ~Clear) | Set;

    /// <summary>
    /// Droits d'un élément du contenu d'un dossier : avec <paramref name="executeOnlyIfAlready"/>, un fichier qui n'était
    /// exécutable par personne ne le devient pas (les dossiers, eux, gardent x pour rester ouvrables).
    /// </summary>
    public int ApplyToContent(int mode, bool isDirectory, bool executeOnlyIfAlready)
    {
        int target = RwxOnly.Apply(mode);
        return executeOnlyIfAlready && !isDirectory && (mode & ExecuteBits) == 0 ? target & ~ExecuteBits : target;
    }

    /// <summary>Notation de chmod : <c>u+x,g-w</c> (bits rwx), <c>+t</c> / <c>-s</c> (bits spéciaux).</summary>
    public string ToSymbolic()
    {
        var parts = new List<string>();
        foreach (var (who, shift) in new[] { ("u", 6), ("g", 3), ("o", 0) })
        {
            string Letters(int bits) => string.Concat("rwx".Where((_, i) => (bits & (4 >> i)) != 0));
            var add = Letters((Set >> shift) & 7);
            var remove = Letters((Clear >> shift) & 7);
            var part = (add.Length > 0 ? "+" + add : "") + (remove.Length > 0 ? "-" + remove : "");
            if (part.Length > 0)
            {
                parts.Add(who + part);
            }
        }

        int set = Set, clear = Clear;
        void Special(int bit, string name)
        {
            if ((set & bit) != 0)
            {
                parts.Add("+" + name);
            }
            else if ((clear & bit) != 0)
            {
                parts.Add("-" + name);
            }
        }

        Special(UnixPermissions.SetUid, "setuid");
        Special(UnixPermissions.SetGid, "setgid");
        Special(UnixPermissions.Sticky, "sticky");
        return string.Join(",", parts);
    }
}
