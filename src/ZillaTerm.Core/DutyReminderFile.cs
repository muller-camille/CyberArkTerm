using System.Globalization;
using System.Text;

namespace ZillaTerm.Core;

/// <summary>Consignes d'astreinte lues dans un fichier ; <see cref="Truncated"/> : fichier trop long, seul son début est là.</summary>
public sealed record DutyReminderText(string Text, bool Truncated);

/// <summary>
/// Fichier des consignes d'astreinte (souvent sur un partage réseau) : les responsables le mettent à jour, ZillaTerm le
/// relit à chaque ouverture de la fenêtre « Intervention ». Texte brut, montré tel quel : rien n'y est interprété.
/// </summary>
public static class DutyReminderFile
{
    /// <summary>Taille lue au plus (octets) : au-delà, seul le début du fichier est montré.</summary>
    public const int MaxBytes = 64 * 1024;

    private const int MaxPathLength = 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Chemin acceptable : complet (« C:\… » ou « \\serveur\partage\… »), sans caractère invisible, et jamais un
    /// périphérique (« \\.\… », « \\?\… », canal nommé, « CON », « COM1 »…) dont la lecture bloquerait ou viserait autre
    /// chose qu'un fichier.
    /// </summary>
    public static bool IsValidPath(string? path)
    {
        var p = (path ?? "").Trim();
        if (p.Length > MaxPathLength || !EnvironmentProfile.IsFullPath(p)
            || p.Any(c => char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format))
        {
            return false;
        }

        var parts = p.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (EnvironmentProfile.NetworkServer(p) is { } server
            && (server is "." or "?" || parts.Length < 3 || parts[1].Equals("pipe", StringComparison.OrdinalIgnoreCase)
                || parts[1].Equals("mailslot", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var name = parts[^1].Split('.')[0].TrimEnd(' ');
        return !(name.Length == 3 && name.ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL")
               && !(name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                    && name[3] is >= '0' and <= '9' or '¹' or '²' or '³');
    }

    /// <summary>Lit le fichier (au plus <see cref="MaxBytes"/> octets) et le décode.</summary>
    /// <exception cref="IOException">Fichier absent, illisible ou partage indisponible.</exception>
    /// <exception cref="UnauthorizedAccessException">Accès refusé.</exception>
    public static async Task<DutyReminderText> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!IsValidPath(path))
        {
            throw new ArgumentException("Chemin des consignes refusé.", nameof(path));
        }

        return await ReadFileAsync(path.Trim(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lecture sans contrôle du chemin (déjà fait), aussi pour les tests hors Windows.</summary>
    internal static async Task<DutyReminderText> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxBytes + 1];
        int read = 0;
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                         4096, FileOptions.Asynchronous))
        {
            while (read < buffer.Length)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
                if (n == 0)
                {
                    break;
                }

                read += n;
            }
        }

        bool truncated = read > MaxBytes;
        return new DutyReminderText(Decode(buffer.AsSpan(0, Math.Min(read, MaxBytes)), truncated), truncated);
    }

    /// <summary>
    /// Texte du fichier : UTF-8 ou UTF-16 (marque d'ordre des octets), sinon UTF-8 et, s'il n'en est pas, Latin-1 (ancien
    /// Bloc-notes). Sauts de ligne « \n » ; caractères de contrôle et invisibles retirés (un inverseur de sens de lecture
    /// pourrait afficher un autre numéro d'urgence que celui écrit).
    /// </summary>
    /// <param name="cut">Le fichier continue après ces octets : une fin de caractère coupée est ignorée.</param>
    public static string Decode(ReadOnlySpan<byte> bytes, bool cut = false)
    {
        string text;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            text = Encoding.UTF8.GetString(cut ? WholeUtf8(bytes[3..]) : bytes[3..]);
        }
        else if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            text = Encoding.Unicode.GetString(Even(bytes[2..]));
        }
        else if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            text = Encoding.BigEndianUnicode.GetString(Even(bytes[2..]));
        }
        else
        {
            try
            {
                text = StrictUtf8.GetString(cut ? WholeUtf8(bytes) : bytes);
            }
            catch (DecoderFallbackException)
            {
                text = Encoding.Latin1.GetString(bytes);
            }
        }

        return Clean(text);
    }

    private static ReadOnlySpan<byte> Even(ReadOnlySpan<byte> bytes) => bytes[..(bytes.Length & ~1)];

    /// <summary>Retire un caractère UTF-8 coupé à la fin (1 à 3 octets d'un caractère commencé).</summary>
    private static ReadOnlySpan<byte> WholeUtf8(ReadOnlySpan<byte> bytes)
    {
        for (int back = 1; back <= Math.Min(3, bytes.Length); back++)
        {
            byte b = bytes[^back];
            if ((b & 0xC0) == 0x80)
            {
                // Octet de suite : le début du caractère est plus loin en arrière.
                continue;
            }

            int length = b >= 0xF0 ? 4 : b >= 0xE0 ? 3 : b >= 0xC0 ? 2 : 1;
            return length > back ? bytes[..^back] : bytes;
        }

        return bytes;
    }

    private static string Clean(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var rune in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').EnumerateRunes())
        {
            if (rune.Value is '\n' or '\t'
                || (!Rune.IsControl(rune) && Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format && rune.Value != 0xFFFD))
            {
                result.Append(rune.ToString());
            }
        }

        return result.ToString().TrimEnd();
    }
}
