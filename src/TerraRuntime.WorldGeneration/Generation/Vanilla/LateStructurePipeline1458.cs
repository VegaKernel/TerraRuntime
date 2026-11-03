using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Seventh source-backed Terraria 1.4.5.8 world-generation overlay. It advances the ordinary canonical pipeline from
/// Water Chests through Floating Island Houses while keeping frame-important containers coupled to generation-owned
/// persistence metadata.
/// </summary>
public sealed class SourceBackedLateStructures1458 : IWorldGenerationProvider
{
    internal static readonly WorldGenerationPassId SpiderCavesId = new("terraria:1.4.5.8/SpiderCaves");
    internal static readonly WorldGenerationPassId GemCavesId = new("terraria:1.4.5.8/GemCaves");
    internal static readonly WorldGenerationPassId MossId = new("terraria:1.4.5.8/Moss");
    internal static readonly WorldGenerationPassId TempleId = new("terraria:1.4.5.8/Temple");
    internal static readonly WorldGenerationPassId CaveWallsId = new("terraria:1.4.5.8/CaveWalls");
    internal static readonly WorldGenerationPassId JungleTreesId = new("terraria:1.4.5.8/JungleTrees");
    internal static readonly WorldGenerationPassId FloatingIslandHousesId = new("terraria:1.4.5.8/FloatingIslandHouses");

    private static readonly WorldGenerationPassId SecretSeedsId = new("terraria:1.4.5.8/SecretSeeds");
    private readonly SourceBackedChestPlacement1458 baseline = new();

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

        var state = new LateStructureState1458();
        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id != SecretSeedsId)
            {
                builder.Add(entry.Descriptor, entry.Pass);
                continue;
            }

            Add(builder, SpiderCavesId, SourceBackedChestPlacement1458.WaterChestsId,
                new LateStructurePass1458(LateStructureStage1458.SpiderCaves, state));
            Add(builder, GemCavesId, SpiderCavesId,
                new LateStructurePass1458(LateStructureStage1458.GemCaves, state));
            Add(builder, MossId, GemCavesId,
                new LateStructurePass1458(LateStructureStage1458.Moss, state));
            Add(builder, TempleId, MossId,
                new LateStructurePass1458(LateStructureStage1458.Temple, state));
            Add(builder, CaveWallsId, TempleId,
                new LateStructurePass1458(LateStructureStage1458.CaveWalls, state));
            Add(builder, JungleTreesId, CaveWallsId,
                new LateStructurePass1458(LateStructureStage1458.JungleTrees, state));
            Add(builder, FloatingIslandHousesId, JungleTreesId,
                new LateStructurePass1458(LateStructureStage1458.FloatingIslandHouses, state));

            builder.Add(CloneDescriptor(entry.Descriptor, [FloatingIslandHousesId]), entry.Pass);
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

internal enum LateStructureStage1458 : byte
{
    SpiderCaves,
    GemCaves,
    Moss,
    Temple,
    CaveWalls,
    JungleTrees,
    FloatingIslandHouses
}

internal sealed class LateStructureState1458
{
    public VanillaWorldGenerationBootstrapState1458? Bootstrap { get; private set; }
    public double WorldSurface { get; private set; }
    public double RockLayer { get; private set; }
    public int UnderworldTop { get; private set; }
    public int LavaLine { get; private set; }

    public void EnsureInitialized(IWorldGenerationContext context, Workspace workspace)
    {
        if (Bootstrap is not null)
            return;

        Bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Late-structure vanilla generation requires Reset bootstrap state.");
        if (context.Metadata is null || !context.Metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Late-structure vanilla generation requires source-backed Terrain layers.");

        WorldSurface = layers.WorldSurface;
        RockLayer = layers.RockLayer;
        VanillaLiquidLines1458 liquidLines = workspace.VanillaLiquidLines ??
            throw new InvalidOperationException("Late-structure vanilla generation requires exact Early-pass liquid lines.");
        LavaLine = liquidLines.LavaLine;
        UnderworldTop = Math.Clamp(workspace.HeightTiles - 200, (int)RockLayer + 120, workspace.HeightTiles - 90);
    }
}

internal readonly record struct CaveRegionStats1458(
    int Count,
    int ShroomCount,
    int LavaCount,
    int IceCount,
    int SandCount,
    int RockCount);

internal sealed class LateStructurePass1458 : IWorldGenerationPass
{
    private const ushort Dirt = 0;
    private const ushort Stone = 1;
    private const ushort Cobweb = 51;
    private const ushort Mud = 59;
    private const ushort JungleGrass = 60;
    private const ushort Sapphire = 63;
    private const ushort Ruby = 64;
    private const ushort Emerald = 65;
    private const ushort Topaz = 66;
    private const ushort Amethyst = 67;
    private const ushort Diamond = 68;
    private const ushort Cloud = 189;
    private const ushort Sunplate = 202;
    private const ushort LihzahrdBrick = 226;
    private const ushort LivingMahogany = 383;
    private const ushort LivingMahoganyLeaves = 384;
    private const ushort Containers = 21;

    private const ushort SpiderUnsafeWall = 62;
    private const ushort DiscWall = 82;
    private const ushort LihzahrdBrickUnsafeWall = 87;
    private const ushort JungleUnsafeWall = 64;
    private const ushort JungleCaveWall = 15;
    private const ushort ShimmerUnsafeWall = 244;

    private const int CaveRegionMaxTiles = 1500;
    // TerrariaServer 1.4.5.8 GenPassNameID.SpiderCaves assigns WorldGen.maxTileCount = 3500 before countTiles.
    // Cave Walls keeps its own 1500-cell safety/selection ceiling, so this cannot share that smaller constant.
    private const int SpiderCaveRegionMaxTiles = 3500;
    private const int JungleWallSpreadLimit = 5000;

    private static readonly ushort[] GemTiles = [Sapphire, Ruby, Emerald, Topaz, Amethyst, Diamond];
    // TerrariaServer WorldGen.randGemTile index order: Amethyst, Topaz, Sapphire, Emerald, Ruby, Diamond.
    private static readonly ushort[] GemCaveTiles = [Amethyst, Topaz, Sapphire, Emerald, Ruby, Diamond];

    private readonly LateStructureStage1458 stage;
    private readonly LateStructureState1458 state;

