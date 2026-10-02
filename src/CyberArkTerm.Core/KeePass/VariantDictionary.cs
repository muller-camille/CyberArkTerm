using System.Buffers.Binary;
using System.Text;

namespace CyberArkTerm.Core.KeePass;

/// <summary>Dictionnaire typé des en-têtes KDBX 4 (paramètres de dérivation de clé, données publiques).</summary>
internal sealed class VariantDictionary
{
    public const byte UInt32 = 0x04;
    public const byte UInt64 = 0x05;
    public const byte Bool = 0x08;
    public const byte Int32 = 0x0C;
    public const byte Int64 = 0x0D;
    public const byte String = 0x18;
    public const byte Bytes = 0x42;

    private readonly Dictionary<string, (byte Type, byte[] Value)> _items = new(StringComparer.Ordinal);

    public static VariantDictionary Parse(ReadOnlySpan<byte> data)
    {
        var result = new VariantDictionary();
        if (data.Length < 2 || (BinaryPrimitives.ReadUInt16LittleEndian(data) & 0xFF00) > 0x0100)
        {
            throw new FormatException("VariantDictionary");
        }

        int pos = 2;
        while (true)
        {
            byte type = data[pos++];
            if (type == 0)
            {
                return result;
            }

            int nameLength = BinaryPrimitives.ReadInt32LittleEndian(data[pos..]);
            pos += 4;
            var name = Encoding.UTF8.GetString(data.Slice(pos, nameLength));
            pos += nameLength;
            int valueLength = BinaryPrimitives.ReadInt32LittleEndian(data[pos..]);
            pos += 4;
            result._items[name] = (type, data.Slice(pos, valueLength).ToArray());
            pos += valueLength;
        }
    }

    public byte[]? GetBytes(string name) => _items.TryGetValue(name, out var item) ? item.Value : null;

    public ulong? GetUInt64(string name) => _items.TryGetValue(name, out var item) ? item.Type switch
    {
        UInt64 or Int64 when item.Value.Length == 8 => BinaryPrimitives.ReadUInt64LittleEndian(item.Value),
        UInt32 or Int32 when item.Value.Length == 4 => BinaryPrimitives.ReadUInt32LittleEndian(item.Value),
        _ => throw new FormatException(name),
    } : null;
}
