namespace TerraRuntime.World;

/// <summary>
/// Bounded authoritative liquid relaxation for protocol-326 worlds.  Terraria's full Liquid.UpdateLiquid
/// machinery is much larger; this slice preserves the important runtime contract: liquid work is amortized,
/// deduplicated, remains on the sole game-thread writer, and never turns a client bucket into an unbounded scan.
/// Existing world liquid is discovered incrementally so loaded/generated worlds begin settling without a
/// stop-the-world initialization pass.
/// </summary>
public sealed class VanillaWorldLiquidSimulator1458
{
    public const int DefaultWorkBudgetPerTick = 64;
    public const int DefaultDiscoveryBudgetPerTick = 4096;
    public const int MaximumPendingCells = 16384;

    private readonly WorldTileStore tiles;
    private readonly int workBudget;
    private readonly int discoveryBudget;
    private int discoveryCursor;
    private bool discoveryComplete;

    public VanillaWorldLiquidSimulator1458(
        WorldTileStore tiles,
        int workBudgetPerTick = DefaultWorkBudgetPerTick,
        int discoveryBudgetPerTick = DefaultDiscoveryBudgetPerTick)
    {
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workBudgetPerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(discoveryBudgetPerTick);
        workBudget = workBudgetPerTick;
        discoveryBudget = discoveryBudgetPerTick;
    }

    public int WorkBudgetPerTick => workBudget;
    public int DiscoveryBudgetPerTick => discoveryBudget;
    public bool DiscoveryComplete => discoveryComplete;

    /// <summary>
    /// Advances at most one bounded slice.  Each processed cell may mutate at most two liquid cells, therefore
    /// callers can provide a fixed stack/pooled span and replicate only committed authoritative changes.
    /// </summary>
    public int Tick(Span<WorldLiquidSimulationChange> changes)
    {
        DiscoverExistingLiquid();
        PromoteBuffered();

        int changed = 0;
        int processed = 0;
        while (processed < workBudget && tiles.LiquidUpdates.TryDequeue(out WorldLiquidUpdate update))
        {
            processed++;
            RelaxCell(update.X, update.Y, changes, ref changed);
        }

        return changed;
    }

    private void DiscoverExistingLiquid()
    {
        if (discoveryComplete || PendingCount >= MaximumPendingCells)
            return;

        int height = tiles.Dimensions.HeightTiles;
        int count = tiles.Count;
        int remaining = Math.Min(discoveryBudget, count - discoveryCursor);
        for (int i = 0; i < remaining && PendingCount < MaximumPendingCells; i++, discoveryCursor++)
        {
            int x = discoveryCursor / height;
            int y = discoveryCursor % height;
            WorldTile tile = tiles.Get(x, y);
            if (tile.LiquidAmount != 0 && NeedsRelaxation(x, y, in tile))
                _ = tiles.LiquidUpdates.TryEnqueue(x, y);
        }

        if (discoveryCursor >= count)
            discoveryComplete = true;
    }

    private void PromoteBuffered()
    {
        int promoted = 0;
        while (promoted < workBudget && PendingCount < MaximumPendingCells &&
               tiles.LiquidUpdates.TryDequeueBuffered(out int x, out int y))
        {
            _ = tiles.LiquidUpdates.TryEnqueue(x, y);
            promoted++;
        }
    }

    private void RelaxCell(int x, int y, Span<WorldLiquidSimulationChange> changes, ref int changed)
    {
        if (!Contains(x, y))
            return;

        WorldTile source = tiles.Get(x, y);
        if (source.LiquidAmount == 0 || IsLiquidBarrier(in source))
            return;

        if (y + 1 < tiles.Dimensions.HeightTiles)
        {
            WorldTile below = tiles.Get(x, y + 1);
            if (CanAccept(in below, source.LiquidKind))
            {
                int capacity = byte.MaxValue - below.LiquidAmount;
                if (capacity > 0)
                {
                    int moved = Math.Min(source.LiquidAmount, capacity);
                    if (moved > 0)
                    {
                        ApplyTransfer(x, y, x, y + 1, in source, in below, moved, changes, ref changed);
                        return;
                    }
                }
            }
        }

        // When gravity cannot consume the cell, level toward the lower horizontal neighbour.  A capped
        // transfer avoids a single source cell creating a large same-tick cascade and keeps packet fan-out bounded.
        int targetX = -1;
        WorldTile target = default;
        int targetAmount = int.MaxValue;
        ConsiderHorizontalTarget(x - 1, y, in source, ref targetX, ref target, ref targetAmount);
        ConsiderHorizontalTarget(x + 1, y, in source, ref targetX, ref target, ref targetAmount);

        if (targetX < 0)
            return;

        int difference = source.LiquidAmount - target.LiquidAmount;
        int horizontalMove = Math.Min(32, difference / 2);
        if (horizontalMove <= 0)
            return;

        ApplyTransfer(x, y, targetX, y, in source, in target, horizontalMove, changes, ref changed);
    }


