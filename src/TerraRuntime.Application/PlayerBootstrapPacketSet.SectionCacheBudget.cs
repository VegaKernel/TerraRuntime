using TerraRuntime.World;

namespace TerraRuntime.Application;

public sealed partial class PlayerBootstrapPacketSet
{
    /// <summary>
    /// Correctness-first byte budget for non-bootstrap packet-10 frames. Base spawn sections stay pinned because
    /// every joining player needs them; all other section frames compete inside this deterministic LRU budget.
    /// The value is intentionally explicit and observable until representative-world measurements justify tuning.
    /// </summary>
    internal const long DefaultDynamicSectionCacheByteBudget = 64L * 1024 * 1024;

    private readonly LinkedList<int> _dynamicSectionCacheLru = new();
    private readonly Dictionary<int, LinkedListNode<int>> _dynamicSectionCacheLruNodes = new();
    private long _dynamicSectionCacheByteBudget = DefaultDynamicSectionCacheByteBudget;
    private long _dynamicSectionCacheBytes;
    private long _sectionCacheEvictions;

    internal bool IsPinnedBaseSection(WorldSectionId section)
    {
        if (_world is null)
            return false;

        int index = TerrariaSectionGeometry.ToLinearIndex(_world.Header.Dimensions, section);
        return IsPinnedBaseSectionIndex(index);
    }

    internal void SetDynamicSectionCacheByteBudgetForTesting(long byteBudget)
    {
        if (byteBudget < ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(byteBudget));

        lock (_sectionCacheGate)
        {
            _dynamicSectionCacheByteBudget = byteBudget;
            TrimDynamicSectionCacheUnderLock();
        }
    }

    private bool TryGetOrEncodeSectionBounded(WorldSectionId section, out SectionCacheEntry entry)
    {
        if (IsPinnedBaseSection(section))
            return TryGetOrEncodeSection(section, out entry);

        WorldFileData world = _world!;
        int index = TerrariaSectionGeometry.ToLinearIndex(world.Header.Dimensions, section);
        long version = world.Tiles.GetSectionVersion(section);
        if ((version & 1L) != 0)
        {
            entry = default;
            return false;
        }

        lock (_sectionCacheGate)
        {
            if (_sectionCache.TryGetValue(index, out entry) && entry.Version == version)
            {
                TouchDynamicSectionCacheEntryUnderLock(index);
                return true;
            }

            RemoveStaleDynamicSectionCacheEntryUnderLock(index);
        }

        if (!world.Tiles.TryCaptureSectionSnapshot(section, out WorldSectionTileSnapshot? snapshot) ||
            snapshot is null ||
            !TryEncodeSection(world, snapshot, out SectionCacheEntry encoded))
        {
            entry = default;
            return false;
        }

        long currentVersion = world.Tiles.GetSectionVersion(section);
        if (currentVersion != snapshot.Revision)
        {
            entry = default;
            return false;
        }

        lock (_sectionCacheGate)
        {
            currentVersion = world.Tiles.GetSectionVersion(section);
            if (currentVersion != snapshot.Revision)
            {
                entry = default;
                return false;
            }

            if (_sectionCache.TryGetValue(index, out SectionCacheEntry existing) &&
                existing.Version == currentVersion)
            {
                TouchDynamicSectionCacheEntryUnderLock(index);
                entry = existing;
                return true;
            }

            RemoveStaleDynamicSectionCacheEntryUnderLock(index);
            if (!TryStoreSectionCacheEntryUnderLock(index, encoded))
            {
                entry = default;
                return false;
            }

            entry = encoded;
            return true;
        }
    }

