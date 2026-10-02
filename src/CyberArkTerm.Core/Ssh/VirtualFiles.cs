using System.Buffers.Binary;
using System.Text;

namespace CyberArkTerm.Core.Ssh;

/// <summary>
/// Fichier ou dossier proposé à l'Explorateur par glisser-déposer avant d'exister sur ce poste (« fichier
/// virtuel ») : chemin relatif Windows, taille et date ; le contenu n'est fourni qu'au dépôt.
/// </summary>
public sealed record VirtualFile(string RelativePath, bool IsDirectory, long Length, DateTime LastWriteTimeUtc);

/// <summary>Noms et description (FILEGROUPDESCRIPTORW) des fichiers virtuels donnés à l'Explorateur.</summary>
public static class VirtualFiles
{
    /// <summary>Taille d'un FILEDESCRIPTORW.</summary>
    public const int DescriptorSize = 592;

    /// <summary>Longueur maximale d'un chemin relatif (MAX_PATH, zéro final compris).</summary>
    public const int MaxPath = 260;

    private const uint FdAttributes = 0x4;
    private const uint FdWritesTime = 0x20;
    private const uint FdFileSize = 0x40;
    private const uint FdProgressUi = 0x4000;
    private const uint FdUnicode = 0x80000000;
    private const uint AttributeDirectory = 0x10;
    private const uint AttributeNormal = 0x80;

    /// <summary>
    /// Chemins relatifs Windows pour des chemins Unix (suites de noms, un dossier avant son contenu) : noms nettoyés
    /// (<see cref="WindowsFileName"/>) et rendus uniques dans chaque dossier, sans tenir compte de la casse comme
    /// Windows (« Rapport » et « rapport » donnent « Rapport » et « rapport (2) »).
    /// </summary>
    public static IReadOnlyList<string> AssignNames(IReadOnlyList<IReadOnlyList<string>> paths)
    {
        var folders = new Dictionary<string, string>(StringComparer.Ordinal) { [""] = "" };
        var used = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(paths.Count);
        foreach (var path in paths)
        {
            var unixParent = string.Join('/', path.Take(path.Count - 1));
            if (!folders.TryGetValue(unixParent, out var parent))
            {
                throw new ArgumentException("Dossier listé après son contenu", nameof(paths));
            }

            if (!used.TryGetValue(parent, out var names))
            {
                names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                used[parent] = names;
            }

            var name = Unique(WindowsFileName.Sanitize(path[^1]), names);
            var windowsPath = parent.Length == 0 ? name : $"{parent}\\{name}";
            folders[string.Join('/', path)] = windowsPath;
            result.Add(windowsPath);
        }

        return result;
    }

    /// <summary>
    /// FILEGROUPDESCRIPTORW (format « FileGroupDescriptorW ») : nombre d'éléments puis un FILEDESCRIPTORW par élément.
    /// </summary>
    /// <exception cref="PathTooLongException">Chemin relatif de plus de 259 caractères.</exception>
    public static byte[] BuildDescriptor(IReadOnlyList<VirtualFile> files)
    {
        var data = new byte[4 + (files.Count * DescriptorSize)];
        BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)files.Count);
        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            if (file.RelativePath.Length >= MaxPath)
            {
                throw new PathTooLongException(file.RelativePath);
            }

            var d = data.AsSpan(4 + (i * DescriptorSize), DescriptorSize);
            BinaryPrimitives.WriteUInt32LittleEndian(d, FdAttributes | FdWritesTime | FdFileSize | FdProgressUi | FdUnicode);
            BinaryPrimitives.WriteUInt32LittleEndian(d[36..], file.IsDirectory ? AttributeDirectory : AttributeNormal);
            var time = file.LastWriteTimeUtc > DateTime.FromFileTimeUtc(0) ? file.LastWriteTimeUtc.ToFileTimeUtc() : 0;
            BinaryPrimitives.WriteInt64LittleEndian(d[56..], time);
            var length = file.IsDirectory ? 0 : Math.Max(file.Length, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(d[64..], (uint)(length >> 32));
            BinaryPrimitives.WriteUInt32LittleEndian(d[68..], (uint)(length & 0xFFFFFFFF));
            Encoding.Unicode.GetBytes(file.RelativePath, d[72..]);
        }

        return data;
    }

    private static string Unique(string name, HashSet<string> names)
    {
        var candidate = name;
        var extension = Path.GetExtension(name);
        var stem = extension.Length > 0 && extension.Length < name.Length ? name[..^extension.Length] : name;
        if (stem.Length == name.Length)
        {
            extension = "";
        }

        for (int n = 2; !names.Add(candidate); n++)
        {
            candidate = $"{stem} ({n}){extension}";
        }

        return candidate;
    }
}
