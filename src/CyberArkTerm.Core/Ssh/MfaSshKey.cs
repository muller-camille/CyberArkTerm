using System.Text.Json;

namespace CyberArkTerm.Core.Ssh;

/// <summary>Clé privée SSH temporaire générée par le PVWA (« MFA caching ») pour le PSMP.</summary>
public sealed record MfaSshKey(string PrivateKey, string? Format, DateTimeOffset? ExpiresAt)
{
    private static readonly string[] PreferredFormats = ["OpenSSH", "PEM", "PPK"];

    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } end && now >= end - TimeSpan.FromMinutes(1);

    /// <summary>Jamais la clé privée dans <c>ToString()</c> (journal, débogueur…).</summary>
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Format = ").Append(Format).Append(", ExpiresAt = ").Append(ExpiresAt);
        return true;
    }

    /// <summary>
    /// Lit la réponse du PVWA, quelle que soit sa forme selon les versions : on cherche les chaînes
    /// qui contiennent une clé privée et on préfère le format OpenSSH, puis PEM, puis PPK.
    /// </summary>
    public static MfaSshKey? Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            var keys = new List<(string Key, string? Format)>();
            DateTimeOffset? expires = null;
            Walk(doc.RootElement, null, keys, ref expires);
            if (keys.Count == 0)
            {
                return null;
            }

            var best = keys
                .OrderBy(k => Array.FindIndex(PreferredFormats, f => string.Equals(f, k.Format, StringComparison.OrdinalIgnoreCase)) is var i && i >= 0 ? i : 99)
                .First();
            return new MfaSshKey(best.Key, best.Format, expires);
        }
    }

    private static void Walk(JsonElement element, string? format, List<(string, string?)> keys, ref DateTimeOffset? expires)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                string? localFormat = format;
                foreach (var p in element.EnumerateObject())
                {
                    if (p.NameEquals("format") && p.Value.ValueKind == JsonValueKind.String)
                    {
                        localFormat = p.Value.GetString();
                    }
                    else if (p.Name.Equals("expirationTime", StringComparison.OrdinalIgnoreCase) && p.Value.TryGetInt64(out var seconds))
                    {
                        expires = UnixTime.ToOffset(seconds) ?? expires;
                    }
                }

                foreach (var p in element.EnumerateObject())
                {
                    Walk(p.Value, localFormat, keys, ref expires);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, format, keys, ref expires);
                }

                break;
            case JsonValueKind.String:
                var text = element.GetString() ?? "";
                if (text.Contains("PRIVATE KEY-----", StringComparison.Ordinal) || text.StartsWith("PuTTY-User-Key-File", StringComparison.Ordinal))
                {
                    keys.Add((text, format));
                }

                break;
        }
    }
}
