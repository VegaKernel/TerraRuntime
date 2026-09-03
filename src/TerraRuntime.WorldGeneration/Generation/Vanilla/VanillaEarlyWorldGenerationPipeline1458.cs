using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration;

/// <summary>
/// Stage-one source-backed Terraria 1.4.5.8 pipeline. It keeps the already verified Reset/Terrain implementation,
/// expands the monolithic compatibility gap into the real early pass order, advances the shared UnifiedRandom stream
/// through source-shaped passes, and falls back to the compatibility pipeline only after the first Jungle pass.
/// </summary>
public sealed class SourceBackedVanillaWorldGenerationPipeline1458 : IWorldGenerationProvider
{
    private static readonly WorldGenerationPassId TerrainLayersId = new("terraria:1.4.5.8/TerrainLayers");
    private static readonly WorldGenerationPassId DunesId = new("terraria:1.4.5.8/Dunes");
    private static readonly WorldGenerationPassId OceanSandId = new("terraria:1.4.5.8/OceanSand");
    private static readonly WorldGenerationPassId SandPatchesId = new("terraria:1.4.5.8/SandPatches");
    private static readonly WorldGenerationPassId TunnelsId = new("terraria:1.4.5.8/Tunnels");
    private static readonly WorldGenerationPassId MountCavesId = new("terraria:1.4.5.8/MountCaves");
    private static readonly WorldGenerationPassId DirtWallBackgroundsId = new("terraria:1.4.5.8/DirtWallBackgrounds");
    private static readonly WorldGenerationPassId RocksInDirtId = new("terraria:1.4.5.8/RocksInDirt");
    private static readonly WorldGenerationPassId DirtInRocksId = new("terraria:1.4.5.8/DirtInRocks");
    private static readonly WorldGenerationPassId ClayId = new("terraria:1.4.5.8/Clay");
    private static readonly WorldGenerationPassId SmallHolesId = new("terraria:1.4.5.8/SmallHoles");
    private static readonly WorldGenerationPassId DirtLayerCavesId = new("terraria:1.4.5.8/DirtLayerCaves");
    private static readonly WorldGenerationPassId RockLayerCavesId = new("terraria:1.4.5.8/RockLayerCaves");
    private static readonly WorldGenerationPassId SurfaceCavesId = new("terraria:1.4.5.8/SurfaceCaves");
    private static readonly WorldGenerationPassId WavyCavesId = new("terraria:1.4.5.8/WavyCaves");
    private static readonly WorldGenerationPassId IceBiomeId = new("terraria:1.4.5.8/GenerateIceBiome");
    private static readonly WorldGenerationPassId GrassId = new("terraria:1.4.5.8/Grass");
    private static readonly WorldGenerationPassId JungleId = new("terraria:1.4.5.8/Jungle");
    private static readonly WorldGenerationPassId CompatibilityBiomesId = new("terraria:1.4.5.8/Biomes");

    private readonly SourceBackedVanillaWorldGenerationProvider1458 baseline = new();

    public WorldGeneratorId Id => baseline.Id;

    public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var capture = new CapturePlanBuilder();
        baseline.BuildPlan(in request, capture);

        WorldGenerationRequest requestCopy = request;
        VanillaWorldSeedProfile1458 profile = VanillaWorldSeedResolver1458.Resolve(in requestCopy);
        bool pureRemix = profile.Special == VanillaSpecialWorldSeed1458.Remix &&
            profile.Secret == VanillaSecretWorldSeed1458.None;
        if ((!profile.IsDefault && !pureRemix) ||
            !VanillaTerrainPass1458.IsCanonicalWorldSize(request.WidthTiles, request.HeightTiles))
        {
            capture.Replay(builder);
            return;
        }

        CapturedPass reset = capture.Require(SourceBackedVanillaWorldGenerationProvider1458.ResetPassId);
        CapturedPass terrain = capture.Require(SourceBackedVanillaWorldGenerationProvider1458.TerrainPassId);
        CapturedPass biomes = capture.Require(CompatibilityBiomesId);
        CapturedPass metadata = capture.Require(SourceBackedVanillaWorldGenerationProvider1458.MetadataPassId);
        var state = new VanillaEarlyWorldGenerationState1458();

        builder.Add(reset.Descriptor, reset.Pass);
        builder.Add(terrain.Descriptor, terrain.Pass);
        Add(builder, TerrainLayersId, WorldGenerationRngMode.VanillaSharedRng, SourceBackedVanillaWorldGenerationProvider1458.TerrainPassId,
            new VanillaEarlyWorldGenerationPass1458(VanillaEarlyWorldGenerationStage1458.TerrainLayers, state, metadata.Pass));
        Add(builder, DunesId, WorldGenerationRngMode.VanillaSharedRng, TerrainLayersId,
            new VanillaEarlyWorldGenerationPass1458(VanillaEarlyWorldGenerationStage1458.Dunes, state));

        if (pureRemix)
        {
            // WorldGen.AddPasses has no Remix-specific dune placement branch. The only difference in this pass is
            // SetupDungeonGenVarVariables, which consumes the same roll but pins the dungeon palette from evil type.
            // Stop immediately afterwards: Ocean Sand and every later early pass still need separate Remix evidence.
            builder.Add(CloneDescriptor(biomes.Descriptor, biomes.Descriptor.RngMode, [DunesId]), biomes.Pass);
            ReplayCompatibilityTail(capture, builder);
            builder.Add(metadata.Descriptor, metadata.Pass);
            return;
        }

        Add(builder, OceanSandId, WorldGenerationRngMode.VanillaSharedRng, DunesId,
            new VanillaEarlyWorldGenerationPass1458(VanillaEarlyWorldGenerationStage1458.OceanSand, state));
        Add(builder, SandPatchesId, WorldGenerationRngMode.VanillaSharedRng, OceanSandId,
            new VanillaEarlyWorldGenerationPass1458(VanillaEarlyWorldGenerationStage1458.SandPatches, state));
        Add(builder, TunnelsId, WorldGenerationRngMode.VanillaSharedRng, SandPatchesId,
            new VanillaEarlyWorldGenerationPass1458(VanillaEarlyWorldGenerationStage1458.Tunnels, state));
        Add(builder, MountCavesId, WorldGenerationRngMode.VanillaSharedRng, TunnelsId,
            new VanillaEarlyWorldGenerationPass1458(VanillaEarlyWorldGenerationStage1458.MountCaves, state));

        Add(builder, DirtWallBackgroundsId, WorldGenerationRngMode.VanillaSharedRng, MountCavesId,
            new VanillaEarlyWorldGenerationPass1458(VanillaEarlyWorldGenerationStage1458.DirtWallBackgrounds, state));
        Add(builder, RocksInDirtId, WorldGenerationRngMode.VanillaSharedRng, DirtWallBackgroundsId,
            new VanillaEarlyWorldGenerationPass1458(VanillaEarlyWorldGenerationStage1458.RocksInDirt, state));
        Add(builder, DirtInRocksId, WorldGenerationRngMode.VanillaSharedRng, RocksInDirtId,
            new VanillaEarlyWorldGenerationPass1458(VanillaEarlyWorldGenerationStage1458.DirtInRocks, state));
        Add(builder, ClayId, WorldGenerationRngMode.VanillaSharedRng, DirtInRocksId,
            new VanillaEarlyWorldGenerationPass1458(VanillaEarlyWorldGenerationStage1458.Clay, state));
        Add(builder, SmallHolesId, WorldGenerationRngMode.VanillaSharedRng, ClayId,
            new VanillaEarlyWorldGenerationPass1458(VanillaEarlyWorldGenerationStage1458.SmallHoles, state));
        Add(builder, DirtLayerCavesId, WorldGenerationRngMode.VanillaSharedRng, SmallHolesId,
            new VanillaEarlyWorldGenerationPass1458(VanillaEarlyWorldGenerationStage1458.DirtLayerCaves, state));
        Add(builder, RockLayerCavesId, WorldGenerationRngMode.VanillaSharedRng, DirtLayerCavesId,
            new VanillaEarlyWorldGenerationPass1458(VanillaEarlyWorldGenerationStage1458.RockLayerCaves, state));
        Add(builder, SurfaceCavesId, WorldGenerationRngMode.VanillaSharedRng, RockLayerCavesId,
            new VanillaEarlyWorldGenerationPass1458(VanillaEarlyWorldGenerationStage1458.SurfaceCaves, state));
        Add(builder, WavyCavesId, WorldGenerationRngMode.VanillaSharedRng, SurfaceCavesId,
            new VanillaEarlyWorldGenerationPass1458(VanillaEarlyWorldGenerationStage1458.WavyCaves, state));
        Add(builder, IceBiomeId, WorldGenerationRngMode.VanillaSharedRng, WavyCavesId,
            new VanillaEarlyWorldGenerationPass1458(VanillaEarlyWorldGenerationStage1458.IceBiome, state));
        Add(builder, GrassId, WorldGenerationRngMode.VanillaSharedRng, IceBiomeId,
            new VanillaEarlyWorldGenerationPass1458(VanillaEarlyWorldGenerationStage1458.Grass, state));
        Add(builder, JungleId, WorldGenerationRngMode.VanillaSharedRng, GrassId,
            new VanillaEarlyWorldGenerationPass1458(VanillaEarlyWorldGenerationStage1458.Jungle, state));

