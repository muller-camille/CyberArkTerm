using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ZillaTerm.Core.Localization;

namespace ZillaTerm.Core.Migration;

/// <summary>Lecture des fichiers de configuration des autres logiciels : taille limitée, encodage détecté, XML sans DTD.</summary>
internal static class ImportText
{
    /// <summary>Taille maximale d'un fichier lu (une liste de sessions, pas une archive).</summary>
    public const int MaxMegabytes = 32;

    /// <summary>Nombre maximal de fichiers parcourus dans un dossier de sessions.</summary>
    public const int MaxFiles = 20000;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string ReadFile(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxMegabytes * 1024L * 1024L)
        {
            throw new InvalidDataException(string.Format(CultureInfo.CurrentCulture, CoreStrings.MigrationFileTooLarge, info.Name, MaxMegabytes));
        }

        return Decode(File.ReadAllBytes(path));
    }

    /// <summary>Texte d'un fichier : UTF-8 ou UTF-16 avec BOM, sinon UTF-8, sinon Latin-1 (fichiers ANSI).</summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes is [0xEF, 0xBB, 0xBF, ..])
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes is [0xFF, 0xFE, ..])
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes is [0xFE, 0xFF, ..])
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>« My%20server » → « My server » (noms de sessions encodés par PuTTY, KiTTY et WinSCP).</summary>
    public static string Unescape(string value)
    {
        if (!value.Contains('%'))
        {
            return value;
        }

        var bytes = new List<byte>(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '%' && i + 2 < value.Length
                && byte.TryParse(value.AsSpan(i + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var b))
            {
                bytes.Add(b);
                i += 2;
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(value[i].ToString()));
            }
        }

        return Decode(bytes.ToArray());
    }

    /// <summary>Profondeur d'éléments XML acceptée : bien au-delà des fichiers réels (dossiers dans des dossiers).</summary>
    internal const int MaxXmlDepth = 160;

    /// <summary>
    /// Document XML, sans DTD ni ressource externe. Un document imbriqué au-delà de <see cref="MaxXmlDepth"/> est refusé
    /// avant tout parcours récursif (fichier forgé : la pile serait épuisée et l'application s'arrêterait).
    /// </summary>
    public static XDocument ParseXml(string text)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        try
        {
            using (var scan = XmlReader.Create(new StringReader(text), settings))
            {
                while (scan.Read())
                {
                    if (scan.Depth > MaxXmlDepth)
                    {
                        throw new InvalidDataException(string.Format(CultureInfo.CurrentCulture, CoreStrings.MigrationTooDeep, MaxXmlDepth));
                    }
                }
            }

            using var reader = XmlReader.Create(new StringReader(text), settings);
            return XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException(string.Format(CultureInfo.CurrentCulture, CoreStrings.MigrationUnreadable, ex.Message), ex);
        }
    }

    /// <summary>Fichiers d'un dossier et de ses sous-dossiers, avec leur sous-dossier relatif (« Prod/Web »).</summary>
    public static IEnumerable<(string Folder, string Path)> Files(string root, string pattern)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(string.Format(CultureInfo.CurrentCulture, CoreStrings.MigrationFolderMissing, root));
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MaxRecursionDepth = 32,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
            MatchCasing = MatchCasing.CaseInsensitive,
        };
        int count = 0;
        foreach (var path in Directory.EnumerateFiles(root, pattern, options))
        {
            if (++count > MaxFiles)
            {
                yield break;
            }

            var relative = Path.GetRelativePath(root, Path.GetDirectoryName(path)!);
            yield return (relative == "." ? "" : SessionFolders.Normalize(relative), path);
        }
    }

    /// <summary>
    /// Lit un fichier d'un dossier de sessions ; null s'il est trop gros pour être une session (quelques Ko) : un dossier
    /// choisi par erreur n'est pas lu en entier.
    /// </summary>
    public static string? ReadSessionFile(string path) =>
        new FileInfo(path).Length > 64 * 1024 ? null : Decode(File.ReadAllBytes(path));

    public static int? Int(string? value) =>
        int.TryParse((value ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    /// <summary>Port valide (1 à 65535), sinon null (« -1 », vide : port par défaut).</summary>
    public static int? Port(int? value) => value is > 0 and < 65536 ? value : null;

    public static int? Port(string? value) => Port(Int(value));
}
