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
                ApplyGemCaves(context, grid, random);
                break;
            case LateStructureStage1458.Moss:
                ApplyMoss(context, workspace);
                break;
            case LateStructureStage1458.Temple:
                ApplyTemplePart2(context, workspace, grid, random, state.WorldSurface);
                break;
            case LateStructureStage1458.CaveWalls:
                ApplyCaveWalls(context, grid, random);
                break;
            case LateStructureStage1458.JungleTrees:
                ApplyJungleTrees(context, grid, random);
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
            });

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
                y);
            accepted++;
        }

        context.ReportProgress(1d, $"Adding source-backed Spider Caves ({accepted}/{attempts} regions)");
    }

    private void ApplyGemCaves(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        // TerrariaServer 1.4.5.8 Gem Caves: candidates are bounded connected caves, not radial ore clusters.
        int accepted = 0;
        int target = (int)(grid.Width * .003d);
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

            ApplyGemCave(grid, random, x, y);
            accepted++;
        }

        context.ReportProgress(1d, $"Adding source-backed Gem Caves ({accepted}/{target} regions)");
    }

    private static void ApplyGemCave(RuntimeGrid grid, IRandom random, int startX, int startY)
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
                // `WorldGen.SolidTile` deliberately treats the six loose gem tile identities as non-solid
                // during this spread. A gem converted at the cave edge can therefore become frontier on a
                // later wave and receive the cave wall behind it.
                if (IsGemCaveSolid(in tile) || tile.Wall != 0)
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
                    // Terraria 1.4.5.8 WorldGen.PlaceTile(178) validates that one cardinal anchor exists.
                    // Its immediately following framing pass preserves the initial 0..36 decoration row.
                    int style = PickGem(random, gems);
                    if (HasGemObjectAnchor(grid, x, y))
                    {
                        tile.Type = 178;
                        tile.Flags |= WorldTileFlags.Active;
                        tile.FrameX = (short)(style * 18);
                        tile.FrameY = (short)(random.Next(3) * 18);
                    }
                }
                foreach ((int nx, int ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                    if (!visited.Contains(nx * grid.Height + ny)) next.Add((nx, ny));
            }
        }
    }

    private static bool HasGemObjectAnchor(RuntimeGrid grid, int x, int y)
    {
        return IsCaveWallSolid(in grid.At(x, y + 1)) || IsCaveWallSolid(in grid.At(x - 1, y)) ||
               IsCaveWallSolid(in grid.At(x + 1, y)) || IsCaveWallSolid(in grid.At(x, y - 1));
    }

    private static bool IsGemCaveSolid(in WorldTile tile) =>
        tile.Type is not (63 or 64 or 65 or 66 or 67 or 68) && IsCaveWallSolid(in tile);

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
        double rockLayer = workspace.VanillaTerrainState?.CurrentRockLayer ?? state.RockLayer;

        // The spreader re-frames every square it converts, and source does that through the ordinary
        // SquareTileFrame rather than through any biome-specific framer. It matters that this is the real one:
        // a moss conversion changes a block's identity while leaving it solid, so every object check the
        // framing triggers finds its own footprint intact and spends nothing - but a framer that threw on
        // identities it had not modelled would refuse a statue standing beside a cave that takes moss.
        var framing = new GenerationTileFraming1458(workspace.TileStore, random);
        var grass = new GenerationGrass1458(
            workspace.TileStore, random, context.CancellationToken, state.WorldSurface,
            (x, y) => framing.SquareTileFrame(x, y));
        var pass = new MossPass1458(
            workspace.TileStore, random, state.WorldSurface, rockLayer, waterLine, lavaLine,
            shimmer.X, shimmer.Y, context.CancellationToken);
        pass.Apply(grass);
        context.ReportProgress(1d, $"Spreading Moss ({pass.Placed} seeded, {grass.Converted} spread)");
    }

    // TerrariaServer 1.4.5.8 WorldGen.templePart2 consumes the retained makeTemple bounds; it does not scan
    // the material map. Keeping that provenance prevents an earlier unrelated Lihzahrd fragment from changing
    // the complete shared-RNG decoration sequence.
    private static void ApplyTemplePart2(IWorldGenerationContext context, Workspace workspace, RuntimeGrid grid, IRandom random, double worldSurface)
    {
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
            }, spikyLihzahrdIsNonSolid: true);

        // TerrariaServer 1.4.5.8 WorldGen.templePart2. Each loop deliberately owns its source draw order;
        // placement helpers below are deterministic equivalents of the TileObjectData layouts used by these styles.
        double traps = temple.Rooms * 1.9d * (1d + random.Next(-15, 16) * .01d);
        int retries = 0;
        while (traps > 0d)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(temple.Left, temple.Right);
            int y = random.Next(temple.Top, temple.Bottom);
            if (IsTempleAir(grid, x, y) && TryMayanTrap(grid, random, x, y, grid.Height - 300, worldSurface))
            {
                traps -= 1d;
                retries = 0;
            }
            else
                retries++;
            if (retries > 100) { retries = 0; traps -= 1d; }
        }

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

        PlaceTempleSpikes(grid, random, temple, context.CancellationToken);
        PlaceTempleFurniture(grid, random, temple, context.CancellationToken);
        PlaceTemplePaintings(grid, random, temple, context.CancellationToken);
        context.ReportProgress(1d, $"Finishing Jungle Temple ({chests.Chests.Count} chest offers accepted)");
    }

    private static bool IsTempleAir(RuntimeGrid grid, int x, int y) => !grid.At(x, y).IsActive && grid.At(x, y).Wall == 87;

    private static bool TryMayanTrap(RuntimeGrid grid, IRandom random, int x, int y, int floorLimit, double worldSurface)
    {
        // TerrariaServer 1.4.5.8 WorldGen.mayanTrap. The first roll selects the horizontal (0) or vertical (1)
        // arm before any rejection, so it must remain above the floor search.
        int direction = random.Next(3) == 0 ? 0 : 1;
        int floor = y;
        while (floor < floorLimit && !IsSolidOrSloped(in grid.At(x, floor))) floor++;
        if (floor >= floorLimit || grid.At(x, floor).Type is 232 or 10) return false;
        floor--;
        if (grid.At(x, floor).LiquidAmount > 0 && grid.At(x, floor).LiquidKind == WorldLiquidKind.Lava) return false;

        for (int dx = -1; dx <= 1; dx++)
        for (int dy = 0; dy >= -2; dy--)
            if ((grid.At(x + dx, floor + dy).Flags & WorldTileFlags.Inactive) != 0) return false;

        if (grid.At(x, floor + 1).Type is 10 or 48 or 232) return false;
        if (direction != 0)
        {
            int roof = floor;
            while (!IsSolidOrSloped(in grid.At(x, roof)))
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
                !IsSolidOrSloped(in grid.At(x, floor + 1)) || grid.At(x, floor).IsActive) return false;

            PlaceSingleTile(grid, x, floor, 135, 0, 108);
            PlaceSingleTile(grid, x, roof, 137, 0, checked((short)(launcherStyle * 18)));
            for (int side = 0; side < 2; side++)
            {
                int length = random.Next(1, 5);
                int wireX = x;
                int sideStep = side == 0 ? -1 : 1;
                while (length-- > 0)
                {
                    wireX += sideStep;
                    if (!IsSolidOrSloped(in grid.At(wireX, roof - 1)) || IsSolidOrSloped(in grid.At(wireX, roof + 1))) break;
                    PlaceSingleTile(grid, wireX, roof, 137, 0, checked((short)(launcherStyle * 18)));
                    AddWire(ref grid.At(wireX, roof), verticalWireColor);
                }
            }
            AddWirePath(grid, x, floor, x, roof, verticalWireColor);
            return true;
        }

        int corridorY = floor - random.Next(3);
        int left = x;
        while (left >= 5 && !IsSolidOrSloped(in grid.At(left, corridorY))) left--;
        int right = x;
        while (right < grid.Width - 5 && !IsSolidOrSloped(in grid.At(right, corridorY))) right++;
        bool canLeft = x - left is > 5 and < 50 && IsSolidOrSloped(in grid.At(left, corridorY + 1));
        bool canRight = right - x is > 5 and < 50 && IsSolidOrSloped(in grid.At(right, corridorY + 1));
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
        if (!IsSolidOrSloped(in grid.At(x, floor + 1))) return false;
        if (grid.At(x, floor).IsActive) return false;

        PlaceSingleTile(grid, x, floor, 135, 0, 108);
        // WorldGen.KillTile clears the solid launcher anchor but keeps its unsafe wall; PlaceTile immediately
        // occupies that same cell, so preserve the backing tile's wall rather than assigning a default tile.
        int wireColor = random.Next(3);
        if ((grid.At(x, floor).Flags & WorldTileFlags.WireRed) != 0) wireColor = 0;
        if ((grid.At(x, floor).Flags & WorldTileFlags.WireBlue) != 0) wireColor = 1;
        if ((grid.At(x, floor).Flags & WorldTileFlags.WireGreen) != 0) wireColor = 2;
        int horizontalLauncherStyle = Math.Abs(launcherX - x) < 10 && random.Next(3) != 0 ? 2 : 1;
        PlaceSingleTile(grid, launcherX, corridorY, 137, 0, checked((short)(horizontalLauncherStyle * 18)));
        if (step == 1) grid.At(launcherX, corridorY).FrameX += 18;

        int wireLength = random.Next(5);
        int wireY = corridorY;
        while (wireLength-- > 0)
        {
            wireY--;
            if (!IsSolidOrSloped(in grid.At(launcherX, wireY)) ||
                !IsSolidOrSloped(in grid.At(launcherX - step, wireY)) ||
                IsSolidOrSloped(in grid.At(launcherX + step, wireY))) break;
            PlaceSingleTile(grid, launcherX, wireY, 137, 0, checked((short)(horizontalLauncherStyle * 18)));
            if (step == 1) grid.At(launcherX, wireY).FrameX += 18;
            AddWire(ref grid.At(launcherX, wireY), wireColor);
        }
        AddWirePath(grid, x, floor, launcherX, corridorY, wireColor);
        return true;
    }

    private static void PlaceSingleTile(RuntimeGrid grid, int x, int y, ushort type, short frameX, short frameY)
    {
        ref WorldTile tile = ref grid.At(x, y);
        tile.Type = type; tile.Flags |= WorldTileFlags.Active; tile.FrameX = frameX; tile.FrameY = frameY;
        tile.Shape = 0; tile.LiquidAmount = 0;
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

    private static void PlaceTempleSpikes(RuntimeGrid grid, IRandom random, VanillaTemplePart2State1458 temple, CancellationToken cancellationToken)
    {
        double budget = temple.Rooms * 1.25d * (1d + random.Next(-25, 36) * .01d);
        for (int attempts = 1; budget > 0d && attempts <= 10000; attempts++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(temple.Left, temple.Right); int y = random.Next(temple.Top, temple.Bottom);
            if (!IsTempleAir(grid, x, y)) continue;
            int floor = FindTempleFloor(grid, x, y, temple.Bottom);
            if (floor > temple.Bottom) continue;
            int style = random.Next(43, 46);
            if (TryPlaceObject(grid, x, floor, 2, 3, 105, style * 36, 0)) budget -= 1d;
        }
    }

    private static void PlaceTempleFurniture(RuntimeGrid grid, IRandom random, VanillaTemplePart2State1458 temple, CancellationToken cancellationToken)
    {
        double budget = temple.Rooms * 1.35d * (1d + random.Next(-15, 26) * .01d);
        for (int attempts = 1; budget > 0d && attempts <= 10000; attempts++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(temple.Left, temple.Right); int y = random.Next(temple.Top, temple.Bottom);
            if (!IsTempleAir(grid, x, y)) continue;
            int floor = FindTempleFloor(grid, x, y, temple.Bottom);
            if (floor > temple.Bottom) continue;
            switch (random.Next(3))
            {
                case 0: if (TryPlaceObject(grid, x, floor, 2, 1, 18, 360, 0)) budget -= 1d; break;
                // WorldGen.Place3x2 takes the centre bottom cell for tables; the visual footprint starts at x - 1.
                case 1: if (TryPlaceObject(grid, x - 1, floor, 3, 2, 14, 486, 0)) budget -= 1d; break;
                // PlaceTile(15) takes the Place1x2 chair arm. Its style stride is 40 pixels in FrameY,
                // not the 3x2 furniture stride used by tables and work benches.
                case 2: if (TryPlaceObject(grid, x, floor, 1, 2, 15, 0, 480)) budget -= 1d; break;
            }
        }
    }

    private static void PlaceTemplePaintings(RuntimeGrid grid, IRandom random, VanillaTemplePart2State1458 temple, CancellationToken cancellationToken)
    {
        int budget = 1;
        if (grid.Width > 4200) budget++;
        if (grid.Width > 6400) budget += random.Next(2);
        for (int attempts = 1; budget > 0 && attempts <= 10000; attempts++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(temple.Left, temple.Right); int y = random.Next(temple.Top, temple.Bottom);
            if (!IsTempleAir(grid, x, y) || HasTemplePaintingOrDoor(grid, x, y)) continue;
            // TileObjectData for painting style 88 maps to this fixed 3x3 frame origin.
            // WorldGen.Place3x3Wall centres the painting on the sampled cell and requires wall, not floor support.
            if (TryPlaceWallObject(grid, x, y, 3, 3, 240, 864, 108)) budget--;
        }
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

    private static bool TryPlaceObject(RuntimeGrid grid, int left, int bottom, int width, int height, ushort type, int frameX, int frameY)
    {
        int top = bottom - height + 1;
        if (left < 1 || top < 1 || left + width >= grid.Width || bottom + 1 >= grid.Height) return false;
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < height; dy++) if (grid.At(left + dx, top + dy).IsActive) return false;
        for (int dx = 0; dx < width; dx++) if (!IsSolidOrSloped(in grid.At(left + dx, bottom + 1))) return false;
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < height; dy++)
        {
            ref WorldTile tile = ref grid.At(left + dx, top + dy);
            tile.Type = type; tile.Flags |= WorldTileFlags.Active; tile.FrameX = checked((short)(frameX + dx * 18));
            tile.FrameY = checked((short)(frameY + dy * 18)); tile.Shape = 0; tile.LiquidAmount = 0;
        }
        return true;
    }

    private static bool TryPlaceWallObject(RuntimeGrid grid, int centerX, int centerY, int width, int height, ushort type, int frameX, int frameY)
    {
        int left = centerX - width / 2;
        int top = centerY - height / 2;
        if (left < 1 || top < 1 || left + width >= grid.Width || top + height >= grid.Height) return false;
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < height; dy++)
        {
            WorldTile tile = grid.At(left + dx, top + dy);
            if (tile.IsActive || tile.Wall == 0) return false;
        }
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < height; dy++)
        {
            ref WorldTile tile = ref grid.At(left + dx, top + dy);
            tile.Type = type; tile.Flags |= WorldTileFlags.Active; tile.FrameX = checked((short)(frameX + dx * 18));
            tile.FrameY = checked((short)(frameY + dy * 18)); tile.Shape = 0; tile.LiquidAmount = 0;
        }
        return true;
    }

    private static bool IsSolidOrSloped(in WorldTile tile) => tile.IsActive && VanillaTileCollisionCatalog.IsSolid(tile.TileType);

    private void ApplyCaveWalls(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
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
        ApplyGemCave(new RuntimeGrid(workspace), new VanillaRandom(random), startX, startY);

    internal static bool TryMayanTrapPrefixForTesting(
        Workspace workspace,
        IWorldGenerationVanillaRandom random,
        int x,
        int y) =>
        TryMayanTrap(new RuntimeGrid(workspace), new VanillaRandom(random), x, y, workspace.HeightTiles - 300, 140d);

    // TerrariaServer 1.4.5.8 WorldGen.PlaceTile dispatches tile 15 to its 1x2 object path.
    // Keep the narrow fixture seam so the Temple furniture call site cannot silently regress to
    // a generic 3x2 placement while the broader TileObjectData implementation is still pending.
    internal static bool TryPlaceTempleChairForTesting(Workspace workspace, int x, int floor) =>
        TryPlaceObject(new RuntimeGrid(workspace), x, floor, 1, 2, 15, 0, 480);

    internal static bool TryPlaceTempleTableForTesting(Workspace workspace, int centerX, int floor) =>
        TryPlaceObject(new RuntimeGrid(workspace), centerX - 1, floor, 3, 2, 14, 486, 0);

    internal static bool TryPlaceTemplePaintingForTesting(Workspace workspace, int centerX, int centerY) =>
        TryPlaceWallObject(new RuntimeGrid(workspace), centerX, centerY, 3, 3, 240, 864, 108);

    internal static bool TryPlaceTempleCandleForTesting(Workspace workspace, int x, int floor) =>
        TryPlaceObject(new RuntimeGrid(workspace), x, floor, 2, 3, 105, 1620, 0);

    internal static bool TryPlaceTempleWorkBenchForTesting(Workspace workspace, int x, int floor) =>
        TryPlaceObject(new RuntimeGrid(workspace), x, floor, 2, 1, 18, 360, 0);

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

            if (IsCaveWallSolid(in tile, spiderSolidity))
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
        int startY)
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
                if (IsCaveWallSolid(in tile, spiderSolidity: true) || tile.Wall != 0)
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
                    if (IsCaveWallSolid(in grid.At(x, y + 1), spiderSolidity: true) && random.Next(3) == 0)
                    {
                        if (random.Next(15) == 0) chests.TryAdd(x, y, out _, out _, 939, true, 15);
                        else GenerationDecorationPlacement1458.TryPlacePot(store, random, x, y, random.Next(19, 21));
                    }
                    if (!tile.IsActive)
                    {
                        if (IsCaveWallSolid(in grid.At(x, y - 1), spiderSolidity: true) && random.Next(3) == 0)
                            speleothems.PlaceTight(x, y, spiders: true);
                        else if (IsCaveWallSolid(in grid.At(x, y + 1), spiderSolidity: true))
                        {
                            GenerationDecorationPlacement1458.TryPlaceTile3x2(store, random, x, y, 187, 9 + random.Next(5));
                            if (random.Next(3) == 0 && !tile.IsActive)
                            {
                                GenerationDecorationPlacement1458.TryPlaceSmallPile(store, x, y, 34 + random.Next(4));
                                if (!tile.IsActive) GenerationDecorationPlacement1458.TryPlaceSmallPile(store, x, y, 48 + random.Next(6), 0);
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
            if (IsCaveWallSolid(in tile) || tile.Wall != 0)
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

            if (!IsCaveWallSolid(in tile))
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

    private static bool IsCaveWallSolid(in WorldTile tile, bool spiderSolidity = false)
    {
        if (!tile.IsActive || tile.IsActuated || tile.Shape != 0)
            return false;

        bool solid = tile.Type switch
        {
            // Spider Caves temporarily executes `Main.tileSolid[379] = false` before both countTiles and
            // Spread.Spider. Keeping this source-scoped avoids changing cave-wall or runtime collision rules.
            379 when spiderSolidity => false,
            162 => false,
            LihzahrdBrick => true,
            232 => false,
            _ => VanillaTileCollisionCatalog.IsSolid(tile.TileType)
        };
        return solid && !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
    }

    private static bool CannotBeReplacedByWallSpread(ushort wall) =>
        wall is 3 or 4 or 34 or 40 or 83 or 87 or 244;

    private static bool IsHousingWall(ushort wall) =>
        VanillaWallDefinitionCatalog.TryGet(new WallTypeId(wall), out VanillaWallDefinition definition) &&
        definition.IsHousingWall;

    private void ApplyJungleTrees(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int target = grid.Width switch
        {
            <= 4200 => 10,
            <= 6400 => 15,
            _ => 20
        };
        int halfWidth = Math.Max(260, grid.Width / 9);
        int left = Math.Max(25, bootstrap.JungleOriginX - halfWidth);
        int right = Math.Min(grid.Width - 25, bootstrap.JungleOriginX + halfWidth);
        int minY = Math.Clamp((int)state.RockLayer + 20, 25, state.UnderworldTop - 120);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 55);
        int placed = 0;

        for (int attempt = 0; attempt < target * 160 && placed < target; attempt++)
        {
            if ((attempt & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int x = random.Next(left, right);
            int probe = random.Next(minY, maxY);
            int floor = grid.FindFirstActiveY(x, probe, Math.Min(grid.Height - 1, probe + 45));
            if (floor < 10 || floor >= grid.Height - 5)
                continue;
            ushort floorType = grid.At(x, floor).Type;
            if (floorType is not (Mud or JungleGrass))
                continue;

            int height = random.Next(7, 14);
            if (!grid.IsEmptyRectangle(x - 2, floor - height - 3, 5, height + 3))
                continue;

            for (int y = floor - 1; y >= floor - height; y--)
                SetBlock(ref grid.At(x, y), LivingMahogany);
            int crownY = floor - height;
            for (int dx = -3; dx <= 3; dx++)
            for (int dy = -2; dy <= 2; dy++)
            {
                if (dx * dx + dy * dy > 10)
                    continue;
                ref WorldTile leaf = ref grid.At(x + dx, crownY + dy);
                if (!leaf.IsActive)
                    SetBlock(ref leaf, LivingMahoganyLeaves);
            }
            placed++;
        }

        context.ReportProgress(1d, $"Growing Living Mahogany structures ({placed}/{target})");
    }

    private void ApplyFloatingIslandHouses(
        IWorldGenerationContext context,
        Workspace workspace,
        RuntimeGrid grid,
        IRandom random)
    {
        int target = grid.Width switch
        {
            <= 4200 => 3,
            <= 6400 => 5,
            _ => 6
        };
        int skyBottom = Math.Clamp((int)state.WorldSurface - 35, 80, grid.Height - 50);
        var candidates = new List<WorldGenerationPoint>();

        for (int x = 40; x < grid.Width - 40; x += 12)
        {
            if ((x & 255) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            for (int y = 45; y < skyBottom; y++)
            {
                if (grid.At(x, y).IsActive && grid.At(x, y).Type == Cloud && !grid.At(x, y - 1).IsActive)
                {
                    if (candidates.Count == 0 || Math.Abs(x - candidates[^1].X) >= 180)
                        candidates.Add(new WorldGenerationPoint(x, y));
                    break;
                }
            }
        }

        int placed = 0;
        foreach (WorldGenerationPoint candidate in candidates)
        {
            if (placed >= target)
                break;
            int floorY = candidate.Y;
            int left = Math.Clamp(candidate.X - random.Next(5, 8), 4, grid.Width - 18);
            if (!CanBuildSkyHouse(grid, left, floorY))
                continue;

            BuildSkyHouse(grid, left, floorY);
            int chestLeft = left + 3;
            int chestTop = floorY - 2;
            if (!PlaceGeneratedChest(workspace, grid, chestLeft, chestTop, style: 13))
                continue;
            placed++;
        }

        context.ReportProgress(1d, $"Building Floating Island Houses ({placed}/{target})");
    }

    private static bool CanBuildSkyHouse(RuntimeGrid grid, int left, int floorY)
    {
        if (floorY < 10 || floorY + 1 >= grid.Height || left < 2 || left + 13 >= grid.Width - 2)
            return false;

        // A house owns both its interior and its Sunplate perimeter.  Checking only the interior let a
        // pre-existing registered chest on the floor or an outer wall be overwritten, leaving a stale
        // chest side-table entry for validation and serialization.
        for (int x = left; x <= left + 12; x++)
        {
            if (!grid.At(x, floorY).IsActive)
                return false;
            for (int y = floorY - 7; y <= floorY; y++)
            {
                WorldTile tile = grid.At(x, y);
                if (tile.IsActive && VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                    return false;
            }
        }
        return true;
    }

    internal static bool CanBuildSkyHouseForTesting(Workspace workspace, int left, int floorY)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return CanBuildSkyHouse(new RuntimeGrid(workspace), left, floorY);
    }

    private static void BuildSkyHouse(RuntimeGrid grid, int left, int floorY)
    {
        const int width = 13;
        const int height = 7;
        int top = floorY - height;

        for (int x = left; x < left + width; x++)
        {
            SetBlock(ref grid.At(x, floorY), Sunplate);
            SetBlock(ref grid.At(x, top), Sunplate);
        }
        for (int y = top; y <= floorY; y++)
        {
            SetBlock(ref grid.At(left, y), Sunplate);
            SetBlock(ref grid.At(left + width - 1, y), Sunplate);
        }
        for (int x = left + 1; x < left + width - 1; x++)
        for (int y = top + 1; y < floorY; y++)
        {
            ref WorldTile tile = ref grid.At(x, y);
            if (tile.IsActive && !VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
            {
                tile.Flags &= ~WorldTileFlags.Active;
                tile.Shape = 0;
                tile.LiquidAmount = 0;
            }
            if (!tile.IsActive)
                tile.Wall = DiscWall;
        }
    }

    private static bool PlaceGeneratedChest(
        Workspace workspace,
        RuntimeGrid grid,
        int left,
        int top,
        int style)
    {
        if (left < 1 || top < 1 || left + 1 >= grid.Width - 1 || top + 2 >= grid.Height - 1)
            return false;
        if (grid.At(left, top).IsActive || grid.At(left + 1, top).IsActive ||
            grid.At(left, top + 1).IsActive || grid.At(left + 1, top + 1).IsActive ||
            !grid.At(left, top + 2).IsActive || !grid.At(left + 1, top + 2).IsActive)
        {
            return false;
        }

        WorldTile a = grid.At(left, top);
        WorldTile b = grid.At(left + 1, top);
        WorldTile c = grid.At(left, top + 1);
        WorldTile d = grid.At(left + 1, top + 1);
        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            ref WorldTile tile = ref grid.At(left + dx, top + dy);
            tile.Type = Containers;
            tile.Flags |= WorldTileFlags.Active;
            tile.FrameX = checked((short)(style * 36 + dx * 18));
            tile.FrameY = checked((short)(dy * 18));
            tile.Shape = 0;
            tile.LiquidAmount = 0;
            tile.LiquidKind = WorldLiquidKind.Water;
        }

        if (workspace.TryAddGeneratedChest(left, top, string.Empty, ReadOnlySpan<WorldChestItem>.Empty))
            return true;
        grid.At(left, top) = a;
        grid.At(left + 1, top) = b;
        grid.At(left, top + 1) = c;
        grid.At(left + 1, top + 1) = d;
        return false;
    }

    private VanillaWorldGenerationBootstrapState1458 RequireBootstrap() =>
        state.Bootstrap ?? throw new InvalidOperationException("Late-structure pass executed before bootstrap initialization.");

    private static bool IsNaturalCarvable(ushort type) =>
        type is Dirt or Stone or Mud or JungleGrass or 123 or 147 or 161 or 179 or 180 or 181 or 182 or 183;

    private static void SetBlock(ref WorldTile tile, ushort type)
    {
        tile.Type = type;
        tile.Flags |= WorldTileFlags.Active;
        tile.FrameX = 0;
        tile.FrameY = 0;
        tile.Shape = 0;
        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
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

    private readonly record struct TileBounds(int Left, int Top, int Right, int Bottom);

    private sealed class RuntimeGrid
    {
        private readonly WorldTileStore store;

        public RuntimeGrid(Workspace workspace) => store = workspace.TileStore;

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