    public LateStructurePass1458(
        LateStructureStage1458 stage,
        LateStructureState1458 state)
    {
        this.stage = stage;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Workspace workspace = context.Workspace as Workspace ??
            throw new InvalidOperationException("Late-structure Terraria generation requires Workspace.");
        state.EnsureInitialized(context, workspace);
        var grid = new RuntimeGrid(workspace);
        var random = new VanillaRandom(
            context.VanillaRandom ??
            throw new InvalidOperationException("Late-structure Terraria generation requires shared UnifiedRandom semantics."));

        switch (stage)
        {
            case LateStructureStage1458.SpiderCaves:
                ApplySpiderCaves(context, workspace, grid, random);
                break;
            case LateStructureStage1458.GemCaves:
                ApplyGemCaves(context, workspace, grid, random);
                break;
            case LateStructureStage1458.Moss:
                ApplyMoss(context, workspace);
                break;
            case LateStructureStage1458.Temple:
                ApplyTemplePart2(context, workspace, grid, random, state.WorldSurface);
                break;
            case LateStructureStage1458.CaveWalls:
                ApplyCaveWalls(context, workspace, grid, random);
                break;
            case LateStructureStage1458.JungleTrees:
                ApplyJungleTrees(context, workspace, grid, random);
                break;
            case LateStructureStage1458.FloatingIslandHouses:
                ApplyFloatingIslandHouses(context, workspace, grid, random);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void ApplySpiderCaves(
        IWorldGenerationContext context,
        Workspace workspace,
        RuntimeGrid grid,
        IRandom random)
    {
        // TerrariaServer 1.4.5.8 GenPassNameID.SpiderCaves: choose enclosed empty regions with countTiles,
        // then let Spread.Spider visit their real frontier. This is deliberately not a carving pass.
        if (workspace.HasVanillaGenerationSolidity)
            workspace.SetVanillaGenerationSolid(new TileTypeId(379), false);
        int attempts = (int)(grid.Width * 0.005d);
        int retryCap = grid.Width / 2;
        int minSize = 500;
        int maxSize = SpiderCaveRegionMaxTiles;
        int accepted = 0;
        IWorldGenerationVanillaRandom vanillaRandom = context.VanillaRandom ??
            throw new InvalidOperationException("Spider Caves requires shared UnifiedRandom semantics.");
        var framing = new GenerationWallFraming1458(workspace.TileStore, vanillaRandom);
        var speleothems = new SpeleothemPass1458(workspace.TileStore, vanillaRandom, state.WorldSurface, state.RockLayer,
            beachDistance: 380, context.CancellationToken);
        BuriedChestContext1458 chestContext = workspace.VanillaBuriedChestContext ??
            throw new InvalidOperationException("Spider Caves requires the run-scoped AddBuriedChest context.");
        var chests = new BuriedChest1458(workspace.TileStore, vanillaRandom, chestContext, workspace.CanRegisterGeneratedChest,
            chest =>
            {
                if (!workspace.TryAddChest(chest.Left, chest.Top, string.Empty, chest.Items))
                    throw new InvalidOperationException("Spider Caves chest could not enter the generation side table.");
            }, workspace: workspace);

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int retries = 0;
            int x = random.Next(200, grid.Width - 200);
            int y = random.Next((int)(state.WorldSurface + state.RockLayer) / 2, grid.Height - 230);
            CaveRegionStats1458 region = CountCaveRegion(
                grid, x, y, jungle: false, lavaOk: true, spiderSolidity: true, maxTiles: maxSize);
            while ((region.Count >= maxSize || region.Count < minSize) && retries < retryCap)
            {
                retries++;
                x = random.Next(200, grid.Width - 200);
                y = random.Next((int)state.RockLayer + 30, grid.Height - 230);
                region = CountCaveRegion(
                    grid, x, y, jungle: false, lavaOk: true, spiderSolidity: true, maxTiles: maxSize);
                if (region.ShroomCount > 1)
                    region = region with { Count = 0 };
            }

            if (retries >= retryCap)
                continue;

            SpreadSpider(
                grid,
                workspace.TileStore,
                vanillaRandom,
                framing,
                speleothems,
                chests,
                state.WorldSurface,
                x,
                y, workspace);
            accepted++;
        }

        if (workspace.HasVanillaGenerationSolidity)
            workspace.SetVanillaGenerationSolid(new TileTypeId(162), true);
        context.ReportProgress(1d, $"Adding source-backed Spider Caves ({accepted}/{attempts} regions)");
    }

    private void ApplyGemCaves(IWorldGenerationContext context, Workspace workspace, RuntimeGrid grid, IRandom random)
    {
        // TerrariaServer 1.4.5.8 Gem Caves: candidates are bounded connected caves, not radial ore clusters.
        int accepted = 0;
        int target = (int)Math.Ceiling(grid.Width * .003d);
        var framing = new GenerationTileFraming1458(workspace.TileStore, context.VanillaRandom!, workspace,
            useWorldGenerationSolidity: workspace.HasVanillaGenerationSolidity);
        for (int attempt = 0; attempt < target; attempt++)
        {
            int retries = 0;
            int x = random.Next(200, grid.Width - 200);
            int y = random.Next((int)state.RockLayer + 30, grid.Height - 230);
            CaveRegionStats1458 region = CountCaveRegion(grid, x, y, jungle: false, lavaOk: false, maxTiles: 300);
            while ((region.Count >= 300 || region.Count < 50 || region.LavaCount > 0 || region.IceCount > 0 || region.RockCount == 0) && retries < 1000)
            {
                retries++;
                x = random.Next(200, grid.Width - 200);
                y = random.Next((int)state.RockLayer + 30, grid.Height - 230);
                region = CountCaveRegion(grid, x, y, jungle: false, lavaOk: false, maxTiles: 300);
            }

            if (retries >= 1000)
                continue;

            ApplyGemCave(grid, random, x, y, framing);
            accepted++;
        }

        context.ReportProgress(1d, $"Adding source-backed Gem Caves ({accepted}/{target} regions)");
    }

    private static void ApplyGemCave(RuntimeGrid grid, IRandom random, int startX, int startY, GenerationTileFraming1458 framing)
    {
        Span<bool> gems = stackalloc bool[6];
        gems[random.Next(6)] = true;
        for (int index = 0; index < 6; index++)
            if (random.Next(6) == 0) gems[index] = true;

        var visited = new HashSet<int>();
        var next = new List<(int X, int Y)> { (startX, startY) };
        var current = new List<(int X, int Y)>();
        while (next.Count > 0)
        {
            current.Clear(); current.AddRange(next); next.Clear();
            while (current.Count > 0)
            {
                (int x, int y) = current[0]; current.RemoveAt(0);
                if (x < 1 || x >= grid.Width - 1 || y < 1 || y >= grid.Height - 1) continue;
                int key = x * grid.Height + y;
                visited.Add(key);
                ref WorldTile tile = ref grid.At(x, y);
                // Converted ore reads the same live solidity table as the surrounding cave.
                if (IsCaveWallSolid(grid, in tile) || tile.Wall != 0)
                {
                    if (!tile.IsActive) continue;
                    ReplaceGem(ref tile, random, gems);
                    ReplaceGem(ref grid.At(x - 1, y), random, gems);
                    ReplaceGem(ref grid.At(x + 1, y), random, gems);
                    ReplaceGem(ref grid.At(x, y - 1), random, gems);
                    ReplaceGem(ref grid.At(x, y + 1), random, gems);
                    continue;
                }

                tile.Wall = (ushort)(48 + PickGem(random, gems));
                // Spread.Gem only rolls an object offer for an actually empty tile. A previously placed
                // non-solid gem object is revisited by the breadth-first frontier, but its second visit
                // merely paints the wall; consuming another roll here desynchronizes every later cave.
                if (!tile.IsActive && random.Next(2) == 0)
                {
                    int style = PickGem(random, gems);
                    framing.PlaceGemInEmptyCell(x, y, style);
                }
                foreach ((int nx, int ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                    if (!visited.Contains(nx * grid.Height + ny)) next.Add((nx, ny));
            }
        }
    }

    private static void ReplaceGem(ref WorldTile tile, IRandom random, ReadOnlySpan<bool> gems)
    {
        if (tile.IsActive && tile.Type is 0 or 1 or 40 or 59 or 60 or 70 or 147 or 161)
            tile.Type = random.Next(20) == 0 ? GemCaveTiles[PickGem(random, gems)] : Stone;
    }

    private static int PickGem(IRandom random, ReadOnlySpan<bool> gems)
    {
        int index = random.Next(6);
        while (!gems[index]) index = random.Next(6);
        return index;
    }

    private void ApplyMoss(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Moss requires shared UnifiedRandom semantics.");

        int waterLine = workspace.VanillaLiquidLines?.WaterLine ?? checked((int)Math.Round(state.RockLayer));
        int lavaLine = workspace.VanillaLiquidLines?.LavaLine ?? checked((int)Math.Round(state.RockLayer));
        // Source reads GenVars.shimmerPosition, which is the zero vector until the Shimmer pass sets it.
        WorldGenerationPoint shimmer = workspace.VanillaShimmerPosition ?? new WorldGenerationPoint(0, 0);
        double rockLayer = state.RockLayer;

        // The spreader re-frames every square it converts, and source does that through the ordinary
        // SquareTileFrame rather than through any biome-specific framer. It matters that this is the real one:
        // a moss conversion changes a block's identity while leaving it solid, so every object check the
        // framing triggers finds its own footprint intact and spends nothing - but a framer that threw on
        // identities it had not modelled would refuse a statue standing beside a cave that takes moss.
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace,
            useWorldGenerationSolidity: workspace.HasVanillaGenerationSolidity);
        var grass = new GenerationGrass1458(
            workspace.TileStore, random, context.CancellationToken, state.WorldSurface,
            (x, y) => framing.SquareTileFrame(x, y), workspace);
        var pass = new MossPass1458(
            workspace.TileStore, random, state.WorldSurface, rockLayer, waterLine, lavaLine,
            shimmer.X, shimmer.Y, context.CancellationToken,
            workspace.HasVanillaGenerationSolidity ? workspace.IsVanillaGenerationSolid : null);
        pass.Apply(grass);
        context.ReportProgress(1d, $"Spreading Moss ({pass.Placed} seeded, {grass.Converted} spread)");
    }

    // TerrariaServer 1.4.5.8 WorldGen.templePart2 consumes the retained makeTemple bounds; it does not scan
    // the material map. Keeping that provenance prevents an earlier unrelated Lihzahrd fragment from changing
    // the complete shared-RNG decoration sequence.
    private static void ApplyTemplePart2(IWorldGenerationContext context, Workspace workspace, RuntimeGrid grid, IRandom random, double worldSurface)
    {
        if (workspace.HasVanillaGenerationSolidity)
        {
            workspace.SetVanillaGenerationSolid(new TileTypeId(162), false);
            workspace.SetVanillaGenerationSolid(new TileTypeId(226), true);
        }
        VanillaTemplePart2State1458? sourceState = workspace.VanillaTemplePart2State;
        if (sourceState is not { } temple)
        {
            // `templePart2` has no discovery step: it consumes the exact GenVars bounds emitted by makeTemple.
            // Running it from a material scan changes every bounded random offer, so an incomplete predecessor must
            // remain explicit rather than silently manufacture a different temple.
            context.ReportProgress(1d, "Temple Part 2 skipped: source temple bounds are unavailable");
            return;
        }

        IWorldGenerationVanillaRandom vanillaRandom = context.VanillaRandom ??
            throw new InvalidOperationException("Temple Part 2 requires shared UnifiedRandom semantics.");
        BuriedChestContext1458 chestContext = workspace.VanillaBuriedChestContext ??
            throw new InvalidOperationException("Temple Part 2 requires the run-scoped AddBuriedChest context.");
        var chests = new BuriedChest1458(workspace.TileStore, vanillaRandom, chestContext, workspace.CanRegisterGeneratedChest,
            chest =>
            {
                if (!workspace.TryAddChest(chest.Left, chest.Top, string.Empty, chest.Items))
                    throw new InvalidOperationException("Temple chest could not enter the generation side table.");
            }, spikyLihzahrdIsNonSolid: true, workspace: workspace);

        // TerrariaServer 1.4.5.8 WorldGen.templePart2. Each loop deliberately owns its source draw order;
        // placement helpers below are deterministic equivalents of the TileObjectData layouts used by these styles.
        var mechanismFramer = new GenerationTileFraming1458(workspace.TileStore, vanillaRandom, workspace, workspace.HasVanillaGenerationSolidity);
        double traps = temple.Rooms * 1.9d * (1d + random.Next(-15, 16) * .01d);
        int retries = 0;
        while (traps > 0d)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(temple.Left, temple.Right);
            int y = random.Next(temple.Top, temple.Bottom);
            if (IsTempleAir(grid, x, y) && TryMayanTrap(workspace, mechanismFramer, grid, random, x, y, grid.Height - 300, worldSurface))
            {
                traps -= 1d;
                retries = 0;
            }
            else
                retries++;
            if (retries > 100) { retries = 0; traps -= 1d; }
        }

        if (workspace.HasVanillaGenerationSolidity)
            workspace.SetVanillaGenerationSolid(new TileTypeId(232), false);

        double chestBudget = temple.Rooms * .35d * (1d + random.Next(-15, 16) * .01d);
        retries = 0;
        while (chestBudget > 0d)
        {
            int x = random.Next(temple.Left, temple.Right);
            int y = random.Next(temple.Top, temple.Bottom);
            if (IsTempleAir(grid, x, y) && chests.TryAdd(x, y, out _, out _, 1293, notNearOtherChests: true, chestStyle: 16))
            {
                chestBudget -= 1d;
                retries = 0;
            }
            retries++;
            if (retries > 10000) break;
        }

        PlaceTempleStatues(workspace, vanillaRandom, grid, random, temple, context.CancellationToken);
        PlaceTempleFurniture(workspace, vanillaRandom, grid, random, temple, context.CancellationToken);
        PlaceTemplePaintings(workspace, vanillaRandom, grid, random, temple, context.CancellationToken);
        // The native helper restores232, then its enclosing pass immediately makes it non-solid again.
        if (workspace.HasVanillaGenerationSolidity)
        {
            workspace.SetVanillaGenerationSolid(new TileTypeId(232), true);
            workspace.SetVanillaGenerationSolid(new TileTypeId(232), false);
        }
        context.ReportProgress(1d, $"Finishing Jungle Temple ({chests.Chests.Count} chest offers accepted)");
    }

    private static bool IsTempleAir(RuntimeGrid grid, int x, int y) => !grid.At(x, y).IsActive && grid.At(x, y).Wall == 87;

    private static bool TryMayanTrap(Workspace workspace, GenerationTileFraming1458 framing, RuntimeGrid grid, IRandom random, int x, int y, int floorLimit, double worldSurface)
    {
        bool Solid(in WorldTile tile, bool requireFlat = false) => tile.IsActive && !tile.IsActuated &&
            (!requireFlat || tile.Shape == 0) &&
            (workspace.HasVanillaGenerationSolidity ? workspace.IsVanillaGenerationSolid(tile.TileType) : VanillaTileCollisionCatalog.IsSolid(tile.TileType)) &&
            !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
        // TerrariaServer 1.4.5.8 WorldGen.mayanTrap. The first roll selects the horizontal (0) or vertical (1)
        // arm before any rejection, so it must remain above the floor search.
        int direction = random.Next(3) == 0 ? 0 : 1;
        int floor = y;
        while (floor < floorLimit && !Solid(in grid.At(x, floor))) floor++;
        if (floor >= floorLimit || grid.At(x, floor).Type is 232 or 10) return false;
        floor--;
        if (grid.At(x, floor).LiquidAmount > 0 && grid.At(x, floor).LiquidKind == WorldLiquidKind.Lava) return false;

        for (int dx = -1; dx <= 1; dx++)
            for (int dy = 0; dy >= -2; dy--)
                if (grid.At(x + dx, floor + dy).IsActive && !grid.At(x + dx, floor + dy).IsActuated) return false;

        if (grid.At(x, floor + 1).Type is 10 or 48 or 232) return false;
        if (direction != 0)
        {
            int roof = floor;
            while (!Solid(in grid.At(x, roof)))
            {
                if (--roof < worldSurface) return false;
            }
            int height = Math.Abs(roof - floor);
            if (height < 3) return false;
            int verticalWireColor = random.Next(3);
            if ((grid.At(x, floor).Flags & WorldTileFlags.WireRed) != 0) verticalWireColor = 0;
            if ((grid.At(x, floor).Flags & WorldTileFlags.WireBlue) != 0) verticalWireColor = 1;
            if ((grid.At(x, floor).Flags & WorldTileFlags.WireGreen) != 0) verticalWireColor = 2;
            int launcherStyle = height < 16 && random.Next(3) != 0 ? 4 : 3;
            WorldTile anchor = grid.At(x, roof);
            if (anchor.Type is 135 or 137 or 232 or 237 or 10 || anchor.Wall != 87 ||
                !framing.PrepareTempleTrigger(x, floor)) return false;

            framing.PlaceTempleMechanism(x, floor, 135, 6);
            framing.PlaceTempleMechanism(x, roof, 137, launcherStyle);
            for (int side = 0; side < 2; side++)
            {
                int length = random.Next(1, 5);
                int wireX = x;
                int sideStep = side == 0 ? -1 : 1;
                while (length-- > 0)
                {
                    wireX += sideStep;
                    if (!Solid(in grid.At(wireX, roof - 1), requireFlat: true) || Solid(in grid.At(wireX, roof + 1))) break;
                    framing.PlaceTempleMechanism(wireX, roof, 137, launcherStyle);
                    AddWire(ref grid.At(wireX, roof), verticalWireColor);
                }
            }
            AddWirePath(grid, x, floor, x, roof, verticalWireColor);
            return true;
        }

        int corridorY = floor - random.Next(3);
        int left = x;
        while (left >= 5 && !Solid(in grid.At(left, corridorY))) left--;
        int right = x;
        while (right < grid.Width - 5 && !Solid(in grid.At(right, corridorY))) right++;
        bool canLeft = x - left is > 5 and < 50 && Solid(in grid.At(left, corridorY + 1));
        bool canRight = right - x is > 5 and < 50 && Solid(in grid.At(right, corridorY + 1));
        if (!canLeft && !canRight) return false;

        int launcherX;
        int step;
        if (canLeft && canRight)
        {
            if (random.Next(2) == 0) { launcherX = right; step = -1; }
            else { launcherX = left; step = 1; }
        }
        else if (canRight) { launcherX = right; step = -1; }
        else { launcherX = left; step = 1; }

        WorldTile launcherAnchor = grid.At(launcherX, corridorY);
        if (launcherAnchor.Wall != 87 || launcherAnchor.Type is 190 or 135 or 137 or 232 or 237 or 10) return false;
        if (!framing.PrepareTempleTrigger(x, floor)) return false;

        framing.PlaceTempleMechanism(x, floor, 135, 6);
        // WorldGen.KillTile clears the solid launcher anchor but keeps its unsafe wall; PlaceTile immediately
        // occupies that same cell, so preserve the backing tile's wall rather than assigning a default tile.
        framing.KillTile(launcherX, corridorY);
        int wireColor = random.Next(3);
        if ((grid.At(x, floor).Flags & WorldTileFlags.WireRed) != 0) wireColor = 0;
        if ((grid.At(x, floor).Flags & WorldTileFlags.WireBlue) != 0) wireColor = 1;
        if ((grid.At(x, floor).Flags & WorldTileFlags.WireGreen) != 0) wireColor = 2;
        int horizontalLauncherStyle = Math.Abs(launcherX - x) < 10 && random.Next(3) != 0 ? 2 : 1;
        framing.PlaceTempleMechanism(launcherX, corridorY, 137, horizontalLauncherStyle);
        if (step == 1) grid.At(launcherX, corridorY).FrameX += 18;

        int wireLength = random.Next(5);
        int wireY = corridorY;
        while (wireLength-- > 0)
        {
            wireY--;
            if (!Solid(in grid.At(launcherX, wireY), requireFlat: true) ||
                !Solid(in grid.At(launcherX - step, wireY), requireFlat: true) ||
                Solid(in grid.At(launcherX + step, wireY))) break;
            framing.PlaceTempleMechanism(launcherX, wireY, 137, horizontalLauncherStyle);
            if (step == 1) grid.At(launcherX, wireY).FrameX += 18;
            AddWire(ref grid.At(launcherX, wireY), wireColor);
        }
        AddWirePath(grid, x, floor, launcherX, corridorY, wireColor);
        return true;
    }

    private static void AddWire(ref WorldTile tile, int color) => tile.Flags |= color switch
    {
        0 => WorldTileFlags.WireRed,
        1 => WorldTileFlags.WireBlue,
        _ => WorldTileFlags.WireGreen
    };

    // TerrariaServer 1.4.5.8 WorldGen.AddWireFromPointToPoint walks from the launcher back to the trigger,
    // taking one horizontal and then one vertical step per iteration and colouring every visited cell.
    private static void AddWirePath(RuntimeGrid grid, int x, int y, int targetX, int targetY, int color)
    {
        while (targetX != x || targetY != y)
        {
            AddWire(ref grid.At(targetX, targetY), color);
            if (targetX > x) targetX--; else if (targetX < x) targetX++;
            AddWire(ref grid.At(targetX, targetY), color);
            if (targetY > y) targetY--; else if (targetY < y) targetY++;
            AddWire(ref grid.At(targetX, targetY), color);
        }
    }

    private static void PlaceTempleStatues(Workspace workspace, IWorldGenerationVanillaRandom vanillaRandom, RuntimeGrid grid, IRandom random, VanillaTemplePart2State1458 temple, CancellationToken cancellationToken)
    {
        double budget = temple.Rooms * 1.25d * (1d + random.Next(-25, 36) * .01d);
        for (int attempts = 1; budget > 0d && attempts <= 10001; attempts++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(temple.Left, temple.Right); int y = random.Next(temple.Top, temple.Bottom);
            if (!IsTempleAir(grid, x, y)) continue;
            int floor = FindTempleFloor(grid, x, y, temple.Bottom);
            if (floor > temple.Bottom) continue;
            int style = random.Next(43, 46);
            CaveHousePlacement1458.PlaceHouseObject(workspace, vanillaRandom, x, floor, 105, style);
            if (grid.At(x, floor).Type == 105) budget -= 1d;
        }
    }

    private static void PlaceTempleFurniture(Workspace workspace, IWorldGenerationVanillaRandom vanillaRandom, RuntimeGrid grid, IRandom random, VanillaTemplePart2State1458 temple, CancellationToken cancellationToken)
    {
        double budget = temple.Rooms * 1.35d * (1d + random.Next(-15, 26) * .01d);
        for (int attempts = 1; budget > 0d && attempts <= 10001; attempts++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(temple.Left, temple.Right); int y = random.Next(temple.Top, temple.Bottom);
            if (!IsTempleAir(grid, x, y)) continue;
            int floor = FindTempleFloor(grid, x, y, temple.Bottom);
            if (floor > temple.Bottom) continue;
            switch (random.Next(3))
            {
                case 0: CaveHousePlacement1458.PlaceHouseObject(workspace, vanillaRandom, x, floor, 18, 10); if (grid.At(x, floor).Type == 18) budget -= 1d; break;
                // WorldGen.Place3x2 takes the centre bottom cell for tables; the visual footprint starts at x - 1.
                case 1: CaveHousePlacement1458.PlaceHouseObject(workspace, vanillaRandom, x, floor, 14, 9); if (grid.At(x, floor).Type == 14) budget -= 1d; break;
                // PlaceTile(15) takes the Place1x2 chair arm. Its style stride is 40 pixels in FrameY,
                // not the 3x2 furniture stride used by tables and work benches.
                case 2: PlaceFloorChair(workspace, vanillaRandom, x, floor,12); if (grid.At(x, floor).Type == 15) budget -= 1d; break;
            }
        }
    }

    private static void PlaceTemplePaintings(Workspace workspace, IWorldGenerationVanillaRandom vanillaRandom, RuntimeGrid grid, IRandom random, VanillaTemplePart2State1458 temple, CancellationToken cancellationToken)
    {
        int budget = 1;
        if (grid.Width > 4200) budget++;
        if (grid.Width > 6400) budget += random.Next(2);
        for (int attempts = 1; budget > 0 && attempts <= 10001; attempts++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(temple.Left, temple.Right); int y = random.Next(temple.Top, temple.Bottom);
            if (!IsTempleAir(grid, x, y) || HasTemplePaintingOrDoor(grid, x, y)) continue;
            // TileObjectData for painting style 88 maps to this fixed 3x3 frame origin.
            // WorldGen.Place3x3Wall centres the painting on the sampled cell and requires wall, not floor support.
            if (CaveHousePlacement1458.PlaceHouseObject(workspace, vanillaRandom, x, y, 240, 88)) budget--;
        }
    }

    private static void PlaceFloorChair(Workspace workspace, IWorldGenerationVanillaRandom random, int x, int y,int style)
    {
        ref WorldTile anchor = ref workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(x, y)];
        if (!anchor.IsActive) HellFortGenerator1458.ClearPlacementAnchor(ref anchor);
        GenerationFurniturePlacement1458.Place(workspace, x, y, 15, style, solidity: workspace.HasVanillaGenerationSolidity ? workspace.IsVanillaGenerationSolid : null);
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace, workspace.HasVanillaGenerationSolidity);
        framing.SquareTileFrame(x, y);
        if (workspace.TileStore.Get(x, y).IsActive) framing.SquareTileFrame(x, y);
    }

