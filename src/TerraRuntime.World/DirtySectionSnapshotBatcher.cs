namespace TerraRuntime.World;

/// <summary>
/// Reusable authoritative-thread handoff from dirty section ids to immutable tile snapshots.
/// Work is bounded by a fixed capacity so compression, persistence and other background consumers can
/// receive stable section images without scanning the world or allocating a new coordination buffer per tick.
/// </summary>
public sealed class DirtySectionSnapshotBatcher
{
    private readonly WorldTileStore _tiles;
    private readonly DirtySectionTracker _dirtySections;
    private readonly WorldSectionId[] _drainedSections;
    private readonly WorldSectionTileSnapshot?[] _capturedSnapshots;
    private int _count;

    public DirtySectionSnapshotBatcher(WorldTileStore tiles, int capacity)
        : this(tiles, tiles?.DirtySections ?? throw new ArgumentNullException(nameof(tiles)), capacity)
    {
    }

    public DirtySectionSnapshotBatcher(
        WorldTileStore tiles,
        DirtySectionTracker dirtySections,
        int capacity)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(dirtySections);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _tiles = tiles;
        _dirtySections = dirtySections;
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

    public int Capture() => Capture(Capacity);

    public int Capture(int maximum) => Capture(maximum, excludedLinearIndices: null);

    /// <summary>
    /// Drains and snapshots at most <paramref name="maximum"/> dirty sections from this batcher's tracker. Callers can
    /// therefore give network rebuild and persistence independent backlogs over the same tile store. Sections listed
    /// in <paramref name="excludedLinearIndices"/> remain dirty. A section that cannot be captured consistently is
    /// marked dirty again on the same tracker instead of being silently lost.
    /// </summary>
    public int Capture(int maximum, IReadOnlySet<int>? excludedLinearIndices)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        if (maximum > Capacity)
            throw new ArgumentOutOfRangeException(nameof(maximum));

        if (_count != 0)
        {
            Array.Clear(_capturedSnapshots, 0, _count);
            _count = 0;
        }

        if (maximum == 0)
            return 0;

        int drained = _dirtySections.Drain(
            _drainedSections.AsSpan(0, maximum),
            excludedLinearIndices);
        int written = 0;
        for (int i = 0; i < drained; i++)
        {
            WorldSectionId section = _drainedSections[i];
            if (_tiles.TryCaptureSectionSnapshot(section, out WorldSectionTileSnapshot? snapshot) && snapshot is not null)
            {
                _capturedSnapshots[written++] = snapshot;
                continue;
            }

            _dirtySections.MarkDirty(section);
        }

        _count = written;
        return written;
    }
}
