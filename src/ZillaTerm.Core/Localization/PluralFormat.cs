using System.Globalization;

namespace ZillaTerm.Core.Localization;

/// <summary>
/// Mise en forme des textes traduits qui accorde les mots avec un nombre. Dans un paramètre, « {0:# fichier|# fichiers} »
/// donne « 1 fichier » ou « 3 fichiers » (« # » est remplacé par le nombre, avec séparateur de milliers) et
/// « {0:|s} » seulement une terminaison. Singulier pour 1 en anglais et en italien, pour 0 et 1 en français (langue de
/// l'interface) ; les autres paramètres sont mis en forme comme par string.Format (réglages régionaux de Windows).
/// </summary>
public static class PluralFormat
{
    public static string Format(string format, params object?[] args) =>
        string.Format(new Formatter(CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture), format, args);

    /// <summary>Le nombre <paramref name="count"/> prend le singulier dans la langue <paramref name="language"/>.</summary>
    public static bool IsSingular(decimal count, CultureInfo language) =>
        language.TwoLetterISOLanguageName == "fr" ? Math.Abs(count) < 2 : count == 1;

    private sealed class Formatter(CultureInfo culture, CultureInfo language) : IFormatProvider, ICustomFormatter
    {
        public object? GetFormat(Type? formatType) => formatType == typeof(ICustomFormatter) ? this : culture.GetFormat(formatType);

        public string Format(string? format, object? arg, IFormatProvider? formatProvider)
        {
            if (format is not null && format.Contains('|', StringComparison.Ordinal) && Count(arg) is { } count)
            {
                var forms = format.Split('|', 2);
                var number = count == decimal.Truncate(count) ? count.ToString("N0", culture) : count.ToString(culture);
                return forms[IsSingular(count, language) ? 0 : 1].Replace("#", number, StringComparison.Ordinal);
            }

            return arg is IFormattable formattable ? formattable.ToString(format, culture) : arg?.ToString() ?? "";
        }

        private static decimal? Count(object? arg) => arg switch
        {
            int i => i,
            long l => l,
            short s => s,
            byte b => b,
            uint u => u,
            ulong u => u,
            ushort u => u,
            decimal d => d,
            _ => null,
        };
    }
}