    private void ConsiderHorizontalTarget(
        int candidateX,
        int y,
        in WorldTile source,
        ref int targetX,
        ref WorldTile target,
        ref int targetAmount)
    {
        if (!Contains(candidateX, y))
            return;
        WorldTile candidate = tiles.Get(candidateX, y);
        if (!CanAccept(in candidate, source.LiquidKind) || candidate.LiquidAmount >= source.LiquidAmount - 1)
            return;
        if (candidate.LiquidAmount < targetAmount)
        {
            targetX = candidateX;
            target = candidate;
            targetAmount = candidate.LiquidAmount;
        }
    }

    private void ApplyTransfer(
        int sourceX,
        int sourceY,
        int targetX,
        int targetY,
        in WorldTile sourceBefore,
        in WorldTile targetBefore,
        int amount,
        Span<WorldLiquidSimulationChange> changes,
        ref int changed)
    {
        WorldTile source = sourceBefore;
        WorldTile target = targetBefore;

        source.LiquidAmount = checked((byte)(source.LiquidAmount - amount));
        if (source.LiquidAmount == 0)
            source.LiquidKind = WorldLiquidKind.Water;
        target.LiquidAmount = checked((byte)(target.LiquidAmount + amount));
        target.LiquidKind = sourceBefore.LiquidKind;

        tiles.Set(sourceX, sourceY, in source);
        tiles.Set(targetX, targetY, in target);
        Record(sourceX, sourceY, in source, changes, ref changed);
        Record(targetX, targetY, in target, changes, ref changed);

        BufferAffected(sourceX, sourceY);
        BufferAffected(targetX, targetY);
    }

    private void BufferAffected(int x, int y)
    {
        if (PendingCount >= MaximumPendingCells)
            return;

        TryBuffer(x, y);
        TryBuffer(x - 1, y);
        TryBuffer(x + 1, y);
        TryBuffer(x, y - 1);
        TryBuffer(x, y + 1);
    }

    private void TryBuffer(int x, int y)
    {
        if (PendingCount < MaximumPendingCells)
            _ = tiles.LiquidUpdates.TryBuffer(x, y);
    }

    private bool NeedsRelaxation(int x, int y, in WorldTile source)
    {
        if (source.LiquidAmount == 0 || IsLiquidBarrier(in source))
            return false;

        if (y + 1 < tiles.Dimensions.HeightTiles)
        {
            WorldTile below = tiles.Get(x, y + 1);
            if (CanAccept(in below, source.LiquidKind) && below.LiquidAmount < byte.MaxValue)
                return true;
        }

        if (x > 0)
        {
            WorldTile left = tiles.Get(x - 1, y);
            if (CanAccept(in left, source.LiquidKind) && left.LiquidAmount + 1 < source.LiquidAmount)
                return true;
        }
        if (x + 1 < tiles.Dimensions.WidthTiles)
        {
            WorldTile right = tiles.Get(x + 1, y);
            if (CanAccept(in right, source.LiquidKind) && right.LiquidAmount + 1 < source.LiquidAmount)
                return true;
        }

        return false;
    }

    private static bool CanAccept(in WorldTile tile, WorldLiquidKind kind) =>
        !IsLiquidBarrier(in tile) &&
        (tile.LiquidAmount == 0 || tile.LiquidKind == kind);

    private static bool IsLiquidBarrier(in WorldTile tile) =>
        tile.IsActive &&
        !tile.IsActuated &&
        VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
        !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);

    private bool Contains(int x, int y) =>
        (uint)x < (uint)tiles.Dimensions.WidthTiles &&
        (uint)y < (uint)tiles.Dimensions.HeightTiles;

    private int PendingCount => tiles.LiquidUpdates.ActiveCount + tiles.LiquidUpdates.BufferedCount;

    private static void Record(
        int x,
        int y,
        in WorldTile tile,
        Span<WorldLiquidSimulationChange> changes,
        ref int changed)
    {
        if ((uint)changed >= (uint)changes.Length)
            return;
        changes[changed++] = new WorldLiquidSimulationChange(x, y, tile.LiquidAmount, tile.LiquidKind);
    }
}

public readonly record struct WorldLiquidSimulationChange(
    int X,
    int Y,
    byte Amount,
    WorldLiquidKind Kind);
