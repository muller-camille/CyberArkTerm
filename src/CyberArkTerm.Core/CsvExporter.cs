using System.Globalization;
using CyberArkTerm.Core.Localization;

namespace CyberArkTerm.Core;

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

    /// <summary>Séparateur de liste de la culture (celui qu'utilise Excel), « ; » s'il n'est pas d'un seul caractère.</summary>
    public static char DefaultSeparator(CultureInfo culture) =>
        culture.TextInfo.ListSeparator is { Length: 1 } s && s[0] is not ('"' or '\r' or '\n') ? s[0] : ';';

    internal static void WriteLine(TextWriter writer, char separator, IReadOnlyList<string> values)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                writer.Write(separator);
            }

            writer.Write(Escape(values[i]));
        }

        writer.Write("\r\n");
    }

    internal static string Escape(string value)
    {
        // Empêche l'interprétation comme formule par Excel (injection CSV).
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        // Guillemets si la valeur contient un séparateur possible (« ; » ou « , »), un guillemet ou un saut de ligne.
        if (value.IndexOfAny([';', ',', '"', '\r', '\n']) >= 0)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
