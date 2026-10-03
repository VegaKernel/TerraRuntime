using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Fourth source-backed Terraria 1.4.5.8 world-generation overlay. It extends the ordinary canonical pipeline from
/// Pyramids through the first Settle Liquids pass. The old compatibility Biomes pass becomes a no-op barrier because
/// source-backed Beaches already owns the remaining ocean geometry.
/// </summary>
public sealed class SourceBackedJungleStructures1458 : IWorldGenerationProvider
{
    internal static readonly WorldGenerationPassId DirtRockWallRunnerId =
        new("terraria:1.4.5.8/DirtRockWallRunner");
    internal static readonly WorldGenerationPassId LivingTreesId =
        new("terraria:1.4.5.8/LivingTrees");
    internal static readonly WorldGenerationPassId WoodTreeWallsId =
        new("terraria:1.4.5.8/WoodTreeWalls");
    internal static readonly WorldGenerationPassId AltarsId =
        new("terraria:1.4.5.8/Altars");
    internal static readonly WorldGenerationPassId WetJungleId =
        new("terraria:1.4.5.8/WetJungle");
    internal static readonly WorldGenerationPassId JungleTempleId =
        new("terraria:1.4.5.8/JungleTemple");
    internal static readonly WorldGenerationPassId HivesId =
        new("terraria:1.4.5.8/Hives");
    internal static readonly WorldGenerationPassId JungleChestsId =
        new("terraria:1.4.5.8/JungleChests");
    internal static readonly WorldGenerationPassId SettleLiquidsId =
        new("terraria:1.4.5.8/SettleLiquids");

    private static readonly WorldGenerationPassId BiomesId = new("terraria:1.4.5.8/Biomes");
    private static readonly WorldGenerationPassId SecretSeedsId = new("terraria:1.4.5.8/SecretSeeds");

    private readonly SourceBackedDungeonPipeline1458 baseline = new();

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

        var state = new JungleStructureState1458();

        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id == BiomesId)
            {
                builder.Add(
                    CloneDescriptor(entry.Descriptor, WorldGenerationRngMode.IsolatedDeterministic),
                    SourceBackedBiomesCompatibilityBarrier1458.Instance);
                continue;
            }

            if (entry.Descriptor.Id == SecretSeedsId)
            {
                Add(builder, DirtRockWallRunnerId, SourceBackedDungeonPipeline1458.PyramidsId,
                    new JungleStructurePass1458(
                        JungleStructureStage1458.DirtRockWallRunner, state));
                Add(builder, LivingTreesId, DirtRockWallRunnerId,
                    new JungleStructurePass1458(
                        JungleStructureStage1458.LivingTrees, state));
                Add(builder, WoodTreeWallsId, LivingTreesId,
                    new JungleStructurePass1458(
                        JungleStructureStage1458.WoodTreeWalls, state));
                Add(builder, AltarsId, WoodTreeWallsId,
                    new JungleStructurePass1458(
                        JungleStructureStage1458.Altars, state));
                Add(builder, WetJungleId, AltarsId,
                    new JungleStructurePass1458(
                        JungleStructureStage1458.WetJungle, state));
                Add(builder, JungleTempleId, WetJungleId,
                    new JungleStructurePass1458(
                        JungleStructureStage1458.JungleTemple, state));
                Add(builder, HivesId, JungleTempleId,
                    new JungleStructurePass1458(
                        JungleStructureStage1458.Hives, state));
                Add(builder, JungleChestsId, HivesId,
                    new JungleStructurePass1458(
                        JungleStructureStage1458.JungleChests, state));
                Add(builder, SettleLiquidsId, JungleChestsId,
                    new JungleStructurePass1458(
                        JungleStructureStage1458.SettleLiquids, state));

                builder.Add(
                    CloneDescriptor(entry.Descriptor, WorldGenerationRngMode.IsolatedDeterministic, [SettleLiquidsId]),
                    entry.Pass);
                continue;
            }

            builder.Add(entry.Descriptor, entry.Pass);
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
        WorldGenerationRngMode rngMode,
        WorldGenerationPassId[]? requiredAfter = null) =>
        new(
            source.Id,
            rngMode,
            requiredAfter ?? source.RequiredAfter.ToArray(),
            source.OptionalAfter.ToArray(),
            source.OptionalBefore.ToArray());

    private readonly record struct CapturedPass(
        WorldGenerationPassDescriptor Descriptor,
        IWorldGenerationPass Pass);

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

internal enum JungleStructureStage1458 : byte
{
    DirtRockWallRunner,
    LivingTrees,
    WoodTreeWalls,
    Altars,
    WetJungle,
    JungleTemple,
    Hives,
    JungleChests,
    SettleLiquids
}

internal sealed class JungleStructureState1458
{
    public VanillaWorldGenerationBootstrapState1458? Bootstrap { get; private set; }
    public double WorldSurface { get; private set; }
    public double RockLayer { get; private set; }
    public int UnderworldTop { get; private set; }
    public int TempleLeft { get; set; } = -1;
    public int TempleRight { get; set; } = -1;
    public int TempleTop { get; set; } = -1;
    public int TempleBottom { get; set; } = -1;
    public List<WorldGenerationPoint> JungleChestCandidates { get; } = [];