        builder.Add(
            CloneDescriptor(biomes.Descriptor, WorldGenerationRngMode.IsolatedDeterministic, [JungleId]),
            new VanillaResidualCompatibilityBiomesPass1458(biomes.Pass));

        ReplayCompatibilityTail(capture, builder);

        builder.Add(metadata.Descriptor, metadata.Pass);
    }

    private static void ReplayCompatibilityTail(CapturePlanBuilder capture, IWorldGenerationPlanBuilder builder)
    {
        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id == SourceBackedVanillaWorldGenerationProvider1458.ResetPassId ||
                entry.Descriptor.Id == SourceBackedVanillaWorldGenerationProvider1458.TerrainPassId ||
                entry.Descriptor.Id == CompatibilityBiomesId ||
                entry.Descriptor.Id == SourceBackedVanillaWorldGenerationProvider1458.MetadataPassId)
            {
                continue;
            }

            builder.Add(entry.Descriptor, entry.Pass);
        }
    }

    private static void Add(
        IWorldGenerationPlanBuilder builder,
        WorldGenerationPassId id,
        WorldGenerationRngMode rngMode,
        WorldGenerationPassId after,
        IWorldGenerationPass pass) =>
        builder.Add(new WorldGenerationPassDescriptor(id, rngMode, requiredAfter: [after]), pass);

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
            throw new InvalidOperationException($"Baseline vanilla plan did not expose required pass '{id}'.");
        }

        public void Replay(IWorldGenerationPlanBuilder builder)
        {
            foreach (CapturedPass entry in entries)
                builder.Add(entry.Descriptor, entry.Pass);
        }
    }
}

internal enum VanillaEarlyWorldGenerationStage1458 : byte
{
    TerrainLayers,
    Dunes,
    OceanSand,
    SandPatches,
    Tunnels,
    MountCaves,
    DirtWallBackgrounds,
    RocksInDirt,
    DirtInRocks,
    Clay,
    SmallHoles,
    DirtLayerCaves,
    RockLayerCaves,
    SurfaceCaves,
    WavyCaves,
    IceBiome,
    Grass,
    Jungle
}

internal sealed class VanillaEarlyWorldGenerationState1458
{
    public VanillaWorldGenerationBootstrapState1458? Bootstrap { get; set; }
    public double MainWorldSurface { get; set; }
    public double MainRockLayer { get; set; }
    public double WorldSurfaceLow { get; set; }
    public double WorldSurfaceHigh { get; set; }
    public double RockLayerLow { get; set; }
    public double RockLayerHigh { get; set; }
    public int WaterLine { get; set; }
    public int LavaLine { get; set; }
    public int SnowTop { get; set; }
    public int SnowBottom { get; set; }
    public int[] SnowMinX { get; set; } = [];
    public int[] SnowMaxX { get; set; } = [];
    public int JungleX { get; set; }
    public bool MudWall { get; set; }
    public VanillaDungeonPalette1458 DungeonPalette { get; set; }
    public List<int> MountainCaveX { get; } = [];
}

internal readonly record struct VanillaDungeonPalette1458(
    int Color,
    ushort BrickTileType,
    ushort BrickWallType,
    ushort CrackedBrickTileType,
    ushort WindowGlassWallType,
    ushort WindowClosedGlassWallType,
    ushort WindowEdgeWallType,
    int WindowPlatformItemType);

internal sealed class VanillaEarlyWorldGenerationPass1458 : IWorldGenerationPass
{
    private const ushort Dirt = 0;
    private const ushort Stone = 1;
    private const ushort Grass = 2;
    private const ushort Clay = 40;
    private const ushort Sand = 53;
    private const ushort Mud = 59;
    private const ushort JungleGrass = 60;
    private const ushort Snow = 147;
    private const ushort Ice = 161;
    private const ushort DirtWall = 2;
    private const ushort MudWall = 15;
    private const ushort JungleWall = 64;
    private static readonly double[] ExtraStepThresholds =
        [50d, 100d, 150d, 200d, 250d, 300d, 400d, 500d, 600d, 700d, 800d, 900d];

    private readonly VanillaEarlyWorldGenerationStage1458 stage;
    private readonly VanillaEarlyWorldGenerationState1458 state;
    private readonly IWorldGenerationPass? bootstrapPublisher;

