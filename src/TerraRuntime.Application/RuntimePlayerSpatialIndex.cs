using System.Numerics;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Authoritative-thread spatial index for player slots, bucketed by Terraria network sections.
/// Four 64-bit words represent all 256 possible byte player slots for each section, keeping the
/// index compact and allowing nearby-recipient discovery without per-query heap allocation.
/// </summary>
internal sealed class RuntimePlayerSpatialIndex
{
    private const int MaxPlayerSlots = 256;
    private const int WordsPerSection = MaxPlayerSlots / 64;
    private const float PixelsPerTile = 16f;

    private readonly WorldDimensions _dimensions;
    private readonly ulong[] _sectionSlots;
    private readonly int[] _slotSections = new int[MaxPlayerSlots];
    private int _indexedPlayers;
    private long _sectionChanges;
    private long _outOfBoundsUpdates;

    public RuntimePlayerSpatialIndex(WorldDimensions dimensions)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        _dimensions = dimensions;
        _sectionSlots = new ulong[checked(dimensions.SectionCount * WordsPerSection)];
        Array.Fill(_slotSections, -1);
    }

    public RuntimePlayerSpatialIndexSnapshot Snapshot =>
        new(_indexedPlayers, _sectionChanges, _outOfBoundsUpdates);

    public bool Update(PlayerSlotId slot, float positionX, float positionY)
    {
        int nextSection = TryGetSectionIndex(positionX, positionY, out int sectionIndex)
            ? sectionIndex
            : -1;
        if (nextSection < 0)
            _outOfBoundsUpdates++;

        int previousSection = _slotSections[slot.Value];
        if (previousSection == nextSection)
            return nextSection >= 0;

        if (previousSection >= 0)
            Clear(previousSection, slot.Value);

        _slotSections[slot.Value] = nextSection;

        if (nextSection >= 0)
        {
            Set(nextSection, slot.Value);
            if (previousSection < 0)
                _indexedPlayers++;
            else
                _sectionChanges++;
            return true;
        }

        if (previousSection >= 0)
            _indexedPlayers--;
        return false;
    }

    public bool Remove(PlayerSlotId slot)
    {
        int previousSection = _slotSections[slot.Value];
        if (previousSection < 0)
            return false;

        Clear(previousSection, slot.Value);
        _slotSections[slot.Value] = -1;
        _indexedPlayers--;
        return true;
    }

    public bool TryGetSection(PlayerSlotId slot, out WorldSectionId section)
    {
        int sectionIndex = _slotSections[slot.Value];
        if (sectionIndex < 0)
        {
            section = default;
            return false;
        }

        section = TerrariaSectionGeometry.FromLinearIndex(_dimensions, sectionIndex);
        return true;
    }

    public int CollectNearbyPlayers(
        PlayerSlotId subject,
        int radiusSections,
        Span<PlayerSlotId> destination,
        bool includeSubject = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radiusSections);
        if (destination.Length < MaxPlayerSlots)
        {
            throw new ArgumentException(
                $"Destination must have room for all {MaxPlayerSlots} possible player slots.",
                nameof(destination));
        }

        int subjectSectionIndex = _slotSections[subject.Value];
        if (subjectSectionIndex < 0)
            return 0;

        int centerX = subjectSectionIndex % _dimensions.SectionColumns;
        int centerY = subjectSectionIndex / _dimensions.SectionColumns;
        int minX = Math.Max(0, centerX - radiusSections);
        int maxX = Math.Min(_dimensions.SectionColumns - 1, centerX + radiusSections);
        int minY = Math.Max(0, centerY - radiusSections);
        int maxY = Math.Min(_dimensions.SectionRows - 1, centerY + radiusSections);
        int count = 0;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int sectionIndex = checked((y * _dimensions.SectionColumns) + x);
                int wordBase = checked(sectionIndex * WordsPerSection);

                for (int wordIndex = 0; wordIndex < WordsPerSection; wordIndex++)
                {
                    ulong word = _sectionSlots[wordBase + wordIndex];
                    if (!includeSubject && wordIndex == subject.Value / 64)
                        word &= ~(1UL << (subject.Value % 64));

                    while (word != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(word);
                        int slotValue = checked((wordIndex * 64) + bit);
                        destination[count++] = new PlayerSlotId(checked((byte)slotValue));
                        word &= word - 1;
                    }
                }
            }
        }

        return count;
    }

    private bool TryGetSectionIndex(float positionX, float positionY, out int sectionIndex)
    {
        if (!float.IsFinite(positionX) ||
            !float.IsFinite(positionY) ||
            positionX < 0f ||
            positionY < 0f ||
            positionX >= _dimensions.WidthTiles * PixelsPerTile ||
            positionY >= _dimensions.HeightTiles * PixelsPerTile)
        {
            sectionIndex = -1;
            return false;
        }

        int tileX = (int)(positionX / PixelsPerTile);
        int tileY = (int)(positionY / PixelsPerTile);
        WorldSectionId section = TerrariaSectionGeometry.FromTile(_dimensions, tileX, tileY);
        sectionIndex = TerrariaSectionGeometry.ToLinearIndex(_dimensions, section);
        return true;
    }

    private void Set(int sectionIndex, byte slot)
    {
        int wordIndex = slot / 64;
        int bit = slot % 64;
        _sectionSlots[checked((sectionIndex * WordsPerSection) + wordIndex)] |= 1UL << bit;
    }

    private void Clear(int sectionIndex, byte slot)
    {
        int wordIndex = slot / 64;
        int bit = slot % 64;
        _sectionSlots[checked((sectionIndex * WordsPerSection) + wordIndex)] &= ~(1UL << bit);
    }
}

internal readonly record struct RuntimePlayerSpatialIndexSnapshot(
    int IndexedPlayers,
    long SectionChanges,
    long OutOfBoundsUpdates);
