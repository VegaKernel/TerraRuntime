using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Second source-backed Terraria 1.4.5.8 world-generation overlay. It extends the ordinary canonical pipeline from
/// the first Jungle pass through Slush, keeping the source catalog order and the same shared UnifiedRandom stream.
/// Aggregate compatibility biomes are reduced to an ocean-only residual and the old aggregate ore pass becomes a
/// dependency barrier after source-shaped Shinies has placed the pre-hardmode ore tiers.
/// </summary>
public sealed class SourceBackedMidPipeline1458 : IWorldGenerationProvider
{
    internal static readonly WorldGenerationPassId MudCavesToGrassId = new("terraria:1.4.5.8/MudCavesToGrass");
    internal static readonly WorldGenerationPassId FullDesertId = new("terraria:1.4.5.8/FullDesert");
    internal static readonly WorldGenerationPassId MushroomPatchesId = new("terraria:1.4.5.8/MushroomPatches");
    internal static readonly WorldGenerationPassId MarbleId = new("terraria:1.4.5.8/Marble");
    internal static readonly WorldGenerationPassId GraniteId = new("terraria:1.4.5.8/Granite");
    internal static readonly WorldGenerationPassId FloatingIslandsId = new("terraria:1.4.5.8/FloatingIslands");
    internal static readonly WorldGenerationPassId DirtToMudId = new("terraria:1.4.5.8/DirtToMud");
    internal static readonly WorldGenerationPassId SiltId = new("terraria:1.4.5.8/Silt");
    internal static readonly WorldGenerationPassId ShiniesId = new("terraria:1.4.5.8/Shinies");
    internal static readonly WorldGenerationPassId WebsId = new("terraria:1.4.5.8/Webs");
    internal static readonly WorldGenerationPassId UnderworldId = new("terraria:1.4.5.8/Underworld");
    internal static readonly WorldGenerationPassId CorruptionId = new("terraria:1.4.5.8/Corruption");
    internal static readonly WorldGenerationPassId LakesId = new("terraria:1.4.5.8/Lakes");
    internal static readonly WorldGenerationPassId SlushId = new("terraria:1.4.5.8/Slush");

    private static readonly WorldGenerationPassId JungleId = new("terraria:1.4.5.8/Jungle");
    private static readonly WorldGenerationPassId CompatibilityBiomesId = new("terraria:1.4.5.8/Biomes");
    private static readonly WorldGenerationPassId CompatibilityOresId = new("terraria:1.4.5.8/Ores");

    private readonly SourceBackedPipeline1458 baseline = new();

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

        CapturedPass residualBiomes = capture.Require(CompatibilityBiomesId);
        var state = new MidState1458();

        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id == CompatibilityBiomesId)
            {
                Add(builder, MudCavesToGrassId, JungleId,
                    new MidPass1458(MidStage1458.MudCavesToGrass, state));
                Add(builder, FullDesertId, MudCavesToGrassId,
                    new MidPass1458(MidStage1458.FullDesert, state));
                Add(builder, MushroomPatchesId, FullDesertId,
                    new MidPass1458(MidStage1458.MushroomPatches, state));
                Add(builder, MarbleId, MushroomPatchesId,
                    new MidPass1458(MidStage1458.Marble, state));
                Add(builder, GraniteId, MarbleId,
                    new MidPass1458(MidStage1458.Granite, state));
                Add(builder, FloatingIslandsId, GraniteId,
                    new MidPass1458(MidStage1458.FloatingIslands, state));
                Add(builder, DirtToMudId, FloatingIslandsId,
                    new MidPass1458(MidStage1458.DirtToMud, state));
                Add(builder, SiltId, DirtToMudId,
                    new MidPass1458(MidStage1458.Silt, state));
                Add(builder, ShiniesId, SiltId,
                    new MidPass1458(MidStage1458.Shinies, state));
                Add(builder, WebsId, ShiniesId,
                    new MidPass1458(MidStage1458.Webs, state));
                Add(builder, UnderworldId, WebsId,
                    new MidPass1458(MidStage1458.Underworld, state));
                Add(builder, CorruptionId, UnderworldId,
                    new MidPass1458(MidStage1458.Corruption, state));
                Add(builder, LakesId, CorruptionId,
                    new MidPass1458(MidStage1458.Lakes, state));
                Add(builder, SlushId, LakesId,
                    new MidPass1458(MidStage1458.Slush, state));

                builder.Add(
                    CloneDescriptor(residualBiomes.Descriptor, WorldGenerationRngMode.IsolatedDeterministic, [SlushId]),
                    new OceanResidualCompatibilityBiomesPass1458(residualBiomes.Pass));
                continue;
            }

            if (entry.Descriptor.Id == CompatibilityOresId)
            {
                builder.Add(entry.Descriptor, SourceBackedOreCompatibilityBarrier1458.Instance);
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
        WorldGenerationPassId[] requiredAfter) =>
        new(
            source.Id,
            rngMode,
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

        public CapturedPass Require(WorldGenerationPassId id)
        {
            foreach (CapturedPass entry in entries)
            {
                if (entry.Descriptor.Id == id)
                    return entry;
            }

            throw new InvalidOperationException($"Baseline early vanilla plan did not expose required pass '{id}'.");
        }

        public void Replay(IWorldGenerationPlanBuilder builder)
        {
            foreach (CapturedPass entry in entries)
                builder.Add(entry.Descriptor, entry.Pass);
        }
    }
}

