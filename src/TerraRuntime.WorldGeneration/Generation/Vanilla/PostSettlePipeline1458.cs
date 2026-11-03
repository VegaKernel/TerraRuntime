using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Fifth source-backed Terraria 1.4.5.8 world-generation overlay. It extends ordinary canonical generation from
/// Remove Water From Sand through Statues, stopping immediately before the chest-placement series. This keeps
/// generation-time terrain decoration separate from chest side-table metadata work.
/// </summary>
public sealed class SourceBackedPostSettle1458 : IWorldGenerationProvider
{
    internal static readonly WorldGenerationPassId RemoveWaterFromSandId =
        new("terraria:1.4.5.8/RemoveWaterFromSand");
    internal static readonly WorldGenerationPassId OasisId = new("terraria:1.4.5.8/Oasis");
    internal static readonly WorldGenerationPassId ShellPilesId = new("terraria:1.4.5.8/ShellPiles");
    internal static readonly WorldGenerationPassId SmoothWorldId = new("terraria:1.4.5.8/SmoothWorld");
    internal static readonly WorldGenerationPassId WaterfallsId = new("terraria:1.4.5.8/Waterfalls");
    internal static readonly WorldGenerationPassId IceId = new("terraria:1.4.5.8/Ice");
    internal static readonly WorldGenerationPassId WallVarietyId = new("terraria:1.4.5.8/WallVariety");
    internal static readonly WorldGenerationPassId LifeCrystalsId = new("terraria:1.4.5.8/LifeCrystals");
    internal static readonly WorldGenerationPassId StatuesId = new("terraria:1.4.5.8/Statues");

    private static readonly WorldGenerationPassId SecretSeedsId = new("terraria:1.4.5.8/SecretSeeds");
    private readonly SourceBackedJungleStructures1458 baseline = new();

    public WorldGeneratorId Id => baseline.Id;

    public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var capture = new CapturePlanBuilder();
        baseline.BuildPlan(in request, capture);

        WorldGenerationRequest requestCopy = request;
        VanillaWorldSeedProfile1458 profile = WorldSeedResolver1458.Resolve(in requestCopy);
        if (!profile.IsDefault || !TerrainPass1458.IsCanonicalWorldSize(request.WidthTiles, request.HeightTiles))
        {
            capture.Replay(builder);
            return;
        }

        var state = new PostSettleState1458();
        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id != SecretSeedsId)
            {
                builder.Add(entry.Descriptor, entry.Pass);
                continue;
            }

            Add(builder, RemoveWaterFromSandId, SourceBackedJungleStructures1458.SettleLiquidsId,
                new PostSettlePass1458(PostSettleStage1458.RemoveWaterFromSand, state));
            Add(builder, OasisId, RemoveWaterFromSandId,
                new PostSettlePass1458(PostSettleStage1458.Oasis, state));
            Add(builder, ShellPilesId, OasisId,
                new PostSettlePass1458(PostSettleStage1458.ShellPiles, state));
            Add(builder, SmoothWorldId, ShellPilesId,
                new PostSettlePass1458(PostSettleStage1458.SmoothWorld, state));
            Add(builder, WaterfallsId, SmoothWorldId,
                new PostSettlePass1458(PostSettleStage1458.Waterfalls, state));
            Add(builder, IceId, WaterfallsId,
                new PostSettlePass1458(PostSettleStage1458.Ice, state));
            Add(builder, WallVarietyId, IceId,
                new PostSettlePass1458(PostSettleStage1458.WallVariety, state));
            Add(builder, LifeCrystalsId, WallVarietyId,
                new PostSettlePass1458(PostSettleStage1458.LifeCrystals, state));
            Add(builder, StatuesId, LifeCrystalsId,
                new PostSettlePass1458(PostSettleStage1458.Statues, state));

            builder.Add(CloneDescriptor(entry.Descriptor, [StatuesId]), entry.Pass);
        }
    }

    private static void Add(
        IWorldGenerationPlanBuilder builder,
        WorldGenerationPassId id,
        WorldGenerationPassId after,
        IWorldGenerationPass pass) =>
        builder.Add(
            new WorldGenerationPassDescriptor(
                id,
                WorldGenerationRngMode.VanillaSharedRng,
                requiredAfter: [after]),
            pass);

    private static WorldGenerationPassDescriptor CloneDescriptor(
        WorldGenerationPassDescriptor source,
        WorldGenerationPassId[] requiredAfter) =>
        new(
            source.Id,
            source.RngMode,
            requiredAfter,
            source.OptionalAfter.ToArray(),
            source.OptionalBefore.ToArray());

    private readonly record struct CapturedPass(WorldGenerationPassDescriptor Descriptor, IWorldGenerationPass Pass);

    private sealed class CapturePlanBuilder : IWorldGenerationPlanBuilder
    {
        private readonly List<CapturedPass> entries = [];
        public IReadOnlyList<CapturedPass> Entries => entries;

        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            entries.Add(new CapturedPass(descriptor, pass));

        public void Replay(IWorldGenerationPlanBuilder builder)
        {
            foreach (CapturedPass entry in entries)
                builder.Add(entry.Descriptor, entry.Pass);
        }
    }
}

