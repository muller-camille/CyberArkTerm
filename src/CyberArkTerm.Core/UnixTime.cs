namespace CyberArkTerm.Core;

/// <summary>Dates du PVWA en secondes Unix.</summary>
internal static class UnixTime
{
    /// <summary>Au-delà, la valeur est prise pour des millisecondes (an 5138 en secondes).</summary>
    private const long MillisecondsFrom = 100_000_000_000;

    /// <summary>
    /// Date locale d'une valeur en secondes Unix ; aucune pour 0 ou moins, ou hors des dates représentables (donnée
    /// erronée). Une valeur en millisecondes (certaines versions du PVWA) est reconnue à sa taille.
    /// </summary>
    public static DateTime? ToLocal(long seconds) => ToOffset(seconds)?.LocalDateTime;

    /// <inheritdoc cref="ToLocal"/>
    public static DateTimeOffset? ToOffset(long seconds)
    {
        if (seconds <= 0)
        {
            return null;
        }

        if (seconds >= MillisecondsFrom)
        {
            seconds /= 1000;
        }

        return seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds() ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
    }

    /// <summary>
    /// Fin de la journée de <paramref name="date"/> (23:59:59 à l'heure de <paramref name="zone"/>), en secondes Unix. Le
    /// décalage est celui de 23:59:59, pas celui de minuit : ils diffèrent les jours de changement d'heure.
    /// </summary>
    public static long EndOfDay(DateTime date, TimeZoneInfo zone)
    {
        var end = DateTime.SpecifyKind(date.Date.AddDays(1).AddSeconds(-1), DateTimeKind.Unspecified);
        return new DateTimeOffset(end, zone.GetUtcOffset(end)).ToUnixTimeSeconds();
    }
}
