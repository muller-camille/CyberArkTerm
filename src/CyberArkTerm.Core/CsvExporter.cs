using System.Globalization;

namespace CyberArkTerm.Core;

/// <summary>
/// Export CSV au format attendu par un Excel français (séparateur « ; »).
/// </summary>
public static class CsvExporter
{
    private const char Separator = ';';

    private static readonly string[] Header =
        ["Serveur", "Utilisateur", "Domaine", "Plateforme", "Safe", "Nom", "Machines autorisées", "Créé le", "ID"];

    public static void Write(TextWriter writer, IEnumerable<PvwaAccount> accounts)
    {
        WriteLine(writer, Header);
        foreach (var a in accounts)
        {
            WriteLine(writer,
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

    private static void WriteLine(TextWriter writer, IReadOnlyList<string> values)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                writer.Write(Separator);
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

        if (value.IndexOfAny([Separator, '"', '\r', '\n']) >= 0)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