internal enum PostSettleStage1458 : byte
{
    RemoveWaterFromSand,
    Oasis,
    ShellPiles,
    SmoothWorld,
    Waterfalls,
    Ice,
    WallVariety,
    LifeCrystals,
    Statues
}

internal sealed class PostSettleState1458
{
    public VanillaWorldGenerationBootstrapState1458? Bootstrap { get; private set; }
    public double WorldSurface { get; private set; }
    public double RockLayer { get; private set; }
    public int UnderworldTop { get; private set; }
    /// <summary>
    /// Source-retained <c>GenVars.oasisPosition/oasisWidth</c> entries in placement order. Later desert passes
    /// read the centre and half-width, so this is the pass's published output and not a diagnostic.
    /// </summary>
    public List<VanillaOasisAnchor1458> OasisAnchors { get; } = [];

    public void EnsureInitialized(IWorldGenerationContext context, Workspace workspace)
    {
        if (Bootstrap is not null)
            return;

        Bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Post-settle vanilla generation requires Reset bootstrap state.");
        if (context.Metadata is null || !context.Metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Post-settle vanilla generation requires source-backed Terrain layers.");

        WorldSurface = layers.WorldSurface;
        RockLayer = layers.RockLayer;
        UnderworldTop = Math.Clamp(workspace.HeightTiles - 200, (int)RockLayer + 120, workspace.HeightTiles - 90);
    }
}

internal sealed class PostSettlePass1458 : IWorldGenerationPass
{
    private const ushort LifeCrystal = 12;
    private const ushort Sand = 53;
    private const ushort SandstoneBrick = 151;
    private const ushort Ice = 161;
    private const ushort Sandstone = 396;
    private const ushort HardenedSand = 397;
    private const ushort DesertFossil = 404;
    private const ushort FossilOre = 407;
    private const ushort ShellPile = 495;


    private readonly PostSettleStage1458 stage;
    private readonly PostSettleState1458 state;