internal enum MidStage1458 : byte
{
    MudCavesToGrass,
    FullDesert,
    MushroomPatches,
    Marble,
    Granite,
    FloatingIslands,
    DirtToMud,
    Silt,
    Shinies,
    Webs,
    Underworld,
    Corruption,
    Lakes,
    Slush
}

internal sealed class MidState1458
{
    public VanillaWorldGenerationBootstrapState1458? Bootstrap { get; private set; }
    public double WorldSurface { get; private set; }
    public double WorldSurfaceLow { get; private set; }
    public double RockLayer { get; private set; }
    public int UnderworldTop { get; set; }
    public int DesertLeft { get; set; } = -1;
    public int DesertRight { get; set; } = -1;

    public void EnsureInitialized(IWorldGenerationContext context, Workspace workspace)
    {
        if (Bootstrap is not null)
            return;

        Bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Mid vanilla world generation requires the Reset bootstrap state.");
        if (context.Metadata is null || !context.Metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Mid vanilla world generation requires source-backed Terrain layers.");

        WorldSurface = layers.WorldSurface;
        WorldSurfaceLow = workspace.VanillaTerrainState?.WorldSurfaceLow ?? layers.WorldSurface;
        RockLayer = layers.RockLayer;
        UnderworldTop = Math.Max((int)RockLayer + 180, workspace.HeightTiles - 200);
    }
}

internal sealed class MidPass1458 : IWorldGenerationPass
{
    private const ushort Dirt = 0;
    private const ushort Stone = 1;
    private const ushort Grass = 2;
    private const ushort Cobweb = 51;
    private const ushort Sand = 53;
    private const ushort Ash = 57;
    private const ushort Mud = 59;
    private const ushort JungleGrass = 60;
    private const ushort MushroomGrass = 70;
    private const ushort Snow = 147;
    private const ushort Ice = 161;
    private const ushort Cloud = 189;
    private const ushort RainCloud = 196;
    private const ushort Sandstone = 396;
    private const ushort HardenedSand = 397;
    private const ushort CloudWall = 73;

    private readonly MidStage1458 stage;
    private readonly MidState1458 state;