    public void EnsureInitialized(IWorldGenerationContext context, Workspace workspace)
    {
        if (Bootstrap is not null)
            return;

        Bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Jungle-structure vanilla generation requires the Reset bootstrap state.");
        if (context.Metadata is null || !context.Metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Jungle-structure vanilla generation requires source-backed Terrain layers.");

        WorldSurface = layers.WorldSurface;
        RockLayer = layers.RockLayer;
        UnderworldTop = Math.Clamp(workspace.HeightTiles - 200, (int)RockLayer + 120, workspace.HeightTiles - 90);
    }
}

internal sealed class JungleStructurePass1458 : IWorldGenerationPass
{
    private const ushort Dirt = 0;
    private const ushort Stone = 1;
    private const ushort Grass = 2;
    private const ushort Sand = 53;
    private const ushort Ash = 57;
    private const ushort Mud = 59;
    private const ushort JungleGrass = 60;
    private const ushort Silt = 123;
    private const ushort Snow = 147;
    private const ushort Ice = 161;
    private const ushort LivingWood = 191;
    private const ushort LeafBlock = 192;
    private const ushort Hive = 225;
    private const ushort LihzahrdBrick = 226;
    private const ushort Marble = 367;
    private const ushort Granite = 368;
    private const ushort Sandstone = 396;
    private const ushort HardenedSand = 397;

    private const ushort DirtUnsafeWall = 2;
    private const ushort RockyDirtUnsafeWall = 59;
    private const ushort JungleUnsafeWall = 64;
    private const ushort HiveUnsafeWall = 86;
    private const ushort LihzahrdBrickUnsafeWall = 87;
    private const ushort LivingWoodUnsafeWall = 244;

    private readonly JungleStructureStage1458 stage;
    private readonly JungleStructureState1458 state;

