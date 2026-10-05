using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CyberArkTerm.Core.Localization;

namespace CyberArkTerm.Core;

/// <summary>Ligne d'un fichier d'import : le compte à créer, ou l'erreur qui empêche de le créer.</summary>
public sealed class ImportRow
{
    /// <summary>Numéro de ligne dans le fichier (1 = en-tête).</summary>
    public int Line { get; init; }

    public string Safe { get; init; } = "";

    public string Platform { get; init; } = "";

    public string Address { get; init; } = "";

    public string UserName { get; init; } = "";

    public string Name { get; init; } = "";

    public string LogonDomain { get; init; } = "";

    public bool HasPassword { get; init; }

    /// <summary>Compte prêt à créer ; null si la ligne est incomplète (voir <see cref="Error"/>).</summary>
    public NewAccount? Account { get; init; }

    public string? Error { get; init; }
}

/// <summary>Contenu d'un fichier d'import ; <see cref="Clear"/> efface les mots de passe lus.</summary>
public sealed class AccountImport
{
    public List<ImportRow> Rows { get; } = [];

    /// <summary>Erreur qui empêche tout l'import (en-tête incomplet, fichier vide...).</summary>
    public string? Error { get; init; }

    public int Ready => Rows.Count(r => r.Account is not null);

    public void Clear()
    {
        foreach (var row in Rows)
        {
            if (row.Account?.Secret is { } secret)
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(secret.AsSpan()));
            }
        }
    }
}

/// <summary>
/// Lecture d'un fichier CSV de comptes à créer : séparateur « ; », « , » ou tabulation (détecté sur l'en-tête),
/// guillemets à la manière d'Excel, en-têtes en français, anglais ou italien (ceux de l'export sont reconnus). La
/// colonne mot de passe, facultative, est lue dans des tableaux de caractères, sans passer par des chaînes.
/// </summary>
public static class AccountCsv
{
    /// <summary>Nombre maximal de lignes de comptes dans un fichier.</summary>
    public const int MaxRows = 5000;

    private enum Column { Safe, Platform, Address, UserName, Name, LogonDomain, Password, Machines, Cpm, Reason }

    /// <summary>Noms acceptés pour chaque colonne (comparés sans casse, espaces, tirets ni accents).</summary>
    private static readonly Dictionary<string, Column> Names = new()
    {
        ["safe"] = Column.Safe, ["safename"] = Column.Safe, ["coffre"] = Column.Safe,
        ["platform"] = Column.Platform, ["platformid"] = Column.Platform, ["plateforme"] = Column.Platform, ["piattaforma"] = Column.Platform,
        ["address"] = Column.Address, ["adresse"] = Column.Address, ["indirizzo"] = Column.Address, ["server"] = Column.Address,
        ["serveur"] = Column.Address, ["host"] = Column.Address, ["hote"] = Column.Address,
        ["username"] = Column.UserName, ["user"] = Column.UserName, ["utilisateur"] = Column.UserName, ["utente"] = Column.UserName,
        ["login"] = Column.UserName,
        ["name"] = Column.Name, ["accountname"] = Column.Name, ["nom"] = Column.Name, ["nomducompte"] = Column.Name, ["nome"] = Column.Name,
        ["logondomain"] = Column.LogonDomain, ["domain"] = Column.LogonDomain, ["domaine"] = Column.LogonDomain,
        ["dominio"] = Column.LogonDomain,
        ["password"] = Column.Password, ["secret"] = Column.Password, ["motdepasse"] = Column.Password, ["mdp"] = Column.Password,
        ["remotemachines"] = Column.Machines, ["allowedmachines"] = Column.Machines, ["machines"] = Column.Machines,
        ["machinesautorisees"] = Column.Machines, ["macchineautorizzate"] = Column.Machines, ["macchineconsentite"] = Column.Machines,
        ["cpm"] = Column.Cpm, ["automaticmanagement"] = Column.Cpm, ["automaticmanagementenabled"] = Column.Cpm,
        ["gestionautomatique"] = Column.Cpm, ["gestioneautomatica"] = Column.Cpm,
        ["manualmanagementreason"] = Column.Reason, ["reason"] = Column.Reason, ["motif"] = Column.Reason, ["motivo"] = Column.Reason,
    };

    /// <summary>Modèle de fichier : en-tête et un exemple fictif.</summary>
    public static string Template(char separator) => string.Join(separator, "safe", "platform", "address", "userName", "name", "logonDomain",
            "password", "remoteMachines", "cpm", "manualManagementReason") + "\r\n"
        + string.Join(separator, "Prod-Windows", "WinDomain", "srv-app01.corp.example", "svc_app01", "", "CORP", "", "", "yes", "") + "\r\n";

