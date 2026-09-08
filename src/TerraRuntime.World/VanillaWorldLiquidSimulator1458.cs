using TerraRuntime.Contracts.Gameplay;

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
    // TerrariaServer 1.4.5.8 Liquid.UpdateLiquid defaults to maxLiquid=25,000 and cycles=10,
    // therefore an empty dedicated server processes 2,500 active liquid entries per update. Player count
    // lowers curMaxLiquid and raises cycles; Tick() derives that exact live slice and this constant is only the cap.
    public const int DefaultWorkBudgetPerTick = 2500;
    public const int DefaultDiscoveryBudgetPerTick = 4096;
    // TerrariaServer 1.4.5.8 defaults: maxLiquid=25000 and maxLiquidBuffer=50000.
    // AddWater admits active entries while numLiquid < curMaxLiquid - 1 and LiquidBuffer admits
    // entries while numLiquidBuffer < maxLiquidBuffer - 2, yielding a source-backed total bound of 74,997.
    public const int LoadingActiveLiquidCapacity1458 = 24999;
    public const int LoadingBufferedLiquidCapacity1458 = 49998;
    public const int MaximumPendingCells = LoadingActiveLiquidCapacity1458 + LoadingBufferedLiquidCapacity1458;
    public const int LoadingWorkBudgetPerUpdate1458 = 2500;
    public const int MaximumChangesPerProcessedCell = 9;
    internal const int LavaFlowDelayUpdates1458 = 5;
    internal const int HoneyFlowDelayUpdates1458 = 10;
    public const int DedicatedServerCountedPlayerSlots1458 = 15;
    internal const int DedicatedServerBaseKillUpdates1458 = 10;
    internal const int DedicatedServerKillPlayerDivisor1458 = 3;
    internal const int GeneratingOrLoadingKillUpdates1458 = 8;
    internal const int UnderworldLayerOffset1458 = 200;
    internal const byte UnderworldWaterEvaporationPerUpdate1458 = 2;

    private readonly WorldTileStore tiles;
    private readonly VanillaWorldTileMutationService tileMutations;
    private readonly IVanillaLiquidTileSideEffectSink1458 sideEffects;
    private readonly int workBudget;
    private readonly int discoveryBudget;
    private int discoveryCursor;
    private bool discoveryComplete;
    private bool useInitialPopulationWrites;

    public VanillaWorldLiquidSimulator1458(
        WorldTileStore tiles,
        int workBudgetPerTick = DefaultWorkBudgetPerTick,
        int discoveryBudgetPerTick = DefaultDiscoveryBudgetPerTick,
        IVanillaLiquidTileSideEffectSink1458? sideEffects = null)
    {
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        tileMutations = new VanillaWorldTileMutationService(tiles);
        this.sideEffects = sideEffects ?? new LocalLiquidTileSideEffectSink1458(tileMutations, tiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workBudgetPerTick);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(discoveryBudgetPerTick);
        workBudget = workBudgetPerTick;
        discoveryBudget = discoveryBudgetPerTick;
        if (tiles.IsPostLoadLiquidPrepared)
        {
            discoveryCursor = tiles.Count;
            discoveryComplete = true;
        }
    }

    public int WorkBudgetPerTick => workBudget;
    public int DiscoveryBudgetPerTick => discoveryBudget;
    public bool DiscoveryComplete => discoveryComplete;

    /// <summary>
    /// Advances at most one bounded slice. Each processed cell may mutate at most
    /// <see cref="MaximumChangesPerProcessedCell"/> liquid cells, so callers can provide a fixed
    /// stack/pooled span and replicate only committed authoritative changes.
    /// </summary>
    public int Tick(int activeServerPlayersInLiquidWindow, Span<WorldLiquidSimulationChange> changes)
    {
        if ((uint)activeServerPlayersInLiquidWindow > DedicatedServerCountedPlayerSlots1458)
        {
            throw new ArgumentOutOfRangeException(
                nameof(activeServerPlayersInLiquidWindow),
                $"TerrariaServer 1.4.5.8 liquid scheduling counts only slots 0..{DedicatedServerCountedPlayerSlots1458 - 1}.");
        }

        int stableKillUpdates = DedicatedServerBaseKillUpdates1458 +
            activeServerPlayersInLiquidWindow / DedicatedServerKillPlayerDivisor1458;

        int cycles = 10 + activeServerPlayersInLiquidWindow / DedicatedServerKillPlayerDivisor1458;
        int currentMaximum = 25_000 - activeServerPlayersInLiquidWindow * 250;
        int sourceSlice = Math.Max(1, currentMaximum / cycles);
        int liveBudget = Math.Min(workBudget, sourceSlice);
        return TickCore(stableKillUpdates, VanillaLiquidUpdateMode1458.Ordinary, liveBudget, changes);
    }

    /// <summary>
    /// Runs one source-backed <c>Liquid.quickSettle</c>/<c>quickFall</c> scheduler slice used while
    /// TerrariaServer 1.4.5.8 is generating or loading a world. In that state <c>UpdateLiquid</c>
    /// uses a fixed kill threshold of 8, forces every active entry's delay to 10 and therefore bypasses
    /// ordinary lava/honey flow delays. The caller remains responsible for the higher-level QuickWater /
    /// WaterCheck orchestration and for draining until the load/generation settle has completed.
    /// </summary>
    public int TickQuickSettle(Span<WorldLiquidSimulationChange> changes)
    {
        bool previous = useInitialPopulationWrites;
        useInitialPopulationWrites = true;
        try
        {
            return TickCore(GeneratingOrLoadingKillUpdates1458, VanillaLiquidUpdateMode1458.QuickSettle, workBudget, changes);
        }
        finally
        {
            useInitialPopulationWrites = previous;
        }
    }

    /// <summary>
    /// Replays TerrariaServer 1.4.5.8 <c>Liquid.QuickWater</c> for an already decoded world. This is the
    /// generating/loading fast-settle pre-pass, not the live bounded scheduler. Its source-backed default scan
    /// bounds are y=3..height-3 and x=4..width-5, processed bottom-up. Boulder-family tiles and tile 546 are
    /// temporarily non-solid while Bubble (379) remains an explicit barrier, matching <c>tilesIgnoreWater</c>.
    /// </summary>
    public void QuickWater(int minY = -1, int maxY = -1)
    {
        int width = tiles.Dimensions.WidthTiles;
        int height = tiles.Dimensions.HeightTiles;
        if (width < 9 || height < 7)
            return;

        minY = minY < 0 ? 3 : minY;
        maxY = maxY < 0 ? height - 3 : maxY;
        if (minY < 0 || maxY >= height || minY > maxY)
            throw new ArgumentOutOfRangeException(nameof(minY));

        for (int y = maxY; y >= minY; y--)
        {
            for (int x = 4; x < width - 4; x++)
            {
                if (tiles.Get(x, y).LiquidAmount != 0)
                    SettleWaterAt1458(x, y);
            }
        }
    }

    private void SettleWaterAt1458(int originX, int originY)
    {
        WorldTile origin = tiles.Get(originX, originY);
        if (origin.LiquidAmount == 0 || IsQuickWaterBubbleBarrier1458(in origin))
            return;

        int x = originX;
        int y = originY;
        bool originWasLava = origin.LiquidKind == WorldLiquidKind.Lava;
        bool originWasHoney = origin.LiquidKind == WorldLiquidKind.Honey;
        bool originWasShimmer = origin.LiquidKind == WorldLiquidKind.Shimmer;
        int remaining = origin.LiquidAmount;
        WorldLiquidKind kind = origin.LiquidKind;

        origin.LiquidAmount = 0;
        origin.LiquidKind = WorldLiquidKind.Water;
        tiles.SetInitialPopulationTile(originX, originY, in origin);

        bool firstRow = true;
        while (true)
        {
            WorldTile below = tiles.Get(x, y + 1);
            while (y < tiles.Dimensions.HeightTiles - 5 &&
                   below.LiquidAmount == 0 &&
                   IsQuickWaterPassable1458(in below))
            {
                y++;
                firstRow = false;
                below = tiles.Get(x, y + 1);
            }

            // Loading (not generating) keeps the original liquid kind; the worldgen water-line conversion is inactive.
            int direction = -1;
            int offset = 0;
            int lastEmptyDirection = -1;
            int lastEmptyOffset = 0;
            bool rightBlocked = false;
            bool leftBlocked = false;
            bool canFallFromRow = false;

            while (true)
            {
                int currentX = x + offset * direction;
                WorldTile current = tiles.Get(currentX, y);
                if (current.LiquidAmount == 0)
                {
                    lastEmptyDirection = direction;
                    lastEmptyOffset = offset;
                }

                if (direction == -1 && currentX < 5)
                    leftBlocked = true;
                else if (direction == 1 && currentX > tiles.Dimensions.WidthTiles - 5)
                    rightBlocked = true;

                WorldTile down = tiles.Get(currentX, y + 1);
                if (down.LiquidAmount != 0 &&
                    down.LiquidAmount != byte.MaxValue &&
                    down.LiquidKind == kind)
                {
                    int amount = Math.Min(byte.MaxValue - down.LiquidAmount, remaining);
                    down.LiquidAmount = checked((byte)(down.LiquidAmount + amount));
                    tiles.SetInitialPopulationTile(currentX, y + 1, in down);
                    remaining -= amount;
                    if (remaining == 0)
                        break;
                }

                if (y < tiles.Dimensions.HeightTiles - 5 &&
                    down.LiquidAmount == 0 &&
                    IsQuickWaterPassable1458(in down))
                {
                    canFallFromRow = true;
                    break;
                }

                int nextX = x + (offset + 1) * direction;
                WorldTile next = tiles.Get(nextX, y);
                if ((next.LiquidAmount != 0 && (!firstRow || direction != 1)) ||
                    IsQuickWaterBarrier1458(in next))
                {
                    if (direction == 1)
                        rightBlocked = true;
                    else
                        leftBlocked = true;
                }

                if (leftBlocked && rightBlocked)
                    break;

                if (rightBlocked)
                {
                    direction = -1;
                    offset++;
                }
                else if (leftBlocked)
                {
                    if (direction == 1)
                        offset++;
                    direction = 1;
                }
                else
                {
                    if (direction == 1)
                        offset++;
                    direction = -direction;
                }
            }

            x += lastEmptyOffset * lastEmptyDirection;
            if (remaining == 0 || !canFallFromRow)
                break;
            y++;
        }

        WorldTile destination = tiles.Get(x, y);
        destination.LiquidAmount = checked((byte)remaining);
        destination.LiquidKind = kind;
        tiles.SetInitialPopulationTile(x, y, in destination);

        if (destination.LiquidAmount == 0)
            return;

        AttemptQuickWaterLoadingReaction1458(x, y, WorldLiquidKind.Lava, originWasLava);
        AttemptQuickWaterLoadingReaction1458(x, y, WorldLiquidKind.Honey, originWasHoney);
        AttemptQuickWaterLoadingReaction1458(x, y, WorldLiquidKind.Shimmer, originWasShimmer);
    }

    private void AttemptQuickWaterLoadingReaction1458(
        int x,
        int y,
        WorldLiquidKind testedKind,
        bool originHadTestedKind)
    {
        Span<(int X, int Y)> neighbours = stackalloc (int X, int Y)[4]
        {
            (x - 1, y),
            (x + 1, y),
            (x, y - 1),
            (x, y + 1)
        };

        for (int i = 0; i < neighbours.Length; i++)
        {
            (int neighbourX, int neighbourY) = neighbours[i];
            WorldTile neighbour = tiles.Get(neighbourX, neighbourY);
            if (neighbour.LiquidAmount == 0 ||
                (neighbour.LiquidKind == testedKind) == originHadTestedKind)
            {
                continue;
            }

            int targetX = originHadTestedKind ? x : neighbourX;
            int targetY = originHadTestedKind ? y : neighbourY;
            WorldTile source = tiles.Get(targetX, targetY);
            if (source.LiquidAmount == 0 || source.LiquidKind != testedKind)
                return;

            WorldTile left = tiles.Get(targetX - 1, targetY);
            WorldTile right = tiles.Get(targetX + 1, targetY);
            WorldTile above = tiles.Get(targetX, targetY - 1);
            Span<WorldLiquidSimulationChange> scratch = stackalloc WorldLiquidSimulationChange[MaximumChangesPerProcessedCell];
            int ignoredChangeCount = 0;
            ApplyGeneratingOrLoadingForeignLiquidReaction1458(
                targetX, targetY, testedKind, in source, in left, in right, in above, scratch, ref ignoredChangeCount);
            return;
        }
    }

    private static bool IsQuickWaterPassable1458(in WorldTile tile) =>
        !IsQuickWaterBarrier1458(in tile);

    private static bool IsQuickWaterBubbleBarrier1458(in WorldTile tile) =>
        tile.IsActive && !tile.IsActuated && tile.TileType == VanillaTileIds.Bubble;

    private static bool IsQuickWaterBarrier1458(in WorldTile tile)
    {
        if (!tile.IsActive || tile.IsActuated)
            return false;
        if (tile.TileType == VanillaTileIds.Bubble)
            return true;
        if (VanillaLiquidQuickWaterFacts1458.IgnoresSolidDuringSettle(tile.TileType))
            return false;
        return VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
               !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
    }

    /// <summary>
    /// Replays the source-backed <c>WorldGen.WaterCheck</c> pass used by TerrariaServer 1.4.5.8 while a
    /// canonical world is still unpublished. The pass clears both liquid queues, applies the temporary
    /// <c>tilesIgnoreWater(true)</c> solidity rules, removes verified single cells and complete no-metadata loading
    /// objects, normalizes nearly-full cells below, and rebuilds active/buffered
    /// liquid work through the exact loading <c>Liquid.AddWater</c> gates. Unsupported object removal fails before
    /// any mutation so startup can discard the candidate rather than publishing an approximated world.
    /// </summary>
    public VanillaWaterCheckDiagnostic1458 WaterCheckLoading()
    {
        VanillaWaterCheckDiagnostic1458 preflight = PreflightWaterCheckLiquidDeaths1458();
        if (!preflight.IsApplied)
            return preflight;

        tiles.LiquidUpdates.Clear();
        discoveryCursor = tiles.Count;
        discoveryComplete = true;

        int width = tiles.Dimensions.WidthTiles;
        int height = tiles.Dimensions.HeightTiles;
        for (int x = 1; x < width - 1; x++)
        {
            for (int y = height - 2; y > 0; y--)
            {
                WorldTile tile = tiles.Get(x, y);
                if (tile.LiquidAmount > 0 && IsWaterCheckSolidBarrier1458(in tile))
                {
                    if (tile.TileType != VanillaTileIds.Bubble)
                    {
                        tile.LiquidAmount = 0;
                        tile.LiquidKind = WorldLiquidKind.Water;
                        tiles.SetInitialPopulationTile(x, y, in tile);
                    }
                    continue;
                }

                if (tile.LiquidAmount == 0)
                    continue;

                if (tile.IsActive && ShouldDieInLoadingLiquid1458(in tile))
                {
                    if (!TryResolveLoadingDeath1458(x, y, in tile, out WorldTileRegion death))
                        throw new InvalidOperationException("A preflighted loading liquid-death object changed.");
                    for (int objectX = death.X; objectX < death.ExclusiveRight; objectX++)
                    for (int objectY = death.Y; objectY < death.ExclusiveBottom; objectY++)
                        KillSingleCellDuringLoading1458(objectX, objectY, tiles.Get(objectX, objectY));
                    tile = tiles.Get(x, y);
                }

                WorldTile below = tiles.Get(x, y + 1);
                if (!IsWaterCheckSolidBarrier1458(in below) && below.LiquidAmount < byte.MaxValue)
                {
                    if (below.LiquidAmount > 250)
                    {
                        below.LiquidAmount = byte.MaxValue;
                        tiles.SetInitialPopulationTile(x, y + 1, in below);
                    }
                    else
                    {
                        TryAddWaterLoading1458(x, y);
                    }
                }

                WorldTile left = tiles.Get(x - 1, y);
                WorldTile right = tiles.Get(x + 1, y);
                if (!IsWaterCheckSolidBarrier1458(in left) && left.LiquidAmount != tile.LiquidAmount)
                {
                    TryAddWaterLoading1458(x, y);
                }
                else if (!IsWaterCheckSolidBarrier1458(in right) && right.LiquidAmount != tile.LiquidAmount)
                {
                    TryAddWaterLoading1458(x, y);
                }

                if (tile.LiquidKind == WorldLiquidKind.Lava &&
                    (IsNonLavaLiquid1458(in left) ||
                     IsNonLavaLiquid1458(in right) ||
                     IsNonLavaLiquid1458(tiles.Get(x, y - 1)) ||
                     IsNonLavaLiquid1458(in below)))
                {
                    TryAddWaterLoading1458(x, y);
                }
            }
        }

        return VanillaWaterCheckDiagnostic1458.Applied;
    }

    private VanillaWaterCheckDiagnostic1458 PreflightWaterCheckLiquidDeaths1458()
    {
        int width = tiles.Dimensions.WidthTiles;
        int height = tiles.Dimensions.HeightTiles;
        for (int x = 1; x < width - 1; x++)
        {
            for (int y = height - 2; y > 0; y--)
            {
                WorldTile tile = tiles.Get(x, y);
                if (tile.LiquidAmount == 0 || IsWaterCheckSolidBarrier1458(in tile) ||
                    !tile.IsActive || !ShouldDieInLoadingLiquid1458(in tile))
                {
                    continue;
                }

                if (!TryResolveLoadingDeath1458(x, y, in tile, out _))
                {
                    return new VanillaWaterCheckDiagnostic1458(
                        VanillaWaterCheckResult1458.UnsupportedLiquidDeathTile,
                        x,
                        y,
                        tile.TileType);
                }
            }
        }

        return VanillaWaterCheckDiagnostic1458.Applied;
    }

    private bool TryResolveLoadingDeath1458(int x, int y, in WorldTile tile, out WorldTileRegion region)
    {
        region = new WorldTileRegion(x, y, 1, 1);
        if (!VanillaTileObjectLiquidDeath1458.TryGet(in tile, out _, out _)) return false;
        // KillTile.CheckTileBreakability checks a locked temple door below even for non-solid objects.
        WorldTile support = tiles.Get(x, y + 1);
        if (support.IsActive && support.Type == 10 && support.FrameY is >= 594 and <= 646 && support.FrameX < 54)
            return false;
        // TileID.Sets.Platforms: apply this before the generic single-cell catalog, which also
        // admits team platforms. Supports above/below can require unrepresented cascading kills.
        if (tile.Type is 19 or 427 or >= 435 and <= 439)
        {
            WorldTile above = tiles.Get(x, y - 1);
            return !above.IsActive && (!support.IsActive ||
                (VanillaTileDefinitionCatalog.TryGet(support.TileType, out var floor) && floor.IsSolid && !floor.IsFrameImportant));
        }
        if (VanillaTileDefinitionCatalog.TryGet(tile.TileType, out VanillaTileDefinition definition) &&
            definition.BreakPath is VanillaTileBreakPath.SimpleCell or VanillaTileBreakPath.FrameImportantSingleCell)
            return true;

        // WorldFile.LoadWorld sets isGeneratingOrLoadingWorld: KillTile forces noItem and Item.NewItem
        // itself refuses creation. PlantCheck identities are single cells despite being frame-important.
        // This does NOT widen live mining/drop authority, whose contextual plant loot is a different boundary.
        // Torches and StyleAlch herbs likewise have no multi-cell or persistent metadata footprint.
        if (tile.Type is 3 or 4 or 24 or 61 or 71 or 73 or 74 or 82 or 83 or 84 or 110 or 113 or 184 or 201 or 637 or 703)
            return true;

        // Source CheckOrb/CheckPot/Check3x2/Check1xX, Check1x2Top/CheckBanner and painting Check*Wall
        // remove the remaining coherent object after a cell is killed.
        // The metadata object catalog intentionally covers chests/signs/entities, not these objects; do not
        // invent a metadata identity or a runtime placement path just to admit their loading-time destruction.
        if (tile.Type is not (12 or 28 or 42 or 91 or 93 or 215 or 233 or 240 or 242 or 245 or 246) || tile.FrameX < 0 || tile.FrameY < 0 ||
            tile.FrameX % 18 != 0 || tile.FrameY % 18 != 0 ||
            (tile.Type == 12 && (tile.FrameX > 54 || tile.FrameY > 18)) ||
            (tile.Type == 233 && tile.FrameY > 54) ||
            (tile.Type == 245 && tile.FrameY >= 54) || (tile.Type == 246 && tile.FrameX >= 54)) return false;
        (int width, int height) = tile.Type switch
        {
            42 => (1, 2),
            91 or 93 => (1, 3),
            215 or 246 => (3, 2),
            233 => (tile.FrameY >= 36 ? 2 : 3, 2),
            240 => (3, 3),
            242 => (6, 4),
            245 => (2, 3),
            _ => (2, 2)
        };
        int column = tile.FrameX / 18 % width, row = tile.FrameY / 18 % height;
        int left = x - column, top = y - row;
        // Source SquareTileFrame cannot propagate object removal inside the five-cell world border.
        if (left <= 5 || top <= 5 || left + width - 1 >= tiles.Dimensions.WidthTiles - 5 || top + height - 1 >= tiles.Dimensions.HeightTiles - 5)
            return false;
        int frameX = tile.FrameX - column * 18, frameY = tile.FrameY - row * 18;
        // CheckJunglePlant's 3x2 branch also scans a third row for same-type remnants. Do not erase
        // an adjacent malformed object: ordinary grass support is unaffected, ambiguous remnants reject.
        if (tile.Type == 233 && width == 3)
            for (int dx = 0; dx < width; dx++)
                if (tiles.Get(left + dx, top + height) is { IsActive: true, Type: 233 }) return false;
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < height; dy++)
        {
            WorldTile cell = tiles.Get(left + dx, top + dy);
            // Incoherent/overlapping objects stay fail-closed; never erase a foreign neighbor or its metadata.
            if (!cell.IsActive || cell.Type != tile.Type || cell.FrameX != frameX + dx * 18 || cell.FrameY != frameY + dy * 18)
                return false;
            WorldTile below = tiles.Get(left + dx, top + dy + 1);
            if (below.IsActive && below.Type == 10 && below.FrameY is >= 594 and <= 646 && below.FrameX < 54)
                return false;
        }
        region = new WorldTileRegion(left, top, width, height);
        return true;
    }

    private static bool ShouldDieInLoadingLiquid1458(in WorldTile tile) =>
        !VanillaTileObjectLiquidDeath1458.TryGet(in tile, out bool water, out bool lava) ||
        (tile.LiquidKind == WorldLiquidKind.Lava ? lava : water);

    private static bool IsWaterCheckSolidBarrier1458(in WorldTile tile)
    {
        if (!tile.IsActive || tile.IsActuated)
            return false;
        if (VanillaLiquidQuickWaterFacts1458.IgnoresSolidDuringSettle(tile.TileType))
            return false;
        return VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
               !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
    }

    private static bool IsNonLavaLiquid1458(in WorldTile tile) =>
        tile.LiquidAmount > 0 && tile.LiquidKind != WorldLiquidKind.Lava;

    private void TryAddWaterLoading1458(int x, int y)
    {
        int width = tiles.Dimensions.WidthTiles;
        int height = tiles.Dimensions.HeightTiles;
        if (x >= width - 5 || y >= height - 5 || x < 5 || y < 5)
            return;

        WorldTile tile = tiles.Get(x, y);
        if (tile.LiquidAmount == 0 || IsQueuedForLoading1458(x, y))
            return;

        // Liquid.AddWater has an explicit tile-546 exception while tilesIgnoreWater(true) also makes the
        // boulder family non-solid. IsWaterCheckSolidBarrier1458 captures the final effective gate.
        if (IsWaterCheckSolidBarrier1458(in tile))
            return;

        if (tiles.LiquidUpdates.ActiveCount >= LoadingActiveLiquidCapacity1458)
        {
            if (tiles.LiquidUpdates.BufferedCount < LoadingBufferedLiquidCapacity1458)
                _ = tiles.LiquidUpdates.TryBuffer(x, y);
            return;
        }

        _ = tiles.LiquidUpdates.TryEnqueue(x, y);
    }

    private bool IsQueuedForLoading1458(int x, int y) =>
        tiles.LiquidUpdates.IsQueued(x, y) || tiles.LiquidUpdates.IsBuffered(x, y);

    private void KillSingleCellDuringLoading1458(int x, int y, in WorldTile before)
    {
        WorldTile after = before;
        after.Type = 0;
        after.FrameX = -1;
        after.FrameY = -1;
        after.TileColor = 0;
        after.Shape = 0;
        after.Flags &= ~(
            WorldTileFlags.Active |
            WorldTileFlags.Inactive |
            WorldTileFlags.InvisibleBlock |
            WorldTileFlags.FullbrightBlock);
        tiles.SetInitialPopulationTile(x, y, in after);
    }

    private int TickCore(
        int stableKillUpdates,
        VanillaLiquidUpdateMode1458 mode,
        int processBudget,
        Span<WorldLiquidSimulationChange> changes)
    {

        DiscoverExistingLiquid();
        PromoteBuffered(processBudget);

        int changed = 0;
        int processed = 0;
        int capacityBudget = changes.Length / MaximumChangesPerProcessedCell;
        int processCount = Math.Min(Math.Min(processBudget, capacityBudget), tiles.LiquidUpdates.ActiveCount);
        while (processed < processCount && tiles.LiquidUpdates.TryDequeue(out WorldLiquidUpdate update))
        {
            processed++;
            WorldLiquidUpdate effectiveUpdate = mode == VanillaLiquidUpdateMode1458.QuickSettle
                ? update with { Delay = HoneyFlowDelayUpdates1458 }
                : update;
            RelaxCell(in effectiveUpdate, stableKillUpdates, mode, changes, ref changed);
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

    private void PromoteBuffered(int processBudget)
    {
        int promoted = 0;
        while (promoted < processBudget && PendingCount < MaximumPendingCells &&
               tiles.LiquidUpdates.TryDequeueBuffered(out int x, out int y))
        {
            _ = tiles.LiquidUpdates.TryEnqueue(x, y);
            promoted++;
        }
    }

    private void RelaxCell(
        in WorldLiquidUpdate update,
        int stableKillUpdates,
        VanillaLiquidUpdateMode1458 mode,
        Span<WorldLiquidSimulationChange> changes,
        ref int changed)
    {
        int x = update.X;
        int y = update.Y;
        if (!Contains(x, y))
            return;

        WorldTile source = tiles.Get(x, y);
        if (source.LiquidAmount == 0 || IsLiquidBarrier(in source))
            return;

        byte amountAtStart = source.LiquidAmount;
        EvaporateUnderworldWater1458(x, y, ref source, changes, ref changed);
        if (source.LiquidAmount == 0)
            return;

        // TerrariaServer 1.4.5.8 Liquid.Update runs LavaCheck/HoneyCheck/ShimmerCheck on the
        // corresponding source cell before the lava/honey delay gates. Water instead wakes adjacent
        // foreign liquid cells so their own LiquidCheck owns the merge location and material.
        if (source.LiquidKind == WorldLiquidKind.Water)
        {
            WakeForeignLiquidNeighbours1458(x, y);
        }
        else
        {
            ApplyForeignLiquidReaction1458(x, y, source.LiquidKind, mode, changes, ref changed);
            source = tiles.Get(x, y);
            if (source.LiquidAmount == 0 || IsLiquidBarrier(in source))
                return;
        }

        if (ShouldDelayFlow1458(in update, in source, mode))
            return;

        if (y + 1 < tiles.Dimensions.HeightTiles)
        {
            WorldTile below = tiles.Get(x, y + 1);
            if (CanAccept(in below, source.LiquidKind) && below.LiquidAmount < byte.MaxValue)
            {
                FlowDown1458(x, y, in source, in below, mode, changes, ref changed);
                source = tiles.Get(x, y);
            }
        }

        if (source.LiquidAmount > 0)
            LevelHorizontally1458(x, y, in source, changes, ref changed);

        CompleteActiveLifecycle1458(in update, amountAtStart, stableKillUpdates, mode, changes, ref changed);
    }

    /// <summary>
    /// TerrariaServer 1.4.5.8 <c>Liquid.Update</c> removes two units of water per update below
    /// <c>Main.UnderworldLayer</c>, where <c>Main.UnderworldLayer == Main.maxTilesY - 200</c>.
    /// Tiny synthetic stores do not represent a vanilla world, so the rule fails closed when the
    /// height cannot contain that source-backed 200-tile Underworld band.
    /// </summary>
    private void EvaporateUnderworldWater1458(
        int x,
        int y,
        ref WorldTile source,
        Span<WorldLiquidSimulationChange> changes,
        ref int changed)
    {
        int height = tiles.Dimensions.HeightTiles;
        if (height <= UnderworldLayerOffset1458 ||
            y <= height - UnderworldLayerOffset1458 ||
            source.LiquidKind != WorldLiquidKind.Water ||
            source.LiquidAmount == 0)
        {
            return;
        }

        byte evaporated = checked((byte)Math.Min(UnderworldWaterEvaporationPerUpdate1458, source.LiquidAmount));
        source.LiquidAmount = checked((byte)(source.LiquidAmount - evaporated));
        if (source.LiquidAmount == 0)
            source.LiquidKind = WorldLiquidKind.Water;

        SetTile(x, y, in source);
        Record(x, y, in source, changes, ref changed);
    }

    /// <summary>
    /// Mirrors the per-entry <c>kill</c> retirement tail of TerrariaServer 1.4.5.8
    /// <c>Liquid.Update</c>/<c>Liquid.UpdateLiquid</c>. Dedicated-server retirement uses the
    /// source-backed <c>10 + activePlayersInSlots0To14 / 3</c> threshold. A retiring 254-unit cell is
    /// normalized back to 255. Entries whose amount changes
    /// reset <c>kill</c> and wake the cell above. Re-enqueued work is deliberately left for the next
    /// TerraRuntime tick so one hot cell cannot consume several vanilla update steps in one budget slice.
    /// </summary>
    private void CompleteActiveLifecycle1458(
        in WorldLiquidUpdate update,
        byte amountAtStart,
        int stableKillUpdates,
        VanillaLiquidUpdateMode1458 mode,
        Span<WorldLiquidSimulationChange> changes,
        ref int changed)
    {
        WorldTile current = tiles.Get(update.X, update.Y);
        if (IsLiquidBarrier(in current))
            return;
        if (current.LiquidAmount == 0)
        {
            if (amountAtStart != 0)
                TryBuffer(update.X, update.Y - 1);
            return;
        }

        bool oneUnitFullSourceCase = current.LiquidAmount == 254 && amountAtStart == byte.MaxValue;
        if (oneUnitFullSourceCase && mode == VanillaLiquidUpdateMode1458.QuickSettle)
        {
            current.LiquidAmount = byte.MaxValue;
            SetTile(update.X, update.Y, in current);
            Record(update.X, update.Y, in current, changes, ref changed);
        }

        int nextKill;
        if (current.LiquidAmount != amountAtStart && !oneUnitFullSourceCase)
        {
            nextKill = 0;
            TryBuffer(update.X, update.Y - 1);
        }
        else
        {
            nextKill = update.Kill + 1;
        }

        if (nextKill >= stableKillUpdates)
        {
            if (current.LiquidAmount == 254)
            {
                current.LiquidAmount = byte.MaxValue;
                SetTile(update.X, update.Y, in current);
                Record(update.X, update.Y, in current, changes, ref changed);
            }
            return;
        }

        int nextDelay = mode == VanillaLiquidUpdateMode1458.QuickSettle
            ? HoneyFlowDelayUpdates1458
            : current.LiquidKind is WorldLiquidKind.Lava or WorldLiquidKind.Honey
                ? 0
                : update.Delay;
        if (!tiles.LiquidUpdates.TryEnqueue(update.X, update.Y, nextDelay, nextKill))
        {
            throw new InvalidOperationException(
                "Dequeued liquid work could not be re-enqueued for its verified vanilla kill lifecycle.");
        }
    }

    /// <summary>
    /// Mirrors the water-side scheduling portion of TerrariaServer 1.4.5.8 <c>Liquid.Update</c>.
    /// Water does not run <c>LiquidCheck</c> on itself; it schedules adjacent lava, honey and shimmer
    /// cells in that source order and lets the foreign-liquid update choose the merge cell.
    /// </summary>
    private void WakeForeignLiquidNeighbours1458(int x, int y)
    {
        WakeForeignKind(x, y, WorldLiquidKind.Lava);
        WakeForeignKind(x, y, WorldLiquidKind.Honey);
        WakeForeignKind(x, y, WorldLiquidKind.Shimmer);
    }

    private void WakeForeignKind(int x, int y, WorldLiquidKind kind)
    {
        TryWakeIfKind(x - 1, y, kind);
        TryWakeIfKind(x + 1, y, kind);
        TryWakeIfKind(x, y - 1, kind);
        TryWakeIfKind(x, y + 1, kind);
    }

    private void TryWakeIfKind(int x, int y, WorldLiquidKind kind)
    {
        if (!Contains(x, y))
            return;

        WorldTile tile = tiles.Get(x, y);
        if (tile.LiquidAmount > 0 && tile.LiquidKind == kind)
            TryBuffer(x, y);
    }

    /// <summary>
    /// Replays TerrariaServer 1.4.5.8 <c>Liquid.LiquidCheck</c> ordering for the supported runtime subset.
    /// The liquid simulator owns the source-ordered liquid clears; irreversible KillTile/ReplaceTile/drop effects
    /// cross <see cref="IVanillaLiquidTileSideEffectSink1458"/> so unsupported active targets fail before any liquid
    /// state is destroyed.
    /// </summary>
    private void ApplyForeignLiquidReaction1458(
        int x,
        int y,
        WorldLiquidKind sourceKind,
        VanillaLiquidUpdateMode1458 mode,
        Span<WorldLiquidSimulationChange> changes,
        ref int changed)
    {
        if (sourceKind == WorldLiquidKind.Water ||
            x <= 0 || y <= 0 ||
            x + 1 >= tiles.Dimensions.WidthTiles ||
            y + 1 >= tiles.Dimensions.HeightTiles)
        {
            return;
        }

        WorldTile source = tiles.Get(x, y);
        WorldTile left = tiles.Get(x - 1, y);
        WorldTile right = tiles.Get(x + 1, y);
        WorldTile above = tiles.Get(x, y - 1);

        if (mode == VanillaLiquidUpdateMode1458.QuickSettle)
        {
            ApplyGeneratingOrLoadingForeignLiquidReaction1458(
                x, y, sourceKind, in source, in left, in right, in above, changes, ref changed);
            return;
        }

        bool foreignLeft = IsForeignLiquid(in left, sourceKind);
        bool foreignRight = IsForeignLiquid(in right, sourceKind);
        bool foreignAbove = IsForeignLiquid(in above, sourceKind);
        if (foreignLeft || foreignRight || foreignAbove)
        {
            bool waterNearby = IsLiquidKind(in left, WorldLiquidKind.Water) ||
                               IsLiquidKind(in right, WorldLiquidKind.Water) ||
                               IsLiquidKind(in above, WorldLiquidKind.Water);
            bool lavaNearby = IsLiquidKind(in left, WorldLiquidKind.Lava) ||
                              IsLiquidKind(in right, WorldLiquidKind.Lava) ||
                              IsLiquidKind(in above, WorldLiquidKind.Lava);
            bool honeyNearby = IsLiquidKind(in left, WorldLiquidKind.Honey) ||
                               IsLiquidKind(in right, WorldLiquidKind.Honey) ||
                               IsLiquidKind(in above, WorldLiquidKind.Honey);
            bool shimmerNearby = IsLiquidKind(in left, WorldLiquidKind.Shimmer) ||
                                 IsLiquidKind(in right, WorldLiquidKind.Shimmer) ||
                                 IsLiquidKind(in above, WorldLiquidKind.Shimmer);

            int foreignAmount =
                (foreignLeft ? left.LiquidAmount : 0) +
                (foreignRight ? right.LiquidAmount : 0) +
                (foreignAbove ? above.LiquidAmount : 0);

            TileTypeId mergeTile = default;
            WorldLiquidKind mergeKind = default;
            bool resolvedMerge = foreignAmount >= 24 &&
                                 VanillaLiquidMergeCatalog1458.TryResolve(
                                     sourceKind,
                                     waterNearby,
                                     lavaNearby,
                                     honeyNearby,
                                     shimmerNearby,
                                     out mergeTile,
                                     out mergeKind);
            bool targetEligible = !source.IsActive ||
                                  VanillaLiquidInteractionFacts1458.IsObsidianKill(source.TileType);

            if (resolvedMerge && targetEligible)
            {
                var request = new VanillaLiquidMergeTileRequest1458(
                    x,
                    y,
                    mergeTile,
                    sourceKind,
                    mergeKind,
                    source,
                    ContainerOverride: false);
                if (!CanRepresentMergeTileSquare1458(x, y) ||
                    !sideEffects.TryPrepareMergeTile(in request))
                {
                    return;
                }

                try
                {
                    if (foreignLeft)
                        ClearLiquidCellRaw(x - 1, y);
                    if (foreignRight)
                        ClearLiquidCellRaw(x + 1, y);
                    if (foreignAbove)
                        ClearLiquidCellRaw(x, y - 1);
                    ClearLiquidCellRaw(x, y);

                    sideEffects.CommitPreparedMergeTile(in request);
                }
                catch
                {
                    sideEffects.AbortPreparedMergeTile();
                    RestoreReactionCells(x, y, in source, in left, in right, in above);
                    throw;
                }

                WorldTile committed = tiles.Get(x, y);
                RecordTileSquare(
                    x,
                    y,
                    in committed,
                    startX: x - 2,
                    startY: y - 2,
                    width: 3,
                    height: 3,
                    VanillaLiquidMergeCatalog1458.ResolveTileChangeType(sourceKind, mergeKind),
                    changes,
                    ref changed);
                return;
            }

            // LiquidCheck consumes foreign left/right/up liquid even when the 24-unit threshold is not reached or
            // the active source tile is not obsidian-kill eligible. No merge tile/packet-20 event is created here.
            if (foreignLeft)
                ClearLiquidCell(x - 1, y, changes, ref changed);
            if (foreignRight)
                ClearLiquidCell(x + 1, y, changes, ref changed);
            if (foreignAbove)
                ClearLiquidCell(x, y - 1, changes, ref changed);
            return;
        }

        WorldTile belowBefore = tiles.Get(x, y + 1);
        if (!IsForeignLiquid(in belowBefore, sourceKind))
            return;

        bool containerOverride =
            source.IsActive &&
            VanillaLiquidInteractionFacts1458.IsContainer(source.TileType) &&
            !VanillaLiquidInteractionFacts1458.IsContainer(belowBefore.TileType);

        // LiquidCheck kills tileCut content below non-water before testing merge eligibility. The cut is therefore a
        // standalone committed side effect: when the application cannot model that KillTile path we fail closed
        // before touching either liquid cell.
        if (sourceKind != WorldLiquidKind.Water &&
            belowBefore.IsActive &&
            VanillaProjectileTileCutFacts.IsCuttable(belowBefore.TileType))
        {
            if (!sideEffects.TryCutTile(x, y + 1))
                return;
        }

        WorldTile below = tiles.Get(x, y + 1);
        bool lowerEligible = !below.IsActive ||
                             VanillaLiquidInteractionFacts1458.IsObsidianKill(below.TileType) ||
                             containerOverride;
        if (!lowerEligible)
            return;

        if (source.LiquidAmount < 24)
        {
            if (!CanRepresentSub24LowerSquare1458(x, y))
                return;

            ClearLiquidCellRaw(x, y);
            WorldTile cleared = tiles.Get(x, y);
            RecordTileSquare(
                x,
                y,
                in cleared,
                startX: x - 2,
                startY: y - 1,
                width: 3,
                height: 3,
                VanillaTileChangeType1458.None,
                changes,
                ref changed);
            return;
        }

        if (!VanillaLiquidMergeCatalog1458.TryResolve(
                sourceKind,
                IsLiquidKind(in below, WorldLiquidKind.Water),
                IsLiquidKind(in below, WorldLiquidKind.Lava),
                IsLiquidKind(in below, WorldLiquidKind.Honey),
                IsLiquidKind(in below, WorldLiquidKind.Shimmer),
                out TileTypeId belowMergeTile,
                out WorldLiquidKind belowMergeKind) ||
            !CanRepresentMergeTileSquare1458(x, y + 1))
        {
            return;
        }

        var lowerRequest = new VanillaLiquidMergeTileRequest1458(
            x,
            y + 1,
            belowMergeTile,
            sourceKind,
            belowMergeKind,
            below,
            containerOverride);
        if (!sideEffects.TryPrepareMergeTile(in lowerRequest))
            return;

        try
        {
            ClearLiquidCellRaw(x, y);
            ClearLiquidCellRaw(x, y + 1);
            sideEffects.CommitPreparedMergeTile(in lowerRequest);
        }
        catch
        {
            sideEffects.AbortPreparedMergeTile();
            SetTile(x, y, in source);
            SetTile(x, y + 1, in below);
            throw;
        }

        WorldTile lowerCommitted = tiles.Get(x, y + 1);
        RecordTileSquare(
            x,
            y + 1,
            in lowerCommitted,
            startX: x - 2,
            startY: y - 1,
            width: 3,
            height: 3,
            VanillaLiquidMergeCatalog1458.ResolveTileChangeType(sourceKind, belowMergeKind),
            changes,
            ref changed);
    }

    /// <summary>
    /// TerrariaServer 1.4.5.8 calls <c>LiquidCheck(..., createMergeTilesDuringGen: false)</c> while
    /// <c>WorldGen.isGeneratingOrLoadingWorld</c>. In that mode <c>CreateLiquidMergeTile</c> does not
    /// place Obsidian/Honey/Crispy/Shimmer blocks. The participating liquid cells have already been
    /// zeroed before the helper is entered, so its <c>LiquidOverwriteStrip</c> starts on an empty target
    /// and performs no material placement. This branch therefore preserves the loading-time liquid clears
    /// without inventing runtime merge tiles.
    /// </summary>
    private void ApplyGeneratingOrLoadingForeignLiquidReaction1458(
        int x,
        int y,
        WorldLiquidKind sourceKind,
        in WorldTile source,
        in WorldTile left,
        in WorldTile right,
        in WorldTile above,
        Span<WorldLiquidSimulationChange> changes,
        ref int changed)
    {
        bool foreignLeft = IsForeignLiquid(in left, sourceKind);
        bool foreignRight = IsForeignLiquid(in right, sourceKind);
        bool foreignAbove = IsForeignLiquid(in above, sourceKind);
        if (foreignLeft || foreignRight || foreignAbove)
        {
            bool waterNearby = IsLiquidKind(in left, WorldLiquidKind.Water) ||
                               IsLiquidKind(in right, WorldLiquidKind.Water) ||
                               IsLiquidKind(in above, WorldLiquidKind.Water);
            bool lavaNearby = IsLiquidKind(in left, WorldLiquidKind.Lava) ||
                              IsLiquidKind(in right, WorldLiquidKind.Lava) ||
                              IsLiquidKind(in above, WorldLiquidKind.Lava);
            bool honeyNearby = IsLiquidKind(in left, WorldLiquidKind.Honey) ||
                               IsLiquidKind(in right, WorldLiquidKind.Honey) ||
                               IsLiquidKind(in above, WorldLiquidKind.Honey);
            bool shimmerNearby = IsLiquidKind(in left, WorldLiquidKind.Shimmer) ||
                                 IsLiquidKind(in right, WorldLiquidKind.Shimmer) ||
                                 IsLiquidKind(in above, WorldLiquidKind.Shimmer);
            int foreignAmount =
                (foreignLeft ? left.LiquidAmount : 0) +
                (foreignRight ? right.LiquidAmount : 0) +
                (foreignAbove ? above.LiquidAmount : 0);

            bool resolvedMerge = foreignAmount >= 24 &&
                                 VanillaLiquidMergeCatalog1458.TryResolve(
                                     sourceKind,
                                     waterNearby,
                                     lavaNearby,
                                     honeyNearby,
                                     shimmerNearby,
                                     out _,
                                     out WorldLiquidKind mergeKind) &&
                                 mergeKind != sourceKind;
            bool targetEligible = !source.IsActive ||
                                  VanillaLiquidInteractionFacts1458.IsObsidianKill(source.TileType);

            if (foreignLeft)
                ClearLiquidCell(x - 1, y, changes, ref changed);
            if (foreignRight)
                ClearLiquidCell(x + 1, y, changes, ref changed);
            if (foreignAbove)
                ClearLiquidCell(x, y - 1, changes, ref changed);

            if (resolvedMerge && targetEligible)
                ClearLiquidCell(x, y, changes, ref changed);
            return;
        }

        WorldTile belowBefore = tiles.Get(x, y + 1);
        if (!IsForeignLiquid(in belowBefore, sourceKind))
            return;

        bool containerOverride =
            source.IsActive &&
            VanillaLiquidInteractionFacts1458.IsContainer(source.TileType) &&
            !VanillaLiquidInteractionFacts1458.IsContainer(belowBefore.TileType);

        if (sourceKind != WorldLiquidKind.Water &&
            belowBefore.IsActive &&
            VanillaProjectileTileCutFacts.IsCuttable(belowBefore.TileType))
        {
            if (!sideEffects.TryCutTile(x, y + 1))
                return;
        }

        WorldTile below = tiles.Get(x, y + 1);
        bool lowerEligible = !below.IsActive ||
                             VanillaLiquidInteractionFacts1458.IsObsidianKill(below.TileType) ||
                             containerOverride;
        if (!lowerEligible)
            return;

        if (source.LiquidAmount < 24)
        {
            ClearLiquidCell(x, y, changes, ref changed);
            return;
        }

        if (!VanillaLiquidMergeCatalog1458.TryResolve(
                sourceKind,
                IsLiquidKind(in below, WorldLiquidKind.Water),
                IsLiquidKind(in below, WorldLiquidKind.Lava),
                IsLiquidKind(in below, WorldLiquidKind.Honey),
                IsLiquidKind(in below, WorldLiquidKind.Shimmer),
                out _,
                out WorldLiquidKind lowerMergeKind) ||
            lowerMergeKind == sourceKind)
        {
            return;
        }

        ClearLiquidCell(x, y, changes, ref changed);
        ClearLiquidCell(x, y + 1, changes, ref changed);
    }

    private bool CanRepresentMergeTileSquare1458(int targetX, int targetY) =>
        ContainsSquare(targetX - 2, targetY - 2, width: 3, height: 3);

    private bool CanRepresentSub24LowerSquare1458(int sourceX, int sourceY) =>
        ContainsSquare(sourceX - 2, sourceY - 1, width: 3, height: 3);

    private bool ContainsSquare(int startX, int startY, int width, int height) =>
        startX >= 0 && startY >= 0 &&
        startX + width <= tiles.Dimensions.WidthTiles &&
        startY + height <= tiles.Dimensions.HeightTiles;

    private void RestoreReactionCells(
        int x,
        int y,
        in WorldTile source,
        in WorldTile left,
        in WorldTile right,
        in WorldTile above)
    {
        SetTile(x, y, in source);
        SetTile(x - 1, y, in left);
        SetTile(x + 1, y, in right);
        SetTile(x, y - 1, in above);
    }

    private void ClearLiquidCellRaw(int x, int y)
    {
        WorldTile tile = tiles.Get(x, y);
        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
        SetTile(x, y, in tile);
    }

    private static void RecordTileSquare(
        int x,
        int y,
        in WorldTile tile,
        int startX,
        int startY,
        byte width,
        byte height,
        VanillaTileChangeType1458 changeType,
        Span<WorldLiquidSimulationChange> changes,
        ref int changed)
    {
        if ((uint)changed >= (uint)changes.Length)
        {
            throw new InvalidOperationException(
                "Liquid simulation change buffer is smaller than the verified per-tick mutation footprint.");
        }

        changes[changed++] = new WorldLiquidSimulationChange(
            x,
            y,
            tile.LiquidAmount,
            tile.LiquidKind,
            RequiresTileSquareReplication: true,
            startX,
            startY,
            width,
            height,
            changeType);
    }

    private void ClearLiquidCell(
        int x,
        int y,
        Span<WorldLiquidSimulationChange> changes,
        ref int changed)
    {
        WorldTile tile = tiles.Get(x, y);
        if (tile.LiquidAmount == 0)
            return;

        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
        SetTile(x, y, in tile);
        Record(x, y, in tile, changes, ref changed);
    }

    private static bool IsForeignLiquid(in WorldTile tile, WorldLiquidKind sourceKind) =>
        tile.LiquidAmount > 0 && tile.LiquidKind != sourceKind;

    private static bool IsLiquidKind(in WorldTile tile, WorldLiquidKind kind) =>
        tile.LiquidAmount > 0 && tile.LiquidKind == kind;

    /// <summary>
    /// Mirrors the ordinary downward transfer in TerrariaServer 1.4.5.8 <c>Liquid.Update</c>.
    /// A one-unit transfer from a full source into a 254-unit cell intentionally fills the target
    /// without decrementing the source; vanilla uses that exception to avoid a 254/255 oscillation.
    /// If only part of the source moves, the same update continues into horizontal leveling.
    /// </summary>
    private void FlowDown1458(
        int x,
        int y,
        in WorldTile sourceBefore,
        in WorldTile belowBefore,
        VanillaLiquidUpdateMode1458 mode,
        Span<WorldLiquidSimulationChange> changes,
        ref int changed)
    {
        int amount = Math.Min(sourceBefore.LiquidAmount, byte.MaxValue - belowBefore.LiquidAmount);
        if (amount <= 0)
            return;

        bool preserveFullSource = amount == 1 && sourceBefore.LiquidAmount == byte.MaxValue;
        WorldTile source = sourceBefore;
        WorldTile below = belowBefore;

        if (!preserveFullSource)
        {
            source.LiquidAmount = checked((byte)(source.LiquidAmount - amount));
            if (source.LiquidAmount == 0)
                source.LiquidKind = WorldLiquidKind.Water;
        }

        below.LiquidAmount = checked((byte)(below.LiquidAmount + amount));
        below.LiquidKind = sourceBefore.LiquidKind;

        bool quickSettleSaturatedSource =
            mode == VanillaLiquidUpdateMode1458.QuickSettle && source.LiquidAmount > 250;
        if (quickSettleSaturatedSource)
        {
            source.LiquidAmount = byte.MaxValue;
            source.LiquidKind = sourceBefore.LiquidKind;
        }

        if (source.LiquidAmount != sourceBefore.LiquidAmount || source.LiquidKind != sourceBefore.LiquidKind)
        {
            SetTile(x, y, in source);
            Record(x, y, in source, changes, ref changed);
        }

        SetTile(x, y + 1, in below);
        Record(x, y + 1, in below, changes, ref changed);

        // Vanilla always schedules the lower cell. It additionally wakes the horizontal neighbours
        // when the source actually lost liquid; the 255->254 exception deliberately skips that wake-up.
        TryBuffer(x, y + 1);
        if (!preserveFullSource && !quickSettleSaturatedSource)
        {
            TryBuffer(x - 1, y);
            TryBuffer(x + 1, y);
        }
    }

    /// <summary>
    /// TerrariaServer 1.4.5.8 <c>Liquid.Update</c> increments per-entry delay before allowing lava
    /// to flow after five delayed updates and honey after ten. Water and shimmer do not use this
    /// flow delay. Material reaction checks occur before those delays in vanilla and are handled earlier
    /// in this update path.
    /// </summary>
    private bool ShouldDelayFlow1458(
        in WorldLiquidUpdate update,
        in WorldTile source,
        VanillaLiquidUpdateMode1458 mode)
    {
        if (mode == VanillaLiquidUpdateMode1458.QuickSettle)
            return false;

        int threshold = source.LiquidKind switch
        {
            WorldLiquidKind.Lava => LavaFlowDelayUpdates1458,
            WorldLiquidKind.Honey => HoneyFlowDelayUpdates1458,
            _ => 0
        };

        if (threshold == 0 || update.Delay >= threshold)
            return false;

        // The current entry has already been dequeued, so re-enqueueing it cannot increase the
        // bounded pending-set cardinality. Delay-gated updates return before vanilla's kill tail,
        // therefore the existing kill state is preserved exactly.
        if (!tiles.LiquidUpdates.TryEnqueue(x: update.X, y: update.Y, delay: update.Delay + 1, kill: update.Kill))
        {
            throw new InvalidOperationException(
                "Dequeued liquid work could not be re-enqueued for its verified vanilla flow delay.");
        }

        return true;
    }

    /// <summary>
    /// Replays the ordinary same-kind horizontal leveling shape from TerrariaServer 1.4.5.8
    /// <c>Liquid.Update</c>. Vanilla widens the averaging window from the immediate neighbours to
    /// two and then three cells only when those outer cells already contain the same liquid. This is
    /// materially different from the former one-sided 32-unit trickle and is what makes a blocked
    /// water column visibly spread across a floor in the same update family. Material reactions and
    /// the ordinary dedicated-server kill/retirement tail are handled by the surrounding update path.
    /// </summary>
    private void LevelHorizontally1458(
        int x,
        int y,
        in WorldTile source,
        Span<WorldLiquidSimulationChange> changes,
        ref int changed)
    {
        bool left1 = CanShareAt(x - 1, y, source.LiquidKind, requireExistingLiquid: false);
        bool right1 = CanShareAt(x + 1, y, source.LiquidKind, requireExistingLiquid: false);
        if (!left1 && !right1)
            return;

        bool left2 = left1 && CanShareAt(x - 2, y, source.LiquidKind, requireExistingLiquid: true);
        bool right2 = right1 && CanShareAt(x + 2, y, source.LiquidKind, requireExistingLiquid: true);

        // Liquid.Update deliberately suppresses the wider ±2/±3 averaging window for nearly-full cells.
        if (source.LiquidAmount > 250)
        {
            left2 = false;
            right2 = false;
        }

        Span<int> xs = stackalloc int[7];
        int count = 0;

        if (left1 && right1)
        {
            if (left2 && right2)
            {
                bool left3 = CanShareAt(x - 3, y, source.LiquidKind, requireExistingLiquid: true);
                bool right3 = CanShareAt(x + 3, y, source.LiquidKind, requireExistingLiquid: true);
                if (left3 && right3)
                {
                    xs[count++] = x - 3;
                    xs[count++] = x - 2;
                    xs[count++] = x - 1;
                    xs[count++] = x;
                    xs[count++] = x + 1;
                    xs[count++] = x + 2;
                    xs[count++] = x + 3;
                }
                else
                {
                    xs[count++] = x - 2;
                    xs[count++] = x - 1;
                    xs[count++] = x;
                    xs[count++] = x + 1;
                    xs[count++] = x + 2;
                }
            }
            else if (left2)
            {
                xs[count++] = x - 2;
                xs[count++] = x - 1;
                xs[count++] = x;
                xs[count++] = x + 1;
            }
            else if (right2)
            {
                xs[count++] = x - 1;
                xs[count++] = x;
                xs[count++] = x + 1;
                xs[count++] = x + 2;
            }
            else
            {
                xs[count++] = x - 1;
                xs[count++] = x;
                xs[count++] = x + 1;
            }
        }
        else if (left1)
        {
            xs[count++] = x - 1;
            xs[count++] = x;
        }
        else
        {
            xs[count++] = x;
            xs[count++] = x + 1;
        }

        int total = source.LiquidAmount < 3 ? -1 : 0;
        for (int i = 0; i < count; i++)
            total += tiles.Get(xs[i], y).LiquidAmount;

        byte level = checked((byte)Math.Round(total / (double)count));
        bool preserveSourceColumn = false;
        if (count is 5 or 7 && y > 0 && tiles.Get(x, y - 1).LiquidAmount > 0)
        {
            preserveSourceColumn = true;
            for (int i = 0; i < count; i++)
            {
                if (xs[i] == x)
                    continue;
                if (tiles.Get(xs[i], y).LiquidAmount != level)
                {
                    preserveSourceColumn = false;
                    break;
                }
            }
        }

        for (int i = 0; i < count; i++)
        {
            if (preserveSourceColumn && xs[i] == x)
                continue;
            SetLeveledCell(xs[i], y, level, source.LiquidKind, changes, ref changed);
        }
    }

    private bool CanShareAt(int x, int y, WorldLiquidKind kind, bool requireExistingLiquid)
    {
        if (!Contains(x, y))
            return false;

        WorldTile tile = tiles.Get(x, y);
        if (IsLiquidBarrier(in tile))
            return false;
        if (tile.LiquidAmount > 0 && tile.LiquidKind != kind)
            return false;
        return !requireExistingLiquid || tile.LiquidAmount > 0;
    }

    private void SetLeveledCell(
        int x,
        int y,
        byte amount,
        WorldLiquidKind kind,
        Span<WorldLiquidSimulationChange> changes,
        ref int changed)
    {
        WorldTile tile = tiles.Get(x, y);
        WorldLiquidKind normalizedKind = amount == 0 ? WorldLiquidKind.Water : kind;
        if (tile.LiquidAmount == amount && tile.LiquidKind == normalizedKind)
            return;

        tile.LiquidAmount = amount;
        tile.LiquidKind = normalizedKind;
        SetTile(x, y, in tile);
        Record(x, y, in tile, changes, ref changed);
        BufferAffected(x, y);
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

        int height = tiles.Dimensions.HeightTiles;
        if (height > UnderworldLayerOffset1458 &&
            y > height - UnderworldLayerOffset1458 &&
            source.LiquidKind == WorldLiquidKind.Water)
        {
            return true;
        }

        if (HasForeignLiquidNeighbour(x, y, source.LiquidKind))
            return true;

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

    private bool HasForeignLiquidNeighbour(int x, int y, WorldLiquidKind sourceKind) =>
        IsForeignLiquidAt(x - 1, y, sourceKind) ||
        IsForeignLiquidAt(x + 1, y, sourceKind) ||
        IsForeignLiquidAt(x, y - 1, sourceKind) ||
        IsForeignLiquidAt(x, y + 1, sourceKind);

    private bool IsForeignLiquidAt(int x, int y, WorldLiquidKind sourceKind)
    {
        if (!Contains(x, y))
            return false;
        WorldTile tile = tiles.Get(x, y);
        return IsForeignLiquid(in tile, sourceKind);
    }

    private static bool CanAccept(in WorldTile tile, WorldLiquidKind kind) =>
        !IsLiquidBarrier(in tile) &&
        (tile.LiquidAmount == 0 || tile.LiquidKind == kind);

    private static bool IsLiquidBarrier(in WorldTile tile) =>
        tile.IsActive &&
        !tile.IsActuated &&
        VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
        !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);

    private void SetTile(int x, int y, in WorldTile tile)
    {
        if (useInitialPopulationWrites)
            tiles.SetInitialPopulationTile(x, y, in tile);
        else
            tiles.Set(x, y, in tile);
    }

    private bool Contains(int x, int y) =>
        (uint)x < (uint)tiles.Dimensions.WidthTiles &&
        (uint)y < (uint)tiles.Dimensions.HeightTiles;

    private int PendingCount => tiles.LiquidUpdates.ActiveCount + tiles.LiquidUpdates.BufferedCount;

    private sealed class LocalLiquidTileSideEffectSink1458(
        VanillaWorldTileMutationService mutations,
        WorldTileStore tiles) : IVanillaLiquidTileSideEffectSink1458
    {
        private VanillaLiquidMergeTileRequest1458 prepared;
        private bool hasPrepared;

        public bool TryCutTile(int x, int y) => false;

        public bool TryPrepareMergeTile(in VanillaLiquidMergeTileRequest1458 request)
        {
            if (hasPrepared)
                throw new InvalidOperationException("A liquid merge preparation is already outstanding.");

            WorldTile current = tiles.Get(request.X, request.Y);
            if (current.IsActive || request.TargetBefore.IsActive ||
                current.Type != request.TargetBefore.Type ||
                current.Flags != request.TargetBefore.Flags ||
                !VanillaTileDefinitionCatalog.TryGet(request.MergeTileType, out VanillaTileDefinition definition) ||
                definition.BreakPath != VanillaTileBreakPath.SimpleCell)
            {
                return false;
            }

            prepared = request;
            hasPrepared = true;
            return true;
        }

        public void CommitPreparedMergeTile(in VanillaLiquidMergeTileRequest1458 request)
        {
            if (!hasPrepared || prepared != request)
                throw new InvalidOperationException("Liquid merge commit does not match the outstanding preparation.");

            var mutation = new WorldTileMutationRequest(
                WorldTileMutationKind.PlaceTile,
                request.X,
                request.Y,
                TileType: request.MergeTileType);
            WorldTileMutationResult result = mutations.Apply(in mutation);
            hasPrepared = false;
            prepared = default;
            if (!result.Applied)
            {
                throw new InvalidOperationException(
                    $"Prepared inactive liquid merge placement failed unexpectedly: {result.Status}.");
            }
        }

        public void AbortPreparedMergeTile()
        {
            hasPrepared = false;
            prepared = default;
        }
    }

    private static void Record(
        int x,
        int y,
        in WorldTile tile,
        Span<WorldLiquidSimulationChange> changes,
        ref int changed,
        bool requiresTileSquareReplication = false)
    {
        // One Liquid.Update can touch the same source cell during gravity and horizontal leveling.
        // Replication only needs the final committed cell state, so coalesce repeated writes rather than
        // publishing transient states or inflating the bounded change buffer.
        for (int i = 0; i < changed; i++)
        {
            if (changes[i].X != x || changes[i].Y != y)
                continue;

            WorldLiquidSimulationChange existing = changes[i];
            changes[i] = new WorldLiquidSimulationChange(
                x,
                y,
                tile.LiquidAmount,
                tile.LiquidKind,
                existing.RequiresTileSquareReplication || requiresTileSquareReplication,
                existing.TileSquareStartX,
                existing.TileSquareStartY,
                existing.TileSquareWidth,
                existing.TileSquareHeight,
                existing.TileChangeType);
            return;
        }

        if ((uint)changed >= (uint)changes.Length)
        {
            throw new InvalidOperationException(
                "Liquid simulation change buffer is smaller than the verified per-tick mutation footprint.");
        }

        changes[changed++] = new WorldLiquidSimulationChange(
            x,
            y,
            tile.LiquidAmount,
            tile.LiquidKind,
            requiresTileSquareReplication);
    }
}

public enum VanillaWaterCheckResult1458 : byte
{
    Applied = 0,
    UnsupportedLiquidDeathTile = 1
}

public readonly record struct VanillaWaterCheckDiagnostic1458(
    VanillaWaterCheckResult1458 Result,
    int X,
    int Y,
    TileTypeId TileType)
{
    public static VanillaWaterCheckDiagnostic1458 Applied =>
        new(VanillaWaterCheckResult1458.Applied, 0, 0, default);

    public bool IsApplied => Result == VanillaWaterCheckResult1458.Applied;
}

public readonly record struct WorldLiquidSimulationChange(
    int X,
    int Y,
    byte Amount,
    WorldLiquidKind Kind,
    bool RequiresTileSquareReplication = false,
    int TileSquareStartX = 0,
    int TileSquareStartY = 0,
    byte TileSquareWidth = 0,
    byte TileSquareHeight = 0,
    VanillaTileChangeType1458 TileChangeType = VanillaTileChangeType1458.None)
{
    public bool HasExplicitTileSquare =>
        RequiresTileSquareReplication && TileSquareWidth != 0 && TileSquareHeight != 0;
}

internal enum VanillaLiquidUpdateMode1458 : byte
{
    Ordinary = 0,
    QuickSettle = 1
}
