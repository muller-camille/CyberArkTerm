using System.Security.Cryptography;
using CyberArkTerm.Core.Localization;
using System.Text;

namespace CyberArkTerm.Core.Ssh;

/// <summary>Fichier trop grand pour être lu en mémoire (comparaison).</summary>
public sealed class FileTooLargeException(string path, long length, long maxBytes)
    : IOException(string.Format(System.Globalization.CultureInfo.CurrentCulture, CoreStrings.FileTooLargeToCompare,
        path, RemotePath.FormatSize(length), RemotePath.FormatSize(maxBytes)))
{
    public long Length { get; } = length;
}

public enum DiffKind
{
    Same,
    Removed,
    Added,

    /// <summary>Ligne modifiée : une ligne retirée à gauche en face d'une ligne ajoutée à droite.</summary>
    Changed,

    /// <summary>Lignes identiques masquées (vue des seules différences).</summary>
    Gap,
}

/// <summary>Ligne de la comparaison côte à côte ; numéro et texte absents du côté où la ligne n'existe pas.</summary>
public sealed record DiffRow(DiffKind Kind, int? LeftNumber, string? Left, int? RightNumber, string? Right, int Hidden = 0);

/// <summary>Contenu d'un fichier pour la comparaison : texte découpé en lignes, ou binaire.</summary>
public sealed class DiffSide
{
    /// <summary>Au-delà, le fichier n'est pas comparé ligne à ligne (taille, somme SHA-256 seulement).</summary>
    public const int MaxTextBytes = 10 * 1024 * 1024;

    private DiffSide(string name, long length, string sha256, IReadOnlyList<string>? lines, bool crlf)
    {
        Name = name;
        Length = length;
        Sha256 = sha256;
        Lines = lines;
        UsesCrlf = crlf;
    }

    public string Name { get; }

    public long Length { get; }

    public string Sha256 { get; }

    /// <summary>Lignes du texte ; null pour un fichier binaire ou trop grand.</summary>
    public IReadOnlyList<string>? Lines { get; }

    public bool IsText => Lines is not null;

    /// <summary>Fins de ligne Windows (« \r\n »).</summary>
    public bool UsesCrlf { get; }

    public static DiffSide FromBytes(string name, byte[] content)
    {
        var sha = Convert.ToHexStringLower(SHA256.HashData(content));
        bool binary = content.Length > MaxTextBytes || content.AsSpan(0, Math.Min(content.Length, 8192)).Contains((byte)0);
        if (binary)
        {
            return new DiffSide(name, content.Length, sha, null, false);
        }

        // UTF-8 tolérant (octet invalide → « � ») ; la marque d'ordre d'octets n'est pas une différence.
        var text = new UTF8Encoding(false, false).GetString(content);
        if (text.StartsWith('﻿'))
        {
            text = text[1..];
        }

        bool crlf = text.Contains("\r\n", StringComparison.Ordinal);
        return new DiffSide(name, content.Length, sha, SplitLines(text), crlf);
    }

    /// <summary>Lignes d'un texte (« \r\n », « \n » ou « \r ») ; la fin de ligne finale n'ajoute pas de ligne vide.</summary>
    public static IReadOnlyList<string> SplitLines(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        if (lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }
}

/// <summary>Résultat d'une comparaison ligne à ligne.</summary>
public sealed class DiffResult
{
    internal DiffResult(IReadOnlyList<DiffRow> rows, bool approximate)
    {
        Rows = rows;
        Approximate = approximate;
        Removed = rows.Count(r => r.Kind is DiffKind.Removed or DiffKind.Changed);
        Added = rows.Count(r => r.Kind is DiffKind.Added or DiffKind.Changed);
        int blocks = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Kind != DiffKind.Same && (i == 0 || rows[i - 1].Kind == DiffKind.Same))
            {
                blocks++;
            }
        }