    public JungleStructurePass1458(
        JungleStructureStage1458 stage,
        JungleStructureState1458 state)
    {
        this.stage = stage;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Workspace workspace = context.Workspace as Workspace ??
            throw new InvalidOperationException(
                "Source-backed jungle-structure Terraria generation requires Workspace.");
        state.EnsureInitialized(context, workspace);
        var grid = new RuntimeGrid(workspace);
        var random = new VanillaRandom(
            context.VanillaRandom ??
            throw new InvalidOperationException(
                "Source-backed jungle-structure Terraria generation requires shared UnifiedRandom semantics."));

        switch (stage)
        {
            case JungleStructureStage1458.DirtRockWallRunner:
                ApplyDirtRockWallRunner(context, grid, random);
                break;
            case JungleStructureStage1458.LivingTrees:
                ApplyLivingTrees(context, workspace);
                break;
            case JungleStructureStage1458.WoodTreeWalls:
                context.ReportProgress(1d, $"Applying source-backed Living Tree walls ({ApplyWoodTreeWalls(grid, state.WorldSurface, context.CancellationToken)} cells)");
                break;
            case JungleStructureStage1458.Altars:
                ApplyAltars(context, workspace);
                break;
            case JungleStructureStage1458.WetJungle:
                context.ReportProgress(1d, $"Applying source-backed surface jungle water ({ApplyWetJungle(workspace, grid, state.WorldSurface)} columns)");
                break;
            case JungleStructureStage1458.JungleTemple:
                ApplyJungleTemple(context, workspace);
                break;
            case JungleStructureStage1458.Hives:
                ApplyHives(context, workspace);
                break;
            case JungleStructureStage1458.JungleChests:
                ApplyJungleChests(context, grid, random);
                break;
            case JungleStructureStage1458.SettleLiquids:
                ApplySettleLiquids(context, grid);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void ApplyDirtRockWallRunner(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int top = Math.Clamp((int)state.WorldSurface + 20, 5, state.UnderworldTop - 30);
        int bottom = Math.Max(top + 1, state.UnderworldTop - 20);
        int attempts = Math.Max(1000, grid.Width * 2);
        int placed = 0;

        for (int i = 0; i < attempts; i++)
        {
            if ((i & 255) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int x = random.Next(3, grid.Width - 3);
            int y = random.Next(top, bottom);
            ref WorldTile tile = ref grid.At(x, y);
            if (tile.IsActive || tile.Wall != 0)
                continue;

            ushort? neighborType = grid.FirstNaturalNeighborType(x, y);
            if (neighborType is null)
                continue;

            tile.Wall = neighborType is Dirt or Grass or Mud or JungleGrass
                ? DirtUnsafeWall
                : RockyDirtUnsafeWall;
            placed++;
        }

        context.ReportProgress(1d, $"Running dirt/rock cave wall background pass ({placed} wall cells)");
    }

    /// <summary>
    /// Source <c>GenPassNameID.LivingTrees</c>. The pass draws its own count from the world width, then for
    /// each one retries a column until a tree takes or it has tried half the world's width. A candidate column
    /// is scanned down to the first active cell, must be dirt above <c>worldSurface</c> and below row 150, must
    /// have no living wood or leaves within ten tiles, and must have no dungeon brick, cloud or aether in the
    /// hundred-by-hundred box around it. Every accepted tree then grows up to three companions either side, in
    /// patch mode, each re-tested against the same box.
    /// </summary>
    /// <remarks>
    /// One exclusion of the source's is absent: it also refuses a column within fifty tiles of a recorded
    /// mountain-cave entrance (<c>GenVars.mCaveX</c>), which this runtime's Mountain Caves pass does not
    /// record. Everything else, including the retry loop's draw pattern, is the source's.
    /// </remarks>
    private void ApplyLivingTrees(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Living trees require shared UnifiedRandom semantics.");
        WorldTileStore store = workspace.TileStore;
        CancellationToken cancellation = context.CancellationToken;
        int width = workspace.WidthTiles;
        double worldSurface = state.WorldSurface;
        var grower = new LivingTreeGrower1458(
            store, random, worldSurface, state.UnderworldTop, cancellation);

        const int spawnHalfWidth = 200;
        const int beachDistance = 380;
        double scale = width / 4200d;
        int target = random.Next(0, (int)(2d * scale) + 1);
        if (target == 0 && random.Next(2) == 0)
            target++;

        int grown = 0;
        for (int tree = 0; tree < target; tree++)
        {
            bool done = false;
            int attempts = 0;
            while (!done)
            {
                cancellation.ThrowIfCancellationRequested();
                attempts++;
                if (attempts > width / 2)
                    done = true;

                int x = random.Next(beachDistance, width - beachDistance);
                if (x > width / 2 - spawnHalfWidth && x < width / 2 + spawnHalfWidth)
                    continue;

                int y = 0;
                while (!store.Get(x, y).IsActive && y < worldSurface)
                    y++;
                if (y >= worldSurface)
                    continue;
                if (store.Get(x, y).Type != Dirt)
                    continue;

                y--;
                if (y <= 150)
                    continue;
                if (!IsLivingTreeSiteAllowed(store, x, y))
                    continue;

                done = grower.TryGrow(x, y);
                if (!done)
                    continue;

                grown++;
                GrowLivingTreeCompanions(store, random, grower, x, y, width, spawnHalfWidth, cancellation);
            }
        }

        context.ReportProgress(1d, $"Growing living trees ({grown}/{target})");
    }

    /// <summary>
    /// The companion half of the pass. Each side gets <c>Next(4)</c> patch trees, walked outward by
    /// <c>Next(13, 31)</c> at a time; each lands on the surface of its own column, which is found by walking up
    /// out of ground or down out of air, and is re-tested against the box around the PARENT tree, not its own.
    /// </summary>
    private void GrowLivingTreeCompanions(
        WorldTileStore store,
        IWorldGenerationVanillaRandom random,
        LivingTreeGrower1458 grower,
        int parentX,
        int parentY,
        int width,
        int spawnHalfWidth,
        CancellationToken cancellation)
    {
        int height = store.Dimensions.HeightTiles;
        for (int side = -1; side <= 1; side++)
        {
            if (side == 0)
                continue;

            int x = parentX;
            int companions = random.Next(4);
            for (int companion = 0; companion < companions; companion++)
            {
                cancellation.ThrowIfCancellationRequested();
                x += random.Next(13, 31) * side;
                if (x > width / 2 - spawnHalfWidth && x < width / 2 + spawnHalfWidth)
                    continue;
                if ((uint)x >= (uint)width)
                    continue;

                int y = parentY;
                if (store.Get(x, y).IsActive)
                {
                    while (y > 0 && store.Get(x, y).IsActive)
                        y--;
                }
                else
                {
                    while (y < height - 1 && !store.Get(x, y).IsActive)
                        y++;
                    y--;
                }

                if (IsLivingTreeSiteAllowed(store, parentX, parentY))
                    grower.TryGrow(x, y, patch: true);
            }
        }
    }

    /// <summary>
    /// The source's two site tests: no living wood or leaves within ten tiles, and no dungeon brick, cloud or
    /// aether identity anywhere in the hundred-by-hundred box whose top-left corner is fifty tiles up and left.
    /// </summary>
    private static bool IsLivingTreeSiteAllowed(WorldTileStore store, int x, int y)
    {
        if (IsTileNearby(store, x, y, LivingWood, 10) || IsTileNearby(store, x, y, LeafBlock, 10))
            return false;

        int width = store.Dimensions.WidthTiles;
        int height = store.Dimensions.HeightTiles;
        for (int column = x - 50; column < x + 50; column++)
        {
            if ((uint)column >= (uint)width)
                continue;
            for (int row = y - 50; row < y + 50; row++)
            {
                if ((uint)row >= (uint)height)
                    continue;

                WorldTile tile = store.Get(column, row);
                if (!tile.IsActive)
                    continue;
                if (tile.Type is 41 or 43 or 44 or 481 or 482 or 483 or
                    189 or 196 or 460 or 717 or 718 or 719)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Source <c>WorldGen.IsTileNearby</c> for the identities this pass asks about.</summary>
    private static bool IsTileNearby(WorldTileStore store, int x, int y, ushort type, int distance)
    {
        int width = store.Dimensions.WidthTiles;
        int height = store.Dimensions.HeightTiles;
        for (int column = x - distance; column <= x + distance; column++)
        {
            if ((uint)column >= (uint)width)
                continue;
            for (int row = y - distance; row <= y + distance; row++)
            {
                if ((uint)row >= (uint)height)
                    continue;

                WorldTile tile = store.Get(column, row);
                if (tile.IsActive && tile.Type == type)
                    return true;
            }
        }

        return false;
    }

    private static long ApplyWoodTreeWalls(RuntimeGrid grid, double worldSurface, CancellationToken cancellationToken)
    {
        long walls = 0;
        // TerrariaServer 1.4.5.8 GenPassNameID.LivingTreeWalls. This is a surface-bounded scan, not a
        // flood or neighbour-fill: it writes only when a Living Wood cross has four diagonal neighbours
        // that are themselves Living Wood (or already carry wall 244).
        for (int x = 25; x < grid.Width - 25; x++)
        {
            if ((x & 63) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            for (int y = 25; y < worldSurface; y++)
            {
                if (grid.At(x, y).Type != LivingWood && grid.At(x, y - 1).Type != LivingWood &&
                    grid.At(x - 1, y).Type != LivingWood && grid.At(x + 1, y).Type != LivingWood &&
                    grid.At(x, y + 1).Type != LivingWood)
                    continue;

                bool enclosed = true;
                for (int neighborX = x - 1; neighborX <= x + 1 && enclosed; neighborX++)
                for (int neighborY = y - 1; neighborY <= y + 1; neighborY++)
                {
                    // The source's conjunction intentionally selects only the four diagonal cells.
                    if (neighborX == x || neighborY == y)
                        continue;
                    WorldTile diagonal = grid.At(neighborX, neighborY);
                    if ((!diagonal.IsActive || diagonal.Type != LivingWood) && diagonal.Wall != LivingWoodUnsafeWall)
                    {
                        enclosed = false;
                        break;
                    }
                }
                if (!enclosed)
                    continue;

                grid.At(x, y).Wall = LivingWoodUnsafeWall;
                walls++;
            }
        }

        return walls;
    }

    internal static void ApplyWoodTreeWallsForTesting(Workspace workspace, double worldSurface) =>
        ApplyWoodTreeWalls(new RuntimeGrid(workspace), worldSurface, CancellationToken.None);

    private void ApplyAltars(IWorldGenerationContext context, Workspace workspace)
    {
        WorldGenerationPoint shimmer = workspace.VanillaShimmerPosition ??
            throw new InvalidOperationException("Evil altars require the preceding Shimmer generation position.");
        int placed = new EvilAltarPlacement1458(workspace.TileStore, context.VanillaRandom!, state.WorldSurface,
            state.RockLayer, context.CancellationToken).Generate(shimmer, context.Request.Options.Evil == WorldGenerationEvil.Crimson);
        context.ReportProgress(1d, $"Placing evil altars ({placed})");
    }

    private static int ApplyWetJungle(Workspace workspace, RuntimeGrid grid, double worldSurface)
    {
        // TerrariaServer 1.4.5.8 GenPassNameID.SurfaceWaterInJungle: scan each column from the retained
        // worldSurfaceLow to just above worldSurface, stop at its FIRST active tile and water only if that tile
        // is Jungle Grass. This pass creates no pools and consumes no RNG.
        int columns = 0;
        int low = (int)(workspace.VanillaTerrainState?.WorldSurfaceLow ?? worldSurface);
        for (int x = 0; x < grid.Width; x++)
        {
            for (int y = low; y < worldSurface - 1d; y++)
            {
                if (!grid.At(x, y).IsActive)
                    continue;
                if (grid.At(x, y).Type == JungleGrass && y >= 2)
                {
                    ref WorldTile first = ref grid.At(x, y - 1);
                    first.LiquidKind = WorldLiquidKind.Water;
                    first.LiquidAmount = byte.MaxValue;
                    ref WorldTile second = ref grid.At(x, y - 2);
                    second.LiquidKind = WorldLiquidKind.Water;
                    second.LiquidAmount = byte.MaxValue;
                    columns++;
                }
                break;
            }
        }
        return columns;
    }

    internal static int ApplyWetJungleForTesting(Workspace workspace, int worldSurfaceLow, double worldSurface) =>
        ApplyWetJungleForTestingCore(workspace, worldSurfaceLow, worldSurface);

    private static int ApplyWetJungleForTestingCore(Workspace workspace, int worldSurfaceLow, double worldSurface)
    {
        int columns = 0;
        var grid = new RuntimeGrid(workspace);
        for (int x = 0; x < grid.Width; x++)
        for (int y = worldSurfaceLow; y < worldSurface - 1d; y++)
        {
            if (!grid.At(x, y).IsActive)
                continue;
            if (grid.At(x, y).Type == JungleGrass && y >= 2)
            {
                ref WorldTile first = ref grid.At(x, y - 1);
                first.LiquidKind = WorldLiquidKind.Water;
                first.LiquidAmount = byte.MaxValue;
                ref WorldTile second = ref grid.At(x, y - 2);
                second.LiquidKind = WorldLiquidKind.Water;
                second.LiquidAmount = byte.MaxValue;
                columns++;
            }
            break;
        }
        return columns;
    }

    /// <summary>
    /// Source <c>GenPassNameID.LihzahrdTemple</c>: its site search, then <see cref="JungleTempleBuilder1458"/>
    /// for the temple itself.
    /// </summary>
    /// <remarks>
    /// The runtime built a hollow rectangle with a few floors and a staircase - about five percent of the
    /// source's brick, which is why a generated temple read as a shell rather than a maze. The source's search
    /// draws a row between the rock layer and six hundred above the bottom and a column biased away from the
    /// dungeon's side, and accepts the first one that lands on jungle grass; the fallback when a million tries
    /// find nothing is the mirror of the dungeon's own column. The fallback's row differs from the source's,
    /// which reads <c>generatingDungeonPositionX</c> mirrored; this runtime uses the dungeon location it
    /// already carries.
    /// </remarks>
    private void ApplyJungleTemple(IWorldGenerationContext context, Workspace workspace)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("The jungle temple requires shared UnifiedRandom semantics.");

        WorldTileStore store = workspace.TileStore;
        int width = workspace.WidthTiles;
        int height = workspace.HeightTiles;
        int dungeonSide = bootstrap.DungeonLocation < width / 2 ? -1 : 1;

        double spread = 0.25;
        long attempts = 0;
        int widenings = 0;
        int entryX = -1;
        int entryY = -1;

        while (entryX < 0)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int top = (int)state.RockLayer;
            int bottom = height - 600;
            if (top > bottom - 1)
                top = bottom - 1;

            int row = random.Next(top, bottom);
            int column = (int)(((random.NextDouble() * spread + 0.1) * -dungeonSide + 0.5) * width);

            if ((uint)column < (uint)width && (uint)row < (uint)height)
            {
                WorldTile candidate = store.Get(column, row);
                if (candidate.IsActive && candidate.Type == JungleGrass)
                {
                    entryX = column;
                    entryY = row;
                    break;
                }
            }

            if (attempts++ <= 1000000)
                continue;

            if (spread == 0.35 && ++widenings > 10)
                break;

            spread = Math.Min(0.35, spread + 0.05);
            attempts = 0;
        }

        if (entryX < 0)
        {
            entryX = Math.Clamp(width - bootstrap.DungeonLocation, 20, width - 20);
            entryY = Math.Clamp((int)state.RockLayer + 100, 20, height - 20);
        }

        var builder = new JungleTempleBuilder1458(
            store, random, state.UnderworldTop, context.CancellationToken);
        builder.Build(entryX, entryY);

        state.TempleLeft = builder.Left;
        state.TempleRight = builder.Right;
        state.TempleTop = builder.Top;
        state.TempleBottom = builder.Bottom;
        workspace.SetVanillaTemplePart2State(new VanillaTemplePart2State1458(
            builder.Left, builder.Right, builder.Top, builder.Bottom, builder.Rooms));

        context.ReportProgress(
            1d,
            $"Building the jungle temple ({builder.Rooms} rooms, " +
            $"{builder.Right - builder.Left}x{builder.Bottom - builder.Top})");
    }

    /// <summary>
    /// Source <c>GenPassNameID.Beehives</c>, delegated to <see cref="HiveBiome1458"/>. The runtime carved a
    /// fixed number of ellipses and produced about a fifth of the source's Hive block; the source drifts two to
    /// four tunnels per lobe through the mud, writing a honey core inside a hive shell at every step.
    /// </summary>
    private void ApplyHives(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Hives require shared UnifiedRandom semantics.");

        var biome = new HiveBiome1458(
            workspace.TileStore,
            random,
            state.WorldSurface,
            state.RockLayer,
            context.CancellationToken);

        int placed = biome.Apply();

        context.ReportProgress(1d, $"Growing bee hives ({placed})");
    }

    private void ApplyJungleChests(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        state.JungleChestCandidates.Clear();
        // TerrariaServer 1.4.5.8 GenPassNameID.JungleShrines. These are the hut sites later consumed by
        // ChestsInJungleShrines, not generic nearby chest candidates.
        _ = random.Next(40, grid.Width - 40);
        _ = random.Next((int)((state.WorldSurface + state.RockLayer) / 2d), grid.Height - 400);
        double target = random.Next(7, 12) * grid.Width / 4200d;
        int totalFailures = 0;

        for (int placed = 0; placed < target; placed++)
        {
            while (true)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                totalFailures++;
                int x = random.Next(40, grid.Width / 2 - 40);
                if (bootstrap.DungeonSide <= 0)
                    x += grid.Width / 2;
                int y = random.Next((int)((state.WorldSurface + state.RockLayer) / 2d), grid.Height - 400);
                int halfWidth = random.Next(2, 4);
                int halfHeight = random.Next(2, 4);

                if (!CanPlaceJungleShrine(grid, x, y, halfWidth, halfHeight))
                {
                    if (totalFailures > grid.Width * 10)
                    {
                        placed++;
                        totalFailures = 0;
                        break;
                    }
                    continue;
                }

                BuildJungleShrine(grid, x, y, halfWidth, halfHeight, (ushort)bootstrap.JungleHut, random);
                state.JungleChestCandidates.Add(new WorldGenerationPoint(x, y));
                totalFailures = 0;
                break;
            }
        }

        context.ReportProgress(
            1d,
            $"Building source-backed jungle shrines ({state.JungleChestCandidates.Count}/{target:0.##})");
    }

    private bool CanPlaceJungleShrine(RuntimeGrid grid, int x, int y, int halfWidth, int halfHeight)
    {
        if (!grid.Contains(x - 31, y - 31) || !grid.Contains(x + 30, y + 30) ||
            IntersectsTemple(x - halfWidth - 1, x + halfWidth + 1, y - halfHeight - 1, y + halfHeight + 1))
            return false;
        if (!grid.At(x, y).IsActive || grid.At(x, y).Type != JungleGrass)
            return false;
        for (int scanX = x - 30; scanX < x + 30; scanX += 3)
        for (int scanY = y - 30; scanY < y + 30; scanY += 3)
        {
            WorldTile tile = grid.At(scanX, scanY);
            if (tile.IsActive && tile.Type is Hive or 229 or LihzahrdBrick or 119 or 120 || tile.Wall is HiveUnsafeWall or LihzahrdBrickUnsafeWall)
                return false;
        }
        return true;
    }

    private static void BuildJungleShrine(RuntimeGrid grid, int x, int y, int halfWidth, int halfHeight, ushort hut, IRandom random)
    {
        ushort wall = hut switch { 119 => 23, 120 => 24, 158 => 42, 175 => 45, 45 => 10, _ => 0 };
        for (int tx = x - halfWidth - 1; tx <= x + halfWidth + 1; tx++)
        for (int ty = y - halfHeight - 1; ty <= y + halfHeight + 1; ty++)
            SetJungleShrineShell(ref grid.At(tx, ty), hut);
        for (int tx = x - halfWidth; tx <= x + halfWidth; tx++)
        for (int ty = y - halfHeight; ty <= y + halfHeight; ty++)
        {
            ClearTile(ref grid.At(tx, ty), preserveWall: false);
            grid.At(tx, ty).Wall = wall;
        }

        // Source attempts a style-3 torch up to one hundred times. Preserve the offer stream; object framing is
        // intentionally delegated to the shared placement boundary as this pass is completed.
        for (int attempts = 0; attempts < 100; attempts++)
        {
            int torchX = random.Next(x - halfWidth, x + halfWidth + 1);
            int torchY = random.Next(y - halfHeight, y + halfHeight - 2);
            if (!grid.At(torchX, torchY).IsActive)
            {
                SetType(ref grid.At(torchX, torchY), 4);
                // Official PlaceTile(4, style: 3) selects the background-attached frame (0,66) in a shrine wall.
                grid.At(torchX, torchY).FrameX = 0;
                grid.At(torchX, torchY).FrameY = 66;
                break;
            }
        }

        // The source reopens the bottom three rows before it grows up to four Mud support cells below
        // every shell column, then builds the stepped hut roof with one-to-two-cell contractions.
        for (int tx = x - halfWidth - 1; tx <= x + halfWidth + 1; tx++)
        for (int ty = y + halfHeight - 2; ty <= y + halfHeight; ty++)
            ClearTile(ref grid.At(tx, ty), preserveWall: true);
        for (int tx = x - halfWidth - 1; tx <= x + halfWidth + 1; tx++)
        {
            int remainingSupport = 4;
            for (int ty = y + halfHeight + 2; ty < grid.Height && remainingSupport > 0 && !grid.At(tx, ty).IsActive; ty++, remainingSupport--)
                SetActiveTypeWithoutNormalization(ref grid.At(tx, ty), Mud);
        }
        halfWidth -= random.Next(1, 3);
        int roofY = y - halfHeight - 2;
        while (halfWidth >= 0)
        {
            for (int tx = x - halfWidth - 1; tx <= x + halfWidth + 1; tx++)
                SetActiveTypeWithoutNormalization(ref grid.At(tx, roofY), hut);
            halfWidth -= random.Next(1, 3);
            roofY--;
        }
    }

    internal static void BuildJungleShrineForTesting(Workspace workspace, int x, int y, int halfWidth, int halfHeight, ushort hut, IWorldGenerationVanillaRandom random) =>
        BuildJungleShrine(new RuntimeGrid(workspace), x, y, halfWidth, halfHeight, hut, new VanillaRandom(random));

    private static void SetJungleShrineShell(ref WorldTile tile, ushort type)
    {
        // JungleShrines changes only activity/type/liquid and lava kind for its shell; frame, shape and wall persist.
        SetActiveTypeWithoutNormalization(ref tile, type);
        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
    }

    private static void SetActiveTypeWithoutNormalization(ref WorldTile tile, ushort type)
    {
        tile.Type = type;
        tile.Flags |= WorldTileFlags.Active;
    }

    private void ApplySettleLiquids(IWorldGenerationContext context, RuntimeGrid grid)
    {
        ApplySettleLiquidsCore(grid.Store, context.CancellationToken);
        // TerrariaServer 1.4.5.8 GenPassNameID.SettleLiquids runs QuickWater, WaterCheck and ten bounded
        // quick-settle rounds. Keep this owner at that source boundary; a fixed gravity sweep changes both
        // barriers and flow ordering.
        context.ReportProgress(1d, "Applied source-backed QuickWater/WaterCheck liquid settle");
    }

    internal static void ApplySettleLiquidsForTesting(Workspace workspace) =>
        ApplySettleLiquidsCore(workspace.TileStore, default);

    private static void ApplySettleLiquidsCore(WorldTileStore store, CancellationToken cancellationToken)
    {
        var simulator = new VanillaWorldLiquidSimulator1458(store);
        VanillaWaterCheckDiagnostic1458 waterCheck = simulator.SettleDuringWorldGeneration(cancellationToken);
        if (!waterCheck.IsApplied)
        {
            throw new InvalidOperationException(
                $"Source Settle Liquids WaterCheck cannot resolve tile {waterCheck.TileType.Value} at {waterCheck.X},{waterCheck.Y}.");
        }
    }

    private bool IntersectsTemple(int left, int right, int top, int bottom) =>
        state.TempleLeft >= 0 &&
        right >= state.TempleLeft &&
        left <= state.TempleRight &&
        bottom >= state.TempleTop &&
        top <= state.TempleBottom;

    private static bool CanPlaceObject(
        RuntimeGrid grid,
        int left,
        int top,
        int width,
        int height,
        bool requireFloor)
    {
        if (left < 1 || top < 1 || left + width >= grid.Width - 1 || top + height >= grid.Height - 1)
            return false;

        for (int x = left; x < left + width; x++)
        for (int y = top; y < top + height; y++)
        {
            if (grid.At(x, y).IsActive)
                return false;
        }

        if (!requireFloor)
            return true;
        int floorY = top + height;
        for (int x = left; x < left + width; x++)
        {
            if (!grid.At(x, floorY).IsActive)
                return false;
        }
        return true;
    }

    private static void MudRing(RuntimeGrid grid, int centerX, int centerY, int radiusX, int radiusY)
    {
        int outerRx = Math.Max(2, radiusX);
        int outerRy = Math.Max(2, radiusY);
        int innerRx = Math.Max(1, outerRx - 4);
        int innerRy = Math.Max(1, outerRy - 3);
        for (int dx = -outerRx; dx <= outerRx; dx++)
        {
            double outerX = dx / (double)outerRx;
            for (int dy = -outerRy; dy <= outerRy; dy++)
            {
                double outerY = dy / (double)outerRy;
                if (outerX * outerX + outerY * outerY > 1d)
                    continue;
                double innerX = dx / (double)innerRx;
                double innerY = dy / (double)innerRy;
                if (innerX * innerX + innerY * innerY < 1d)
                    continue;

                int x = centerX + dx;
                int y = centerY + dy;
                if (!grid.Contains(x, y))
                    continue;
                ref WorldTile tile = ref grid.At(x, y);
                if (!tile.IsActive || IsNatural(tile.Type))
                {
                    SetType(ref tile, Mud);
                    tile.Wall = JungleUnsafeWall;
                }
            }
        }
    }

    private static void FillEllipse(
        RuntimeGrid grid,
        int centerX,
        int centerY,
        int radiusX,
        int radiusY,
        ushort type,
        bool overwriteAir,
        ushort wall)
    {
        radiusX = Math.Max(1, radiusX);
        radiusY = Math.Max(1, radiusY);
        for (int dx = -radiusX; dx <= radiusX; dx++)
        {
            double nx = dx / (double)radiusX;
            for (int dy = -radiusY; dy <= radiusY; dy++)
            {
                double ny = dy / (double)radiusY;
                if (nx * nx + ny * ny > 1d)
                    continue;
                int x = centerX + dx;
                int y = centerY + dy;
                if (!grid.Contains(x, y))
                    continue;
                ref WorldTile tile = ref grid.At(x, y);
                if (!overwriteAir && !tile.IsActive)
                    continue;
                SetType(ref tile, type);
                if (wall != 0)
                    tile.Wall = wall;
            }
        }
    }

    private static void CarveEllipse(
        RuntimeGrid grid,
        int centerX,
        int centerY,
        int radiusX,
        int radiusY,
        ushort wall)
    {
        radiusX = Math.Max(1, radiusX);
        radiusY = Math.Max(1, radiusY);
        for (int dx = -radiusX; dx <= radiusX; dx++)
        {
            double nx = dx / (double)radiusX;
            for (int dy = -radiusY; dy <= radiusY; dy++)
            {
                double ny = dy / (double)radiusY;
                if (nx * nx + ny * ny > 1d)
                    continue;
                int x = centerX + dx;
                int y = centerY + dy;
                if (!grid.Contains(x, y))
                    continue;
                ref WorldTile tile = ref grid.At(x, y);
                ClearTile(ref tile, preserveWall: false);
                tile.Wall = wall;
            }
        }
    }

    private static void FillLiquidEllipse(
        RuntimeGrid grid,
        int centerX,
        int centerY,
        int radiusX,
        int radiusY,
        WorldLiquidKind liquid)
    {
        radiusX = Math.Max(1, radiusX);
        radiusY = Math.Max(1, radiusY);
        for (int dx = -radiusX; dx <= radiusX; dx++)
        {
            double nx = dx / (double)radiusX;
            for (int dy = 0; dy <= radiusY; dy++)
            {
                double ny = dy / (double)radiusY;
                if (nx * nx + ny * ny > 1d)
                    continue;
                int x = centerX + dx;
                int y = centerY + dy;
                if (!grid.Contains(x, y))
                    continue;
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive)
                    continue;
                tile.LiquidAmount = byte.MaxValue;
                tile.LiquidKind = liquid;
            }
        }
    }

    private static bool IsNatural(ushort type) =>
        type is Dirt or Stone or Grass or Sand or Ash or Mud or JungleGrass or Silt or Snow or Ice or Marble or Granite or
            Sandstone or HardenedSand;

    private VanillaWorldGenerationBootstrapState1458 RequireBootstrap() =>
        state.Bootstrap ?? throw new InvalidOperationException(
            "Jungle-structure vanilla pass executed before bootstrap state initialization.");

    private static void SetType(ref WorldTile tile, ushort type)
    {
        tile.Type = type;
        tile.Flags |= WorldTileFlags.Active;
        tile.FrameX = -1;
        tile.FrameY = -1;
        tile.Shape = 0;
        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
    }

    private static void ClearTile(ref WorldTile tile, bool preserveWall)
    {
        ushort wall = tile.Wall;
        tile.Type = 0;
        tile.Flags &= ~WorldTileFlags.Active;
        tile.FrameX = -1;
        tile.FrameY = -1;
        tile.Shape = 0;
        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
        if (!preserveWall)
            tile.Wall = 0;
        else
            tile.Wall = wall;
    }

    private interface IRandom
    {
        int Next();
        int Next(int max);
        int Next(int min, int max);
        double NextDouble();
    }

    private sealed class VanillaRandom(IWorldGenerationVanillaRandom inner) : IRandom
    {
        public int Next() => inner.Next();
        public int Next(int max) => inner.Next(max);
        public int Next(int min, int max) => inner.Next(min, max);
        public double NextDouble() => inner.NextDouble();
    }

    private sealed class RuntimeGrid
    {
        private readonly WorldTileStore store;

        public RuntimeGrid(Workspace workspace) => store = workspace.TileStore;

        public WorldTileStore Store => store;

        public int Width => store.Dimensions.WidthTiles;
        public int Height => store.Dimensions.HeightTiles;

        public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

        public ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];

        public int FindFirstActiveY(int x, int minY, int maxExclusive)
        {
            int max = Math.Min(Height, maxExclusive);
            for (int y = Math.Max(0, minY); y < max; y++)
            {
                if (At(x, y).IsActive)
                    return y;
            }
            return max;
        }

        public bool HasNeighborType(int x, int y, ushort type) =>
            At(x - 1, y).Type == type && At(x - 1, y).IsActive ||
            At(x + 1, y).Type == type && At(x + 1, y).IsActive ||
            At(x, y - 1).Type == type && At(x, y - 1).IsActive ||
            At(x, y + 1).Type == type && At(x, y + 1).IsActive;

        public ushort? FirstNaturalNeighborType(int x, int y)
        {
            WorldTile left = At(x - 1, y);
            if (left.IsActive && IsNatural(left.Type)) return left.Type;
            WorldTile right = At(x + 1, y);
            if (right.IsActive && IsNatural(right.Type)) return right.Type;
            WorldTile above = At(x, y - 1);
            if (above.IsActive && IsNatural(above.Type)) return above.Type;
            WorldTile below = At(x, y + 1);
            if (below.IsActive && IsNatural(below.Type)) return below.Type;
            return null;
        }
    }
}

/// <summary>
/// Source-backed Beaches now owns ocean/beach geometry. The old compatibility Biomes identity stays in the graph only
/// to preserve dependency contracts for downstream migration layers; it performs no writes and consumes no vanilla RNG.
/// </summary>
internal sealed class SourceBackedBiomesCompatibilityBarrier1458 : IWorldGenerationPass
{
    public static SourceBackedBiomesCompatibilityBarrier1458 Instance { get; } = new();

    private SourceBackedBiomesCompatibilityBarrier1458()
    {
    }

    public void Execute(IWorldGenerationContext context) =>
        context.ReportProgress(1d, "Compatibility Biomes replaced by source-backed biome and beach passes");
}
