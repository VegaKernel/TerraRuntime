using System.Collections;

namespace TerraRuntime.World;

/// <summary>
/// Runtime liquid work state. Active entries preserve the per-cell delay/kill state used by liquid
/// simulation, while buffered cells preserve deferred work without requiring a full-world rediscovery scan.
/// Membership in each queue is deduplicated independently.
/// </summary>
public sealed class WorldLiquidUpdateQueue
{
    private readonly WorldDimensions _dimensions;
    private readonly int _tileCount;
    private Queue<WorldLiquidUpdateEntry> _active = new();
    private Queue<int> _buffered = new();
    private BitArray? _activeMembership;
    private BitArray? _bufferMembership;
    private BitArray? _skipNextUpdate;

    public WorldLiquidUpdateQueue(WorldDimensions dimensions)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        _dimensions = dimensions;

        long tileCount = (long)dimensions.WidthTiles * dimensions.HeightTiles;
        if (tileCount > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dimensions),
                "World contains too many tiles for liquid queue indexing.");
        }

        _tileCount = checked((int)tileCount);
    }

    public WorldDimensions Dimensions => _dimensions;

    public int ActiveCount => _active.Count;

    public int BufferedCount => _buffered.Count;

    public bool HasPendingWork => _active.Count != 0 || _buffered.Count != 0;

    public bool TryEnqueue(int x, int y, int delay = 0, int kill = 0)
    {
        if (!TryGetIndex(x, y, out int index))
            return false;

        _activeMembership ??= new BitArray(_tileCount);
        if (_activeMembership[index])
            return false;

        _activeMembership[index] = true;
        _active.Enqueue(new WorldLiquidUpdateEntry(index, delay, kill));
        return true;
    }

    public bool TryBuffer(int x, int y)
    {
        if (!TryGetIndex(x, y, out int index))
            return false;

        _bufferMembership ??= new BitArray(_tileCount);
        if (_bufferMembership[index])
            return false;

        _bufferMembership[index] = true;
        _buffered.Enqueue(index);
        return true;
    }

    /// <summary>
    /// Sets vanilla's per-cell <c>Tile.skipLiquid</c>. <c>Liquid.Update</c> marks both ends of a downward
    /// transfer, and <c>Liquid.UpdateLiquid</c>'s ordinary loop then passes over the marked cell once instead of
    /// updating it, which is what keeps falling water from advancing twice as fast as the source. The flag is
    /// transient simulation state that never reaches the saved world, so it lives with the delay/kill work state
    /// rather than on the tile.
    /// </summary>
    public void SetSkipNextUpdate(int x, int y)
    {
        if (!TryGetIndex(x, y, out int index))
            return;

        _skipNextUpdate ??= new BitArray(_tileCount);
        _skipNextUpdate[index] = true;
    }

    /// <summary>
    /// Reads and clears the skip flag, mirroring the <c>else</c> arm of <c>Liquid.UpdateLiquid</c>'s ordinary
    /// loop: a marked cell is passed over and unmarked in the same step.
    /// </summary>
    public bool ConsumeSkipNextUpdate(int x, int y)
    {
        if (_skipNextUpdate is null || !TryGetIndex(x, y, out int index) || !_skipNextUpdate[index])
            return false;

        _skipNextUpdate[index] = false;
        return true;
    }

    /// <summary>
    /// Clears the skip flag without consuming a pass. <c>Liquid.UpdateLiquid</c>'s quick-fall loop clears it
    /// after every update, and a successful <c>Liquid.AddWater</c> clears it as it takes a fresh array slot.
    /// </summary>
    public void ClearSkipNextUpdate(int x, int y)
    {
        if (_skipNextUpdate is not null && TryGetIndex(x, y, out int index))
            _skipNextUpdate[index] = false;
    }

    public bool IsSkipNextUpdate(int x, int y) =>
        _skipNextUpdate is not null && TryGetIndex(x, y, out int index) && _skipNextUpdate[index];

    public bool IsQueued(int x, int y)
    {
        if (!TryGetIndex(x, y, out int index) || _activeMembership is null)
            return false;

        return _activeMembership[index];
    }

    public bool IsBuffered(int x, int y)
    {
        if (!TryGetIndex(x, y, out int index) || _bufferMembership is null)
            return false;

        return _bufferMembership[index];
    }

    public bool TryDequeue(out WorldLiquidUpdate update)
    {
        if (!_active.TryDequeue(out WorldLiquidUpdateEntry entry))
        {
            update = default;
            return false;
        }

        _activeMembership![entry.TileIndex] = false;
        DecodeIndex(entry.TileIndex, out int x, out int y);
        update = new WorldLiquidUpdate(x, y, entry.Delay, entry.Kill);
        return true;
    }

    public bool TryDequeueBuffered(out int x, out int y)
    {
        if (!_buffered.TryDequeue(out int index))
        {
            x = 0;
            y = 0;
            return false;
        }

        _bufferMembership![index] = false;
        DecodeIndex(index, out x, out y);
        return true;
    }

    public void Clear()
    {
        _active.Clear();
        _buffered.Clear();
        _activeMembership?.SetAll(false);
        _bufferMembership?.SetAll(false);
        _skipNextUpdate?.SetAll(false);
    }

    internal WorldLiquidUpdateEntry[] CaptureActiveSnapshot() => _active.ToArray();

    internal int[] CaptureBufferSnapshot() => _buffered.ToArray();

    internal bool TryRestoreSnapshot(
        ReadOnlySpan<WorldLiquidUpdateEntry> active,
        ReadOnlySpan<int> buffered)
    {
        if (active.Length > _tileCount || buffered.Length > _tileCount)
            return false;

        var restoredActive = new Queue<WorldLiquidUpdateEntry>(active.Length);
        var restoredBuffered = new Queue<int>(buffered.Length);
        BitArray? activeMembership = active.Length == 0 ? null : new BitArray(_tileCount);
        BitArray? bufferMembership = buffered.Length == 0 ? null : new BitArray(_tileCount);

        foreach (WorldLiquidUpdateEntry entry in active)
        {
            if ((uint)entry.TileIndex >= (uint)_tileCount || activeMembership![entry.TileIndex])
                return false;

            activeMembership[entry.TileIndex] = true;
            restoredActive.Enqueue(entry);
        }

        foreach (int index in buffered)
        {
            if ((uint)index >= (uint)_tileCount || bufferMembership![index])
                return false;

            bufferMembership[index] = true;
            restoredBuffered.Enqueue(index);
        }

        _active = restoredActive;
        _buffered = restoredBuffered;
        _activeMembership = activeMembership;
        _bufferMembership = bufferMembership;
        return true;
    }

    private bool TryGetIndex(int x, int y, out int index)
    {
        if ((uint)x >= (uint)_dimensions.WidthTiles ||
            (uint)y >= (uint)_dimensions.HeightTiles)
        {
            index = 0;
            return false;
        }

        index = (x * _dimensions.HeightTiles) + y;
        return true;
    }

    private void DecodeIndex(int index, out int x, out int y)
    {
        x = index / _dimensions.HeightTiles;
        y = index % _dimensions.HeightTiles;
    }
}

/// <summary>
/// One active liquid simulation cell in tile coordinates.
/// </summary>
public readonly record struct WorldLiquidUpdate(int X, int Y, int Delay, int Kill);

internal readonly record struct WorldLiquidUpdateEntry(int TileIndex, int Delay, int Kill);