    public MidPass1458(
        MidStage1458 stage,
        MidState1458 state)
    {
        this.stage = stage;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Workspace workspace = context.Workspace as Workspace ??
            throw new InvalidOperationException("Source-backed mid Terraria generation requires Workspace.");
        state.EnsureInitialized(context, workspace);
        var grid = new RuntimeGrid(workspace);
        var random = new VanillaRandom(
            context.VanillaRandom ??
            throw new InvalidOperationException("Source-backed mid Terraria generation requires shared UnifiedRandom semantics."));

        switch (stage)
        {
            case MidStage1458.MudCavesToGrass:
                ApplyMudCavesToGrass(context, workspace);
                break;
            case MidStage1458.FullDesert:
                ApplyFullDesert(context, workspace, grid, random);
                break;
            case MidStage1458.MushroomPatches:
                ApplyMushroomPatches(context, workspace);
                break;
            case MidStage1458.Marble:
                MarbleBiome1458.Apply(workspace, context.VanillaRandom ??
                    throw new InvalidOperationException("Marble generation requires the shared vanilla RNG."),
                    workspace.VanillaTerrainState?.CurrentRockLayer ??
                    throw new InvalidOperationException("Marble generation requires retained GenVars terrain state."),
                    context.CancellationToken);
                break;
            case MidStage1458.Granite:
                GraniteBiome1458.Apply(workspace, context.VanillaRandom ??
                    throw new InvalidOperationException("Granite generation requires the shared vanilla RNG."),
                    context.Request.ResolveVanillaSeed1458(), context.CancellationToken);
                break;
            case MidStage1458.FloatingIslands:
                ApplyFloatingIslands(context, workspace, grid, random);
                break;
            case MidStage1458.DirtToMud:
            case MidStage1458.Silt:
            case MidStage1458.Shinies:
                MineralDeposits1458.Apply(stage, context, workspace);
                break;
            case MidStage1458.Webs:
                ApplyWebs(context, workspace);
                break;
            case MidStage1458.Underworld:
                UnderworldTerrain1458.Generate(workspace.TileStore, context.VanillaRandom!, state.WorldSurface,
                    workspace.VanillaLiquidLines ?? throw new InvalidOperationException("Underworld requires exact Early-pass liquid lines."),
                    context.CancellationToken);
                UnderworldVegetation1458.Generate(workspace.TileStore, context.VanillaRandom!, context.CancellationToken);
                HellFortGenerator1458.Generate(workspace.TileStore, context.VanillaRandom!, context.CancellationToken);
                HellFortLighting1458.Generate(workspace.TileStore, context.VanillaRandom!, context.CancellationToken);
                HellFortFurniture1458.Generate(workspace, context.VanillaRandom!, context.CancellationToken);
                HellFortDecoration1458.Generate(workspace.TileStore, context.VanillaRandom!, context.CancellationToken);
                context.ReportProgress(1d, "Generating underworld terrain, lava, hellstone and forts");
                break;
            case MidStage1458.Corruption:
                ApplyEvilBiome(context, workspace, grid);
                break;
            case MidStage1458.Lakes:
                ApplyLakes(context, workspace);
                break;
            case MidStage1458.Slush:
                ApplySlush(context, workspace);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static void ApplyMudCavesToGrass(IWorldGenerationContext context, Workspace workspace)
    {
        JungleMudSurface1458.Apply(workspace.TileStore, context.CancellationToken);
        context.ReportProgress(1d, "Spreading jungle grass and removing small terrain clumps");
    }

    private void ApplyFullDesert(IWorldGenerationContext context, Workspace workspace, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        IWorldGenerationVanillaRandom vanilla = context.VanillaRandom ??
            throw new InvalidOperationException("Full Desert requires the pass-local vanilla RNG.");
        int half = grid.Width / 2, side = bootstrap.DungeonSide;
        int center = half + (vanilla.Next(half) / 8 + half / 8) * -side;
        int attemptsOnSide = 0, flips = 0;
        DesertSurface1458? desert = null;
        for (int attempt = 0; attempt < 100000; attempt++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            desert = DesertSurface1458.TryDescribe(workspace.TileStore, vanilla, center,
                state.WorldSurface, flips >= 2, context.CancellationToken);
            if (desert is not null) break;
            int offset = vanilla.Next(half) / 2 + half / 8 + vanilla.Next(attemptsOnSide / 12);
            center = half + offset * -side;
            if (++attemptsOnSide > grid.Width / 4) { side = -side; attemptsOnSide = 0; flips++; }
        }
        if (desert is null) throw new InvalidOperationException("Desert placement exhausted its bounded search.");
        state.DesertLeft = desert.Combined.X;
        state.DesertRight = desert.Combined.ExclusiveRight;
        workspace.SetVanillaUndergroundDesertRegion(desert.Combined.X - 10, desert.Combined.Y - 10,
            desert.Combined.Width + 20, desert.Combined.Height + 20);
        desert.PlaceMound(workspace.TileStore, vanilla, state.WorldSurface, context.CancellationToken);
        // WorldBuilding.Configuration.json overrides the class field default with 0.5.
        if (vanilla.NextDouble() <= .5)
            new DesertEntrances1458(workspace.TileStore, desert, vanilla, context.CancellationToken).Place(vanilla.Next(4));
        WorldTileRegion bounds = DesertHive1458.PlaceClusters(workspace.TileStore, desert, vanilla,
            context.Request.ResolveVanillaSeed1458(), state.WorldSurface, context.CancellationToken);
        var decoration = new DesertDecoration1458(workspace.TileStore, vanilla);
        decoration.Apply(desert, context.CancellationToken);
        for (int x = desert.Hive.X - 20; x < desert.Hive.ExclusiveRight + 20; x++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            for (int y = desert.Hive.Y - 20; y < desert.Hive.ExclusiveBottom + 20; y++)
            {
                if (x <= 0 || y <= 0 || x >= grid.Width - 1 || y >= grid.Height - 1) continue;
                DesertSurface1458.FrameWalls(workspace.TileStore, vanilla, x, y);
                // All admitted decoration footprints are complete and this phase makes
                // no new active-tile edits. Generation TileFrame still clears inactive metadata.
                decoration.FrameNeighbours(x, y);
            }
        }
        workspace.SetVanillaDesertGenerationState(new(desert.Hive, bounds,
            new WorldTileRegion(desert.Combined.X, 50, desert.Combined.Width, desert.Combined.ExclusiveBottom - 20)));
        context.ReportProgress(1d, "Generating vanilla desert surface, entrances and hive");
    }

    private void ApplyMushroomPatches(IWorldGenerationContext context, Workspace workspace)
    {
        MushroomBiome1458.Apply(workspace, context.VanillaRandom ??
            throw new InvalidOperationException("Mushroom generation requires the shared vanilla RNG."),
            state.WorldSurface, state.RockLayer, context.CancellationToken);
        context.ReportProgress(1d, "Generating glowing mushroom patches");
    }

    private void ApplyFloatingIslands(IWorldGenerationContext context, Workspace workspace, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        // 1.4.5.8 creates floor(width * 0.0008) ordinary islands *plus* GenVars.skyLakes.
        // The old code folded lakes into the same fixed count and even emitted seven total on large
        // worlds, losing the source topology/count relationship.
        int islandCount = Math.Max(1, (int)(grid.Width * 0.0008d));
        int lakeBudget = Math.Max(0, bootstrap.SkyLakes);
        int count = islandCount + lakeBudget;
        var used = new List<int>(count);
        var anchors = new List<VanillaSkyIsland1458>(count);
        var islands = new SkyIsland1458(workspace.TileStore,
            context.VanillaRandom ?? throw new InvalidOperationException("Sky islands require the shared vanilla RNG."));

        for (int i = 0; i < count; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (!islands.TryFindAnchor(state.WorldSurface, state.WorldSurfaceLow, used, out int x, out int y))
                continue;
            bool lake = used.Count >= islandCount;
            used.Add(x);
            islands.Generate(x, y, lake);
            anchors.Add(new(x, y, 0, lake));
        }

        workspace.SetVanillaSkyIslands(anchors.ToArray());
        context.ReportProgress(1d, "Generating floating islands and sky lakes");
    }



    private void ApplyWebs(IWorldGenerationContext context, Workspace workspace)
    {
        var terrain = workspace.VanillaTerrainState ?? throw new InvalidOperationException("Webs require Terrain state.");
        var lines = workspace.VanillaLiquidLines ?? throw new InvalidOperationException("Webs require liquid lines.");
        var random = context.VanillaRandom ?? throw new InvalidOperationException("Webs require shared vanilla RNG.");
        var runner = new SmallTerrainRunner1458(workspace.TileStore, random, state.WorldSurface, lines, context.CancellationToken);
        int width = workspace.WidthTiles, height = workspace.HeightTiles;
        int count = (int)((double)(width * height) * .0006);
        ReadOnlySpan<WorldGenerationPoint> caves = workspace.VanillaMountainCaves;
        for (int i = 0; i < count; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            // Source consumes both samples even for retained mountain-cave locations.
            int x = random.Next(20, width - 20), y = random.Next((int)terrain.WorldSurfaceHigh, height - 20);
            if (i < caves.Length) { x = caves[i].X; y = caves[i].Y; }
            if (workspace.TileStore.Get(x,y).IsActive ||
                (y <= state.WorldSurface && workspace.TileStore.Get(x,y).Wall == 0)) continue;
            while (!workspace.TileStore.Get(x,y).IsActive && y > (int)terrain.WorldSurfaceLow) y--;
            y++;
            int direction = random.Next(2) == 0 ? -1 : 1;
            while (!workspace.TileStore.Get(x,y).IsActive && x > 10 && x < width - 10) x += direction;
            x -= direction;
            if (y > state.WorldSurface || workspace.TileStore.Get(x,y).Wall > 0)
                runner.Run(x, y, random.Next(4,11), random.Next(2,4), Cobweb, addTile: true,
                    speedX: direction, speedY: -1, overRide: false);
        }
        context.ReportProgress(1d, "Generating cave cobweb patches");
    }


    private void ApplyEvilBiome(IWorldGenerationContext context, Workspace workspace, RuntimeGrid grid)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        var bounds = EvilBiomePlacement1458.Scan(workspace.TileStore, state.WorldSurface, context.CancellationToken);
        var desert = workspace.VanillaUndergroundDesertRegion ??
            throw new InvalidOperationException("Evil-biome placement requires the generated desert bounds.");
        bool crimson = context.Request.Options.Evil == WorldGenerationEvil.Crimson;
        var crimsonCaves = crimson ? new CrimsonCaves1458(workspace.TileStore, context.VanillaRandom!,
            state.WorldSurface, context.CancellationToken) : null;
        var corruptionCaves = crimson ? null : new CorruptionCaves1458(workspace.TileStore, context.VanillaRandom!,
            state.WorldSurface, state.RockLayer, workspace.VanillaLiquidLines ??
                throw new InvalidOperationException("Corruption requires exact Early-pass liquid lines."), context.CancellationToken);

        for (int biome = 0; biome < grid.Width * 0.00045d; biome++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var region = EvilBiomePlacement1458.Select(grid.Width, bounds, bootstrap.DungeonLocation,
                bootstrap.DungeonSide, desert.X, desert.Right, crimson, context.VanillaRandom!, context.CancellationToken);
            int left = region.Left, right = region.Right;

            // Ordinary Crimson has one CrimStart per selected region, before surface conversion.
            crimsonCaves?.Start(region.Center, (int)state.WorldSurfaceLow - 10);

            if (crimson)
            {
                var surface = new EvilBiomeSurface1458(workspace.TileStore, context.VanillaRandom!, state.WorldSurfaceLow,
                    state.WorldSurface, context.CancellationToken);
                for (int x = left; x < right; x++) surface.ConvertJungleColumn(x,left,right,true);
                surface.ConvertRegion(left,right,true);
                new EvilAltarPlacement1458(workspace.TileStore, context.VanillaRandom!, state.WorldSurface,
                    state.RockLayer, context.CancellationToken).GenerateCrimsonRegion(left,right);
                continue;
            }

            corruptionCaves!.GenerateRegion(left, right, region.Center, state.WorldSurfaceLow);
        }

        crimsonCaves?.PlaceHearts();
        context.ReportProgress(1d, crimson
            ? "Generating crimson surface conversion and chasms"
            : "Generating corruption surface conversion and chasms");
    }

    private void ApplyLakes(IWorldGenerationContext context, Workspace workspace)
    {
        new SurfaceLakes1458(workspace.TileStore, context.VanillaRandom!, state.WorldSurface,
            state.WorldSurfaceLow, context.CancellationToken).Generate(workspace);
        context.ReportProgress(1d, "Generating ordinary surface lakes");
    }

    private static void ApplySlush(IWorldGenerationContext context, Workspace workspace)
    {
        SnowMaterialConversion1458.Apply(workspace, context.CancellationToken);
        context.ReportProgress(1d, "Converting stored materials inside the snow biome");
    }



    private static void CarveTunnel(
        RuntimeGrid grid,
        IRandom random,
        int startX,
        int startY,
        int steps,
        int radius,
        double downwardBias)
    {
        double x = startX;
        double y = startY;
        double vx = random.Next(-10, 11) * 0.08d;
        double vy = downwardBias + random.Next(-5, 6) * 0.04d;

        for (int step = 0; step < steps; step++)
        {
            ClearCircle(grid, (int)x, (int)y, radius);
            x = Math.Clamp(x + vx, radius + 1, grid.Width - radius - 2);
            y = Math.Clamp(y + vy, radius + 1, grid.Height - radius - 2);
            vx = Math.Clamp(vx + random.Next(-10, 11) * 0.02d, -1.1d, 1.1d);
            vy = Math.Clamp(vy + random.Next(-10, 11) * 0.02d, -0.4d, 1.3d);
        }
    }



    private static void FillEllipse(
        RuntimeGrid grid,
        int centerX,
        int centerY,
        int radiusX,
        int radiusY,
        ushort type,
        bool overwriteAir)
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
                tile.LiquidAmount = 0;
                tile.LiquidKind = WorldLiquidKind.Water;
            }
        }
    }

    private static void CarveEllipse(RuntimeGrid grid, int centerX, int centerY, int radiusX, int radiusY)
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
                if (grid.Contains(x, y))
                    ClearTile(ref grid.At(x, y));
            }
        }
    }


    private static void ConvertExposedSurface(
        RuntimeGrid grid,
        int minX,
        int maxX,
        int minY,
        int maxY,
        ushort from,
        ushort to,
        IRandom random,
        int chanceDivisor)
    {
        int left = Math.Max(1, minX);
        int right = Math.Min(grid.Width - 1, maxX);
        int top = Math.Max(1, minY);
        int bottom = Math.Min(grid.Height - 1, maxY);
        for (int x = left; x < right; x++)
        {
            for (int y = top; y < bottom; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (!tile.IsActive || tile.Type != from || !grid.HasOpenNeighbor(x, y))
                    continue;
                if (chanceDivisor > 1 && random.Next(chanceDivisor) != 0)
                    continue;
                tile.Type = to;
                tile.FrameX = -1;
                tile.FrameY = -1;
            }
        }
    }

    private static void PaintCircle(
        RuntimeGrid grid,
        int centerX,
        int centerY,
        int radius,
        ushort type,
        bool onlyReplaceNatural)
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (dx * dx + dy * dy > radius * radius)
                    continue;
                int x = centerX + dx;
                int y = centerY + dy;
                if (!grid.Contains(x, y))
                    continue;
                ref WorldTile tile = ref grid.At(x, y);
                if (!tile.IsActive)
                    continue;
                if (onlyReplaceNatural && !IsNaturalReplaceable(tile.Type))
                    continue;
                tile.Type = type;
                tile.FrameX = -1;
                tile.FrameY = -1;
            }
        }
    }

    private static void ClearCircle(RuntimeGrid grid, int centerX, int centerY, int radius)
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (dx * dx + dy * dy > radius * radius)
                    continue;
                int x = centerX + dx;
                int y = centerY + dy;
                if (grid.Contains(x, y))
                    ClearTile(ref grid.At(x, y));
            }
        }
    }

    private static bool IsNaturalReplaceable(ushort type) =>
        type is Dirt or Stone or Sand or Mud or Snow or Ice or HardenedSand or Sandstone or Ash;

    private VanillaWorldGenerationBootstrapState1458 RequireBootstrap() =>
        state.Bootstrap ?? throw new InvalidOperationException("Mid vanilla pass executed before bootstrap state initialization.");

    private static void SetType(ref WorldTile tile, ushort type)
    {
        tile.Type = type;
        tile.Flags |= WorldTileFlags.Active;
        tile.FrameX = -1;
        tile.FrameY = -1;
        tile.Shape = 0;
    }

    private static void ClearTile(ref WorldTile tile)
    {
        tile.Flags &= ~WorldTileFlags.Active;
        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
        tile.FrameX = -1;
        tile.FrameY = -1;
        tile.Shape = 0;
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

        public bool HasOpenNeighbor(int x, int y) =>
            !At(x - 1, y).IsActive ||
            !At(x + 1, y).IsActive ||
            !At(x, y - 1).IsActive ||
            !At(x, y + 1).IsActive;

        public bool HasSolidNeighbor(int x, int y) =>
            At(x - 1, y).IsActive ||
            At(x + 1, y).IsActive ||
            At(x, y - 1).IsActive ||
            At(x, y + 1).IsActive;
    }
}