    private static int FindTempleFloor(RuntimeGrid grid, int x, int y, int bottom)
    {
        while (!grid.At(x, y).IsActive && ++y <= bottom) { }
        return y - 1;
    }

    private static bool HasTemplePaintingOrDoor(RuntimeGrid grid, int x, int y)
    {
        for (int dx = -70; dx <= 70; dx++)
            for (int dy = -70; dy <= 70; dy++)
            {
                int px = x + dx, py = y + dy;
                if (px < 5 || px >= grid.Width - 5 || py < 5 || py >= grid.Height - 5) continue;
                WorldTile tile = grid.At(px, py);
                if (tile.IsActive && (tile.Type == 240 || (dx is >= -4 and <= 4 && dy is >= -4 and <= 4 && tile.Type == 226))) return true;
            }
        return false;
    }

    private void ApplyCaveWalls(IWorldGenerationContext context, Workspace workspace, RuntimeGrid grid, IRandom random)
    {
        if (workspace.HasVanillaGenerationSolidity)
        {
            workspace.SetVanillaGenerationSolid(new TileTypeId(162), false);
            workspace.SetVanillaGenerationSolid(new TileTypeId(226), true);
            workspace.SetVanillaGenerationSolid(new TileTypeId(232), false);
        }
        // TerrariaServer 1.4.5.8 WorldGen "Cave Walls" ordinary-world path. The old clean-room
        // implementation painted fixed ellipses, which produced detached artificial wall blobs and did not
        // preserve the shared-RNG contract. Vanilla instead searches for a bounded connected cave, derives the
        // wall family from the tiles/liquid touching that cave, and flood-spreads through that exact cavity.
        int ordinaryAttempts = (int)(grid.Width * 0.04d);
        int minY = (int)(state.WorldSurface + state.RockLayer) / 2;
        int maxY = grid.Height - 220;
        int painted = 0;
        int accepted = 0;

        for (int attempt = 0; attempt < ordinaryAttempts; attempt++)
        {
            if ((attempt & 15) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int retries = 0;
            int x = random.Next(200, grid.Width - 200);
            int y = random.Next(minY, maxY);
            CaveRegionStats1458 stats = CountCaveRegion(grid, x, y, jungle: false, lavaOk: true);
            while ((stats.Count >= CaveRegionMaxTiles || stats.Count < 10) && retries < 500)
            {
                retries++;
                x = random.Next(200, grid.Width - 200);
                y = random.Next(minY, maxY);
                stats = CountCaveRegion(grid, x, y, jungle: false, lavaOk: true);
            }

            if (retries >= 500)
                continue;

            int wallChoice = random.Next(2); // consumed unconditionally by the pinned source before its branch chain.
            ushort wall;
            if (stats.ShroomCount > stats.RockCount * 0.75d)
            {
                wall = 80;
            }
            else if (stats.IceCount > 0)
            {
                wall = wallChoice == 0 ? (ushort)40 : (ushort)71;
            }
            else if (stats.LavaCount > 0)
            {
                wall = 79;
            }
            else
            {
                wall = random.Next(4) switch
                {
                    0 => (ushort)59,
                    1 => (ushort)61,
                    2 => (ushort)170,
                    _ => (ushort)171
                };
            }

            painted += SpreadWall(grid, x, y, wall);
            accepted++;
        }

        // The ordinary jungle-wall phase depends on the exact GenVars.lavaLine selected by the Early pass. That
        // source state is now carried forward explicitly instead of reconstructing it from later layer metadata.
        double jungleAttempts = grid.Width * 0.02d;
        int jungleMaxY = state.LavaLine;
        if (jungleMaxY > (int)state.WorldSurface)
        {
            for (int attempt = 0; attempt < jungleAttempts; attempt++)
            {
                if ((attempt & 15) == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();

                int retries = 0;
                int x = random.Next(200, grid.Width - 200);
                int y = random.Next((int)state.WorldSurface, jungleMaxY);
                CaveRegionStats1458 stats = default;
                if (grid.At(x, y).Wall == JungleUnsafeWall)
                    stats = CountCaveRegion(grid, x, y, jungle: true, lavaOk: false);

                while ((stats.Count >= CaveRegionMaxTiles || stats.Count < 10) && retries < 1000)
                {
                    retries++;
                    x = random.Next(200, grid.Width - 200);
                    y = random.Next((int)state.WorldSurface, jungleMaxY);
                    ushort currentWall = grid.At(x, y).Wall;
                    if (!IsHousingWall(currentWall) && currentWall != ShimmerUnsafeWall)
                    {
                        stats = currentWall == JungleUnsafeWall
                            ? CountCaveRegion(grid, x, y, jungle: true, lavaOk: false)
                            : default;
                    }
                }

                if (retries < 1000)
                    painted += SpreadWall2(grid, x, y, JungleCaveWall);
            }
        }

        context.ReportProgress(1d, $"Adding source-backed cave walls ({accepted} enclosed caves, {painted} wall cells)");
    }

    internal static CaveRegionStats1458 CountCaveRegionForTesting(
        Workspace workspace,
        int x,
        int y,
        bool jungle = false,
        bool lavaOk = false) =>
        CountCaveRegion(new RuntimeGrid(workspace), x, y, jungle, lavaOk);

    internal static CaveRegionStats1458 CountSpiderCaveRegionForTesting(
        Workspace workspace,
        int x,
        int y) =>
        CountCaveRegion(new RuntimeGrid(workspace), x, y, jungle: false, lavaOk: true, spiderSolidity: true,
            maxTiles: SpiderCaveRegionMaxTiles);

    internal static int SpreadWallForTesting(Workspace workspace, int x, int y, ushort wall) =>
        SpreadWall(new RuntimeGrid(workspace), x, y, wall);

    // The isolated fixture mirrors the registered TerrariaServer Spread.Spider delegate.  It is deliberately
    // internal: production enters through the ordered Spider Caves pass, while the fixture pins its frontier,
    // framing and shared RNG behavior independently of candidate selection.
    internal static void SpreadSpiderForTesting(
        Workspace workspace,
        IWorldGenerationVanillaRandom random,
        double worldSurface,
        double rockLayer,
        int startX,
        int startY)
    {
        var chestContext = new BuriedChestContext1458
        {
            Height = workspace.TileStore.Dimensions.HeightTiles,
            WorldSurface = worldSurface,
            RockLayer = rockLayer,
            LavaLine = workspace.TileStore.Dimensions.HeightTiles - 200,
            CopperBar = 20,
            IronBar = 22,
            SilverBar = 21,
            GoldBar = 19,
            TungstenIsSilverTier = false,
            DesertHiveLow = 0,
            DesertHiveHigh = 0,
            HellChestItem = [220, 274, 275]
        };
        var store = workspace.TileStore;
        SpreadSpider(
            new RuntimeGrid(workspace),
            store,
            random,
            new GenerationWallFraming1458(store, random),
            new SpeleothemPass1458(store, random, worldSurface, rockLayer, beachDistance: 380, CancellationToken.None),
            new BuriedChest1458(store, random, chestContext),
            worldSurface,
            startX,
            startY);
    }

    internal static void ApplyGemCaveForTesting(
        Workspace workspace,
        IWorldGenerationVanillaRandom random,
        int startX,
        int startY) =>
        ApplyGemCave(new RuntimeGrid(workspace), new VanillaRandom(random), startX, startY,
            new GenerationTileFraming1458(workspace.TileStore, random, workspace,
                useWorldGenerationSolidity: workspace.HasVanillaGenerationSolidity));

    internal static bool TryPlaceMayanTrapForTesting(
        Workspace workspace,
        IWorldGenerationVanillaRandom random,
        int x,
        int y) =>
        TryMayanTrap(workspace, new GenerationTileFraming1458(workspace.TileStore, random, workspace, workspace.HasVanillaGenerationSolidity), new RuntimeGrid(workspace), new VanillaRandom(random), x, y, workspace.HeightTiles - 300, 140d);

    internal static bool TryPlaceTempleChairForTesting(Workspace workspace, IWorldGenerationVanillaRandom random, int x, int floor)
    { PlaceFloorChair(workspace, random, x, floor,12); return workspace.TileStore.Get(x, floor).IsActive; }
    internal static bool TryPlaceTempleTableForTesting(Workspace workspace, IWorldGenerationVanillaRandom random, int centerX, int floor) => CaveHousePlacement1458.PlaceHouseObject(workspace, random, centerX, floor, 14, 9);
    internal static bool TryPlaceTemplePaintingForTesting(Workspace workspace, IWorldGenerationVanillaRandom random, int centerX, int centerY) => CaveHousePlacement1458.PlaceHouseObject(workspace, random, centerX, centerY, 240, 88);
    internal static bool TryPlaceTempleStatueForTesting(Workspace workspace, IWorldGenerationVanillaRandom random, int x, int floor) => CaveHousePlacement1458.PlaceHouseObject(workspace, random, x, floor, 105, 45);
    internal static bool TryPlaceTempleWorkBenchForTesting(Workspace workspace, IWorldGenerationVanillaRandom random, int x, int floor) => CaveHousePlacement1458.PlaceHouseObject(workspace, random, x, floor, 18, 10);

    internal static void ApplyTemplePart2ForTesting(IWorldGenerationContext context, Workspace workspace, double worldSurface) =>
        ApplyTemplePart2(
            context,
            workspace,
            new RuntimeGrid(workspace),
            new VanillaRandom(context.VanillaRandom ?? throw new InvalidOperationException("Temple test requires vanilla RNG.")),
            worldSurface);

    private static CaveRegionStats1458 CountCaveRegion(
        RuntimeGrid grid,
        int startX,
        int startY,
        bool jungle,
        bool lavaOk,
        bool spiderSolidity = false,
        int maxTiles = CaveRegionMaxTiles)
    {
        int count = 0;
        int shroomCount = 0;
        int lavaCount = 0;
        int iceCount = 0;
        int sandCount = 0;
        int rockCount = 0;
        var countedOpenTiles = new HashSet<int>();
        var pending = new Stack<(int X, int Y)>();
        pending.Push((startX, startY));

        while (pending.Count > 0)
        {
            if (count >= maxTiles)
                break;

            (int x, int y) = pending.Pop();
            if (x <= 1 || x >= grid.Width - 1 || y <= 1 || y >= grid.Height - 1)
                return new CaveRegionStats1458(maxTiles, shroomCount, lavaCount, iceCount, sandCount, rockCount);

            int key = x * grid.Height + y;
            if (countedOpenTiles.Contains(key))
                continue;

            WorldTile tile = grid.At(x, y);
            if (tile.Wall == ShimmerUnsafeWall ||
                (tile.LiquidAmount > 0 && tile.LiquidKind == WorldLiquidKind.Shimmer))
            {
                return new CaveRegionStats1458(maxTiles, shroomCount, lavaCount, iceCount, sandCount, rockCount);
            }

            if (!jungle)
            {
                if (tile.Wall != 0)
                    return new CaveRegionStats1458(maxTiles, shroomCount, lavaCount, iceCount, sandCount, rockCount);

                if (tile.LiquidAmount > 0 && tile.LiquidKind == WorldLiquidKind.Lava)
                {
                    lavaCount++;
                    if (!lavaOk)
                        return new CaveRegionStats1458(maxTiles, shroomCount, lavaCount, iceCount, sandCount, rockCount);
                }
            }

            if (tile.IsActive)
            {
                switch (tile.Type)
                {
                    case 70:
                        shroomCount++;
                        break;
                    case Stone:
                        rockCount++;
                        break;
                    case 147:
                    case 161:
                        iceCount++;
                        break;
                    case 53:
                    case 396:
                    case 397:
                        sandCount++;
                        break;
                }
            }

            if (IsCaveWallSolid(grid, in tile, spiderSolidity))
                continue;

            countedOpenTiles.Add(key);
            count++;

            // Source recursion order is left, right, up, down. Stack push order is reversed to preserve it.
            pending.Push((x, y + 1));
            pending.Push((x, y - 1));
            pending.Push((x + 1, y));
            pending.Push((x - 1, y));
        }

        return new CaveRegionStats1458(count, shroomCount, lavaCount, iceCount, sandCount, rockCount);
    }

    private static void SpreadSpider(
        RuntimeGrid grid, WorldTileStore store, IWorldGenerationVanillaRandom random,
        GenerationWallFraming1458 framing,
        SpeleothemPass1458 speleothems,
        BuriedChest1458 chests,
        double worldSurface,
        int startX,
        int startY, Workspace? workspace = null)
    {
        var visited = new HashSet<int>();
        var next = new List<(int X, int Y)> { (startX, startY) };
        var current = new List<(int X, int Y)>();
        while (next.Count > 0)
        {
            current.Clear(); current.AddRange(next); next.Clear();
            while (current.Count > 0)
            {
                (int x, int y) = current[0]; current.RemoveAt(0);
                if (x < 1 || x >= grid.Width - 1 || y < 1 || y >= grid.Height - 1 ||
                    y >= grid.Height - 200 - random.Next(5) || y < worldSurface + random.Next(5)) continue;
                int key = x * grid.Height + y;
                visited.Add(key);
                ref WorldTile tile = ref grid.At(x, y);
                if (IsCaveWallSolid(grid, in tile, spiderSolidity: true) || tile.Wall != 0)
                {
                    if (tile.IsActive && tile.Wall == 0) tile.Wall = SpiderUnsafeWall;
                    continue;
                }
                // WorldGen.SquareWallFrame defaults resetFrame to true. Each new spider wall therefore rolls
                // its centre frame through the shared stream before the tile-decoration branches below.
                tile.Wall = SpiderUnsafeWall;
                framing.SquareWallFrame(x, y);
                if (!tile.IsActive)
                {
                    tile.LiquidAmount = 0; UnderworldTerrain1458.ClearLavaFlag(ref tile);
                    if (IsCaveWallSolid(grid, in grid.At(x, y + 1), spiderSolidity: true) && random.Next(3) == 0)
                    {
                        if (random.Next(15) == 0) chests.TryAdd(x, y, out _, out _, 939, true, 15);
                        else GenerationDecorationPlacement1458.TryPlacePot(store, random, x, y, random.Next(19, 21), workspace);
                    }
                    if (!tile.IsActive)
                    {
                        if (IsCaveWallSolid(grid, in grid.At(x, y - 1), spiderSolidity: true) && random.Next(3) == 0)
                            speleothems.PlaceTight(x, y, spiders: true);
                        else if (IsCaveWallSolid(grid, in grid.At(x, y + 1), spiderSolidity: true))
                        {
                            GenerationDecorationPlacement1458.TryPlaceTile3x2(store, random, x, y, 187, 9 + random.Next(5), workspace: workspace);
                            if (random.Next(3) == 0 && !tile.IsActive)
                            {
                                GenerationDecorationPlacement1458.TryPlaceSmallPile(store, x, y, 34 + random.Next(4), workspace: workspace);
                                if (!tile.IsActive) GenerationDecorationPlacement1458.TryPlaceSmallPile(store, x, y, 48 + random.Next(6), 0, workspace: workspace);
                            }
                        }
                    }
                }
                foreach ((int nx, int ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                    if (!visited.Contains(nx * grid.Height + ny)) next.Add((nx, ny));
            }
        }
    }

    private static int SpreadWall(RuntimeGrid grid, int startX, int startY, ushort wall)
    {
        if ((uint)startX >= (uint)grid.Width || (uint)startY >= (uint)grid.Height)
            return 0;

        int painted = 0;
        var visited = new HashSet<int>();
        var pending = new Queue<(int X, int Y)>();
        pending.Enqueue((startX, startY));

        while (pending.Count > 0)
        {
            (int x, int y) = pending.Dequeue();
            if (x < 1 || x >= grid.Width - 1 || y < 1 || y >= grid.Height - 1)
                continue;

            int key = x * grid.Height + y;
            if (!visited.Add(key))
                continue;

            ref WorldTile tile = ref grid.At(x, y);
            if (IsCaveWallSolid(grid, in tile) || tile.Wall != 0)
            {
                if (tile.IsActive && tile.Wall == 0)
                {
                    tile.Wall = wall;
                    painted++;
                }
                continue;
            }

            if (tile.Wall != wall)
            {
                tile.Wall = wall;
                painted++;
            }

            pending.Enqueue((x - 1, y));
            pending.Enqueue((x + 1, y));
            pending.Enqueue((x, y - 1));
            pending.Enqueue((x, y + 1));
        }

        return painted;
    }

    private static int SpreadWall2(RuntimeGrid grid, int startX, int startY, ushort wall)
    {
        if ((uint)startX >= (uint)grid.Width || (uint)startY >= (uint)grid.Height)
            return 0;

        int wallOut = 0;
        int painted = 0;
        var visited = new HashSet<int>();
        var pending = new Queue<(int X, int Y)>();
        pending.Enqueue((startX, startY));

        while (pending.Count > 0)
        {
            (int x, int y) = pending.Dequeue();
            if (x < 1 || x >= grid.Width - 1 || y < 1 || y >= grid.Height - 1)
                continue;

            int key = x * grid.Height + y;
            if (!visited.Add(key))
                continue;

            ref WorldTile tile = ref grid.At(x, y);
            if (tile.Wall == wall || CannotBeReplacedByWallSpread(tile.Wall))
                continue;

            if (!IsCaveWallSolid(grid, in tile))
            {
                wallOut++;
                if (wallOut >= JungleWallSpreadLimit)
                    continue;

                tile.Wall = wall;
                painted++;
                pending.Enqueue((x - 1, y));
                pending.Enqueue((x + 1, y));
                pending.Enqueue((x, y - 1));
                pending.Enqueue((x, y + 1));
            }
            else if (tile.IsActive)
            {
                tile.Wall = wall;
                painted++;
            }
        }

        return painted;
    }

    private static bool IsCaveWallSolid(RuntimeGrid grid, in WorldTile tile, bool spiderSolidity = false)
    {
        if (!tile.IsActive || tile.IsActuated || tile.Shape != 0)
            return false;

        bool solid = grid.GenerationSolid?.Invoke(tile.TileType) ??
            (!(spiderSolidity && tile.Type == 379) && VanillaTileCollisionCatalog.IsSolid(tile.TileType));
        return solid && !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
    }

    private static bool CannotBeReplacedByWallSpread(ushort wall) =>
        wall is 3 or 4 or 34 or 40 or 83 or 87 or 244;

    private static bool IsHousingWall(ushort wall) =>
        VanillaWallDefinitionCatalog.TryGet(new WallTypeId(wall), out VanillaWallDefinition definition) &&
        definition.IsHousingWall;

    private void ApplyJungleTrees(IWorldGenerationContext context, Workspace workspace, RuntimeGrid grid, IRandom random)
    {
        if (workspace.HasVanillaGenerationSolidity) workspace.SetVanillaGenerationSolid(new TileTypeId(379), true);
        IWorldGenerationVanillaRandom physical = context.VanillaRandom!;
        var framing = new GenerationTileFraming1458(workspace.TileStore, physical, workspace, workspace.HasVanillaGenerationSolidity);
        var walls = new GenerationWallFraming1458(workspace.TileStore, physical);
        int grown = 0;
        for (int x = 0; x < grid.Width; x++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            for (int y = (int)state.WorldSurface - 1; y < grid.Height - 350; y++)
                if (random.Next(10) == 0 && TreeGrower1458.TryGrowUnderground(workspace.TileStore, x, y, physical, framing, walls)) grown++;
        }
        context.ReportProgress(1d, $"Growing underground jungle trees ({grown})");
    }

    private void ApplyFloatingIslandHouses(IWorldGenerationContext context,Workspace workspace,RuntimeGrid grid,IRandom random)
    {
        foreach(VanillaSkyIsland1458 island in workspace.VanillaSkyIslands)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if(!island.IsLake)PlaceIslandHouse(workspace,context.VanillaRandom!,island.X,island.Y,island.Style,state.WorldSurface);
        }
        context.ReportProgress(1d,$"Building Floating Island Houses ({workspace.VanillaSkyIslandHouseCount})");
    }

    private static void PlaceIslandHouse(Workspace workspace,IWorldGenerationVanillaRandom random,int x,int y,int style,double surface)
    {
        if(style!=0)throw new InvalidOperationException("Island House fountain/tree palettes require an owned generation placement path.");
        WorldTileStore store=workspace.TileStore;int width=workspace.WidthTiles,height=workspace.HeightTiles;
        ref WorldTile At(int a,int b)=>ref store.Tiles[store.GetUncheckedIndex(a,b)];
        for(int a=x-30;a<=x+30;a++)for(int b=y-30;b<=y+30;b++)
            if(a>=10&&b>=10&&a<width-10&&b<height-10&&(DungeonGenerationTiles1458.IsDungeonWall(At(a,b).Wall)||At(a,b).Wall is 87 or 3 or 83))return;
        int direction=random.Next(2)==0?-1:1,halfWidth=random.Next(7,12),roomHeight=random.Next(5,7);
        int floor=y;
        int probe=x+(halfWidth+2)*direction;
        if(y>surface)floor=y-5;
        else for(int row=y-15;row<y+30;row++)
        {
            WorldTile tile=At(probe,row);
            if(tile.Wall!=0&&tile.IsActive&&!tile.IsActuated&&!tile.IsHalfBrick&&tile.SourceSlope==0&&
                (workspace.HasVanillaGenerationSolidity?workspace.IsVanillaGenerationSolid(tile.TileType):VanillaTileCollisionCatalog.IsSolid(tile.TileType))&&
                !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType)&&tile.Type is not(192 or 191 or 384)){floor=row-1;break;}
        }
        int left=Math.Max(0,x-halfWidth-1),right=Math.Min(width,x+halfWidth+1),top=Math.Max(0,floor-roomHeight-1),bottom=Math.Min(height,floor+2);
        for(int a=left;a<=right;a++)for(int b=top-1;b<=bottom;b++)
            if(b!=top-1||a!=left&&a!=right){ref WorldTile tile=ref At(a,b);tile.Flags|=WorldTileFlags.Active;tile.LiquidAmount=0;tile.Type=202;tile.Wall=0;tile.ClearSourceShape();}
        left=Math.Max(0,x-halfWidth);right=Math.Min(width,x+halfWidth);top=Math.Max(0,floor-roomHeight);bottom=Math.Min(height,floor+1);
        for(int a=left;a<=right;a++)for(int b=top;b<bottom;b++)
            if((b!=top||a!=left&&a!=right)&&At(a,b).Wall==0){At(a,b).Flags&=~WorldTileFlags.Active;At(a,b).Wall=82;}
        int doorX=x+(halfWidth+1)*direction;
        for(int a=doorX-2;a<=doorX+2;a++)for(int b=floor-2;b<=floor;b++)At(a,b).Flags&=~WorldTileFlags.Active;
        CaveHousePlacement1458.PlaceHouseDoor(workspace,random,doorX,floor,9);
        int backX=x+(halfWidth+1)*-direction-direction;
        for(int b=top;b<=bottom+1;b++){ref WorldTile tile=ref At(backX,b);tile.Flags|=WorldTileFlags.Active;tile.LiquidAmount=0;tile.Type=202;tile.Wall=0;tile.ClearSourceShape();}
        int itemChoice=workspace.VanillaSkyIslandHouseCount;if(itemChoice>3)itemChoice=random.Next(4);
        int mainItem=itemChoice switch{0=>159,1=>65,2=>158,_=>2219};
        BuriedChestContext1458 chestContext=workspace.VanillaBuriedChestContext??throw new InvalidOperationException("Island House requires retained chest loot context.");
        var chests=new BuriedChest1458(store,random,chestContext,workspace.CanRegisterGeneratedChest,c=>{if(!workspace.TryAddChest(c.Left,c.Top,string.Empty,c.Items))throw new InvalidOperationException("Island chest could not enter retained side table.");},workspace:workspace);
        chests.TryAdd(x,floor-3,out _,out _,mainItem,notNearOtherChests:false,chestStyle:13);
        workspace.VanillaSkyIslandHouseCount++;
        int windowLeft=x-halfWidth/2+1,windowRight=x+halfWidth/2-1,windowRadius=halfWidth>10?2:1,windowY=(top+bottom)/2-1;
        for(int a=windowLeft-windowRadius;a<=windowLeft+windowRadius;a++)for(int b=windowY-1;b<=windowY+1;b++)At(a,b).Wall=21;
        for(int a=windowRight-windowRadius;a<=windowRight+windowRadius;a++)for(int b=windowY-1;b<=windowY+1;b++)At(a,b).Wall=21;
        int tableX=x+(halfWidth/2+1)*-direction;
        CaveHousePlacement1458.PlaceHouseObject(workspace,random,tableX,bottom-1,14,7);
        PlaceFloorChair(workspace,random,tableX-2,bottom-1,10);PlaceFloorChair(workspace,random,tableX+2,bottom-1,10);
        At(tableX-2,bottom-1).FrameX+=18;At(tableX-2,bottom-2).FrameX+=18;
        CaveHousePlacement1458.PlaceHouseObject(workspace,random,left+1,top,91,random.Next(7,10));
        CaveHousePlacement1458.PlaceHouseObject(workspace,random,right-1,top,91,random.Next(7,10));
        CaveHousePlacement1458.PlaceHouseObject(workspace,random,direction>0?left:right,top+1,91,random.Next(7,10));
    }

    internal static void PlaceIslandHouseForTesting(Workspace workspace,IWorldGenerationVanillaRandom random,int x,int y,int style,double surface)=>PlaceIslandHouse(workspace,random,x,y,style,surface);









    private VanillaWorldGenerationBootstrapState1458 RequireBootstrap() =>
        state.Bootstrap ?? throw new InvalidOperationException("Late-structure pass executed before bootstrap initialization.");

    private static bool IsNaturalCarvable(ushort type) =>
        type is Dirt or Stone or Mud or JungleGrass or 123 or 147 or 161 or 179 or 180 or 181 or 182 or 183;



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

    private readonly record struct TileBounds(int Left, int Top, int Right, int Bottom);

    private sealed class RuntimeGrid
    {
        private readonly WorldTileStore store;

        public RuntimeGrid(Workspace workspace)
        {
            store = workspace.TileStore;
            if (workspace.HasVanillaGenerationSolidity)
                GenerationSolid = workspace.IsVanillaGenerationSolid;
        }

        public Func<TileTypeId, bool>? GenerationSolid { get; }

        public int Width => store.Dimensions.WidthTiles;
        public int Height => store.Dimensions.HeightTiles;

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

        public bool HasOpenNeighbor(int x, int y) =>
            !At(x - 1, y).IsActive || !At(x + 1, y).IsActive || !At(x, y - 1).IsActive || !At(x, y + 1).IsActive;

        public bool HasActiveNeighborType(int x, int y, ushort type) =>
            (At(x - 1, y).IsActive && At(x - 1, y).Type == type) ||
            (At(x + 1, y).IsActive && At(x + 1, y).Type == type) ||
            (At(x, y - 1).IsActive && At(x, y - 1).Type == type) ||
            (At(x, y + 1).IsActive && At(x, y + 1).Type == type);

        public bool IsEmptyRectangle(int left, int top, int width, int height)
        {
            if (left < 1 || top < 1 || left + width >= Width - 1 || top + height >= Height - 1)
                return false;
            for (int x = left; x < left + width; x++)
                for (int y = top; y < top + height; y++)
                {
                    if (At(x, y).IsActive)
                        return false;
                }
            return true;
        }

        public bool HasSpecialMaterialNearby(int centerX, int centerY, int radiusX, int radiusY)
        {
            int left = Math.Max(1, centerX - radiusX);
            int right = Math.Min(Width - 2, centerX + radiusX);
            int top = Math.Max(1, centerY - radiusY);
            int bottom = Math.Min(Height - 2, centerY + radiusY);
            for (int x = left; x <= right; x += 3)
                for (int y = top; y <= bottom; y += 3)
                {
                    WorldTile tile = At(x, y);
                    if (!tile.IsActive)
                        continue;
                    ushort type = tile.Type;
                    if (type is 225 or LihzahrdBrick or 367 or 368)
                        return true;
                }
            return false;
        }

        public bool TryFindMaterialBounds(ushort type, out TileBounds bounds)
        {
            int left = Width;
            int right = -1;
            int top = Height;
            int bottom = -1;
            for (int x = 1; x < Width - 1; x++)
                for (int y = 1; y < Height - 1; y++)
                {
                    WorldTile tile = At(x, y);
                    if (!tile.IsActive || tile.Type != type)
                        continue;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }

            if (right < left || bottom < top)
            {
                bounds = default;
                return false;
            }
            bounds = new TileBounds(left, top, right, bottom);
            return true;
        }
    }
}
