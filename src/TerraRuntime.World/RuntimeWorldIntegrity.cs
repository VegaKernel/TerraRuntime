using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TerraRuntime.World;

/// <summary>
/// Fast non-cryptographic integrity hash for disposable runtime snapshot payloads.
/// Snapshot files are local cache/checkpoint data, so corruption detection matters while
/// cryptographic collision resistance does not justify an extra SHA-256 pass over hundreds of MiB.
/// </summary>
internal static class RuntimeWorldIntegrity
{
    private const ulong Prime1 = 11400714785074694791UL;
    private const ulong Prime2 = 14029467366897019727UL;
    private const ulong Prime3 = 1609587929392839161UL;
    private const ulong Prime4 = 9650029242287828579UL;
    private const ulong Prime5 = 2870177450012600261UL;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static ulong Hash64(ReadOnlySpan<byte> data)
    {
        int length = data.Length;
        int offset = 0;
        ulong hash;
        ref byte source = ref MemoryMarshal.GetReference(data);

        if (length >= 32)
        {
            ulong v1 = unchecked(Prime1 + Prime2);
            ulong v2 = Prime2;
            ulong v3 = 0;
            ulong v4 = unchecked(0UL - Prime1);
            int limit = length - 32;

            do
            {
                v1 = Round(v1, ReadUInt64LittleEndian(ref source, offset));
                v2 = Round(v2, ReadUInt64LittleEndian(ref source, offset + 8));
                v3 = Round(v3, ReadUInt64LittleEndian(ref source, offset + 16));
                v4 = Round(v4, ReadUInt64LittleEndian(ref source, offset + 24));
                offset += 32;
            }
            while (offset <= limit);

            hash = BitOperations.RotateLeft(v1, 1) +
                   BitOperations.RotateLeft(v2, 7) +
                   BitOperations.RotateLeft(v3, 12) +
                   BitOperations.RotateLeft(v4, 18);
            hash = MergeRound(hash, v1);
            hash = MergeRound(hash, v2);
            hash = MergeRound(hash, v3);
            hash = MergeRound(hash, v4);
        }
        else
        {
            hash = Prime5;
        }

        hash = unchecked(hash + (ulong)length);

        while (offset <= length - 8)
        {
            ulong lane = Round(0, ReadUInt64LittleEndian(ref source, offset));
            hash ^= lane;
            hash = unchecked((BitOperations.RotateLeft(hash, 27) * Prime1) + Prime4);
            offset += 8;
        }

        if (offset <= length - 4)
        {
            hash ^= unchecked((ulong)ReadUInt32LittleEndian(ref source, offset) * Prime1);
            hash = unchecked((BitOperations.RotateLeft(hash, 23) * Prime2) + Prime3);
            offset += 4;
        }

        while (offset < length)
        {
            hash ^= unchecked(Unsafe.Add(ref source, offset) * Prime5);
            hash = unchecked(BitOperations.RotateLeft(hash, 11) * Prime1);
            offset++;
        }

        hash ^= hash >> 33;
        hash = unchecked(hash * Prime2);
        hash ^= hash >> 29;
        hash = unchecked(hash * Prime3);
        hash ^= hash >> 32;
        return hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ReadUInt64LittleEndian(ref byte source, int offset)
    {
        ulong value = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref source, offset));
        return BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadUInt32LittleEndian(ref byte source, int offset)
    {
        uint value = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref source, offset));
        return BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Round(ulong accumulator, ulong input)
    {
        accumulator = unchecked(accumulator + (input * Prime2));
        accumulator = BitOperations.RotateLeft(accumulator, 31);
        return unchecked(accumulator * Prime1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MergeRound(ulong accumulator, ulong value)
    {
        accumulator ^= Round(0, value);
        return unchecked((accumulator * Prime1) + Prime4);
    }
}
