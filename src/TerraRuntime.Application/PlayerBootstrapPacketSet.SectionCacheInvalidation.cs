using TerraRuntime.World;

namespace TerraRuntime.Application;

public sealed partial class PlayerBootstrapPacketSet
{
    private long _sectionCacheInvalidations;

    /// <summary>
    /// Physically removes a cache entry only after its committed tile-section revision no longer matches.
    /// Tile mutation itself remains lock-free with respect to the cache: the version token is the immediate
    /// invalidation boundary, while stale byte ownership is reclaimed lazily by the next cache read/publication.
    /// </summary>
    private bool InvalidateStaleSectionCacheEntryUnderLock(int index, long currentRevision)
    {
        if (!_sectionCache.TryGetValue(index, out SectionCacheEntry entry) || entry.Version == currentRevision)
            return false;

        if (IsPinnedBaseSectionIndex(index))
        {
            _sectionCache.Remove(index);
        }
        else
        {
            RemoveStaleDynamicSectionCacheEntryUnderLock(index);
        }

        _sectionCacheFailedRebuildGenerations.Remove(index);
        Interlocked.Increment(ref _sectionCacheInvalidations);
        return true;
    }
}