    public VanillaEarlyWorldGenerationPass1458(
        VanillaEarlyWorldGenerationStage1458 stage,
        VanillaEarlyWorldGenerationState1458 state,
        IWorldGenerationPass? bootstrapPublisher = null)
    {
        this.stage = stage;
        this.state = state;
        this.bootstrapPublisher = bootstrapPublisher;
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RuntimeWorldGenerationWorkspace workspace = context.Workspace as RuntimeWorldGenerationWorkspace ??
            throw new InvalidOperationException("Source-backed early Terraria generation requires RuntimeWorldGenerationWorkspace.");
        var grid = new RuntimeGrid(workspace);

        switch (stage)
        {
            case VanillaEarlyWorldGenerationStage1458.TerrainLayers:
                CaptureTerrainState(context, workspace, grid);
                break;
            case VanillaEarlyWorldGenerationStage1458.Dunes:
                ApplyDunes(context, workspace, grid, RequireVanilla(context));
                break;
            case VanillaEarlyWorldGenerationStage1458.OceanSand:
                ApplyOceanSand(context, workspace, grid, RequireVanilla(context));
                break;
            case VanillaEarlyWorldGenerationStage1458.SandPatches:
                ApplySandPatches(context, grid, RequireVanilla(context));
                break;
            case VanillaEarlyWorldGenerationStage1458.Tunnels:
                ApplyTunnels(context, grid, RequireVanilla(context));
                break;
            case VanillaEarlyWorldGenerationStage1458.MountCaves:
                ApplyMountCaves(context, grid, RequireVanilla(context));
                break;
            case VanillaEarlyWorldGenerationStage1458.DirtWallBackgrounds:
                ApplyDirtWallBackgrounds(context, grid, RequireVanilla(context));
                break;
            case VanillaEarlyWorldGenerationStage1458.RocksInDirt:
                ApplyRocksInDirt(context, grid, RequireVanilla(context));
                break;
            case VanillaEarlyWorldGenerationStage1458.DirtInRocks:
                ApplyDirtInRocks(context, grid, RequireVanilla(context));
                break;
            case VanillaEarlyWorldGenerationStage1458.Clay:
                ApplyClay(context, grid, RequireVanilla(context));
                break;
            case VanillaEarlyWorldGenerationStage1458.SmallHoles:
                ApplySmallHoles(context, grid, RequireVanilla(context));
                break;
            case VanillaEarlyWorldGenerationStage1458.DirtLayerCaves:
                ApplyDirtLayerCaves(context, grid, RequireVanilla(context));
                break;
            case VanillaEarlyWorldGenerationStage1458.RockLayerCaves:
                ApplyRockLayerCaves(context, grid, RequireVanilla(context));
                break;
            case VanillaEarlyWorldGenerationStage1458.SurfaceCaves:
                ApplySurfaceCaves(context, grid, RequireVanilla(context));
                break;
            case VanillaEarlyWorldGenerationStage1458.WavyCaves:
                context.ReportProgress(1d, "Skipping ordinary-world Wavy Caves secret-seed branch");
                break;
            case VanillaEarlyWorldGenerationStage1458.IceBiome:
                ApplyIceBiome(context, grid, RequireVanilla(context));
                break;
            case VanillaEarlyWorldGenerationStage1458.Grass:
                ApplyGrass(context, grid, RequireVanilla(context));
                break;
            case VanillaEarlyWorldGenerationStage1458.Jungle:
                ApplyJungle(context, grid, RequireVanilla(context));
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void CaptureTerrainState(IWorldGenerationContext context, RuntimeWorldGenerationWorkspace workspace, RuntimeGrid grid)
    {
        if (bootstrapPublisher is null)
            throw new InvalidOperationException("Terrain layer bridge was created without the baseline metadata parity pass.");

        bootstrapPublisher.Execute(new ChildContext(
            context,
            metadata: new MetadataSink(),
            vanillaRandom: new CompatibilityVanillaRandom(unchecked((int)context.Request.Seed)),
            suppressProgress: true));

        state.Bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Reset bootstrap state was not published by the baseline parity pass.");
        if (context.Metadata is null || !context.Metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Source-backed Terrain did not publish world layers.");

        VanillaTerrainGenerationState1458 terrain = workspace.VanillaTerrainState ??
            throw new InvalidOperationException("Source-backed Terrain did not publish its exact end state.");
        state.MainWorldSurface = terrain.WorldSurface;
        state.MainRockLayer = terrain.RockLayer;
        state.WorldSurfaceLow = terrain.WorldSurfaceLow;
        state.WorldSurfaceHigh = terrain.WorldSurfaceHigh;
        state.RockLayerLow = terrain.RockLayerLow;
        state.RockLayerHigh = terrain.RockLayerHigh;

        IRandom random = RequireVanilla(context);
        WorldGenerationRequest request = context.Request;
        VanillaWorldSeedProfile1458 profile = VanillaWorldSeedResolver1458.Resolve(in request);
        (state.WaterLine, state.LavaLine) = ResolveLiquidLines(
            random,
            state.MainWorldSurface,
            state.MainRockLayer,
            terrain.CurrentRockLayer,
            grid.Height,
            profile.Special == VanillaSpecialWorldSeed1458.Remix);
        state.SnowMinX = new int[grid.Height];
        state.SnowMaxX = new int[grid.Height];
        context.ReportProgress(1d, "Finalizing Terraria Terrain layer state");
    }

    internal static (int WaterLine, int LavaLine) ResolveLiquidLines(
        IRandom random,
        double worldSurface,
        double rockLayer,
        double currentRockLayer,
        int height,
        bool isRemix)
    {
        ArgumentNullException.ThrowIfNull(random);
        int waterLine = (int)(rockLayer + height) / 2 + random.Next(-100, 20);
        int ordinaryLavaLine = waterLine + random.Next(50, 80);
        int lavaLine = isRemix
            ? (int)(worldSurface * 4d + currentRockLayer) / 5
            : ordinaryLavaLine;
        return (waterLine, lavaLine);
    }

    private void ApplyDunes(
        IWorldGenerationContext context,
        RuntimeWorldGenerationWorkspace workspace,
        RuntimeGrid grid,
        IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 b = RequireBootstrap();
        workspace.ResetVanillaPyramidCandidates();
        WorldGenerationRequest request = context.Request;
        VanillaWorldSeedProfile1458 profile = VanillaWorldSeedResolver1458.Resolve(in request);
        VanillaDungeonSetupProfile1458 dungeonSetup = SetupDungeonProfile(
            random,
            profile.Special == VanillaSpecialWorldSeed1458.Remix,
            b.EffectiveCrimson);
        state.DungeonPalette = dungeonSetup.Palette;
        workspace.SetVanillaDungeonSetupProfile(dungeonSetup);

        (int minimum, int maximum) = GetDuneCountRange(grid.Width);
        int count = random.Next(minimum, maximum + 1);
        for (int i = 0; i < count; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int originX = PickDuneOrigin(grid.Width, b, random);
            PlaceDunes(grid, random, originX);
            if (random.NextDouble() <= 0.8d)
            {
                int candidateX = random.Next(originX - 200, originX + 200);
                int candidateSurface = grid.FindFirstActiveY(candidateX, 0, grid.Height);
                if (candidateSurface < grid.Height)
                    workspace.AddVanillaPyramidCandidate(candidateX, candidateSurface + 20);
            }
            context.ReportProgress((i + 1d) / count, "Generating Terraria dunes");
        }
    }

    internal static VanillaDungeonPalette1458 SetupDungeonPalette(IRandom random, bool isRemix, bool crimson)
    {
        ArgumentNullException.ThrowIfNull(random);
        int color = random.Next(3);
        if (isRemix)
            color = crimson ? 2 : 0;

        return color switch
        {
            0 => new VanillaDungeonPalette1458(0, 41, 7, 481, 91, 96, 8, 1386),
            1 => new VanillaDungeonPalette1458(1, 43, 8, 482, 92, 94, 9, 1385),
            _ => new VanillaDungeonPalette1458(2, 44, 9, 483, 90, 98, 7, 1384)
        };
    }

    internal static VanillaDungeonSetupProfile1458 SetupDungeonProfile(
        IRandom random,
        bool isRemix,
        bool crimson)
    {
        VanillaDungeonPalette1458 palette = SetupDungeonPalette(random, isRemix, crimson);
        VanillaDungeonEntranceKind1458 entrance = VanillaDungeonEntranceKind1458.Legacy;
        if (random.Next(3) == 0)
            entrance = VanillaDungeonEntranceKind1458.Dome;
        if (random.Next(3) == 0)
            entrance = VanillaDungeonEntranceKind1458.Tower;

        return new VanillaDungeonSetupProfile1458(palette, entrance, random.Next());
    }

    internal static (int Minimum, int Maximum) GetDuneCountRange(int width)
    {
        if (width is not (4200 or 6400 or 8400))
            throw new ArgumentOutOfRangeException(nameof(width));
        double scale = width / 4200d;
        return ((int)scale, (int)(2d * scale));
    }

    private static int PickDuneOrigin(int width, VanillaWorldGenerationBootstrapState1458 b, IRandom random)
    {
        double scale = width / 4200d;
        int attempts = 0;
        while (true)
        {
            int x = random.Next(500, width - 500);
            _ = random.Next(0, width == 4200 ? 1200 : width == 6400 ? 1800 : 2400);
            bool jungle = Math.Abs(x - b.JungleOriginX) < (int)(600d * scale);
            bool spawn = Math.Abs(x - width / 2) < 300;
            bool snow = x > b.SnowOriginLeft - 300 && x < b.SnowOriginRight + 300;
            attempts++;
            if (attempts >= width) jungle = false;
            if (attempts >= width * 2) snow = false;
            if (!(jungle || spawn || snow)) return x;
        }
    }

    private static void PlaceDunes(RuntimeGrid grid, IRandom random, int originX)
    {
        _ = random.Next(60, 100);
        _ = random.Next(60, 100);
        int width1 = random.Next(150, 251);
        int width2 = random.Next(150, 251);
        DuneDescription first = DuneDescription.Create(grid, random, originX - width1 / 2 + 30, width1);
        DuneDescription second = DuneDescription.Create(grid, random, originX + width2 / 2 - 30, width2);
        PlaceSingleDune(grid, random, first);
        PlaceSingleDune(grid, random, second);
    }

    private static void PlaceSingleDune(RuntimeGrid grid, IRandom random, DuneDescription d)
    {
        int hillCount = random.Next(3) + 8;
        for (int i = 0; i < hillCount - 1; i++)
        {
            int width = (int)(2d / hillCount * d.Width);
            int center = (int)((double)i / hillCount * d.Width + d.Left) + width * 2 / 5 + random.Next(-5, 6);
            double progress = (double)i / (hillCount - 2);
            double scale = 1d - Math.Abs(progress - 0.5d) * 2d;
            PlaceDuneHill(grid, random, center - width / 2, center + width / 2, scale * 0.3d + 0.2d, d);
        }
        int central = random.Next(2) + 1;
        for (int i = 0; i < central; i++)
        {
            int width = d.Width / 2;
            int center = d.CenterX + random.Next(-10, 11);
            PlaceDuneHill(grid, random, center - width / 2, center + width / 2, 0.8d, d);
        }
    }

    private static void PlaceDuneHill(RuntimeGrid grid, IRandom random, int startX, int endX, double scale, DuneDescription d)
    {
        int startY = d.SurfaceAt(startX);
        int endY = d.SurfaceAt(endX);
        int middleX = (startX + endX) / 2;
        int middleY = (startY + endY) / 2 - (int)(35d * scale);
        int maxOffset = Math.Max(1, (endX - middleX) / 4);
        int minOffset = Math.Max(0, (endX - middleX) / 16);
        int offset = random.Next(minOffset, maxOffset + 1);
        middleX += d.WindRight ? offset : -offset;
        int positive = (int)(scale * 12d);
        int negative = positive / -2;
        if (d.WindRight)
        {
            PlaceDuneCurve(grid, startX, startY, middleX, middleY, negative, d);
            PlaceDuneCurve(grid, middleX, middleY, endX, endY, positive, d);
        }
        else
        {
            PlaceDuneCurve(grid, startX, startY, middleX, middleY, positive, d);
            PlaceDuneCurve(grid, middleX, middleY, endX, endY, negative, d);
        }
    }

    private static void PlaceDuneCurve(RuntimeGrid grid, int startX, int startY, int endX, int endY, int anchorOffsetY, DuneDescription d)
    {
        double anchorX = (startX + endX) / 2d;
        double anchorY = (startY + endY) / 2d + anchorOffsetY;
        double step = 0.5d / Math.Max(1, endX - startX);
        int lastX = -1, lastY = -1;
        for (double t = 0d; t <= 1d; t += step)
        {
            double ax = startX + (anchorX - startX) * t;
            double ay = startY + (anchorY - startY) * t;
            double bx = anchorX + (endX - anchorX) * t;
            double by = anchorY + (endY - anchorY) * t;
            int x = (int)(ax + (bx - ax) * t);
            int y = (int)(ay + (by - ay) * t);
            if (x == lastX && y == lastY) continue;
            lastX = x; lastY = y;
            int widthFromCenter = Math.Max(0, d.Width / 2 - Math.Abs(x - d.CenterX));
            int bottom = d.SurfaceAt(x) + (int)(Math.Sqrt(widthFromCenter) * 3d);
            for (int yy = y - 10; yy < y; yy++)
            {
                if (!grid.Contains(x, yy)) continue;
                ref WorldTile tile = ref grid.At(x, yy);
                if (tile.IsActive && tile.Type != Sand) ClearTile(ref tile);
            }
            for (int yy = y; yy < bottom && grid.Contains(x, yy); yy++)
            {
                ref WorldTile tile = ref grid.At(x, yy);
                ClearTile(ref tile);
                SetType(ref tile, Sand, active: true);
            }
        }
    }

    private void ApplyOceanSand(
        IWorldGenerationContext context,
        RuntimeWorldGenerationWorkspace workspace,
        RuntimeGrid grid,
        IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 b = RequireBootstrap();
        double widthScale = grid.Width / 4200d;
        for (int i = 0; i < 3; i++)
        {
            int center = random.Next(grid.Width);
            while (center > grid.Width * 0.4d && center < grid.Width * 0.6d) center = random.Next(grid.Width);
            int span = random.Next(35, 90);
            if (i == 1) span += (int)(random.Next(20, 40) * widthScale);
            if (random.Next(3) == 0) span *= 2;
            if (i == 1) span *= 2;
            int left = Math.Max(0, center - span);
            span = random.Next(35, 90);
            if (random.Next(3) == 0) span *= 2;
            if (i == 1) span *= 2;
            int right = Math.Min(grid.Width, center + span);
            if (i == 0) { left = 0; right = b.LeftBeachEnd; }
            else if (i == 2) { left = b.RightBeachStart; right = grid.Width; }
            else continue;

            int sandDepth = random.Next(50, 100);
            for (int x = left; x < right; x++)
            {
                if (random.Next(2) == 0)
                    sandDepth = Math.Clamp(sandDepth + random.Next(-1, 2), 50, 200);
                int maxScan = Math.Min(grid.Height, (int)((state.MainWorldSurface + state.MainRockLayer) / 2d));
                int y = grid.FindFirstActiveY(x, 0, maxScan);
                if (y >= maxScan) continue;
                if (x == (left + right) / 2 && random.Next(6) == 0)
                    workspace.AddVanillaPyramidCandidate(x, y);
                int depth = Math.Min(sandDepth, Math.Min(x - left, right - x)) + random.Next(5);
                for (int yy = y; yy < y + depth && yy < grid.Height; yy++)
                {
                    if (x > left + random.Next(5) && x < right - random.Next(5))
                        grid.At(x, yy).Type = Sand;
                }
            }
            context.ReportProgress((i + 1d) / 3d, "Generating Terraria ocean sand");
        }
    }

    private void ApplySandPatches(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int count = (int)(grid.Width * 0.013d);
        for (int i = 0; i < count; i++)
        {
            int x = random.Next(0, grid.Width);
            int y = random.Next((int)state.MainWorldSurface, (int)state.MainRockLayer);
            while (x > grid.Width * 0.46d && x < grid.Width * 0.54d && y < state.MainWorldSurface + 150d)
            {
                x = random.Next(0, grid.Width);
                y = random.Next((int)state.MainWorldSurface, (int)state.MainRockLayer);
            }
            RunSandPatch(grid, random, x, y, random.Next(15, 70), random.Next(20, 130));
            if ((i & 7) == 0) context.CancellationToken.ThrowIfCancellationRequested();
        }
        context.ReportProgress(1d, "Generating Terraria sand patches");
    }

    private void ApplyTunnels(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int count = (int)(grid.Width * 0.0015d);
        for (int i = 0; i < count; i++)
        {
            int[] xs = new int[10];
            int[] ys = new int[10];
            int x = random.Next(450, grid.Width - 450);
            while (x > grid.Width * 0.4d && x < grid.Width * 0.6d) x = random.Next(450, grid.Width - 450);
            int y = 0;
            bool sandTouched;
            do
            {
                sandTouched = false;
                for (int k = 0; k < 10; k++)
                {
                    x %= grid.Width;
                    y = grid.FindFirstActiveY(x, y, grid.Height);
                    if (y >= grid.Height) y = grid.Height - 1;
                    if (grid.At(x, y).Type == Sand) sandTouched = true;
                    xs[k] = x;
                    ys[k] = y - random.Next(11, 16);
                    x += random.Next(5, 11);
                }
            } while (sandTouched);

            for (int k = 0; k < 10; k++)
            {
                RunTileRunner(grid, random, xs[k], ys[k], random.Next(5, 8), random.Next(6, 9), Dirt, true, -2d, -0.3d);
                RunTileRunner(grid, random, xs[k], ys[k], random.Next(5, 8), random.Next(6, 9), Dirt, true, 2d, -0.3d);
            }
            context.ReportProgress((i + 1d) / Math.Max(1, count), "Generating Terraria tunnels");
        }
    }

    private void ApplyMountCaves(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int count = (int)(grid.Width * 0.001d);
        for (int i = 0; i < count; i++)
        {
            int retries = 0;
            int x = random.Next((int)(grid.Width * 0.25d), (int)(grid.Width * 0.75d));
            while (true)
            {
                while (x > grid.Width / 2 - 90 && x < grid.Width / 2 + 90)
                    x = random.Next((int)(grid.Width * 0.25d), (int)(grid.Width * 0.75d));
                bool close = state.MountainCaveX.Any(existing => Math.Abs(x - existing) < 100);
                if (!close) break;
                retries++;
                if (retries >= grid.Width / 5) break;
            }
            if (retries >= grid.Width / 5) continue;

            int surface = grid.FindFirstActiveY(x, 0, Math.Min(grid.Height, (int)state.MainWorldSurface));
            if (surface >= grid.Height) continue;
            bool blocked = false;
            for (int sx = x - 50; sx < x + 50 && !blocked; sx++)
            for (int sy = surface - 25; sy < surface + 25; sy++)
            {
                if (!grid.Contains(sx, sy)) continue;
                ref WorldTile tile = ref grid.At(sx, sy);
                if (tile.IsActive && tile.Type is Sand or 45 or 397) { blocked = true; break; }
            }
            if (!blocked)
            {
                Mountinater(grid, random, x, surface);
                state.MountainCaveX.Add(x);
            }
            context.ReportProgress((i + 1d) / Math.Max(1, count), "Generating Terraria mount caves");
        }
    }

    private static void Mountinater(RuntimeGrid grid, IRandom random, int x0, int y0)
    {
        double strength = random.Next(80, 120);
        double steps = random.Next(40, 55);
        double x = x0;
        double y = y0 + steps / 2d;
        double vx = random.Next(-10, 11) * 0.1d;
        double vy = random.Next(-20, -10) * 0.1d;
        while (strength > 0d && steps > 0d)
        {
            strength -= random.Next(4);
            steps--;
            double radius = strength * random.Next(80, 120) * 0.01d;
            int left = Math.Max(0, (int)(x - strength * 0.5d));
            int right = Math.Min(grid.Width, (int)(x + strength * 0.5d));
            int top = Math.Max(0, (int)(y - strength * 0.5d));
            int bottom = Math.Min(grid.Height, (int)(y + strength * 0.5d));
            for (int tx = left; tx < right; tx++)
            for (int ty = top; ty < bottom; ty++)
            {
                double dx = Math.Abs(tx - x), dy = Math.Abs(ty - y);
                ref WorldTile tile = ref grid.At(tx, ty);
                if (Math.Sqrt(dx * dx + dy * dy) < radius * 0.4d && !tile.IsActive)
                    SetType(ref tile, Dirt, true);
            }
            x += vx; y += vy;
            vx = Math.Clamp(vx + random.Next(-10, 11) * 0.05d, -0.5d, 0.5d);
            vy = Math.Clamp(vy + random.Next(-10, 11) * 0.05d, -1.5d, -0.5d);
        }
    }

    private static void ApplyDirtWallBackgrounds(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int drift = 0;
        for (int x = 1; x < grid.Width - 1; x++)
        {
            drift = Math.Clamp(drift + random.Next(-1, 2), -6, 6);
            int top = grid.FindFirstActiveY(x, 0, grid.Height);
            int bottom = Math.Min(grid.Height - 210, top + 80 + Math.Abs(drift) * 4);
            for (int y = top + 2; y < bottom; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive && tile.Wall == 0 && tile.Type is Dirt or Stone)
                    tile.Wall = DirtWall;
            }
        }
        context.ReportProgress(1d, "Generating Terraria dirt wall backgrounds");
    }

    private void ApplyRocksInDirt(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int count = (int)(grid.Width * grid.Height * 0.00015d);
        for (int i = 0; i < count; i++)
            RunTileRunner(grid, random, random.Next(grid.Width), random.Next(0, (int)state.WorldSurfaceLow + 1), random.Next(4, 15), random.Next(5, 40), Stone);
        count = (int)(grid.Width * grid.Height * 0.0002d);
        for (int i = 0; i < count; i++)
        {
            int x = random.Next(grid.Width);
            int y = random.Next((int)state.WorldSurfaceLow, (int)state.WorldSurfaceHigh + 1);
            if (grid.Contains(x, y - 10) && !grid.At(x, y - 10).IsActive)
                y = random.Next((int)state.WorldSurfaceLow, (int)state.WorldSurfaceHigh + 1);
            RunTileRunner(grid, random, x, y, random.Next(4, 10), random.Next(5, 30), Stone);
        }
        count = (int)(grid.Width * grid.Height * 0.0045d);
        for (int i = 0; i < count; i++)
            RunTileRunner(grid, random, random.Next(grid.Width), random.Next((int)state.WorldSurfaceHigh, (int)state.RockLayerHigh + 1), random.Next(2, 7), random.Next(2, 23), Stone);
        context.ReportProgress(1d, "Generating Terraria rocks in dirt");
    }

    private void ApplyDirtInRocks(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int count = (int)(grid.Width * grid.Height * 0.005d);
        for (int i = 0; i < count; i++)
            RunTileRunner(grid, random, random.Next(grid.Width), random.Next((int)state.RockLayerLow, grid.Height), random.Next(2, 6), random.Next(2, 40), Dirt);
        context.ReportProgress(1d, "Generating Terraria dirt in rocks");
    }

    private void ApplyClay(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        PlaceMaterialRuns(grid, random, Clay, (int)(grid.Width * grid.Height * 0.00002d), 0, (int)state.WorldSurfaceLow, 4, 14, 10, 50);
        PlaceMaterialRuns(grid, random, Clay, (int)(grid.Width * grid.Height * 0.00005d), (int)state.WorldSurfaceLow, (int)state.WorldSurfaceHigh + 1, 8, 14, 15, 45);
        PlaceMaterialRuns(grid, random, Clay, (int)(grid.Width * grid.Height * 0.00002d), (int)state.WorldSurfaceHigh, (int)state.RockLayerHigh + 1, 8, 15, 5, 50);
        for (int x = 5; x < grid.Width - 5; x++)
        {
            int y = grid.FindFirstActiveY(x, 1, Math.Min(grid.Height, (int)state.MainWorldSurface));
            if (y >= grid.Height) continue;
            for (int yy = y; yy < y + 5 && yy < grid.Height; yy++)
                if (grid.At(x, yy).Type == Clay) grid.At(x, yy).Type = Dirt;
        }
        context.ReportProgress(1d, "Generating Terraria clay");
    }

    private void PlaceMaterialRuns(RuntimeGrid grid, IRandom random, ushort type, int count, int minY, int maxY, int strengthMin, int strengthMax, int stepsMin, int stepsMax)
    {
        maxY = Math.Clamp(maxY, minY + 1, grid.Height);
        for (int i = 0; i < count; i++)
            RunTileRunner(grid, random, random.Next(grid.Width), random.Next(minY, maxY), random.Next(strengthMin, strengthMax), random.Next(stepsMin, stepsMax), type);
    }

    private void ApplySmallHoles(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int beachAvoidance = 340;
        int count = (int)(grid.Width * grid.Height * 0.0015d);
        for (int i = 0; i < count; i++)
        {
            int type = random.Next(5) == 0 ? -2 : -1;
            RunSmallHole(grid, random, beachAvoidance, type, 2, 5, 2, 20);
            RunSmallHole(grid, random, beachAvoidance, type, 8, 15, 7, 30);
        }
        context.ReportProgress(1d, "Generating Terraria small holes");
    }

    private void RunSmallHole(RuntimeGrid grid, IRandom random, int beachAvoidance, int type, int s0, int s1, int n0, int n1)
    {
        int x = random.Next(grid.Width);
        int y = random.Next((int)state.WorldSurfaceHigh, grid.Height);
        while (((x < beachAvoidance || x > grid.Width - beachAvoidance) && y < state.WorldSurfaceHigh) ||
               (x > grid.Width * 0.45d && x < grid.Width * 0.55d && y < state.MainWorldSurface))
        {
            x = random.Next(grid.Width);
            y = random.Next((int)state.WorldSurfaceHigh, grid.Height);
        }
        RunTileRunner(grid, random, x, y, random.Next(s0, s1), random.Next(n0, n1), type);
    }

    private void ApplyDirtLayerCaves(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int beachAvoidance = 340;
        int count = (int)(grid.Width * grid.Height * 0.00003d);
        for (int i = 0; i < count; i++)
        {
            int type = random.Next(6) == 0 ? -2 : -1;
            int x = random.Next(grid.Width);
            int y = random.Next((int)state.WorldSurfaceLow, (int)state.RockLayerHigh + 1);
            while (((x < beachAvoidance || x > grid.Width - beachAvoidance) && y < state.WorldSurfaceHigh) ||
                   (x >= grid.Width * 0.45d && x <= grid.Width * 0.55d && y < state.MainWorldSurface))
            {
                x = random.Next(grid.Width);
                y = random.Next((int)state.WorldSurfaceLow, (int)state.RockLayerHigh + 1);
            }
            RunTileRunner(grid, random, x, y, random.Next(5, 15), random.Next(30, 200), type);
        }
        context.ReportProgress(1d, "Generating Terraria dirt-layer caves");
    }

    private void ApplyRockLayerCaves(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int count = (int)(grid.Width * grid.Height * 0.00013d);
        for (int i = 0; i < count; i++)
        {
            int type = random.Next(10) == 0 ? -2 : -1;
            RunTileRunner(grid, random, random.Next(grid.Width), random.Next((int)state.RockLayerHigh, grid.Height), random.Next(6, 20), random.Next(50, 300), type);
        }
        context.ReportProgress(1d, "Generating Terraria rock-layer caves");
    }

    private void ApplySurfaceCaves(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        RunSurfaceCaveFamily(grid, random, (int)(grid.Width * 0.002d), 0.45d, 0.55d, 3, 6, 5, 50, 1d);
        RunSurfaceCaveFamily(grid, random, (int)(grid.Width * 0.0007d), 0.43d, 0.57d, 10, 15, 50, 130, 2d);
        int large = (int)(grid.Width * 0.0003d);
        for (int i = 0; i < large; i++)
        {
            int x = PickSurfaceCaveX(grid.Width, random, 0.4d, 0.6d);
            int y = grid.FindFirstActiveY(x, 0, Math.Min(grid.Height, (int)state.WorldSurfaceHigh));
            if (y >= grid.Height) continue;
            RunTileRunner(grid, random, x, y, random.Next(12, 25), random.Next(150, 500), -1, false, random.Next(-10, 11) * 0.1d, 4d);
            RunTileRunner(grid, random, x, y, random.Next(8, 17), random.Next(60, 200), -1, false, random.Next(-10, 11) * 0.1d, 2d);
            RunTileRunner(grid, random, x, y, random.Next(5, 13), random.Next(40, 170), -1, false, random.Next(-10, 11) * 0.1d, 2d);
        }
        int vertical = (int)(grid.Width * 0.0004d);
        for (int i = 0; i < vertical; i++)
        {
            int x = PickSurfaceCaveX(grid.Width, random, 0.4d, 0.6d);
            int y = grid.FindFirstActiveY(x, 0, Math.Min(grid.Height, (int)state.WorldSurfaceHigh));
            if (y < grid.Height)
                RunTileRunner(grid, random, x, y, random.Next(7, 12), random.Next(150, 250), -1, false, 0d, 1d, true);
        }
        int caverers = (int)(5d * (grid.Width / 4200d));
        for (int i = 0; i < caverers; i++)
        {
            int minY = (int)state.MainRockLayer;
            int maxY = grid.Height - 400;
            if (minY >= maxY) minY = maxY - 1;
            Caverer(grid, random, random.Next(340, grid.Width - 340), random.Next(minY, maxY));
        }
        context.ReportProgress(1d, "Generating Terraria surface caves");
    }

    private void RunSurfaceCaveFamily(RuntimeGrid grid, IRandom random, int count, double centerLeft, double centerRight, int s0, int s1, int n0, int n1, double speedY)
    {
        for (int i = 0; i < count; i++)
        {
            int x = PickSurfaceCaveX(grid.Width, random, centerLeft, centerRight);
            int y = grid.FindFirstActiveY(x, 0, Math.Min(grid.Height, (int)state.WorldSurfaceHigh));
            if (y < grid.Height)
                RunTileRunner(grid, random, x, y, random.Next(s0, s1), random.Next(n0, n1), -1, false, random.Next(-10, 11) * 0.1d, speedY);
        }
    }

    private int PickSurfaceCaveX(int width, IRandom random, double centerLeft, double centerRight)
    {
        VanillaWorldGenerationBootstrapState1458 b = RequireBootstrap();
        int x = random.Next(width);
        while ((x > width * centerLeft && x < width * centerRight) || x < b.LeftBeachEnd + 20 || x > b.RightBeachStart - 20)
            x = random.Next(width);
        return x;
    }

    private void Caverer(RuntimeGrid grid, IRandom random, int x, int y)
    {
        if (random.Next(2) == 0)
        {
            int segments = random.Next(7, 9);
            double dx = random.Next(100) * 0.01d;
            double dy = 1d - dx;
            if (random.Next(2) == 0) dx = -dx;
            if (random.Next(2) == 0) dy = -dy;
            double cx = x, cy = y;
            for (int i = 0; i < segments; i++)
            {
                (cx, cy) = DigTunnel(grid, random, cx, cy, dx, dy, random.Next(6, 20), random.Next(4, 9));
                dx = Math.Clamp(dx + random.Next(-20, 21) * 0.1d, -1.5d, 1.5d);
                dy = Math.Clamp(dy + random.Next(-20, 21) * 0.1d, -1.5d, 1.5d);
                double bx = random.Next(100) * 0.01d;
                double by = 1d - bx;
                if (random.Next(2) == 0) bx = -bx;
                if (random.Next(2) == 0) by = -by;
                (double ex, double ey) = DigTunnel(grid, random, cx, cy, bx, by, random.Next(30, 50), random.Next(3, 6));
                RunTileRunner(grid, random, (int)ex, (int)ey, random.Next(10, 20), random.Next(5, 10), -1);
            }
        }
        else
        {
            int segments = random.Next(15, 30);
            double dx = random.Next(100) * 0.01d;
            double dy = 1d - dx;
            if (random.Next(2) == 0) dx = -dx;
            if (random.Next(2) == 0) dy = -dy;
            double cx = x, cy = y;
            for (int i = 0; i < segments; i++)
            {
                (cx, cy) = DigTunnel(grid, random, cx, cy, dx, dy, random.Next(5, 15), random.Next(2, 6));
                dx = Math.Clamp(dx + random.Next(-20, 21) * 0.1d, -1.5d, 1.5d);
                dy = Math.Clamp(dy + random.Next(-20, 21) * 0.1d, -1.5d, 1.5d);
            }
        }
    }

    private static (double X, double Y) DigTunnel(RuntimeGrid grid, IRandom random, double x, double y, double directionX, double directionY, int steps, int size)
    {
        double driftX = 0d, driftY = 0d, radius = size;
        x = Math.Clamp(x, radius + 1d, grid.Width - radius - 1d);
        y = Math.Clamp(y, radius + 1d, grid.Height - radius - 1d);
        for (int i = 0; i < steps; i++)
        {
            int left = Math.Max(0, (int)(x - radius));
            int right = Math.Min(grid.Width - 1, (int)(x + radius));
            int top = Math.Max(0, (int)(y - radius));
            int bottom = Math.Min(grid.Height - 1, (int)(y + radius));
            for (int tx = left; tx <= right; tx++)
            for (int ty = top; ty <= bottom; ty++)
                if (Math.Abs(tx - x) + Math.Abs(ty - y) < radius * (1d + random.Next(-10, 11) * 0.005d))
                    SetActive(ref grid.At(tx, ty), false);
            radius = Math.Clamp(radius + random.Next(-50, 51) * 0.03d, size * 0.6d, size * 2d);
            driftX = Math.Clamp(driftX + random.Next(-20, 21) * 0.01d, -1d, 1d);
            driftY = Math.Clamp(driftY + random.Next(-20, 21) * 0.01d, -1d, 1d);
            x += (directionX + driftX) * 0.6d;
            y += (directionY + driftY) * 0.6d;
        }
        return (x, y);
    }

    private void ApplyIceBiome(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 b = RequireBootstrap();
        state.SnowTop = (int)state.MainWorldSurface;
        int iceCoreTop = state.LavaLine - random.Next(160, 200);
        int left = b.SnowOriginLeft;
        int right = b.SnowOriginRight;
        int thickness = 10;
        int lastRow = Math.Min(state.LavaLine - 140, grid.Height - 2);
        for (int row = 0; row <= lastRow; row++)
        {
            left += random.Next(-4, 4);
            right += random.Next(-3, 5);
            if (row > 0)
            {
                left = (left + state.SnowMinX[row - 1]) / 2;
                right = (right + state.SnowMaxX[row - 1]) / 2;
            }
            if (b.DungeonSide >= 1)
            {
                if (random.Next(4) == 0) { left++; right++; }
            }
            else if (random.Next(4) == 0) { left--; right--; }
            left = Math.Clamp(left, 1, grid.Width - 2);
            right = Math.Clamp(right, left + 1, grid.Width - 1);
            state.SnowMinX[row] = left;
            state.SnowMaxX[row] = right;
            for (int x = left; x < right; x++)
            {
                if (row < iceCoreTop) { ConvertSnowTile(ref grid.At(x, row)); continue; }
                thickness += random.Next(-3, 4);
                if (random.Next(3) == 0)
                {
                    thickness += random.Next(-4, 5);
                    if (random.Next(3) == 0) thickness += random.Next(-6, 7);
                }
                if (thickness < 0) thickness = random.Next(3);
                else if (thickness > 50) thickness = 50 - random.Next(3);
                int bottom = Math.Min(row + thickness, grid.Height - 2);
                for (int y = row; y < bottom; y++) ConvertSnowTile(ref grid.At(x, y));
            }
            state.SnowBottom = Math.Max(state.SnowBottom, row);
            if ((row & 31) == 0) context.CancellationToken.ThrowIfCancellationRequested();
        }
        context.ReportProgress(1d, "Generating Terraria ice biome");
    }

    private static void ConvertSnowTile(ref WorldTile tile)
    {
        if (tile.Wall == DirtWall) tile.Wall = 40;
        if (tile.Type is Dirt or Grass or 23 or Clay or Sand) tile.Type = Snow;
        else if (tile.Type == Stone) tile.Type = Ice;
    }

    private void ApplyGrass(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int iterations = (int)(grid.Width * grid.Height * 0.002d);
        for (int i = 0; i < iterations; i++)
        {
            TryGrass(grid, random.Next(1, grid.Width - 1), Math.Clamp(random.Next((int)state.WorldSurfaceLow, (int)state.WorldSurfaceHigh), 1, grid.Height - 2));
            TryGrass(grid, random.Next(1, grid.Width - 1), Math.Clamp(random.Next(5, (int)state.WorldSurfaceLow), 1, grid.Height - 2));
        }
        context.ReportProgress(1d, "Generating Terraria grass");
    }

    private static void TryGrass(RuntimeGrid grid, int x, int y)
    {
        ref WorldTile target = ref grid.At(x, y);
        if (!IsActiveType(target, Dirt))
            return;

        bool exposed = false;
        for (int tx = x - 1; tx <= x + 1 && !exposed; tx++)
        for (int ty = y - 1; ty <= y + 1; ty++)
        {
            ref WorldTile neighbor = ref grid.At(tx, ty);
            if (!neighbor.IsActive)
            {
                exposed = true;
                break;
            }
        }

        if (exposed)
            SetType(ref target, Grass, true);
    }

    private void ApplyJungle(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 b = RequireBootstrap();
        double scale = grid.Width / 4200d * 1.5d;
        int x = b.JungleOriginX;
        int y = (int)((grid.Height + state.MainRockLayer) / 2d);
        int sumX = 0, sumY = 0;

        ApplyRandomMovement(random, scale, ref x, ref y, 100, 100, grid.Height);
        sumX += x; sumY += y;
        PlaceFirstMud(grid, random, scale, x, y, b.DungeonSide * 3);
        PlaceGems(grid, random, scale, x, y, 63);
        ApplyRandomMovement(random, scale, ref x, ref y, 250, 150, grid.Height);
        sumX += x; sumY += y;
        PlaceFirstMud(grid, random, scale, x, y, 0);
        PlaceGems(grid, random, scale, x, y, 65);
        int oldX = x, oldY = y;
        ApplyRandomMovement(random, scale, ref x, ref y, 400, 150, grid.Height);
        sumX += x; sumY += y;
        PlaceFirstMud(grid, random, scale, x, y, b.DungeonSide * -3);
        PlaceGems(grid, random, scale, x, y, 67);

        x = sumX / 3; y = sumY / 3;
        int strength = random.Next((int)(400d * scale), (int)(600d * scale));
        int padding = (int)(25d * scale);
        x = Math.Clamp(x, b.LeftBeachEnd + strength / 2 + padding, b.RightBeachStart - strength / 2 - padding);
        state.MudWall = true;
        RunTileRunner(grid, random, x, y, strength, 10000, Mud, false, 0d, -20d, true);
        GenerateJungleTunnel(grid, random, x, y);
        state.MudWall = false;
        GenerateMudWallHoles(grid, random);
        GenerateJungleFinishing(grid, random, scale, oldX, oldY, context);
        context.ReportProgress(1d, "Generating source-backed Terraria Jungle");
    }

    private void ApplyRandomMovement(IRandom random, double scale, ref int x, ref int y, int xr, int yr, int height)
    {
        x += random.Next((int)(-xr * scale), 1 + (int)(xr * scale));
        y += random.Next((int)(-yr * scale), 1 + (int)(yr * scale));
        y = Math.Clamp(y, (int)state.MainRockLayer, height);
    }

    private void PlaceFirstMud(RuntimeGrid grid, IRandom random, double scale, int x, int y, int speedX)
    {
        state.MudWall = true;
        RunTileRunner(grid, random, x, y, random.Next((int)(250d * scale), (int)(500d * scale)), random.Next(50, 150), Mud, false, speedX);
        state.MudWall = false;
    }

    private void PlaceGems(RuntimeGrid grid, IRandom random, double scale, int x, int y, int baseGem)
    {
        for (int i = 0; i < 6d * scale; i++)
            RunTileRunner(grid, random,
                x + random.Next(-(int)(125d * scale), (int)(125d * scale)),
                y + random.Next(-(int)(125d * scale), (int)(125d * scale)),
                random.Next(3, 7), random.Next(3, 8), random.Next(baseGem, baseGem + 2));
    }

    private void GenerateJungleTunnel(RuntimeGrid grid, IRandom random, int startX, int startY)
    {
        double strength = random.Next(5, 11), x = startX, y = startY;
        double vx = random.Next(-10, 11) * 0.1d;
        double vy = random.Next(10, 20) * 0.1d;
        int branch = 0;
        for (int guard = 0; guard < 6000; guard++)
        {
            if (y < state.MainWorldSurface)
            {
                int tx = Math.Clamp((int)x, 10, grid.Width - 10);
                int ty = Math.Clamp((int)y, 10, grid.Height - 10);
                if (IsOpenAirColumn(grid, tx, Math.Max(5, ty))) break;
            }
            state.JungleX = (int)x;
            strength = Math.Clamp(strength + random.Next(-20, 21) * 0.1d, 5d, 10d);
            int left = Math.Clamp((int)(x - strength * 0.5d), 10, grid.Width - 10);
            int right = Math.Clamp((int)(x + strength * 0.5d), 10, grid.Width - 10);
            int top = Math.Clamp((int)(y - strength * 0.5d), 10, grid.Height - 10);
            int bottom = Math.Clamp((int)(y + strength * 0.5d), 10, grid.Height - 10);
            for (int tx = left; tx < right; tx++)
            for (int ty = top; ty < bottom; ty++)
                if (Math.Abs(tx - x) + Math.Abs(ty - y) < strength * 0.5d * (1d + random.Next(-10, 11) * 0.015d))
                    SetActive(ref grid.At(tx, ty), false);
            branch++;
            if (branch > 10 && random.Next(50) < branch)
            {
                branch = 0;
                int sx = random.Next(2) == 0 ? 2 : -2;
                RunTileRunner(grid, random, (int)x, (int)y, random.Next(3, 20), random.Next(10, 100), -1, false, sx);
            }
            x += vx; y += vy;
            vy = Math.Clamp(vy + random.Next(-10, 11) * 0.01d, -2d, 0d);
            vx += random.Next(-10, 11) * 0.1d;
            if (x < startX - 200) vx += random.Next(5, 21) * 0.1d;
            if (x > startX + 200) vx -= random.Next(5, 21) * 0.1d;
            vx = Math.Clamp(vx, -1.5d, 1.5d);
        }
    }

    private static bool IsOpenAirColumn(RuntimeGrid grid, int x, int y)
    {
        for (int i = 0; i <= 5; i++)
        {
            ref WorldTile tile = ref grid.At(x, y - i);
            if (tile.Wall != 0 || tile.IsActive) return false;
        }
        return true;
    }

    private void GenerateMudWallHoles(RuntimeGrid grid, IRandom random)
    {
        int underworld = grid.Height - 200;
        for (int i = 0; i < grid.Width / 4; i++)
        {
            int x = 0, y = 0;
            bool found = false;
            for (int attempt = 0; attempt < 10000; attempt++)
            {
                x = random.Next(20, grid.Width - 20);
                y = random.Next((int)state.WorldSurfaceLow + 10, underworld);
                ushort wall = grid.At(x, y).Wall;
                if (wall is JungleWall or MudWall) { found = true; break; }
            }
            if (found) MudWallRunner(grid, random, x, y);
        }
    }

    private void GenerateJungleFinishing(RuntimeGrid grid, IRandom random, double scale, int oldX, int oldY, IWorldGenerationContext context)
    {
        int x = oldX, y = oldY;
        for (int i = 0; i <= 20d * scale; i++)
        {
            x += random.Next((int)(-5d * scale), (int)(6d * scale));
            y += random.Next((int)(-5d * scale), (int)(6d * scale));
            RunTileRunner(grid, random, x, y, random.Next(40, 100), random.Next(300, 500), Mud);
        }
        for (int j = 0; j <= 10d * scale; j++)
        {
            PickMudPoint(grid, random, scale, oldX, oldY, out x, out y);
            for (int k = 0; k < 8d * scale; k++)
            {
                x += random.Next(-30, 31); y += random.Next(-30, 31);
                int type = random.Next(7) == 0 ? -2 : -1;
                RunTileRunner(grid, random, x, y, random.Next(10, 20), random.Next(30, 70), type);
            }
        }
        for (int i = 0; i <= 300d * scale; i++)
        {
            PickMudPoint(grid, random, scale, oldX, oldY, out x, out y);
            RunTileRunner(grid, random, x, y, random.Next(4, 10), random.Next(5, 30), Stone);
            if (random.Next(4) == 0)
                RunTileRunner(grid, random, x + random.Next(-1, 2), y + random.Next(-1, 2), random.Next(3, 7), random.Next(4, 8), random.Next(63, 69));
            if ((i & 31) == 0) context.CancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static void PickMudPoint(RuntimeGrid grid, IRandom random, double scale, int originX, int originY, out int x, out int y)
    {
        for (int attempt = 0; ; attempt++)
        {
            x = originX + random.Next((int)(-600d * scale), (int)(600d * scale));
            y = originY + random.Next((int)(-200d * scale), (int)(200d * scale));
            if (grid.Contains(x, y) && grid.At(x, y).Type == Mud) return;
            if (attempt > 20000) { x = Math.Clamp(originX, 1, grid.Width - 2); y = Math.Clamp(originY, 1, grid.Height - 2); return; }
        }
    }

    private void MudWallRunner(RuntimeGrid grid, IRandom random, int i, int j)
    {
        double strength = random.Next(8, 21);
        double steps = random.Next(8, 33), remaining = steps;
        double x = i, y = j;
        double vx = random.Next(-10, 11) * 0.1d;
        double vy = random.Next(-10, 11) * 0.1d;
        while (strength > 0d && remaining > 0d)
        {
            double current = strength * (remaining / steps);
            remaining--;
            int left = Math.Clamp((int)(x - current * 0.5d), 0, grid.Width);
            int right = Math.Clamp((int)(x + current * 0.5d), 0, grid.Width);
            int top = Math.Clamp((int)(y - current * 0.5d), 0, grid.Height);
            int bottom = Math.Clamp((int)(y + current * 0.5d), 0, grid.Height);
            for (int tx = left; tx < right; tx++)
            for (int ty = top; ty < bottom; ty++)
                if (Math.Abs(tx - x) + Math.Abs(ty - y) < strength * 0.5d * (1d + random.Next(-10, 11) * 0.015d) && ty > state.MainWorldSurface)
                    grid.At(tx, ty).Wall = 0;
            x += vx; y += vy;
            vx = Math.Clamp(vx + random.Next(-10, 11) * 0.05d, -1d, 1d);
            vy = Math.Clamp(vy + random.Next(-10, 11) * 0.05d, -1d, 1d);
        }
    }

    private void RunSandPatch(RuntimeGrid grid, IRandom random, int i, int j, double strength, int steps)
    {
        double current = strength, remaining = steps, x = i, y = j;
        double vx = random.Next(-10, 11) * 0.1d, vy = random.Next(-10, 11) * 0.1d;
        _ = random.Next(4);
        while (current > 0d && remaining > 0d)
        {
            current = strength * (remaining / steps); remaining--;
            int left = Math.Max(1, (int)(x - current * 0.5d));
            int right = Math.Min(grid.Width - 1, (int)(x + current * 0.5d));
            int top = Math.Max(1, (int)(y - current * 0.5d));
            int bottom = Math.Min(grid.Height - 1, (int)(y + current * 0.5d));
            for (int tx = left; tx < right; tx++)
            for (int ty = top; ty < bottom; ty++)
            {
                double manhattan = Math.Abs(tx - x) + Math.Abs(ty - y);
                if (manhattan >= strength * 0.575d) { _ = random.Next(-10, 11); continue; }
                if (manhattan >= strength * 0.5d * (1d + random.Next(-10, 11) * 0.015d)) continue;
                ref WorldTile tile = ref grid.At(tx, ty);
                if (tile.IsActive && tile.Type == Sand && ty < state.MainWorldSurface) continue;
                tile.Type = Sand;
            }
            x += vx; y += vy;
            foreach (double threshold in ExtraStepThresholds)
            {
                if (current <= threshold) break;
                x += vx; y += vy; remaining--;
                vy += random.Next(-10, 11) * 0.05d;
                vx += random.Next(-10, 11) * 0.05d;
            }
            vx = Math.Clamp(vx + random.Next(-10, 11) * 0.05d, -1d, 1d);
            vy = Math.Clamp(vy + random.Next(-10, 11) * 0.05d, -1d, 1d);
        }
    }

    private void RunTileRunner(RuntimeGrid grid, IRandom random, int i, int j, double strength, int steps, int type,
        bool addTile = false, double speedX = 0d, double speedY = 0d, bool noYChange = false)
    {
        double current = strength, remaining = steps, x = i, y = j;
        double vx = random.Next(-10, 11) * 0.1d, vy = random.Next(-10, 11) * 0.1d;
        if (speedX != 0d || speedY != 0d) { vx = speedX; vy = speedY; }
        _ = random.Next(4);
        const WorldLiquidKind ordinaryLiquidKind = WorldLiquidKind.Water;
        while (current > 0d && remaining > 0d)
        {
            if (y < 0d && type == Mud) remaining = 0d;
            current = strength * (remaining / steps); remaining--;
            int left = Math.Max(1, (int)(x - current * 0.5d));
            int right = Math.Min(grid.Width - 1, (int)(x + current * 0.5d));
            int top = Math.Max(1, (int)(y - current * 0.5d));
            int bottom = Math.Min(grid.Height - 1, (int)(y + current * 0.5d));
            for (int tx = left; tx < right; tx++)
            for (int ty = top; ty < bottom; ty++)
            {
                if (Math.Abs(tx - x) + Math.Abs(ty - y) >= strength * 0.5d * (1d + random.Next(-10, 11) * 0.015d)) continue;
                ref WorldTile tile = ref grid.At(tx, ty);
                if (state.MudWall && ty > state.MainWorldSurface && grid.At(tx, ty - 1).Wall != DirtWall &&
                    ty < grid.Height - 210 - random.Next(3) &&
                    Math.Abs(tx - x) + Math.Abs(ty - y) < strength * 0.45d * (1d + random.Next(-10, 11) * 0.01d))
                {
                    if (ty > state.LavaLine - random.Next(0, 4) - 50)
                    {
                        if (grid.At(tx, ty - 1).Wall != JungleWall && grid.At(tx, ty + 1).Wall != JungleWall &&
                            grid.At(tx - 1, ty).Wall != JungleWall && grid.At(tx + 1, ty).Wall != JungleWall)
                            tile.Wall = MudWall;
                    }
                    else if (grid.At(tx, ty - 1).Wall != MudWall && grid.At(tx, ty + 1).Wall != MudWall &&
                             grid.At(tx - 1, ty).Wall != MudWall && grid.At(tx + 1, ty).Wall != MudWall)
                        tile.Wall = JungleWall;
                }
                if (type < 0)
                {
                    if (tile.IsActive && tile.Type == Sand) continue;
                    if (type == -2 && tile.IsActive && (ty < state.WaterLine || ty > state.LavaLine))
                    {
                        tile.LiquidAmount = byte.MaxValue;
                        tile.LiquidKind = ty > state.LavaLine ? WorldLiquidKind.Lava : ordinaryLiquidKind;
                    }
                    SetActive(ref tile, false);
                    continue;
                }
                bool skip = false;
                if (tile.IsActive)
                {
                    if (type is >= 63 and <= 68 && tile.Type != Stone) skip = true;
                    if (tile.Type == Sand && ty < state.MainWorldSurface && type != Mud) skip = true;
                    if (tile.Type == Stone && type == Mud && ty < state.MainWorldSurface + random.Next(-50, 50)) skip = true;
                    if (tile.Type is 147 or 189 or 190 or 196 or 460 or 717 or 718 or 719) skip = true;
                }
                if (!skip) tile.Type = checked((ushort)type);
                if (addTile) { SetActive(ref tile, true); tile.LiquidAmount = 0; tile.LiquidKind = WorldLiquidKind.Water; }
                if (noYChange && ty < state.MainWorldSurface && type != Mud) tile.Wall = DirtWall;
                if (type == Mud && ty > state.WaterLine && tile.LiquidAmount > 0) { tile.LiquidAmount = 0; tile.LiquidKind = WorldLiquidKind.Water; }
            }
            x += vx; y += vy;
            foreach (double threshold in ExtraStepThresholds)
            {
                if (current <= threshold) break;
                x += vx; y += vy; remaining--;
                vy += random.Next(-10, 11) * 0.05d;
                vx += random.Next(-10, 11) * 0.05d;
            }
            vx = Math.Clamp(vx + random.Next(-10, 11) * 0.05d, -1d, 1d);
            if (!noYChange) vy = Math.Clamp(vy + random.Next(-10, 11) * 0.05d, -1d, 1d);
            else if (type != Mud && current < 3d) vy = Math.Clamp(vy, -1d, 1d);
            if (type == Mud && !noYChange)
            {
                vy = Math.Clamp(vy, -0.5d, 0.5d);
                if (y < state.MainRockLayer + 100d) vy = 1d;
                if (y > grid.Height - 300) vy = -1d;
            }
        }
    }

    private VanillaWorldGenerationBootstrapState1458 RequireBootstrap() => state.Bootstrap ??
        throw new InvalidOperationException("Early vanilla pass executed before Reset bootstrap publication.");

    private static IRandom RequireVanilla(IWorldGenerationContext context) => new VanillaRandom(
        context.VanillaRandom ?? throw new InvalidOperationException("Source-backed early pass requires shared UnifiedRandom semantics."));

    private static bool IsActiveType(in WorldTile tile, ushort type) => tile.IsActive && tile.Type == type;
    private static void SetActive(ref WorldTile tile, bool active) => tile.Flags = active ? tile.Flags | WorldTileFlags.Active : tile.Flags & ~WorldTileFlags.Active;
    private static void SetType(ref WorldTile tile, ushort type, bool active) { tile.Type = type; SetActive(ref tile, active); tile.FrameX = -1; tile.FrameY = -1; }
    private static void ClearTile(ref WorldTile tile) { SetActive(ref tile, false); tile.LiquidAmount = 0; tile.LiquidKind = WorldLiquidKind.Water; tile.FrameX = -1; tile.FrameY = -1; }

    internal interface IRandom
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

    private sealed class IsolatedRandom(IWorldGenerationRandom inner) : IRandom
    {
        public int Next() => unchecked((int)(inner.NextUInt32() & 0x7fffffff));
        public int Next(int max) => max <= 0 ? 0 : inner.NextInt32(max);
        public int Next(int min, int max) => max <= min ? min : min + inner.NextInt32(max - min);
        public double NextDouble() => inner.NextUInt64() / ((double)ulong.MaxValue + 1d);
    }

    private sealed class RuntimeGrid
    {
        private readonly WorldTileStore store;
        public RuntimeGrid(RuntimeWorldGenerationWorkspace workspace) => store = workspace.TileStore;
        public int Width => store.Dimensions.WidthTiles;
        public int Height => store.Dimensions.HeightTiles;
        public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;
        public ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
        public int FindFirstActiveY(int x, int minY, int maxExclusive)
        {
            int max = Math.Min(Height, maxExclusive);
            for (int y = Math.Max(0, minY); y < max; y++) if (At(x, y).IsActive) return y;
            return max;
        }
    }

    private sealed class DuneDescription
    {
        private DuneDescription(int left, int width, int surfaceX, short[] surface, bool windRight)
        { Left = left; Width = width; CenterX = left + width / 2; SurfaceX = surfaceX; Surface = surface; WindRight = windRight; }
        public int Left { get; }
        public int Width { get; }
        public int CenterX { get; }
        public int SurfaceX { get; }
        public short[] Surface { get; }
        public bool WindRight { get; }
        public int SurfaceAt(int x) => Surface[x - SurfaceX];
        public static DuneDescription Create(RuntimeGrid grid, IRandom random, int centerX, int width)
        {
            int left = centerX - width / 2;
            int surfaceX = left - 20;
            var surface = new short[width + 40];
            for (int x = surfaceX; x < surfaceX + surface.Length; x++)
            {
                int y = grid.FindFirstActiveY(x, 50, Math.Min(grid.Height, 50 + grid.Height / 2));
                surface[x - surfaceX] = checked((short)Math.Min(short.MaxValue, y));
            }
            return new DuneDescription(left, width, surfaceX, surface, random.Next(2) != 0);
        }
    }

    private sealed class ChildContext(
        IWorldGenerationContext parent,
        IWorldGenerationMetadataWorkspace? metadata,
        IWorldGenerationVanillaRandom? vanillaRandom,
        bool suppressProgress) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => parent.Request;
        public IWorldGenerationWorkspace Workspace => parent.Workspace;
        public IWorldGenerationMetadataWorkspace? Metadata => metadata;
        public IWorldGenerationRandom Random => parent.Random;
        public IWorldGenerationVanillaRandom? VanillaRandom => vanillaRandom;
        public CancellationToken CancellationToken => parent.CancellationToken;
        public void ReportProgress(double fraction, string? message = null)
        {
            if (!suppressProgress) parent.ReportProgress(fraction, message);
        }
    }

    private sealed class MetadataSink : IWorldGenerationMetadataWorkspace
    {
        private WorldGenerationPoint spawn, dungeon;
        private WorldGenerationLayers layers;
        private bool hasSpawn, hasDungeon, hasLayers;
        public bool TryGetSpawn(out WorldGenerationPoint value) { value = spawn; return hasSpawn; }
        public bool TrySetSpawn(int x, int y) { spawn = new(x, y); hasSpawn = true; return true; }
        public bool TryGetDungeon(out WorldGenerationPoint value) { value = dungeon; return hasDungeon; }
        public bool TrySetDungeon(int x, int y) { dungeon = new(x, y); hasDungeon = true; return true; }
        public bool TryGetLayers(out WorldGenerationLayers value) { value = layers; return hasLayers; }
        public bool TrySetLayers(double surface, double rock) { layers = new(surface, rock); hasLayers = true; return true; }
    }

    private sealed class CompatibilityVanillaRandom(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly Random random = new(seed);
        public int Next() => random.Next();
        public int Next(int maxValue) => random.Next(maxValue);
        public int Next(int minValue, int maxValue) => random.Next(minValue, maxValue);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}

internal sealed class VanillaResidualCompatibilityBiomesPass1458 : IWorldGenerationPass
{
    private readonly IWorldGenerationPass inner;
    public VanillaResidualCompatibilityBiomesPass1458(IWorldGenerationPass inner) => this.inner = inner;

    public void Execute(IWorldGenerationContext context)
    {
        var filtered = new JunglePreservingWorkspace(context.Workspace);
        inner.Execute(new ResidualContext(context, filtered));
    }

    private sealed class ResidualContext(IWorldGenerationContext parent, IWorldGenerationWorkspace workspace) : IWorldGenerationContext
    {
        private readonly CompatibilityVanillaRandom random = new(unchecked((int)(parent.Request.Seed ^ 0x6A09E667F3BCC909UL)));
        public WorldGenerationRequest Request => parent.Request;
        public IWorldGenerationWorkspace Workspace => workspace;
        public IWorldGenerationMetadataWorkspace? Metadata => parent.Metadata;
        public IWorldGenerationRandom Random => parent.Random;
        public IWorldGenerationVanillaRandom? VanillaRandom => random;
        public CancellationToken CancellationToken => parent.CancellationToken;
        public void ReportProgress(double fraction, string? message = null) => parent.ReportProgress(fraction, message);
    }

    private sealed class JunglePreservingWorkspace(IWorldGenerationWorkspace inner) : IWorldGenerationWorkspace
    {
        public int WidthTiles => inner.WidthTiles;
        public int HeightTiles => inner.HeightTiles;
        public bool TryGetTile(int x, int y, out WorldGenerationTile tile) => inner.TryGetTile(x, y, out tile);
        public bool TrySetTile(int x, int y, in WorldGenerationTile tile)
        {
            if (tile.Type is 59 or 60) return true;
            return inner.TrySetTile(x, y, in tile);
        }
    }

    private sealed class CompatibilityVanillaRandom(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly Random random = new(seed);
        public int Next() => random.Next();
        public int Next(int maxValue) => random.Next(maxValue);
        public int Next(int minValue, int maxValue) => random.Next(minValue, maxValue);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
