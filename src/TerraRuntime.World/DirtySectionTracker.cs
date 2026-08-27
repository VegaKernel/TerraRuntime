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

    public bool IsDirty(WorldSectionId section)
    {
        int index = TerrariaSectionGeometry.ToLinearIndex(dimensions, section);
        return (words[index >> 6] & (1UL << (index & 63))) != 0;
    }

    public int Drain(Span<WorldSectionId> destination)
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

                bits &= ~mask;
                words[wordIndex] &= ~mask;
                if (linearIndex >= dimensions.SectionCount)
                {
                    continue;
                }

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