        Blocks = blocks;
    }

    /// <summary>Toutes les lignes, côte à côte.</summary>
    public IReadOnlyList<DiffRow> Rows { get; }

    public int Removed { get; }

    public int Added { get; }

    /// <summary>Nombre de zones de différences.</summary>
    public int Blocks { get; }

    public bool Identical => Blocks == 0;

    /// <summary>Fichiers trop différents : la partie centrale est montrée comme entièrement remplacée.</summary>
    public bool Approximate { get; }

    /// <summary>Seulement les différences, avec <paramref name="context"/> lignes autour ; le reste est résumé.</summary>
    public IReadOnlyList<DiffRow> OnlyDifferences(int context = 3)
    {
        var keep = new bool[Rows.Count];
        for (int i = 0; i < Rows.Count; i++)
        {
            if (Rows[i].Kind != DiffKind.Same)
            {
                for (int j = Math.Max(0, i - context); j <= Math.Min(Rows.Count - 1, i + context); j++)
                {
                    keep[j] = true;
                }
            }
        }

        var shown = new List<DiffRow>();
        int hidden = 0;
        for (int i = 0; i < Rows.Count; i++)
        {
            if (keep[i])
            {
                if (hidden > 0)
                {
                    shown.Add(new DiffRow(DiffKind.Gap, null, null, null, null, hidden));
                    hidden = 0;
                }

                shown.Add(Rows[i]);
            }
            else
            {
                hidden++;
            }
        }

        if (hidden > 0 && shown.Count > 0)
        {
            shown.Add(new DiffRow(DiffKind.Gap, null, null, null, null, hidden));
        }

        return shown;
    }
}

/// <summary>
/// Comparaison de deux textes ligne à ligne (algorithme de Myers, après les lignes communes du début et de la fin),
/// en mémoire : rien n'est écrit sur le disque.
/// </summary>
public static class TextDiff
{
    /// <summary>Au-delà de ce nombre de lignes différentes, la comparaison devient approximative.</summary>
    public const int MaxEdits = 2000;

    public static DiffResult Compare(IReadOnlyList<string> left, IReadOnlyList<string> right, bool ignoreWhitespace = false)
    {
        string Key(string line) => ignoreWhitespace ? string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) : line;
        var a = left.Select(Key).ToArray();
        var b = right.Select(Key).ToArray();