    /// <summary>
    /// Texte d'un fichier : UTF-8 (avec ou sans BOM), UTF-16 avec BOM, sinon la page de code ANSI de la région Windows
    /// (CSV « ANSI » d'Excel : Windows-1252 en français, anglais et italien, où « € » ou « ’ » ne sont pas du Latin-1).
    /// Le résultat est un tableau que l'appelant efface après lecture.
    /// </summary>
    public static char[] Decode(ReadOnlySpan<byte> bytes)
    {
        Encoding encoding;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            bytes = bytes[3..];
            encoding = Encoding.UTF8;
        }
        else if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            bytes = bytes[2..];
            encoding = Encoding.Unicode;
        }
        else
        {
            encoding = new UTF8Encoding(false, throwOnInvalidBytes: true);
            try
            {
                encoding.GetCharCount(bytes);
            }
            catch (DecoderFallbackException)
            {
                encoding = AnsiEncoding();
            }
        }

        var chars = new char[encoding.GetCharCount(bytes)];
        encoding.GetChars(bytes, chars);
        return chars;
    }

    private static Encoding AnsiEncoding()
    {
        int codePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
        return CodePagesEncodingProvider.Instance.GetEncoding(codePage is 0 or 65001 ? 1252 : codePage)
            ?? CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
    }

    /// <summary>
    /// Lit les comptes de <paramref name="text"/>. Le safe et la plateforme par défaut servent quand la colonne est
    /// absente ou vide. Les lignes vides sont ignorées ; une ligne incomplète est gardée avec son erreur.
    /// </summary>
    public static AccountImport Parse(ReadOnlySpan<char> text, string? defaultSafe, string? defaultPlatform)
    {
        // Contenu des champs décodé dans un tampon effacé à la fin (il peut contenir des mots de passe).
        var scratch = new char[text.Length];
        try
        {
            var records = Records(text, scratch, out var unclosed);
            if (unclosed)
            {
                return new AccountImport { Error = CoreStrings.ImportUnclosedQuote };
            }

            if (records.Count == 0)
            {
                return new AccountImport { Error = CoreStrings.ImportNoRow };
            }

            var header = records[0];
            var columns = new Dictionary<Column, int>();
            for (int i = 0; i < header.Fields.Count; i++)
            {
                if (Names.TryGetValue(Normalize(Field(scratch, header.Fields[i])), out var column))
                {
                    columns.TryAdd(column, i);
                }
            }

            var headerError = Missing(columns, Column.Address, CoreStrings.ImportColumnAddress)
                ?? Missing(columns, Column.UserName, CoreStrings.ImportColumnUser)
                ?? (string.IsNullOrWhiteSpace(defaultSafe) ? Missing(columns, Column.Safe, CoreStrings.ImportColumnSafe) : null)
                ?? (string.IsNullOrWhiteSpace(defaultPlatform) ? Missing(columns, Column.Platform, CoreStrings.ImportColumnPlatform) : null);
            if (headerError is not null)
            {
                return new AccountImport { Error = headerError };
            }

            if (records.Count - 1 > MaxRows)
            {
                return new AccountImport { Error = Format(CoreStrings.ImportTooManyRows, MaxRows) };
            }

            var import = new AccountImport();
            foreach (var record in records.Skip(1))
            {
                if (record.Fields.All(f => Field(scratch, f).Trim().Length == 0))
                {
                    continue;
                }

                import.Rows.Add(ReadRow(record, columns, scratch, defaultSafe, defaultPlatform));
            }

            return import.Rows.Count == 0 ? new AccountImport { Error = CoreStrings.ImportNoRow } : import;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(scratch.AsSpan()));
        }
    }

    private static ImportRow ReadRow(Record record, Dictionary<Column, int> columns, char[] scratch, string? defaultSafe, string? defaultPlatform)
    {
        string Text(Column column) =>
            columns.TryGetValue(column, out var i) && i < record.Fields.Count ? Unescape(Field(scratch, record.Fields[i]).Trim()).ToString() : "";

        var safe = Text(Column.Safe) is { Length: > 0 } s ? s : defaultSafe?.Trim() ?? "";
        var platform = Text(Column.Platform) is { Length: > 0 } p ? p : defaultPlatform?.Trim() ?? "";
        var address = Text(Column.Address);
        var user = Text(Column.UserName);
        var cpmText = Text(Column.Cpm);
        bool? cpm = cpmText.Length == 0 ? true : ParseBoolean(cpmText);
        var password = columns.TryGetValue(Column.Password, out var pi) && pi < record.Fields.Count
            ? Field(scratch, record.Fields[pi])
            : [];

        string? error = safe.Length == 0 ? Format(CoreStrings.ImportMissingValue, CoreStrings.ImportColumnSafe)
            : platform.Length == 0 ? Format(CoreStrings.ImportMissingValue, CoreStrings.ImportColumnPlatform)
            : address.Length == 0 ? Format(CoreStrings.ImportMissingValue, CoreStrings.ImportColumnAddress)
            : user.Length == 0 ? Format(CoreStrings.ImportMissingValue, CoreStrings.ImportColumnUser)
            : cpm is null ? Format(CoreStrings.ImportBadBoolean, cpmText, CoreStrings.ImportColumnCpm)
            : null;

        return new ImportRow
        {
            Line = record.Line,
            Safe = safe,
            Platform = platform,
            Address = address,
            UserName = user,
            Name = Text(Column.Name),
            LogonDomain = Text(Column.LogonDomain),
            HasPassword = password.Length > 0,
            Error = error,
            Account = error is not null ? null : new NewAccount
            {
                SafeName = safe,
                PlatformId = platform,
                Address = address,
                UserName = user,
                Name = Text(Column.Name),
                LogonDomain = Text(Column.LogonDomain),
                RemoteMachines = Text(Column.Machines),
                AutomaticManagement = cpm!.Value,
                ManualManagementReason = Text(Column.Reason),
                // Copié tel quel (espaces compris) depuis le tampon, sans chaîne intermédiaire.
                Secret = password.Length > 0 ? password.ToArray() : null,
            },
        };
    }

    private static bool? ParseBoolean(string value) => value.ToLowerInvariant() switch
    {
        "1" or "true" or "yes" or "y" or "oui" or "o" or "vrai" or "si" or "sì" => true,
        "0" or "false" or "no" or "n" or "non" or "faux" => false,
        _ => null,
    };

    private static string? Missing(Dictionary<Column, int> columns, Column column, string label) =>
        columns.ContainsKey(column)
            ? null
            : Format(CoreStrings.ImportMissingColumn, label, string.Join(", ", Names.Where(n => n.Value == column).Select(n => n.Key)));

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    /// <summary>« Nom du compte » → « nomducompte » : minuscules, sans espaces, tirets, soulignés ni accents.</summary>
    private static string Normalize(ReadOnlySpan<char> name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.Trim().ToString().Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    private readonly record struct FieldRange(int Start, int Length);

    private sealed record Record(int Line, List<FieldRange> Fields);

    private static ReadOnlySpan<char> Field(char[] scratch, FieldRange field) => scratch.AsSpan(field.Start, field.Length);

    /// <summary>Retire l'apostrophe ajoutée par l'export devant =, +, -, @ (protection contre les formules Excel).</summary>
    private static ReadOnlySpan<char> Unescape(ReadOnlySpan<char> value) =>
        value.Length > 1 && value[0] == '\'' && value[1] is '=' or '+' or '-' or '@' ? value[1..] : value;

    /// <summary>
    /// Découpe le texte en enregistrements et en champs ; le contenu des champs (guillemets retirés, « "" » → « " »)
    /// est écrit dans <paramref name="scratch"/>. Un champ entre guillemets peut contenir séparateurs et retours à la ligne.
    /// </summary>
    private static List<Record> Records(ReadOnlySpan<char> text, char[] scratch, out bool unclosed)
    {
        char separator = DetectSeparator(text);
        var records = new List<Record>();
        int position = 0, written = 0, line = 1;
        unclosed = false;
        while (position < text.Length)
        {
            var fields = new List<FieldRange>();
            int recordLine = line;
            while (true)
            {
                int start = written;
                if (position < text.Length && text[position] == '"')
                {
                    position++;
                    while (true)
                    {
                        if (position >= text.Length)
                        {
                            unclosed = true;
                            return records;
                        }

                        char c = text[position++];
                        if (c == '"')
                        {
                            if (position < text.Length && text[position] == '"')
                            {
                                scratch[written++] = '"';
                                position++;
                                continue;
                            }

                            break;
                        }

                        if (c == '\n')
                        {
                            line++;
                        }

                        scratch[written++] = c;
                    }

                    // Texte après le guillemet fermant (mal formé) : gardé jusqu'au séparateur.
                    while (position < text.Length && text[position] != separator && text[position] is not ('\r' or '\n'))
                    {
                        scratch[written++] = text[position++];
                    }
                }
                else
                {
                    while (position < text.Length && text[position] != separator && text[position] is not ('\r' or '\n'))
                    {
                        scratch[written++] = text[position++];
                    }
                }

                fields.Add(new FieldRange(start, written - start));
                if (position < text.Length && text[position] == separator)
                {
                    position++;
                    continue;
                }

                // Fin d'enregistrement : \r\n, \n, \r ou fin du texte.
                if (position < text.Length && text[position] == '\r')
                {
                    position++;
                }

                if (position < text.Length && text[position] == '\n')
                {
                    position++;
                }

                line++;
                break;
            }

            records.Add(new Record(recordLine, fields));
        }

        return records;
    }

    /// <summary>Séparateur le plus fréquent dans la première ligne, hors guillemets (« ; » à égalité).</summary>
    private static char DetectSeparator(ReadOnlySpan<char> text)
    {
        int semicolons = 0, commas = 0, tabs = 0;
        bool quoted = false;
        foreach (char c in text)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && c is '\r' or '\n')
            {
                break;
            }
            else if (!quoted)
            {
                semicolons += c == ';' ? 1 : 0;
                commas += c == ',' ? 1 : 0;
                tabs += c == '\t' ? 1 : 0;
            }
        }

        return tabs > semicolons && tabs > commas ? '\t' : commas > semicolons ? ',' : ';';
    }
}
