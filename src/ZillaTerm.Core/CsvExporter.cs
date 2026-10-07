using System.Globalization;
using ZillaTerm.Core.Localization;

namespace ZillaTerm.Core;

/// <summary>
/// Export CSV lisible par Excel : séparateur de liste de la région Windows (« ; » en France et en Italie,
/// « , » aux États-Unis), en-têtes dans la langue de l'interface.
/// </summary>
public static class CsvExporter
{
    public static void Write(TextWriter writer, IEnumerable<PvwaAccount> accounts, char? separator = null)
    {
        char sep = separator ?? DefaultSeparator(CultureInfo.CurrentCulture);
        WriteLine(writer, sep,
        [
            CoreStrings.ColumnServer, CoreStrings.ColumnUser, CoreStrings.ColumnDomain, CoreStrings.ColumnPlatform,
            CoreStrings.ColumnSafe, CoreStrings.ColumnName, CoreStrings.ColumnAllowedMachines, CoreStrings.ColumnCreated, "ID",
        ]);
        foreach (var a in accounts)
        {
            WriteLine(writer, sep,
            [
                a.Address ?? "",
                a.UserName ?? "",
                a.LogonDomain,
                a.PlatformId ?? "",
                a.SafeName ?? "",
                a.Name ?? "",
                a.RemoteMachines,
                a.Created?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "",
                a.Id,
            ]);
        }
    }

    /// <summary>
    /// Séparateur de liste de la culture (celui qu'utilise Excel) ; « ; » s'il n'est pas d'un seul caractère, ou si c'est
    /// une lettre, un chiffre, un espace, un guillemet ou un saut de ligne.
    /// </summary>
    public static char DefaultSeparator(CultureInfo culture) =>
        culture.TextInfo.ListSeparator is { Length: 1 } s && IsUsableSeparator(s[0]) ? s[0] : ';';

    private static bool IsUsableSeparator(char c) =>
        c == '\t' || !(char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || char.IsControl(c) || c is '"' or '\'');

    internal static void WriteLine(TextWriter writer, char separator, IReadOnlyList<string> values)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                writer.Write(separator);
            }

            writer.Write(Escape(values[i], separator));
        }

        writer.Write("\r\n");
    }

    internal static string Escape(string value, char separator = ';')
    {
        // Empêche l'interprétation comme formule par Excel (injection CSV).
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        // Guillemets si la valeur contient le séparateur utilisé ou un séparateur courant (« ; », « , »), un guillemet ou
        // un saut de ligne.
        if (value.IndexOfAny([separator, ';', ',', '"', '\r', '\n']) >= 0)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
