using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace CyberArkTerm.Core.KeePass;

/// <summary>
/// Chiffrement par flot (suite pseudo-aléatoire combinée par XOR) : ChaCha20 (RFC 8439) et Salsa20, utilisés par
/// KeePass pour le contenu des coffres KDBX 4 et pour les valeurs protégées (mots de passe) à l'intérieur du XML.
/// </summary>
internal abstract class StreamCipher : IDisposable
{
    private readonly byte[] _block = new byte[64];
    private int _used = 64;

    protected uint[] State { get; } = new uint[16];

    /// <summary>Combine <paramref name="data"/> avec la suite, en continuant là où l'appel précédent s'est arrêté.</summary>
    public void Xor(Span<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            if (_used == 64)
            {
                NextBlock(_block);
                _used = 0;
            }

            data[i] ^= _block[_used++];
        }
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_block);
        Array.Clear(State);
        GC.SuppressFinalize(this);
    }

    protected abstract void NextBlock(Span<byte> output);

    protected static void LoadKey(uint[] state, ReadOnlySpan<byte> key, params int[] positions)
    {
        for (int i = 0; i < positions.Length; i++)
        {
            state[positions[i]] = BinaryPrimitives.ReadUInt32LittleEndian(key.Slice(i * 4, 4));
        }
    }

    protected static void Output(ReadOnlySpan<uint> working, ReadOnlySpan<uint> state, Span<byte> output)
    {
        for (int i = 0; i < 16; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(i * 4, 4), unchecked(working[i] + state[i]));
        }
    }
}

/// <summary>ChaCha20 : clé de 32 octets, nonce de 12 octets, compteur de blocs sur 32 bits.</summary>
internal sealed class ChaCha20 : StreamCipher
{
    public ChaCha20(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, uint counter = 0)
    {
        if (key.Length != 32 || nonce.Length != 12)
        {
            throw new ArgumentException("ChaCha20 : clé de 32 octets et nonce de 12 octets.");
        }

        State[0] = 0x61707865;
        State[1] = 0x3320646e;
        State[2] = 0x79622d32;
        State[3] = 0x6b206574;
        LoadKey(State, key, 4, 5, 6, 7, 8, 9, 10, 11);
        State[12] = counter;
        LoadKey(State, nonce, 13, 14, 15);
    }

    protected override void NextBlock(Span<byte> output)
    {
        Span<uint> x = stackalloc uint[16];
        State.CopyTo(x);
        for (int i = 0; i < 10; i++)
        {
            Quarter(x, 0, 4, 8, 12);
            Quarter(x, 1, 5, 9, 13);
            Quarter(x, 2, 6, 10, 14);
            Quarter(x, 3, 7, 11, 15);
            Quarter(x, 0, 5, 10, 15);
            Quarter(x, 1, 6, 11, 12);
            Quarter(x, 2, 7, 8, 13);
            Quarter(x, 3, 4, 9, 14);
        }

        Output(x, State, output);
        x.Clear();
        State[12] = unchecked(State[12] + 1);
        if (State[12] == 0)
        {
            // 256 Gio pour un même nonce : n'arrive pas avec un coffre, mais on ne réutilise jamais la suite.
            throw new CryptographicException("ChaCha20 : compteur épuisé.");
        }
    }

    private static void Quarter(Span<uint> x, int a, int b, int c, int d)
    {
        unchecked
        {
            x[a] += x[b];
            x[d] = BitOperations.RotateLeft(x[d] ^ x[a], 16);
            x[c] += x[d];
            x[b] = BitOperations.RotateLeft(x[b] ^ x[c], 12);
            x[a] += x[b];
            x[d] = BitOperations.RotateLeft(x[d] ^ x[a], 8);
            x[c] += x[d];
            x[b] = BitOperations.RotateLeft(x[b] ^ x[c], 7);
        }
    }
}

/// <summary>Salsa20/20 : clé de 32 octets, nonce de 8 octets, compteur de blocs sur 64 bits.</summary>
internal sealed class Salsa20 : StreamCipher
{
    public Salsa20(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce)
    {
        if (key.Length != 32 || nonce.Length != 8)
        {
            throw new ArgumentException("Salsa20 : clé de 32 octets et nonce de 8 octets.");
        }

        State[0] = 0x61707865;
        State[5] = 0x3320646e;
        State[10] = 0x79622d32;
        State[15] = 0x6b206574;
        LoadKey(State, key, 1, 2, 3, 4, 11, 12, 13, 14);
        LoadKey(State, nonce, 6, 7);
    }

    protected override void NextBlock(Span<byte> output)
    {
        Span<uint> x = stackalloc uint[16];
        State.CopyTo(x);
        for (int i = 0; i < 10; i++)
        {
            Quarter(x, 0, 4, 8, 12);
            Quarter(x, 5, 9, 13, 1);
            Quarter(x, 10, 14, 2, 6);
            Quarter(x, 15, 3, 7, 11);
            Quarter(x, 0, 1, 2, 3);
            Quarter(x, 5, 6, 7, 4);
            Quarter(x, 10, 11, 8, 9);
            Quarter(x, 15, 12, 13, 14);
        }

        Output(x, State, output);
        x.Clear();
        State[8] = unchecked(State[8] + 1);
        if (State[8] == 0)
        {
            State[9] = unchecked(State[9] + 1);
        }
    }

    private static void Quarter(Span<uint> x, int a, int b, int c, int d)
    {
        unchecked
        {
            x[b] ^= BitOperations.RotateLeft(x[a] + x[d], 7);
            x[c] ^= BitOperations.RotateLeft(x[b] + x[a], 9);
            x[d] ^= BitOperations.RotateLeft(x[c] + x[b], 13);
            x[a] ^= BitOperations.RotateLeft(x[d] + x[c], 18);
        }
    }
}