        int prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix])
        {
            suffix++;
        }

        var middleA = a[prefix..(a.Length - suffix)];
        var middleB = b[prefix..(b.Length - suffix)];
        var ops = Myers(middleA, middleB);
        bool approximate = ops is null;
        ops ??= [.. Enumerable.Range(0, middleA.Length).Select(i => new Op('-', i, -1)), .. Enumerable.Range(0, middleB.Length).Select(j => new Op('+', -1, j))];

        var rows = new List<DiffRow>(left.Count + right.Count);
        for (int i = 0; i < prefix; i++)
        {
            rows.Add(new DiffRow(DiffKind.Same, i + 1, left[i], i + 1, right[i]));
        }

        // Retraits et ajouts consécutifs : mis en face (lignes modifiées), le reste seul de son côté.
        var removed = new List<int>();
        var added = new List<int>();
        void Flush()
        {
            for (int i = 0; i < Math.Max(removed.Count, added.Count); i++)
            {
                int? l = i < removed.Count ? prefix + removed[i] : null;
                int? r = i < added.Count ? prefix + added[i] : null;
                var kind = l is not null && r is not null ? DiffKind.Changed : l is not null ? DiffKind.Removed : DiffKind.Added;
                rows.Add(new DiffRow(kind, l + 1, l is { } li ? left[li] : null, r + 1, r is { } ri ? right[ri] : null));
            }

            removed.Clear();
            added.Clear();
        }

        foreach (var op in ops)
        {
            switch (op.Kind)
            {
                case '-':
                    removed.Add(op.A);
                    break;
                case '+':
                    added.Add(op.B);
                    break;
                default:
                    Flush();
                    rows.Add(new DiffRow(DiffKind.Same, prefix + op.A + 1, left[prefix + op.A], prefix + op.B + 1, right[prefix + op.B]));
                    break;
            }
        }

        Flush();
        for (int i = suffix; i > 0; i--)
        {
            int l = left.Count - i;
            int r = right.Count - i;
            rows.Add(new DiffRow(DiffKind.Same, l + 1, left[l], r + 1, right[r]));
        }

        return new DiffResult(rows, approximate);
    }

    /// <summary>Différences au format « diff -u » (lignes d'origine, sans les espaces ignorés).</summary>
    public static string Unified(DiffResult result, string leftName, string rightName, int context = 3)
    {
        var text = new StringBuilder();
        text.Append("--- ").Append(leftName).Append('\n');
        text.Append("+++ ").Append(rightName).Append('\n');
        var rows = result.Rows;
        int i = 0;
        while (i < rows.Count)
        {
            if (rows[i].Kind == DiffKind.Same)
            {
                i++;
                continue;
            }

            // Zone : du contexte avant la première différence au contexte après la dernière proche.
            int start = Math.Max(0, i - context);
            int end = i;
            while (true)
            {
                while (end < rows.Count && rows[end].Kind != DiffKind.Same)
                {
                    end++;
                }

                int next = end;
                while (next < rows.Count && rows[next].Kind == DiffKind.Same && next - end < 2 * context)
                {
                    next++;
                }

                if (next < rows.Count && rows[next].Kind != DiffKind.Same)
                {
                    end = next;
                    continue;
                }

                end = Math.Min(rows.Count, end + context);
                break;
            }

            var hunk = new StringBuilder();
            int leftCount = 0;
            int rightCount = 0;
            int? leftStart = null;
            int? rightStart = null;
            var removed = new List<string>();
            var added = new List<string>();
            void Flush()
            {
                foreach (var line in removed)
                {
                    hunk.Append('-').Append(line).Append('\n');
                }

                foreach (var line in added)
                {
                    hunk.Append('+').Append(line).Append('\n');
                }

                removed.Clear();
                added.Clear();
            }

            for (int j = start; j < end; j++)
            {
                var row = rows[j];
                leftStart ??= row.LeftNumber;
                rightStart ??= row.RightNumber;
                if (row.Kind == DiffKind.Same)
                {
                    Flush();
                    hunk.Append(' ').Append(row.Left).Append('\n');
                    leftCount++;
                    rightCount++;
                    continue;
                }

                if (row.Left is not null)
                {
                    removed.Add(row.Left);
                    leftCount++;
                }

                if (row.Right is not null)
                {
                    added.Add(row.Right);
                    rightCount++;
                }
            }

            Flush();
            text.Append("@@ -").Append(leftCount == 0 ? Math.Max(0, (leftStart ?? 1) - 1) : leftStart ?? 1).Append(',').Append(leftCount)
                .Append(" +").Append(rightCount == 0 ? Math.Max(0, (rightStart ?? 1) - 1) : rightStart ?? 1).Append(',').Append(rightCount)
                .Append(" @@\n").Append(hunk);
            i = end;
        }

        return text.ToString();
    }

    private readonly record struct Op(char Kind, int A, int B);

    /// <summary>Chemin d'édition le plus court (Myers) ; null au-delà de <see cref="MaxEdits"/> modifications.</summary>
    private static List<Op>? Myers(string[] a, string[] b)
    {
        int n = a.Length;
        int m = b.Length;
        if (n == 0 || m == 0)
        {
            return [.. Enumerable.Range(0, n).Select(i => new Op('-', i, -1)), .. Enumerable.Range(0, m).Select(j => new Op('+', -1, j))];
        }

        int maxD = Math.Min(n + m, MaxEdits);
        int offset = maxD + 1;
        var v = new int[2 * maxD + 3];
        var trace = new List<int[]>();
        int found = -1;
        for (int d = 0; d <= maxD; d++)
        {
            // État avant l'étape d (indices k de -d-1 à d+1), pour remonter le chemin ensuite.
            var snapshot = new int[2 * d + 3];
            Array.Copy(v, offset - d - 1, snapshot, 0, snapshot.Length);
            trace.Add(snapshot);
            for (int k = -d; k <= d; k += 2)
            {
                int x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]) ? v[offset + k + 1] : v[offset + k - 1] + 1;
                int y = x - k;
                while (x < n && y < m && a[x] == b[y])
                {
                    x++;
                    y++;
                }

                v[offset + k] = x;
                if (x >= n && y >= m)
                {
                    found = d;
                    break;
                }
            }

            if (found >= 0)
            {
                break;
            }
        }

        if (found < 0)
        {
            return null;
        }

        var ops = new List<Op>();
        int cx = n;
        int cy = m;
        for (int d = found; d >= 0; d--)
        {
            var before = trace[d];
            int Get(int k) => before[k + d + 1];
            int k = cx - cy;
            int prevK = k == -d || (k != d && Get(k - 1) < Get(k + 1)) ? k + 1 : k - 1;
            int prevX = Get(prevK);
            int prevY = prevX - prevK;
            while (cx > prevX && cy > prevY)
            {
                ops.Add(new Op('=', cx - 1, cy - 1));
                cx--;
                cy--;
            }

            if (d > 0)
            {
                ops.Add(cx == prevX ? new Op('+', -1, prevY) : new Op('-', prevX, -1));
            }

            cx = prevX;
            cy = prevY;
        }

        ops.Reverse();
        return ops;
    }
}
