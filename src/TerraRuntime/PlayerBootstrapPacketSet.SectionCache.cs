using TerraRuntime.World;

namespace TerraRuntime;

public sealed partial class PlayerBootstrapPacketSet
{
    /// <summary>
    /// Publishes one pre-encoded packet-10 frame only if it still represents the current committed section
    /// revision. Background encoders never mutate this cache directly; the authoritative thread applies their
    /// immutable results through this method after validating the world revision.
    /// </summary>
    internal bool TryPublishSectionFrame(
        WorldSectionId section,
        long revision,
        ReadOnlyMemory<byte> frame)
    {
        if (_world is null || !IsValidFrame(frame) || (revision & 1L) != 0)
            return false;

        int index = TerrariaSectionGeometry.ToLinearIndex(_world.Header.Dimensions, section);
        if (_world.Tiles.GetSectionVersion(section) != revision)
            return false;

        lock (_sectionCacheGate)
        {
            if (_world.Tiles.GetSectionVersion(section) != revision)
                return false;

            if (_sectionCache.TryGetValue(index, out SectionCacheEntry existing) && existing.Version == revision)
                return true;

            _sectionCache[index] = new SectionCacheEntry(frame, [], revision);
            return true;
        }
    }

    internal bool TryGetCachedSectionFrame(
        WorldSectionId section,
        long revision,
        out ReadOnlyMemory<byte> frame)
    {
        frame = default;
        if (_world is null)
            return false;

        int index = TerrariaSectionGeometry.ToLinearIndex(_world.Header.Dimensions, section);
        lock (_sectionCacheGate)
        {
            if (!_sectionCache.TryGetValue(index, out SectionCacheEntry entry) || entry.Version != revision)
                return false;

            frame = entry.TileSectionFrame;
            return true;
        }
    }
}