/// <summary>
/// Leaves the compatibility-biome aggregate alive only as a temporary source for the ocean body. Interior biome
/// writes and underworld writes are acknowledged but discarded so source-backed Jungle/Desert/evil/Underworld state
/// cannot be painted over by the older compatibility approximation.
/// </summary>
internal sealed class OceanResidualCompatibilityBiomesPass1458 : IWorldGenerationPass
{
    private readonly IWorldGenerationPass inner;

    public OceanResidualCompatibilityBiomesPass1458(IWorldGenerationPass inner) =>
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public void Execute(IWorldGenerationContext context)
    {
        Workspace workspace = context.Workspace as Workspace ??
            throw new InvalidOperationException("Ocean residual requires Workspace.");
        VanillaWorldGenerationBootstrapState1458 bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Ocean residual requires Reset beach bounds.");
        var filtered = new OceanOnlyWorkspace(context.Workspace, bootstrap);
        inner.Execute(new ResidualContext(context, filtered));
    }

    private sealed class ResidualContext(IWorldGenerationContext parent, IWorldGenerationWorkspace workspace) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => parent.Request;
        public IWorldGenerationWorkspace Workspace => workspace;
        public IWorldGenerationMetadataWorkspace? Metadata => parent.Metadata;
        public IWorldGenerationRandom Random => parent.Random;
        public IWorldGenerationVanillaRandom? VanillaRandom => parent.VanillaRandom;
        public CancellationToken CancellationToken => parent.CancellationToken;
        public void ReportProgress(double fraction, string? message = null) =>
            parent.ReportProgress(fraction, "Applying compatibility ocean residual");
    }

    private sealed class OceanOnlyWorkspace(
        IWorldGenerationWorkspace inner,
        VanillaWorldGenerationBootstrapState1458 bootstrap) : IWorldGenerationWorkspace
    {
        private readonly int verticalLimit = (int)(inner.HeightTiles * 0.70d);
        private readonly int leftLimit = Math.Min(inner.WidthTiles, bootstrap.LeftBeachEnd + 96);
        private readonly int rightLimit = Math.Max(0, bootstrap.RightBeachStart - 96);

        public int WidthTiles => inner.WidthTiles;
        public int HeightTiles => inner.HeightTiles;

        public bool TryGetTile(int x, int y, out WorldGenerationTile tile) =>
            inner.TryGetTile(x, y, out tile);

        public bool TrySetTile(int x, int y, in WorldGenerationTile tile)
        {
            bool oceanBand = y < verticalLimit && (x < leftLimit || x >= rightLimit);
            if (!oceanBand)
                return true;
            return inner.TrySetTile(x, y, in tile);
        }
    }
}

/// <summary>
/// Keeps the aggregate compatibility Ores identity in the dependency graph after the source-shaped Shinies pass has
/// placed the Reset-selected ore tiers. The barrier intentionally consumes no RNG and performs no tile writes.
/// </summary>
internal sealed class SourceBackedOreCompatibilityBarrier1458 : IWorldGenerationPass
{
    public static SourceBackedOreCompatibilityBarrier1458 Instance { get; } = new();

    private SourceBackedOreCompatibilityBarrier1458()
    {
    }

    public void Execute(IWorldGenerationContext context) =>
        context.ReportProgress(1d, "Compatibility Ores replaced by source-shaped Shinies");
}