    public PostSettlePass1458(
        PostSettleStage1458 stage,
        PostSettleState1458 state)
    {
        this.stage = stage;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Workspace workspace = context.Workspace as Workspace ??
            throw new InvalidOperationException("Post-settle Terraria generation requires Workspace.");
        state.EnsureInitialized(context, workspace);
        var grid = new RuntimeGrid(workspace);
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Post-settle Terraria generation requires shared UnifiedRandom semantics.");

        switch (stage)
        {
            case PostSettleStage1458.RemoveWaterFromSand:
                ApplyRemoveWaterFromSand(context, workspace, grid);
                break;
            case PostSettleStage1458.Oasis:
                ApplyOasis(context, workspace);
                break;
            case PostSettleStage1458.ShellPiles:
                ApplyShellPiles(context, workspace, grid, random);
                break;
            case PostSettleStage1458.SmoothWorld:
                ApplySmoothWorld(context, workspace);
                break;
            case PostSettleStage1458.Waterfalls:
                ApplyWaterfalls(context, workspace, grid, random);
                break;
            case PostSettleStage1458.Ice:
                ApplyIce(context, workspace, grid);
                break;
            case PostSettleStage1458.WallVariety:
                ApplyWallVariety(context, workspace);
                break;
            case PostSettleStage1458.LifeCrystals:
                ApplyLifeCrystals(context, workspace, grid, random);
                break;
            case PostSettleStage1458.Statues:
                ApplyStatues(context, workspace, grid, random);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void ApplyRemoveWaterFromSand(IWorldGenerationContext context, Workspace workspace, RuntimeGrid grid)
    {
        long drained = 0;
        // WorldGen.AddPasses / RemoveSurfaceWaterAboveSand: inspect only the first active
        // surface tile, away from the oceans. This is not an embedded-liquid cleanup.
        for (int x = 400; x < grid.Width - 400; x++)
        {
            if ((x & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            for (int y = 100; y < state.WorldSurface - 1d; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (!tile.IsActive)
                    continue;
                if (tile.Type is Sand or Sandstone or HardenedSand or DesertFossil or FossilOre or SandstoneBrick)
                {
                    for (int aboveY = y - 1; aboveY >= 100; aboveY--)
                    {
                        ref WorldTile above = ref grid.At(x, aboveY);
                        if (above.IsActive)
                            break;
                        drained += above.LiquidAmount;
                        above.LiquidAmount = 0;
                    }
                }
                break;
            }
        }

        if (workspace.HasVanillaGenerationSolidity)
            workspace.SetVanillaGenerationSolid(new TileTypeId(192), true);
        context.ReportProgress(1d, $"Removing surface liquid above sand ({drained} liquid units)");
    }

    /// <summary>
    /// The registered TerrariaServer 1.4.5.8 <c>GenPassNameID.Oasis</c> pass. It draws
    /// <c>maxTilesX / 2100 + genRand.Next(2)</c> basins and, for each, retries up to <c>maxTilesX * 2</c> times
    /// with a column in <c>[beachDistance + 300, maxTilesX - (beachDistance + 300))</c> and a row in
    /// <c>[100, worldSurface)</c> until <c>WorldGen.PlaceOasis</c> accepts one.
    /// </summary>
    /// <remarks>
    /// Each basin gets its own fresh attempt budget, and an exhausted budget simply produces one fewer oasis -
    /// the source does not widen the search or fall back to a forced placement. Retention stops at the source's
    /// twenty entries while placement continues, so the count of shaped basins and the count of retained anchors
    /// are not necessarily the same number.
    /// </remarks>
    private void ApplyOasis(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Oasis requires shared UnifiedRandom semantics.");
        int width = workspace.WidthTiles;
        var basin = new OasisBasin1458(
            workspace.TileStore,
            random,
            state.WorldSurface,
            context.CancellationToken);

        state.OasisAnchors.Clear();
        int count = width / 2100;
        count += random.Next(2);
        int margin = DungeonGenerationCatalog1458.BeachDistance + 300;
        int placed = 0;
        for (int index = 0; index < count; index++)
        {
            int remaining = width * 2;
            while (remaining > 0)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                remaining--;
                int x = random.Next(margin, width - margin);
                int y = random.Next(100, (int)state.WorldSurface);
                if (basin.TryPlace(x, y, state.OasisAnchors) is not VanillaOasisAnchor1458 anchor)
                    continue;

                placed++;
                if (state.OasisAnchors.Count < OasisBasin1458.MaximumRetainedOasis1458)
                    state.OasisAnchors.Add(anchor);
                remaining = -1;
            }
        }

        workspace.SetVanillaOasisAnchors(state.OasisAnchors);
        context.ReportProgress(1d, $"Generating oasis basins ({placed}/{count})");
    }

    private void ApplyShellPiles(IWorldGenerationContext context, Workspace workspace, RuntimeGrid grid, IWorldGenerationVanillaRandom random)
    {
        // TerrariaServer 1.4.5.8 GenPassNameID.ShellPilesMarblePilesAndSpikePits.
        // The source-backed provider enters this pass only for ordinary seeds;
        // MarblePiles, SpikeCaves and the notTheBees refusal belong to the secret-seed barrier.
        VanillaShellAnchor1458 left = workspace.VanillaLeftShellAnchor;
        VanillaShellAnchor1458 right = workspace.VanillaRightShellAnchor;
        if (random.Next(2) == 0)
        {
            for (int x = left.X - 20; x <= left.X + 20; x++)
                for (int y = left.Y - 10; y <= left.Y + 10; y++)
                    if (grid.At(x, y).IsActive && grid.At(x, y).Type == Sand &&
                        !grid.At(x, y - 1).IsActive && grid.At(x, y - 1).LiquidAmount == 0 &&
                        !grid.At(x - 1, y).IsActive && grid.At(x - 1, y).LiquidAmount > 0)
                        left = new VanillaShellAnchor1458(x, y);
            left = left with { Y = left.Y - 50, X = left.X - random.Next(5) };
            if (random.Next(2) == 0) left = left with { X = left.X - random.Next(10) };
            if (random.Next(3) == 0) left = left with { X = left.X - random.Next(15) };
            if (random.Next(4) != 0) ShellPileAt(grid, random, left.X, left.Y);
            int denominator = random.Next(2, 4);
            if (random.Next(denominator) == 0) ShellPileAt(grid, random, left.X - random.Next(10, 35), left.Y);
            if (random.Next(denominator) == 0) ShellPileAt(grid, random, left.X - random.Next(40, 65), left.Y);
            if (random.Next(denominator) == 0) ShellPileAt(grid, random, left.X - random.Next(70, 95), left.Y);
            if (random.Next(denominator) == 0) ShellPileAt(grid, random, left.X - random.Next(100, 125), left.Y);
            if (random.Next(denominator) == 0) ShellPileAt(grid, random, left.X + random.Next(10, 25), left.Y);
        }
        if (random.Next(2) == 0)
        {
            for (int x = right.X - 20; x <= right.X + 20; x++)
                for (int y = right.Y - 10; y <= right.Y + 10; y++)
                    if (grid.At(x, y).IsActive && grid.At(x, y).Type == Sand &&
                        !grid.At(x, y - 1).IsActive && grid.At(x, y - 1).LiquidAmount == 0 &&
                        !grid.At(x + 1, y).IsActive && grid.At(x + 1, y).LiquidAmount > 0)
                        right = new VanillaShellAnchor1458(x, y);
            right = right with { Y = right.Y - 50, X = right.X + random.Next(5) };
            // The source's next two right-beach draws alter shellStartXLeft, not shellStartXRight.
            if (random.Next(2) == 0) left = left with { X = left.X + random.Next(10) };
            if (random.Next(3) == 0) left = left with { X = left.X + random.Next(15) };
            if (random.Next(4) != 0) ShellPileAt(grid, random, right.X, right.Y);
            int denominator = random.Next(2, 4);
            if (random.Next(denominator) == 0) ShellPileAt(grid, random, right.X + random.Next(10, 35), right.Y);
            if (random.Next(denominator) == 0) ShellPileAt(grid, random, right.X + random.Next(40, 65), right.Y);
            if (random.Next(denominator) == 0) ShellPileAt(grid, random, right.X + random.Next(70, 95), right.Y);
            if (random.Next(denominator) == 0) ShellPileAt(grid, random, right.X + random.Next(100, 125), right.Y);
            if (random.Next(denominator) == 0) ShellPileAt(grid, random, right.X - random.Next(10, 25), right.Y);
        }
        workspace.SetVanillaShellAnchors(left, right);
        context.ReportProgress(1d, "Applied source-backed ocean shell-pile offers");
    }

    private bool ShellPileAt(RuntimeGrid grid, IWorldGenerationVanillaRandom random, int x, int y)
    {
        if (!grid.Contains(x, y)) return false;
        int groundY = y;
        while (!SolidShellFloor(grid.At(x, groundY)))
        {
            groundY++;
            if (groundY > state.WorldSurface || !grid.Contains(x, groundY)) return false;
        }
        if (grid.At(x, groundY).Type != Sand || grid.At(x, groundY).Wall != 0) return false;

        int originY = groundY - 1;
        double centerX = x;
        double centerY = originY;
        double velocityX = random.NextDouble() * 0.6 - 0.3;
        double velocityY = random.NextDouble() * 0.5 + 0.5;
        double radius = random.Next(2, 4);
        if (random.Next(10) == 0) radius += 1d;
        int steps = random.Next(3, 6);
        while (steps-- > 0)
        {
            for (int column = x - (int)radius * 4; column <= x + radius * 4d; column++)
            {
                int row = originY + (int)radius * 4;
                while (row > originY - radius * 4d)
                {
                    double threshold = radius * random.Next(70, 91) * 0.01;
                    double dx = (centerX - column) * 0.6;
                    double dy = centerY - row;
                    if (Math.Sqrt(dx * dx + dy * dy) < threshold)
                    {
                        if (row <= originY + 1 || random.Next(6) == 0)
                        {
                            SetShellMaterial(grid, column, row, ShellPile);
                            if (!grid.At(column, row + 1).IsActive)
                            {
                                SetShellMaterial(grid, column, row + 1, ShellPile);
                                if (!grid.At(column, row + 2).IsActive)
                                {
                                    SetShellMaterial(grid, column, row + 2, Sand);
                                    if (!grid.At(column, row + 3).IsActive)
                                        SetShellMaterial(grid, column, row + 3, HardenedSand);
                                }
                            }
                        }
                        else if (grid.At(column, row).Type != ShellPile)
                            SetShellMaterial(grid, column, row, Sand);
                    }
                    row--;
                }
            }
            centerX += velocityX;
            centerY += velocityY;
            velocityX += random.NextDouble() * 0.2 - 0.1;
            velocityY += random.NextDouble() * 0.2 - 0.1;
            // Source Utils.Clamp returns a value that this caller discards.
        }
        return true;
    }

    private static bool SolidShellFloor(in WorldTile tile) =>
        tile.IsActive && !tile.IsActuated && tile.Shape == 0 && tile.Type != 484 &&
        VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
        !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);

    private static void SetShellMaterial(RuntimeGrid grid, int x, int y, ushort type)
    {
        ref WorldTile tile = ref grid.At(x, y);
        tile.Type = type;
        tile.Flags |= WorldTileFlags.Active;
        tile.Shape = 0;
    }

    private static void ApplySmoothWorld(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Smooth World requires shared UnifiedRandom semantics.");
        WorldSmoothingResult1458 result = WorldSmoother1458.Apply(
            workspace,
            random,
            context.CancellationToken);
        context.ReportProgress(
            1d,
            $"Smoothing terrain (slopes={result.SlopedTiles}, half={result.HalfBricks}, " +
            $"removed={result.RemovedTiles}, filled={result.FilledTiles})");
    }

    private void ApplyWaterfalls(IWorldGenerationContext context, Workspace workspace, RuntimeGrid grid, IWorldGenerationVanillaRandom random)
    {
        if (workspace.HasVanillaGenerationSolidity)
        {
            workspace.SetVanillaGenerationSolid(new TileTypeId(191), false);
        }
        // TerrariaServer 1.4.5.8 GenPassNameID.Waterfalls scans the entire interior twice.
        // It shapes the lip of a liquid fall; it never paints a descending liquid column.
        long pounded = 0;
        for (int x = 20; x < grid.Width - 20; x++)
        {
            if ((x & 31) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            for (int y = 20; y < grid.Height - 20; y++)
            {
                if (!SolidWaterfallTile(workspace, grid.At(x, y)) || grid.At(x - 1, y).IsActive ||
                    !SolidWaterfallTile(workspace, grid.At(x, y + 1)) || grid.At(x + 1, y).IsActive ||
                    grid.At(x - 1, y).LiquidAmount == 0 && grid.At(x + 1, y).LiquidAmount == 0)
                    continue;

                int top = y - random.Next(8, 20);
                int bottom = y + random.Next(8, 20);
                bool allowed = true;
                for (int scanY = top; scanY <= bottom; scanY++)
                    if (grid.At(x, scanY).IsActive && grid.At(x, scanY).Shape == 1)
                        allowed = false;
                if (grid.At(x, y).Type is 75 or 76 && random.Next(10) != 0)
                    allowed = false;
                if (allowed && PoundWaterfallTile(grid, x, y))
                    pounded++;
            }
        }
        for (int x = 20; x < grid.Width - 20; x++)
        {
            if ((x & 31) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            for (int y = 20; y < grid.Height - 20; y++)
            {
                WorldTile tile = grid.At(x, y);
                if (tile.Type is 48 or 232 || !SolidWaterfallTile(workspace, tile) ||
                    !SolidWaterfallTile(workspace, grid.At(x, y + 1)))
                    continue;
                if (!SolidWaterfallTile(workspace, grid.At(x + 1, y)) && grid.At(x - 1, y).IsActive &&
                    grid.At(x - 1, y).Shape == 1 && grid.At(x - 2, y).LiquidAmount > 0 &&
                    PoundWaterfallTile(grid, x, y))
                    pounded++;
                if (!SolidWaterfallTile(workspace, grid.At(x - 1, y)) && grid.At(x + 1, y).IsActive &&
                    grid.At(x + 1, y).Shape == 1 && grid.At(x + 2, y).LiquidAmount > 0 &&
                    PoundWaterfallTile(grid, x, y))
                    pounded++;
            }
        }

        if (workspace.HasVanillaGenerationSolidity)
        {
            workspace.SetVanillaGenerationSolid(new TileTypeId(191), true);
        }
        context.ReportProgress(1d, $"Shaped waterfall lips ({pounded} pound operations)");
    }

    private static bool SolidWaterfallTile(Workspace workspace, in WorldTile tile) =>
        tile.IsActive && !tile.IsActuated && tile.Shape == 0 &&
        (workspace.HasVanillaGenerationSolidity ? workspace.IsVanillaGenerationSolid(tile.TileType) : VanillaTileCollisionCatalog.IsSolid(tile.TileType)) &&
        !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);

    private static bool PoundWaterfallTile(RuntimeGrid grid, int x, int y)
    {
        ref WorldTile tile = ref grid.At(x, y);
        WorldTile above = grid.At(x, y - 1);
        if (!tile.IsActive || tile.Wall == 350 ||
            !WorldSmoothingCatalog1458.CanBePounded(tile.TileType) ||
            above.IsActive && WorldSmoothingCatalog1458.ForbidsSlopingBelow(above.TileType) ||
            !WorldSmoothingCatalog1458.CanRemoveTileBelow(in above, tile.TileType))
            return false;
        // Generation PoundTile toggles halfBrick without SquareTileFrame or effects.
        tile.Shape = tile.Shape == 1 ? (byte)0 : (byte)1;
        return true;
    }

    private void ApplyIce(IWorldGenerationContext context, Workspace workspace, RuntimeGrid grid)
    {
        // TerrariaServer 1.4.5.8 GenPassNameID.FragileIceOverIceBiomeWater.
        // Ordinary generation scans every liquid cell below worldSurface; it
        // does not sample the snow bounds or convert stone/walls at random.
        var framer = new GenerationTileFraming1458(workspace.TileStore,
            context.VanillaRandom ?? throw new InvalidOperationException("Ice requires shared vanilla RNG."), workspace,
            useWorldGenerationSolidity: workspace.HasVanillaGenerationSolidity);
        long placed = 0;
        for (int x = 10; x < grid.Width - 10; x++)
        {
            if ((x & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            for (int y = (int)state.WorldSurface; y < grid.Height - 100; y++)
            {
                WorldTile tile = grid.At(x, y);
                if (tile.LiquidAmount > 0 && tile.LiquidKind != WorldLiquidKind.Lava)
                    placed += MakeWateryIceThing(grid, framer, x, y);
            }
        }
        // The source ends this pass by making Lihzahrd brick and fragile ice
        // temporarily non-solid for later generation owners.
        if (workspace.HasVanillaGenerationSolidity)
        {
            workspace.SetVanillaGenerationSolid(new TileTypeId(162), false);
            workspace.SetVanillaGenerationSolid(new TileTypeId(226), false);
        }

        context.ReportProgress(1d, $"Placed thin ice over snow-biome water ({placed} cells)");
    }

    private static int MakeWateryIceThing(RuntimeGrid grid, GenerationTileFraming1458 framer, int x, int y)
    {
        WorldTile start = grid.At(x, y);
        if (!grid.Contains(x, y) || start.LiquidAmount == 0 || start.IsActive ||
            start.LiquidKind == WorldLiquidKind.Lava)
            return 0;
        int floorY = y;
        while (!grid.At(x, floorY).IsActive && grid.At(x, floorY).LiquidAmount > 0)
        {
            floorY++;
            if (floorY > grid.Height - 50) return 0;
        }
        WorldTile floor = grid.At(x, floorY);
        if (!floor.IsActive || floor.Type is not (147 or 161 or 200 or 163 or 164)) return 0;

        int topY = floorY - 1;
        while (grid.At(x, topY).LiquidAmount > 0)
        {
            topY--;
            if (topY < 10) return 0;
        }
        if (grid.At(x, topY).IsActive) return 0;
        topY++;
        if (grid.At(x, topY).IsActive) return 0;

        int placed = 0;
        for (int column = x;
             grid.Contains(column, topY) && column > 0 && !grid.At(column, topY).IsActive &&
             grid.At(column, topY).LiquidAmount > 0 && grid.At(column, topY - 1).LiquidAmount == 0 &&
             !grid.At(column, topY - 1).IsActive && grid.At(column - 1, topY).Shape != 1;
             column--)
            placed += PlaceThinIce(grid, framer, column, topY);
        for (int column = x + 1;
             grid.Contains(column, topY) && column + 1 < grid.Width && !grid.At(column, topY).IsActive &&
             grid.At(column, topY).LiquidAmount > 0 && grid.At(column, topY - 1).LiquidAmount == 0 &&
             !grid.At(column, topY - 1).IsActive && grid.At(column + 1, topY).Shape != 1;
             column++)
            placed += PlaceThinIce(grid, framer, column, topY);
        return placed;
    }

    private static int PlaceThinIce(RuntimeGrid grid, GenerationTileFraming1458 framer, int x, int y)
    {
        // WorldGen.PlaceTile(162) on an inactive water cell clears only Tile,
        // TilePaint and Slope. Wall, liquid, wiring and actuator are retained.
        ref WorldTile tile = ref grid.At(x, y);
        tile.Type = 162;
        tile.FrameX = 0;
        tile.FrameY = 0;
        tile.TileColor = 0;
        tile.Shape = 0;
        tile.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        tile.Flags |= WorldTileFlags.Active;
        framer.SquareTileFrame(x, y);
        return 1;
    }

    private void ApplyWallVariety(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Wall variety requires shared UnifiedRandom semantics.");

        int lavaLine = workspace.VanillaLiquidLines?.LavaLine ?? checked((int)Math.Round(state.RockLayer));
        WorldGenerationPoint shimmer = workspace.VanillaShimmerPosition ?? new WorldGenerationPoint(0, 0);
        // The depth split reads the RETAINED GenVars.rockLayer, not the published gameplay layer. Handing it
        // the published one puts most pockets above the line and floods the world with the shallow family.
        double rockLayer = workspace.VanillaTerrainState?.CurrentRockLayer ?? state.RockLayer;
        var pass = new CaveWallVarietyPass1458(
            workspace.TileStore, random, workspace.VanillaTerrainState?.CurrentWorldSurface ?? state.WorldSurface, rockLayer, lavaLine, shimmer,
            context.CancellationToken, workspace.HasVanillaGenerationSolidity ? workspace.IsVanillaGenerationSolid : null);
        pass.Apply();
        context.ReportProgress(1d, $"Adding Wall Variety ({pass.Painted} cells in {pass.Pockets} pockets)");
    }

    private void ApplyLifeCrystals(IWorldGenerationContext context, Workspace workspace, RuntimeGrid grid, IWorldGenerationVanillaRandom random)
    {
        int quota = (int)((double)grid.Width * grid.Height * 0.00002);
        int top = (int)(state.WorldSurface * 2d + state.RockLayer) / 3;
        int placed = 0;
        for (int offer = 0; offer < quota; offer++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            for (int attempt = 0; attempt < 10000; attempt++)
            {
                int y = random.Next(top, grid.Height - 300);
                int x = random.Next(40, grid.Width - 40);
                if (TryLifeCrystal(workspace, grid, x, y)) { placed++; break; }
            }
        }
        if (workspace.HasVanillaGenerationSolidity)
        {
            workspace.SetVanillaGenerationSolid(new TileTypeId(225), false);
        }
        context.ReportProgress(1d, $"Placing Life Crystals ({placed}/{quota})");
    }

    private static bool LifeCrystalSolid(Workspace workspace, ushort type) =>
        workspace.HasVanillaGenerationSolidity ? workspace.IsVanillaGenerationSolid(new TileTypeId(type)) : VanillaTileCollisionCatalog.IsSolid(new TileTypeId(type));

    private static bool TryLifeCrystal(Workspace workspace, RuntimeGrid grid, int x, int y)
    {
        for (int floor = y; floor < grid.Height; floor++)
        {
            WorldTile ground = grid.At(x, floor);
            if (!ground.IsActive || !LifeCrystalSolid(workspace, ground.Type)) continue;
            int bottom = floor - 1, top = floor - 2;
            WorldTile a = grid.At(x, top), b = grid.At(x - 1, top);
            if (a.LiquidAmount > 0 && (a.LiquidKind == WorldLiquidKind.Lava || a.LiquidKind == WorldLiquidKind.Shimmer) ||
                b.LiquidAmount > 0 && b.LiquidKind == WorldLiquidKind.Lava) return false;
            for (int column = x - 1; column <= x; column++)
                for (int row = top; row <= bottom; row++)
                    if (grid.At(column, row).IsActive) return false;
            ushort wall = grid.At(x, bottom).Wall;
            if (wall is 7 or 8 or 9 or >= 94 and <= 99) return false;
            WorldTile left = grid.At(x - 1, floor);
            if (!left.IsActive || left.IsActuated || !LifeCrystalSolid(workspace, left.Type) || ground.IsActuated) return false;
            grid.At(x - 1, floor).ClearSourceShape();
            grid.At(x, floor).ClearSourceShape();
            for (int column = 0; column < 2; column++)
                for (int row = 0; row < 2; row++)
                {
                    ref WorldTile tile = ref grid.At(x - 1 + column, top + row);
                    tile.Flags |= WorldTileFlags.Active;
                    tile.Type = LifeCrystal;
                    tile.FrameX = checked((short)(18 * column));
                    tile.FrameY = checked((short)(18 * row));
                }
            return true;
        }
        return false;
    }

    private void ApplyStatues(IWorldGenerationContext context, Workspace workspace, RuntimeGrid grid, IWorldGenerationVanillaRandom random)
    {
        int listIndex = 0, quota = (int)(146d * grid.Width / 4200d), placed = 0;
        double oceanLine = (state.WorldSurface + state.RockLayer) / 2d + 40d;
        int top = (int)(state.WorldSurface * 2d + state.RockLayer) / 3;
        for (int offer = 0; offer < quota; offer++)
        {
            if (listIndex == 73) listIndex = 0;
            (ushort material, int style) = CaveHousePlacement1458.ResolveStatue(listIndex);
            for (int attempt = 0; attempt < 10000; attempt++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                int x = random.Next(20, grid.Width - 20), y = random.Next(top, grid.Height - 300);
                int oceanBudget = 100;
                while (y <= oceanLine && (x < 380 || x > grid.Width - 380))
                {
                    if (--oceanBudget <= 0) break;
                    x = random.Next(20, grid.Width - 20); y = random.Next(top, grid.Height - 300);
                }
                if (oceanBudget <= 0) break;
                while (y < grid.Height && !grid.At(x, y).IsActive) y++;
                if (y == grid.Height) break;
                y--;
                WorldTile anchor = grid.At(x, y);
                bool allowed = (!anchor.IsActive || !VanillaWorldFrameImportance326.IsFrameImportant(anchor.Type) ||
                    VanillaProjectileTileCutFacts.IsCuttable(anchor.TileType)) &&
                    !(anchor.LiquidAmount > 0 && anchor.LiquidKind == WorldLiquidKind.Shimmer);
                if (allowed) CaveHousePlacement1458.PlaceHouseObject(workspace, context.VanillaRandom!, x, y, material, style, forced: true);
                if (grid.At(x, y) is { IsActive: true } result && result.Type == material)
                {
                    if (listIndex is 4 or 7 or 10 or 18)
                        CaveHousePlacement1458.PlaceStatueTrap(workspace, context.VanillaRandom!, x, y);
                    placed++; listIndex++; break;
                }
            }
        }
        context.ReportProgress(1d, $"Placing Statues ({placed}/{quota})");
    }

    private VanillaWorldGenerationBootstrapState1458 RequireBootstrap() =>
        state.Bootstrap ?? throw new InvalidOperationException("Post-settle pass executed before bootstrap initialization.");

    private sealed class RuntimeGrid
    {
        private readonly WorldTileStore store;

        public RuntimeGrid(Workspace workspace) => store = workspace.TileStore;

        public int Width => store.Dimensions.WidthTiles;
        public int Height => store.Dimensions.HeightTiles;

        public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;
        public ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];

    }
}
