namespace TerraRuntime.World;

/// <summary>
/// Reusable authoritative-thread handoff from dirty section ids to immutable tile snapshots.
/// Work is bounded by a fixed capacity so compression, persistence and other background consumers can
/// receive stable section images without scanning the world or allocating a new coordination buffer per tick.
/// </summary>
public sealed class DirtySectionSnapshotBatcher
{
    private readonly WorldTileStore _tiles;
    private readonly WorldSectionId[] _drainedSections;
    private readonly WorldSectionTileSnapshot?[] _capturedSnapshots;
    private int _count;

    public DirtySectionSnapshotBatcher(WorldTileStore tiles, int capacity)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _tiles = tiles;
        _drainedSections = new WorldSectionId[capacity];
        _capturedSnapshots = new WorldSectionTileSnapshot?[capacity];
    }

    public int Capacity => _capturedSnapshots.Length;

    public int Count => _count;

    /// <summary>
    /// Snapshots captured by the most recent <see cref="Capture"/> call. The span remains valid until the
    /// next capture; each snapshot and its tile backing array are immutable after publication.
    /// </summary>
    public ReadOnlySpan<WorldSectionTileSnapshot?> Captured => _capturedSnapshots.AsSpan(0, _count);

    /// <summary>
    /// Drains at most <see cref="Capacity"/> dirty section ids and snapshots each stable section. A section
    /// that cannot be captured consistently is marked dirty again instead of being silently lost.
    /// </summary>
    public int Capture()
    {
        if (_count != 0)
        {
            Array.Clear(_capturedSnapshots, 0, _count);
            _count = 0;
        }

        int drained = _tiles.DirtySections.Drain(_drainedSections);
        int written = 0;
        for (int i = 0; i < drained; i++)
        {
            WorldSectionId section = _drainedSections[i];
            if (_tiles.TryCaptureSectionSnapshot(section, out WorldSectionTileSnapshot? snapshot) && snapshot is not null)
            {
                _capturedSnapshots[written++] = snapshot;
                continue;
            }

            _tiles.DirtySections.MarkDirty(section);
        }

        _count = written;
        return written;
    }
}
