using System.Globalization;
using CyberArkTerm.Core.Localization;

namespace CyberArkTerm.Core.Ssh;

/// <summary>Manipulation de chemins Unix (côté serveur), indépendante du système local.</summary>
public static class RemotePath
{
    public static string Combine(string directory, string name)
    {
        if (name.StartsWith('/'))
        {
            return Normalize(name);
        }

        return Normalize(directory.TrimEnd('/') + "/" + name);
    }

    /// <summary>Résout « . », « .. » et les « / » multiples ; renvoie toujours un chemin absolu.</summary>
    public static string Normalize(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }

                continue;
            }

            parts.Add(part);
        }

        return "/" + string.Join('/', parts);
    }

    public static string Parent(string path)
    {
        var normalized = Normalize(path);
        int slash = normalized.LastIndexOf('/');
        return slash <= 0 ? "/" : normalized[..slash];
    }

    public static string Name(string path)
    {
        var normalized = Normalize(path);
        return normalized == "/" ? "/" : normalized[(normalized.LastIndexOf('/') + 1)..];
    }

    public static string FormatSize(long bytes)
    {
        var culture = CultureInfo.CurrentCulture;
        return bytes switch
        {
            < 1024 => string.Format(culture, CoreStrings.SizeBytes, bytes),
            < 1024 * 1024 => string.Format(culture, CoreStrings.SizeKilobytes, (bytes / 1024.0).ToString("0.#", culture)),
            < 1024L * 1024 * 1024 => string.Format(culture, CoreStrings.SizeMegabytes, (bytes / 1024.0 / 1024).ToString("0.#", culture)),
            _ => string.Format(culture, CoreStrings.SizeGigabytes, (bytes / 1024.0 / 1024 / 1024).ToString("0.##", culture)),
        };
    }
}
