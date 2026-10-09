using System.Globalization;

namespace ZillaTerm.Core.Ssh;

/// <summary>
/// Noms du propriétaire et du groupe lus dans la ligne « longname » qu'un serveur SFTP version 3 envoie pour chaque
/// fichier d'une liste, au format de <c>ls -l</c> : <c>-rw-r--r--    1 oracle   dba    1024 Oct  9 14:31 nom</c>.
/// </summary>
/// <remarks>
/// Le protocole ne fixe pas ce format, et un nom peut contenir une espace (« Domain Users ») : la ligne n'est retenue
/// que si elle a la forme attendue (droits, nombre de liens, propriétaire, groupe) et que la taille qu'elle montre au
/// cinquième champ est celle des attributs du fichier. Sinon, null : on garde les numéros (UID et GID).
/// </remarks>
public static class SftpLongName
{
    public static (string Owner, string Group)? Parse(string? longName, long size)
    {
        if (string.IsNullOrEmpty(longName))
        {
            return null;
        }

        var fields = longName.Split((char[]?)null, 6, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 6 || !LooksLikeMode(fields[0]) || !IsNumber(fields[1]) ||
            fields[4] != size.ToString(CultureInfo.InvariantCulture))
        {
            return null;
        }

        var owner = RemoteEntry.CleanName(fields[2]);
        var group = RemoteEntry.CleanName(fields[3]);
        return owner.Length > 0 && group.Length > 0 ? (owner, group) : null;
    }

    /// <summary>Type puis neuf droits (<c>drwxr-x---</c>), suivis éventuellement d'une marque d'ACL (« + », « @ », « . »).</summary>
    private static bool LooksLikeMode(string field) =>
        field.Length is 10 or 11 && "-dlcbps".Contains(field[0]) && field.Skip(1).Take(9).All(c => "-rwxsStTl".Contains(c));

    private static bool IsNumber(string field) => field.All(char.IsAsciiDigit);
}
