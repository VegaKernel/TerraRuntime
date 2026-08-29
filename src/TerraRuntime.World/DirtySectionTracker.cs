using System.Numerics;

namespace TerraRuntime.World;

/// <summary>
/// Single-writer bitset of network sections dirtied by committed world mutations. The game thread owns
/// mutation; consumers drain bounded batches instead of scanning the entire tile map.
/// </summary>
public sealed class DirtySectionTracker
{
    private readonly WorldDimensions dimensions;
    private readonly ulong[] words;
    private int dirtyCount;

    public DirtySectionTracker(WorldDimensions dimensions)
    {
        this.dimensions = dimensions ?? throw new ArgumentNullException(nameof(dimensions));
        words = new ulong[(dimensions.SectionCount + 63) / 64];
    }

    public int DirtyCount => dirtyCount;

    public bool MarkDirty(WorldSectionId section)
    {
        int index = TerrariaSectionGeometry.ToLinearIndex(dimensions, section);
        int wordIndex = index >> 6;
        ulong mask = 1UL << (index & 63);
        if ((words[wordIndex] & mask) != 0)
        {
            return false;
        }

        words[wordIndex] |= mask;
        dirtyCount++;
        return true;
    }

    public bool MarkTileDirty(int tileX, int tileY) =>
        MarkDirty(TerrariaSectionGeometry.FromTile(dimensions, tileX, tileY));

    /// <summary>
    /// Clears one known section without scanning the bitset. This remains a single-writer operation and is used
    /// when authoritative code captures a specifically requested section ahead of the ordinary dirty backlog.
    /// </summary>
    public bool ClearDirty(WorldSectionId section)
    {
        int index = TerrariaSectionGeometry.ToLinearIndex(dimensions, section);
        int wordIndex = index >> 6;
        ulong mask = 1UL << (index & 63);
        if ((words[wordIndex] & mask) == 0)
            return false;

        words[wordIndex] &= ~mask;
        dirtyCount--;
        return true;
    }

    public bool IsDirty(WorldSectionId section)
    {
        int index = TerrariaSectionGeometry.ToLinearIndex(dimensions, section);
        return (words[index >> 6] & (1UL << (index & 63))) != 0;
    }

    public int Drain(Span<WorldSectionId> destination) =>
        Drain(destination, excludedLinearIndices: null);

    /// <summary>
    /// Drains dirty sections while leaving explicitly excluded linear section indices dirty. This lets bounded
    /// consumers avoid repeatedly allocating snapshots for sections that already have work in flight, without
    /// dropping the newer dirty revision that must be processed after the current work completes.
    /// </summary>
    public int Drain(Span<WorldSectionId> destination, IReadOnlySet<int>? excludedLinearIndices)
    {
        if (destination.IsEmpty || dirtyCount == 0)
        {
            return 0;
        }

        int written = 0;
        for (int wordIndex = 0; wordIndex < words.Length && written < destination.Length; wordIndex++)
        {
            ulong bits = words[wordIndex];
            while (bits != 0 && written < destination.Length)
            {
                int bitIndex = BitOperations.TrailingZeroCount(bits);
                ulong mask = 1UL << bitIndex;
                int linearIndex = (wordIndex << 6) + bitIndex;

                // Always clear the local scan bit so an excluded dirty section cannot spin this loop. The
                // authoritative bitset remains untouched for excluded sections and will be reconsidered later.
                bits &= ~mask;
                if (linearIndex >= dimensions.SectionCount)
                {
                    words[wordIndex] &= ~mask;
                    continue;
                }

                if (excludedLinearIndices?.Contains(linearIndex) == true)
                    continue;

                words[wordIndex] &= ~mask;
                destination[written++] = TerrariaSectionGeometry.FromLinearIndex(dimensions, linearIndex);
                dirtyCount--;
            }
        }

        return written;
    }

    public void Clear()
    {
        Array.Clear(words);
        dirtyCount = 0;
    }
}
