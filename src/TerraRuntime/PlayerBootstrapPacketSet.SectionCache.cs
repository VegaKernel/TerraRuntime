using TerraRuntime.World;

namespace TerraRuntime;

internal readonly record struct SectionPacketCacheSnapshot(
    int Entries,
    long Bytes,
    int MaximumEntries);

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

    internal SectionPacketCacheSnapshot CaptureSectionCacheSnapshot()
    {
        int maximumEntries = _world?.Header.Dimensions.SectionCount ?? _sectionCache.Count;
        lock (_sectionCacheGate)
        {
            long bytes = 0;
            foreach (SectionCacheEntry entry in _sectionCache.Values)
            {
                bytes += entry.TileSectionFrame.Length;
                for (int i = 0; i < entry.PostSectionFrames.Length; i++)
                    bytes += entry.PostSectionFrames[i].Length;
            }

            return new SectionPacketCacheSnapshot(
                Entries: _sectionCache.Count,
                Bytes: bytes,
                MaximumEntries: maximumEntries);
        }
    }
}