    private bool TryStoreSectionCacheEntryUnderLock(int index, SectionCacheEntry entry)
    {
        if (IsPinnedBaseSectionIndex(index))
        {
            RemoveDynamicLruNodeUnderLock(index, subtractCachedEntry: true);
            _sectionCache[index] = entry;
            return true;
        }

        long entryBytes = GetSectionCacheEntryBytes(entry);
        if (entryBytes > _dynamicSectionCacheByteBudget)
            return false;

        RemoveDynamicLruNodeUnderLock(index, subtractCachedEntry: true);

        while (_dynamicSectionCacheBytes + entryBytes > _dynamicSectionCacheByteBudget)
        {
            LinkedListNode<int>? oldest = _dynamicSectionCacheLru.First;
            if (oldest is null)
                return false;

            EvictDynamicSectionCacheEntryUnderLock(oldest.Value);
        }

        _sectionCache[index] = entry;
        _dynamicSectionCacheBytes += entryBytes;
        LinkedListNode<int> node = _dynamicSectionCacheLru.AddLast(index);
        _dynamicSectionCacheLruNodes[index] = node;
        _sectionCacheFailedRebuildGenerations.Remove(index);
        return true;
    }

    private void TouchDynamicSectionCacheEntryUnderLock(int index)
    {
        if (!_dynamicSectionCacheLruNodes.TryGetValue(index, out LinkedListNode<int>? node) ||
            ReferenceEquals(node, _dynamicSectionCacheLru.Last))
        {
            return;
        }

        _dynamicSectionCacheLru.Remove(node);
        _dynamicSectionCacheLru.AddLast(node);
    }

    private void RemoveStaleDynamicSectionCacheEntryUnderLock(int index)
    {
        if (IsPinnedBaseSectionIndex(index))
            return;

        RemoveDynamicLruNodeUnderLock(index, subtractCachedEntry: true);
        _sectionCache.Remove(index);
    }

    private void TrimDynamicSectionCacheUnderLock()
    {
        while (_dynamicSectionCacheBytes > _dynamicSectionCacheByteBudget)
        {
            LinkedListNode<int>? oldest = _dynamicSectionCacheLru.First;
            if (oldest is null)
            {
                _dynamicSectionCacheBytes = 0;
                return;
            }

            EvictDynamicSectionCacheEntryUnderLock(oldest.Value);
        }
    }

    private void EvictDynamicSectionCacheEntryUnderLock(int index)
    {
        if (!_dynamicSectionCacheLruNodes.TryGetValue(index, out LinkedListNode<int>? node))
            return;

        _dynamicSectionCacheLru.Remove(node);
        _dynamicSectionCacheLruNodes.Remove(index);
        if (_sectionCache.Remove(index, out SectionCacheEntry entry))
            _dynamicSectionCacheBytes -= GetSectionCacheEntryBytes(entry);

        _sectionCacheFailedRebuildGenerations.Remove(index);
        Interlocked.Increment(ref _sectionCacheEvictions);
    }

    private void RemoveDynamicLruNodeUnderLock(int index, bool subtractCachedEntry)
    {
        if (!_dynamicSectionCacheLruNodes.Remove(index, out LinkedListNode<int>? node))
            return;

        _dynamicSectionCacheLru.Remove(node);
        if (subtractCachedEntry && _sectionCache.TryGetValue(index, out SectionCacheEntry existing))
            _dynamicSectionCacheBytes -= GetSectionCacheEntryBytes(existing);
    }

    private bool IsPinnedBaseSectionIndex(int index)
    {
        WorldFileData? world = _world;
        if (world is null)
            return false;

        for (int i = 0; i < _baseSections.Length; i++)
        {
            if (TerrariaSectionGeometry.ToLinearIndex(world.Header.Dimensions, _baseSections[i]) == index)
                return true;
        }

        return false;
    }

    private static long GetSectionCacheEntryBytes(SectionCacheEntry entry)
    {
        long bytes = entry.TileSectionFrame.Length;
        for (int i = 0; i < entry.PostSectionFrames.Length; i++)
            bytes += entry.PostSectionFrames[i].Length;
        return bytes;
    }
}
