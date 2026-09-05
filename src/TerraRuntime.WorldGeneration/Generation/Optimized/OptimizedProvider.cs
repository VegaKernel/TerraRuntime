using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Optimized;

/// <summary>
/// Runtime-owned production-oriented generator. Unlike <c>terraruntime:vanilla</c>, this profile does not attempt
/// seed-identical Terraria worldgen. It uses deterministic spatial planning first, then generates an official-client-
/// compatible, progression-shaped world inside reserved bounds. Required structures fail closed instead of being
/// silently omitted when a layout cannot fit them.
/// </summary>
public sealed class OptimizedProvider : IWorldGenerationProvider
{
    public static readonly WorldGeneratorId GeneratorId = new("terraruntime:optimized");

    private static readonly WorldGenerationPassId LayoutId = new("terraruntime:optimized/layout");
    private static readonly WorldGenerationPassId TerrainId = new("terraruntime:optimized/terrain");
    private static readonly WorldGenerationPassId BiomesId = new("terraruntime:optimized/biomes");
    private static readonly WorldGenerationPassId CavesId = new("terraruntime:optimized/caves");
    private static readonly WorldGenerationPassId IslandsId = new("terraruntime:optimized/floating-islands");
    private static readonly WorldGenerationPassId OresId = new("terraruntime:optimized/ores");
    private static readonly WorldGenerationPassId StructuresId = new("terraruntime:optimized/structures");
    private static readonly WorldGenerationPassId MetadataId = new("terraruntime:optimized/metadata");
    private static readonly WorldGenerationPassId ValidationId = new("terraruntime:optimized/validation");

    private const ushort CopperOre = 7;
    private const ushort IronOre = 6;
    private const ushort SilverOre = 9;
    private const ushort GoldOre = 8;
    private const ushort Hellstone = 58;
    private const ushort BlueDungeonBrick = 41;

    public WorldGeneratorId Id => GeneratorId;

    public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        request.Validate();
        if (request.WidthTiles < 512 || request.HeightTiles < 240)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Optimized generation requires at least a 512x240 candidate workspace so all mandatory progression regions fit.");
        }

        var state = new GenerationState(request.WidthTiles, request.HeightTiles);
        Add(builder, LayoutId, new LayoutPass(state));
        Add(builder, TerrainId, new TerrainPass(state), LayoutId);
        Add(builder, BiomesId, new BiomesPass(state), TerrainId);
        Add(builder, CavesId, new CavesPass(state), BiomesId);
        Add(builder, IslandsId, new FloatingIslandsPass(state), CavesId);
        Add(builder, OresId, new OresPass(state), IslandsId);
        Add(builder, StructuresId, new StructuresPass(state), OresId);
        Add(builder, MetadataId, new MetadataPass(state), StructuresId);
        Add(builder, ValidationId, new ValidationPass(state), MetadataId);
    }

    private static void Add(
        IWorldGenerationPlanBuilder builder,
        WorldGenerationPassId id,
        IWorldGenerationPass pass,
        params WorldGenerationPassId[] requiredAfter) =>
        builder.Add(
            new WorldGenerationPassDescriptor(
                id,
                WorldGenerationRngMode.IsolatedDeterministic,
                requiredAfter.Length == 0 ? null : requiredAfter),
            pass);

    private sealed class GenerationState(int width, int height)
    {
        public int WorldWidth { get; } = width;
        public int WorldHeight { get; } = height;
        public int[] SurfaceY { get; } = new int[width];
        public List<ReservedRegion> Reservations { get; } = [];
        public List<FloatingIslandSpec> FloatingIslands { get; } = [];

        public int BaseSurface { get; set; }
        public int RockLayer { get; set; }
        public int UnderworldTop { get; set; }
        public int OceanWidth { get; set; }

        public HorizontalBand Snow { get; set; }
        public HorizontalBand Desert { get; set; }
        public HorizontalBand Jungle { get; set; }
        public HorizontalBand Evil { get; set; }
        public HorizontalBand Mushroom { get; set; }

        public ReservedRegion SpawnReserve { get; set; }
        public ReservedRegion Dungeon { get; set; }
        public ReservedRegion Temple { get; set; }
        public ReservedRegion Hive { get; set; }
        public ReservedRegion Shimmer { get; set; }

        public int DungeonX { get; set; }
        public bool LayoutReady { get; set; }

        public bool IsProtected(int x, int y)
        {
            foreach (ReservedRegion region in Reservations)
            {
                if (region.ProtectFromCaves && region.Contains(x, y))
                    return true;
            }

            return false;
        }
    }

    private readonly record struct HorizontalBand(int Left, int Right)
    {
        public int Width => Right - Left + 1;
        public int Center => Left + Width / 2;
        public bool Contains(int x) => x >= Left && x <= Right;
    }

    private readonly record struct ReservedRegion(
        string Role,
        int Left,
        int Top,
        int Right,
        int Bottom,
        bool ProtectFromCaves = true)
    {
        public int Width => Right - Left + 1;
        public int Height => Bottom - Top + 1;
        public int CenterX => Left + Width / 2;
        public int CenterY => Top + Height / 2;

        public bool Contains(int x, int y) =>
            x >= Left && x <= Right && y >= Top && y <= Bottom;

        public bool Overlaps(ReservedRegion other, int clearance = 0) =>
            Left - clearance <= other.Right &&
            Right + clearance >= other.Left &&
            Top - clearance <= other.Bottom &&
            Bottom + clearance >= other.Top;
    }

    private readonly record struct FloatingIslandSpec(
        ReservedRegion Region,
        int SurfaceY,
        int RadiusX,
        int Depth,
        int Ordinal);

    private sealed class LayoutPass(GenerationState state) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            int width = context.Workspace.WidthTiles;
            int height = context.Workspace.HeightTiles;

            state.Reservations.Clear();
            state.FloatingIslands.Clear();
            state.BaseSurface = Math.Clamp((int)Math.Round(height * 0.30d), 64, height - 150);
            state.RockLayer = Math.Clamp((int)Math.Round(height * 0.52d), state.BaseSurface + 40, height - 90);
            state.UnderworldTop = Math.Clamp((int)Math.Round(height * 0.84d), state.RockLayer + 40, height - 45);
            state.OceanWidth = Math.Clamp(width / 12, 48, 360);

            bool jungleOnRight = (context.Request.Seed & 1UL) == 0UL;
            HorizontalBand leftCold = Band(width, 0.12d, 0.24d);
            HorizontalBand leftWarm = Band(width, 0.27d, 0.39d);
            HorizontalBand rightWarm = Band(width, 0.61d, 0.73d);
            HorizontalBand rightJungle = Band(width, 0.76d, 0.88d);

            state.Snow = jungleOnRight ? leftCold : rightJungle;
            state.Jungle = jungleOnRight ? rightJungle : leftCold;
            state.Desert = jungleOnRight ? leftWarm : rightWarm;
            state.Evil = jungleOnRight ? rightWarm : leftWarm;
            state.Mushroom = Band(width, 0.43d, 0.56d);

            int spawnHalfWidth = Math.Clamp(width / 28, 18, 110);
            state.SpawnReserve = Reserve(
                state,
                new ReservedRegion(
                    "spawn",
                    width / 2 - spawnHalfWidth,
                    state.BaseSurface - 20,
                    width / 2 + spawnHalfWidth,
                    state.BaseSurface + 36));

            HorizontalBand dungeonSide = state.Snow;
            int dungeonWidth = Math.Clamp(width / 18, 38, 160);
            int dungeonX = Math.Clamp(dungeonSide.Center, state.OceanWidth + dungeonWidth, width - state.OceanWidth - dungeonWidth - 1);
            int dungeonTop = Math.Clamp(state.BaseSurface - 12, 24, height - 160);
            int dungeonBottom = Math.Clamp((int)Math.Round(height * 0.72d), dungeonTop + 80, state.UnderworldTop - 20);
            state.Dungeon = Reserve(
                state,
                new ReservedRegion(
                    "dungeon",
                    dungeonX - dungeonWidth / 2,
                    dungeonTop,
                    dungeonX + dungeonWidth / 2,
                    dungeonBottom));
            state.DungeonX = state.Dungeon.CenterX;

            int templeWidth = Math.Clamp(width / 28, 34, 120);
            int templeHeight = Math.Clamp(height / 15, 24, 70);
            int templeTop = Math.Clamp((int)Math.Round(height * 0.58d), state.RockLayer + 16, state.UnderworldTop - templeHeight - 16);
            state.Temple = Reserve(
                state,
                new ReservedRegion(
                    "jungle-temple",
                    state.Jungle.Center - templeWidth / 2,
                    templeTop,
                    state.Jungle.Center + templeWidth / 2,
                    templeTop + templeHeight));

            int hiveWidth = Math.Clamp(width / 45, 24, 72);
            int hiveHeight = Math.Clamp(height / 28, 16, 44);
            int hiveCenterX = Math.Clamp(state.Jungle.Center - Math.Max(hiveWidth, state.Jungle.Width / 4), state.Jungle.Left + hiveWidth / 2 + 2, state.Jungle.Right - hiveWidth / 2 - 2);
            int hiveTop = Math.Clamp((int)Math.Round(height * 0.43d), state.BaseSurface + 30, state.Temple.Top - hiveHeight - 12);
            state.Hive = Reserve(
                state,
                new ReservedRegion(
                    "jungle-hive",
                    hiveCenterX - hiveWidth / 2,
                    hiveTop,
                    hiveCenterX + hiveWidth / 2,
                    hiveTop + hiveHeight));

            int shimmerWidth = Math.Clamp(width / 55, 20, 64);
            int shimmerHeight = Math.Clamp(height / 34, 12, 34);
            int shimmerCenterX = state.Jungle.Center;
            int shimmerTop = Math.Clamp(
                state.Temple.Bottom + 10,
                state.RockLayer + 12,
                state.UnderworldTop - shimmerHeight - 6);
            state.Shimmer = Reserve(
                state,
                new ReservedRegion(
                    "aether",
                    shimmerCenterX - shimmerWidth / 2,
                    shimmerTop,
                    shimmerCenterX + shimmerWidth / 2,
                    shimmerTop + shimmerHeight));

            int islandCount = Math.Clamp(width / 900 + 3, 3, 8);
            int skyTop = Math.Max(18, height / 18);
            int skyBottom = Math.Max(skyTop + 16, state.BaseSurface - 80);
            int left = state.OceanWidth + 36;
            int right = width - state.OceanWidth - 36;

            for (int i = 0; i < islandCount; i++)
            {
                int radius = Math.Clamp(width / 95 + (i % 3) * 3, 18, 54);
                int depth = Math.Clamp(height / 48 + (i & 1) * 3, 8, 24);
                double fraction = (i + 1d) / (islandCount + 1d);
                if (Math.Abs(fraction - 0.5d) < 0.08d)
                    fraction += (i & 1) == 0 ? -0.09d : 0.09d;
                int x = Math.Clamp(
                    left + (int)Math.Round((right - left) * fraction),
                    left + radius,
                    right - radius);
                int ySpan = Math.Max(1, skyBottom - skyTop);
                int surfaceY = skyTop + (int)(Hash01(context.Request.Seed ^ 0x4F5054494D495A45UL, i) * ySpan);
                var region = new ReservedRegion(
                    $"floating-island-{i + 1}",
                    x - radius - 3,
                    surfaceY - 10,
                    x + radius + 3,
                    surfaceY + depth + 5,
                    ProtectFromCaves: false);
                region = Reserve(state, region, clearance: 0);
                state.FloatingIslands.Add(new FloatingIslandSpec(region, surfaceY, radius, depth, i));
            }

            state.LayoutReady = true;
            context.ReportProgress(
                1d,
                $"Reserved dungeon, jungle temple, hive, aether and {state.FloatingIslands.Count} floating islands");
        }

        private static HorizontalBand Band(int width, double start, double end)
        {
            int left = Math.Clamp((int)Math.Round(width * start), 1, width - 2);
            int right = Math.Clamp((int)Math.Round(width * end), left, width - 2);
            return new HorizontalBand(left, right);
        }

        private static ReservedRegion Reserve(
            GenerationState state,
            ReservedRegion candidate,
            int clearance = 4)
        {
            if (candidate.Left < 1 || candidate.Top < 1 ||
                candidate.Right >= state.WorldWidth - 1 ||
                candidate.Bottom >= state.WorldHeight - 1 ||
                candidate.Bottom < candidate.Top)
            {
                throw new InvalidOperationException(
                    $"Optimized layout cannot fit required region '{candidate.Role}' in the candidate bounds.");
            }

            foreach (ReservedRegion existing in state.Reservations)
            {
                if (candidate.Overlaps(existing, clearance))
                {
                    throw new InvalidOperationException(
                        $"Optimized layout regions '{candidate.Role}' and '{existing.Role}' overlap; world is too small for a safe layout.");
                }
            }

            state.Reservations.Add(candidate);
            return candidate;
        }
    }

    private sealed class TerrainPass(GenerationState state) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            RequireLayout(state);
            int width = context.Workspace.WidthTiles;
            int height = context.Workspace.HeightTiles;
            ushort grass = Tile(VanillaTileIds.Grass);
            ushort dirt = Tile(VanillaTileIds.Dirt);
            ushort stone = Tile(VanillaTileIds.Stone);
            int progressStride = Math.Max(1, width / 100);

            for (int x = 0; x < width; x++)
            {
                if ((x & 63) == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();

                double broad = FractalNoise1D(context.Request.Seed ^ 0x7465727261696E31UL, x, 96d, 4);
                double detail = FractalNoise1D(context.Request.Seed ^ 0x7465727261696E32UL, x, 31d, 3);
                double mountain = Math.Pow(Math.Abs(FractalNoise1D(context.Request.Seed ^ 0x7465727261696E33UL, x, 210d, 2)), 2d);
                int surface = state.BaseSurface + (int)Math.Round(broad * 13d + detail * 5d - mountain * 7d);

                double spawnDistance = Math.Abs(x - width / 2d) / Math.Max(1d, state.SpawnReserve.Width / 2d);
                if (spawnDistance < 1d)
                {
                    double blend = SmoothStep(spawnDistance);
                    surface = (int)Math.Round(state.BaseSurface * (1d - blend) + surface * blend);
                }

                surface = Math.Clamp(surface, 32, state.RockLayer - 20);
                state.SurfaceY[x] = surface;

                for (int y = surface; y < height; y++)
                {
                    ushort type = y == surface
                        ? grass
                        : y < state.RockLayer ? dirt : stone;
                    SetTile(context.Workspace, x, y, type, 0, WorldGenerationTileFlags.Active);
                }

                if (x % progressStride == 0 || x == width - 1)
                    context.ReportProgress((x + 1d) / width, "Building coherent terrain heightfield");
            }
        }
    }

    private sealed class BiomesPass(GenerationState state) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            RequireLayout(state);
            PaintBand(
                context.Workspace,
                state,
                state.Snow,
                Tile(VanillaTileIds.SnowBlock),
                Tile(VanillaTileIds.IceBlock),
                Math.Clamp(context.Workspace.HeightTiles / 8, 18, 100));
            PaintBand(
                context.Workspace,
                state,
                state.Desert,
                Tile(VanillaTileIds.Sand),
                Tile(VanillaTileIds.Sand),
                Math.Clamp(context.Workspace.HeightTiles / 7, 22, 120));
            PaintBand(
                context.Workspace,
                state,
                state.Jungle,
                Tile(VanillaTileIds.JungleGrass),
                Tile(VanillaTileIds.Mud),
                Math.Clamp(context.Workspace.HeightTiles / 3, 50, 260));

            bool crimson = context.Request.Options.Evil == WorldGenerationEvil.Crimson;
            PaintBand(
                context.Workspace,
                state,
                state.Evil,
                crimson ? Tile(VanillaTileIds.CrimsonGrass) : Tile(VanillaTileIds.CorruptGrass),
                crimson ? Tile(VanillaTileIds.Crimstone) : Tile(VanillaTileIds.Ebonstone),
                Math.Clamp(context.Workspace.HeightTiles / 5, 34, 160));

            PaintUndergroundPatch(
                context.Workspace,
                state.Mushroom,
                Math.Clamp(state.RockLayer + 18, 1, context.Workspace.HeightTiles - 2),
                Tile(VanillaTileIds.Mud),
                Tile(VanillaTileIds.MushroomGrass));

            BuildOcean(context.Workspace, state, left: true);
            BuildOcean(context.Workspace, state, left: false);
            BuildUnderworld(context.Workspace, state, context.Request.Seed);
            context.ReportProgress(1d, "Painting bounded biomes, oceans and underworld");
        }
    }

    private sealed class CavesPass(GenerationState state) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            RequireLayout(state);
            int width = context.Workspace.WidthTiles;
            int height = context.Workspace.HeightTiles;
            int caveCount = Math.Clamp(checked(width * height) / 15000, 18, 900);

            for (int cave = 0; cave < caveCount; cave++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                double x = NextRange(context.Random, state.OceanWidth + 8, width - state.OceanWidth - 8);
                double y = NextRange(context.Random, state.BaseSurface + 14, state.UnderworldTop - 8);
                double angle = NextUnit(context.Random) * Math.PI * 2d;
                double radius = 2.4d + NextUnit(context.Random) * 4.2d;
                int steps = NextRange(context.Random, 28, Math.Clamp(width / 8, 55, 190));

                for (int step = 0; step < steps; step++)
                {
                    CarveOrganicCircle(context.Workspace, state, (int)Math.Round(x), (int)Math.Round(y), radius);
                    angle += (NextUnit(context.Random) - 0.5d) * 0.55d;
                    double speed = 0.8d + NextUnit(context.Random) * 1.4d;
                    x = Math.Clamp(x + Math.Cos(angle) * speed, state.OceanWidth + 4d, width - state.OceanWidth - 5d);
                    y = Math.Clamp(y + Math.Sin(angle) * speed * 0.72d, state.BaseSurface + 8d, state.UnderworldTop - 3d);
                    radius = Math.Clamp(radius + (NextUnit(context.Random) - 0.5d) * 0.65d, 1.8d, 7.5d);
                }

                if ((cave & 15) == 0 || cave == caveCount - 1)
                    context.ReportProgress((cave + 1d) / caveCount, "Carving deterministic cave networks");
            }
        }
    }

    private sealed class FloatingIslandsPass(GenerationState state) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            RequireLayout(state);
            for (int i = 0; i < state.FloatingIslands.Count; i++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                BuildFloatingIsland(context.Workspace, context.Request.Seed, state.FloatingIslands[i]);
                context.ReportProgress(
                    (i + 1d) / state.FloatingIslands.Count,
                    "Building bounded organic floating islands");
            }
        }
    }

    private sealed class OresPass(GenerationState state) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            RequireLayout(state);
            PlaceOreFamily(context, state, CopperOre, divisor: 5600, state.BaseSurface + 18, state.UnderworldTop - 10);
            PlaceOreFamily(context, state, IronOre, divisor: 6500, state.RockLayer - 20, state.UnderworldTop - 8);
            PlaceOreFamily(context, state, SilverOre, divisor: 7600, state.RockLayer, state.UnderworldTop - 6);
            PlaceOreFamily(context, state, GoldOre, divisor: 9000, state.RockLayer + 16, state.UnderworldTop - 4);
            PlaceOreFamily(context, state, Hellstone, divisor: 4800, state.UnderworldTop + 4, context.Workspace.HeightTiles - 8);
            context.ReportProgress(1d, "Embedding progression ore tiers");
        }
    }

    private sealed class StructuresPass(GenerationState state) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            RequireLayout(state);
            BuildDungeon(context.Workspace, state);
            BuildHive(context.Workspace, state.Hive);
            BuildTemple(context.Workspace, state.Temple);
            BuildShimmer(context.Workspace, state.Shimmer);
            PlaceEvilAltar(context.Workspace, state, context.Request.Options.Evil);
            PlaceHellforge(context.Workspace, state);
            context.ReportProgress(1d, "Building mandatory dungeon and progression structures");
        }
    }

    private sealed class MetadataPass(GenerationState state) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            RequireLayout(state);
            IWorldGenerationMetadataWorkspace metadata = context.Metadata ??
                throw new InvalidOperationException("Optimized generation requires semantic world metadata.");

            int spawnX = context.Workspace.WidthTiles / 2;
            int spawnY = Math.Max(1, state.SurfaceY[spawnX] - 1);
            int dungeonY = Math.Max(1, state.Dungeon.Top - 1);

            if (!metadata.TrySetSpawn(spawnX, spawnY))
                throw new InvalidOperationException("Optimized generator could not set the spawn point.");
            if (!metadata.TrySetDungeon(state.DungeonX, dungeonY))
                throw new InvalidOperationException("Optimized generator could not set the dungeon anchor.");
            if (!metadata.TrySetLayers(state.BaseSurface, state.RockLayer))
                throw new InvalidOperationException("Optimized generator could not set world layers.");

            if (context.Workspace is Workspace runtimeWorkspace)
            {
                float x = checked(spawnX * 16f);
                float y = checked(spawnY * 16f);
                if (!runtimeWorkspace.TryAddGeneratedTownNpc(
                        22,
                        "Andrew",
                        x,
                        y,
                        homeless: true,
                        homeTileX: spawnX,
                        homeTileY: spawnY,
                        townNpcVariationIndex: null,
                        homelessDespawn: false))
                {
                    throw new InvalidOperationException("Optimized generator could not register the starting Guide.");
                }
            }

            context.ReportProgress(1d, "Finalizing spawn, dungeon and layer metadata");
        }
    }

    private sealed class ValidationPass(GenerationState state) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            RequireLayout(state);

            ValidateRegionInsideWorld(context.Workspace, state.Dungeon);
            ValidateRegionInsideWorld(context.Workspace, state.Temple);
            ValidateRegionInsideWorld(context.Workspace, state.Hive);
            ValidateRegionInsideWorld(context.Workspace, state.Shimmer);
            foreach (FloatingIslandSpec island in state.FloatingIslands)
                ValidateRegionInsideWorld(context.Workspace, island.Region);

            RequireTileInRegion(context.Workspace, state.Dungeon, BlueDungeonBrick, "dungeon");
            RequireTileInRegion(context.Workspace, state.Temple, Tile(VanillaTileIds.LihzahrdBrick), "jungle temple");
            RequireTileInRegion(context.Workspace, state.Hive, Tile(VanillaTileIds.Hive), "jungle hive");
            RequireLiquidInRegion(context.Workspace, state.Shimmer, WorldGenerationLiquidKind.Shimmer, "Aether/Shimmer");
            RequireBiome(context.Workspace, state.Snow, Tile(VanillaTileIds.SnowBlock), "snow");
            RequireBiome(context.Workspace, state.Desert, Tile(VanillaTileIds.Sand), "desert");
            RequireBiome(context.Workspace, state.Jungle, Tile(VanillaTileIds.Mud), "jungle");
            RequireBiome(
                context.Workspace,
                state.Evil,
                context.Request.Options.Evil == WorldGenerationEvil.Crimson
                    ? Tile(VanillaTileIds.Crimstone)
                    : Tile(VanillaTileIds.Ebonstone),
                "world evil");

            int skyTiles = 0;
            int skyLimit = Math.Max(1, state.BaseSurface - 20);
            for (int y = 1; y < skyLimit; y++)
            {
                for (int x = state.OceanWidth; x < context.Workspace.WidthTiles - state.OceanWidth; x++)
                {
                    if (context.Workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                        (tile.Flags & WorldGenerationTileFlags.Active) != 0)
                    {
                        skyTiles++;
                    }
                }
            }

            if (skyTiles < state.FloatingIslands.Count * 30)
                throw new InvalidOperationException("Optimized generator validation found too little floating-island terrain.");

            RequireEdgeOcean(context.Workspace, state, left: true);
            RequireEdgeOcean(context.Workspace, state, left: false);
            RequireTileAnywhere(context.Workspace, Tile(VanillaTileIds.DemonAltar), "evil altar");
            RequireTileAnywhere(context.Workspace, Tile(VanillaTileIds.Hellforge), "Hellforge");
            RequireTileAnywhere(context.Workspace, Hellstone, "Hellstone");

            int underworldProbeY = Math.Min(context.Workspace.HeightTiles - 2, state.UnderworldTop + 10);
            if (!context.Workspace.TryGetTile(context.Workspace.WidthTiles / 2, underworldProbeY, out WorldGenerationTile underworld) ||
                (underworld.Flags & WorldGenerationTileFlags.Active) == 0)
            {
                throw new InvalidOperationException("Optimized generator validation found no solid underworld.");
            }

            context.ReportProgress(1d, "Validated mandatory optimized-world progression geography");
        }
    }

    private static void PaintBand(
        IWorldGenerationWorkspace workspace,
        GenerationState state,
        HorizontalBand band,
        ushort topType,
        ushort bodyType,
        int depth)
    {
        for (int x = band.Left; x <= band.Right; x++)
        {
            int top = state.SurfaceY[x];
            int bottom = Math.Min(workspace.HeightTiles - 2, top + depth);
            for (int y = top; y <= bottom; y++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) ||
                    (tile.Flags & WorldGenerationTileFlags.Active) == 0)
                {
                    continue;
                }

                SetTile(
                    workspace,
                    x,
                    y,
                    y == top ? topType : bodyType,
                    tile.Wall,
                    WorldGenerationTileFlags.Active);
            }
        }
    }

    private static void PaintUndergroundPatch(
        IWorldGenerationWorkspace workspace,
        HorizontalBand band,
        int centerY,
        ushort bodyType,
        ushort topType)
    {
        int radiusY = Math.Clamp(workspace.HeightTiles / 26, 8, 34);
        for (int x = band.Left; x <= band.Right; x++)
        {
            double nx = (x - band.Center) / (double)Math.Max(1, band.Width / 2);
            int localRadius = Math.Max(2, (int)Math.Round(radiusY * Math.Sqrt(Math.Max(0d, 1d - nx * nx))));
            for (int y = centerY - localRadius; y <= centerY + localRadius; y++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) ||
                    (tile.Flags & WorldGenerationTileFlags.Active) == 0)
                {
                    continue;
                }

                ushort type = y <= centerY - localRadius + 1 ? topType : bodyType;
                SetTile(workspace, x, y, type, tile.Wall, WorldGenerationTileFlags.Active);
            }
        }
    }

    private static void BuildOcean(IWorldGenerationWorkspace workspace, GenerationState state, bool left)
    {
        int width = workspace.WidthTiles;
        int height = workspace.HeightTiles;
        int oceanWidth = state.OceanWidth;
        ushort sand = Tile(VanillaTileIds.Sand);
        int waterLine = Math.Clamp(state.BaseSurface + 4, 8, height - 20);

        for (int local = 0; local < oceanWidth; local++)
        {
            int x = left ? local : width - 1 - local;
            double towardLand = local / (double)Math.Max(1, oceanWidth - 1);
            int floor = Math.Clamp(
                waterLine + 24 - (int)Math.Round(towardLand * 22d),
                waterLine + 2,
                state.RockLayer - 4);

            int floorDepth = Math.Clamp(height / 80, 12, 28);
            Geometry.BuildOceanColumn(workspace, x, Math.Max(1, waterLine - 8), floor, sand, floorDepth);
        }
    }

    private static void BuildUnderworld(IWorldGenerationWorkspace workspace, GenerationState state, ulong seed)
    {
        ushort stone = Tile(VanillaTileIds.Stone);
        int width = workspace.WidthTiles;
        int height = workspace.HeightTiles;
        for (int x = 1; x < width - 1; x++)
        {
            int ceiling = state.UnderworldTop + (int)Math.Round(
                FractalNoise1D(seed ^ 0x554E444552574F52UL, x, 44d, 3) * 5d);
            ceiling = Math.Clamp(ceiling, state.UnderworldTop - 6, state.UnderworldTop + 8);

            for (int y = ceiling; y < height - 1; y++)
                SetTile(workspace, x, y, stone, 0, WorldGenerationTileFlags.Active);

            int lavaTop = Math.Min(height - 8, ceiling + Math.Max(8, (height - ceiling) / 3));
            for (int y = lavaTop; y < Math.Min(height - 3, lavaTop + 4); y++)
            {
                if (((x + y) & 3) == 0)
                {
                    SetTile(
                        workspace,
                        x,
                        y,
                        0,
                        0,
                        WorldGenerationTileFlags.None,
                        byte.MaxValue,
                        WorldGenerationLiquidKind.Lava);
                }
            }
        }
    }

    private static void CarveOrganicCircle(
        IWorldGenerationWorkspace workspace,
        GenerationState state,
        int centerX,
        int centerY,
        double radius)
    {
        int r = Math.Max(2, (int)Math.Ceiling(radius));
        double rr = radius * radius;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                int x = centerX + dx;
                int y = centerY + dy;
                if (dx * dx + dy * dy > rr)
                    continue;
                if (state.IsProtected(x, y))
                    continue;
                if ((uint)x >= (uint)workspace.WidthTiles || (uint)y >= (uint)workspace.HeightTiles)
                    continue;

                SetTile(workspace, x, y, 0, 0, WorldGenerationTileFlags.None);
            }
        }
    }

    private static void BuildFloatingIsland(
        IWorldGenerationWorkspace workspace,
        ulong seed,
        FloatingIslandSpec island)
    {
        ushort grass = Tile(VanillaTileIds.Grass);
        ushort dirt = Tile(VanillaTileIds.Dirt);
        ushort stone = Tile(VanillaTileIds.Stone);
        int centerX = island.Region.CenterX;

        for (int dx = -island.RadiusX; dx <= island.RadiusX; dx++)
        {
            double nx = dx / (double)island.RadiusX;
            double arch = Math.Sqrt(Math.Max(0d, 1d - nx * nx));
            double noise = FractalNoise1D(seed ^ (0x49534C414E440000UL + (ulong)island.Ordinal), centerX + dx, 17d, 2);
            int top = island.SurfaceY + (int)Math.Round(noise * 2d - arch * 2d);
            int depth = Math.Max(2, (int)Math.Round(island.Depth * arch + noise * 1.5d));

            for (int localY = 0; localY < depth; localY++)
            {
                int y = top + localY;
                ushort type = localY == 0 ? grass : localY < 3 ? dirt : stone;
                SetTile(workspace, centerX + dx, y, type, 0, WorldGenerationTileFlags.Active);
            }
        }

        // Sky lakes are placed later by LandmarkProvider, which can carve a closed basin after
        // houses/progression content are known. Do not seed naked water strips here: once liquid
        // simulation starts, an uncontained strip simply drains off the island.
    }

    private static void PlaceOreFamily(
        IWorldGenerationContext context,
        GenerationState state,
        ushort oreType,
        int divisor,
        int minY,
        int maxY)
    {
        int width = context.Workspace.WidthTiles;
        int height = context.Workspace.HeightTiles;
        minY = Math.Clamp(minY, 2, height - 3);
        maxY = Math.Clamp(maxY, minY + 1, height - 2);
        int clusters = Math.Max(4, checked(width * Math.Max(1, maxY - minY)) / divisor);

        for (int i = 0; i < clusters; i++)
        {
            int x = NextRange(context.Random, state.OceanWidth + 4, width - state.OceanWidth - 4);
            int y = NextRange(context.Random, minY, maxY);
            int radius = NextRange(context.Random, 1, 4);
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (dx * dx + dy * dy > radius * radius + 1)
                        continue;
                    int tx = x + dx;
                    int ty = y + dy;
                    if (!context.Workspace.TryGetTile(tx, ty, out WorldGenerationTile tile) ||
                        (tile.Flags & WorldGenerationTileFlags.Active) == 0)
                    {
                        continue;
                    }

                    if (oreType != Hellstone &&
                        tile.Type != VanillaTileIds.Stone.Value &&
                        tile.Type != VanillaTileIds.Dirt.Value &&
                        tile.Type != VanillaTileIds.Mud.Value)
                    {
                        continue;
                    }

                    SetTile(context.Workspace, tx, ty, oreType, tile.Wall, WorldGenerationTileFlags.Active);
                }
            }
        }
    }

    private static void BuildDungeon(IWorldGenerationWorkspace workspace, GenerationState state)
    {
        ReservedRegion dungeon = state.Dungeon;
        ushort wall = checked((ushort)VanillaWallIds.BlueDungeonUnsafe.Value);
        int roomHalfWidth = Math.Max(8, dungeon.Width / 3);
        int corridorHalfWidth = 3;

        for (int y = dungeon.Top; y <= dungeon.Bottom; y++)
        {
            int roomIndex = (y - dungeon.Top) / 24;
            bool roomRow = (y - dungeon.Top) % 24 < 10;
            int halfWidth = roomRow ? roomHalfWidth : corridorHalfWidth;
            int offset = roomRow && (roomIndex & 1) == 1 ? roomHalfWidth / 3 : 0;
            int centerX = Math.Clamp(dungeon.CenterX + ((roomIndex & 1) == 0 ? -offset : offset), dungeon.Left + halfWidth + 1, dungeon.Right - halfWidth - 1);

            for (int x = centerX - halfWidth; x <= centerX + halfWidth; x++)
            {
                bool shell = x == centerX - halfWidth || x == centerX + halfWidth;
                if (shell)
                {
                    SetTile(workspace, x, y, BlueDungeonBrick, wall, WorldGenerationTileFlags.Active);
                }
                else
                {
                    SetTile(workspace, x, y, 0, wall, WorldGenerationTileFlags.None);
                }
            }
        }

        for (int x = dungeon.CenterX - roomHalfWidth; x <= dungeon.CenterX + roomHalfWidth; x++)
            SetTile(workspace, x, dungeon.Top, BlueDungeonBrick, wall, WorldGenerationTileFlags.Active);
    }

    private static void BuildHive(IWorldGenerationWorkspace workspace, ReservedRegion hive)
    {
        ushort tile = Tile(VanillaTileIds.Hive);
        ushort wall = checked((ushort)VanillaWallIds.HiveUnsafe.Value);
        double rx = Math.Max(2d, hive.Width / 2d);
        double ry = Math.Max(2d, hive.Height / 2d);

        for (int x = hive.Left; x <= hive.Right; x++)
        {
            for (int y = hive.Top; y <= hive.Bottom; y++)
            {
                double nx = (x - hive.CenterX) / rx;
                double ny = (y - hive.CenterY) / ry;
                double d = nx * nx + ny * ny;
                if (d > 1d)
                    continue;

                bool shell = d > 0.72d;
                SetTile(
                    workspace,
                    x,
                    y,
                    shell ? tile : (ushort)0,
                    wall,
                    shell ? WorldGenerationTileFlags.Active : WorldGenerationTileFlags.None,
                    shell ? (byte)0 : byte.MaxValue,
                    shell ? WorldGenerationLiquidKind.Water : WorldGenerationLiquidKind.Honey);
            }
        }
    }

    private static void BuildTemple(IWorldGenerationWorkspace workspace, ReservedRegion temple)
    {
        ushort brick = Tile(VanillaTileIds.LihzahrdBrick);
        ushort wall = checked((ushort)VanillaWallIds.LihzahrdBrickUnsafe.Value);
        int entranceY = temple.Top + temple.Height / 3;

        for (int x = temple.Left; x <= temple.Right; x++)
        {
            for (int y = temple.Top; y <= temple.Bottom; y++)
            {
                bool shell = x == temple.Left || x == temple.Right || y == temple.Top || y == temple.Bottom;
                bool divider = y == temple.CenterY && x > temple.Left + 4 && x < temple.Right - 4;
                if (shell || divider)
                {
                    SetTile(workspace, x, y, brick, wall, WorldGenerationTileFlags.Active);
                }
                else
                {
                    SetTile(workspace, x, y, 0, wall, WorldGenerationTileFlags.None);
                }
            }
        }

        for (int y = entranceY - 2; y <= entranceY + 2; y++)
            SetTile(workspace, temple.Left, y, 0, wall, WorldGenerationTileFlags.None);

        PlaceFramedObject(
            workspace,
            temple.CenterX - 1,
            temple.Bottom - 2,
            width: 3,
            height: 2,
            Tile(VanillaTileIds.LihzahrdAltar),
            styleOffsetX: 0,
            wall);
    }

    private static void BuildShimmer(IWorldGenerationWorkspace workspace, ReservedRegion aether)
    {
        ushort stone = Tile(VanillaTileIds.Stone);
        double rx = Math.Max(2d, aether.Width / 2d);
        double ry = Math.Max(2d, aether.Height / 2d);

        for (int x = aether.Left; x <= aether.Right; x++)
        {
            for (int y = aether.Top; y <= aether.Bottom; y++)
            {
                double nx = (x - aether.CenterX) / rx;
                double ny = (y - aether.CenterY) / ry;
                double d = nx * nx + ny * ny;
                if (d > 1d)
                    continue;

                if (d > 0.78d)
                    SetTile(workspace, x, y, stone, 0, WorldGenerationTileFlags.Active);
                else if (y >= aether.CenterY - 1)
                    SetTile(workspace, x, y, 0, 0, WorldGenerationTileFlags.None, byte.MaxValue, WorldGenerationLiquidKind.Shimmer);
                else
                    SetTile(workspace, x, y, 0, 0, WorldGenerationTileFlags.None);
            }
        }
    }

    private static void PlaceEvilAltar(
        IWorldGenerationWorkspace workspace,
        GenerationState state,
        WorldGenerationEvil evil)
    {
        int x = state.Evil.Center - 1;
        int surface = state.SurfaceY[state.Evil.Center];
        int y = Math.Clamp(surface - 2, 2, workspace.HeightTiles - 4);
        short styleOffsetX = evil == WorldGenerationEvil.Crimson ? (short)54 : (short)0;
        PlaceFramedObject(
            workspace,
            x,
            y,
            3,
            2,
            Tile(VanillaTileIds.DemonAltar),
            styleOffsetX);
    }

    private static void PlaceHellforge(IWorldGenerationWorkspace workspace, GenerationState state)
    {
        int x = workspace.WidthTiles / 2 - 1;
        int y = Math.Clamp(state.UnderworldTop + 8, 2, workspace.HeightTiles - 4);
        ClearRectangle(workspace, x - 2, y - 3, x + 4, y + 2);
        for (int floorX = x - 2; floorX <= x + 4; floorX++)
            SetTile(workspace, floorX, y + 2, Tile(VanillaTileIds.Stone), 0, WorldGenerationTileFlags.Active);
        PlaceFramedObject(
            workspace,
            x,
            y,
            3,
            2,
            Tile(VanillaTileIds.Hellforge),
            0);
    }

    private static void ClearRectangle(
        IWorldGenerationWorkspace workspace,
        int left,
        int top,
        int right,
        int bottom)
    {
        for (int x = Math.Max(0, left); x <= Math.Min(workspace.WidthTiles - 1, right); x++)
        {
            for (int y = Math.Max(0, top); y <= Math.Min(workspace.HeightTiles - 1, bottom); y++)
                SetTile(workspace, x, y, 0, 0, WorldGenerationTileFlags.None);
        }
    }

    private static void PlaceFramedObject(
        IWorldGenerationWorkspace workspace,
        int left,
        int top,
        int width,
        int height,
        ushort tileType,
        short styleOffsetX,
        ushort wall = 0)
    {
        for (int dx = 0; dx < width; dx++)
        {
            for (int dy = 0; dy < height; dy++)
            {
                SetTile(
                    workspace,
                    left + dx,
                    top + dy,
                    tileType,
                    wall,
                    WorldGenerationTileFlags.Active,
                    frameX: checked((short)(styleOffsetX + dx * 18)),
                    frameY: checked((short)(dy * 18)));
            }
        }
    }

    private static void ValidateRegionInsideWorld(IWorldGenerationWorkspace workspace, ReservedRegion region)
    {
        if (region.Left < 0 || region.Top < 0 ||
            region.Right >= workspace.WidthTiles || region.Bottom >= workspace.HeightTiles)
        {
            throw new InvalidOperationException(
                $"Optimized generator required region '{region.Role}' escaped the world bounds.");
        }
    }

    private static void RequireTileInRegion(
        IWorldGenerationWorkspace workspace,
        ReservedRegion region,
        ushort type,
        string role)
    {
        for (int x = region.Left; x <= region.Right; x++)
        {
            for (int y = region.Top; y <= region.Bottom; y++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    (tile.Flags & WorldGenerationTileFlags.Active) != 0 &&
                    tile.Type == type)
                {
                    return;
                }
            }
        }

        throw new InvalidOperationException($"Optimized generator validation could not find required {role} content.");
    }

    private static void RequireTileAnywhere(
        IWorldGenerationWorkspace workspace,
        ushort type,
        string role)
    {
        for (int x = 0; x < workspace.WidthTiles; x++)
        {
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    (tile.Flags & WorldGenerationTileFlags.Active) != 0 &&
                    tile.Type == type)
                {
                    return;
                }
            }
        }

        throw new InvalidOperationException($"Optimized generator validation could not find required {role} content.");
    }

    private static void RequireLiquidInRegion(
        IWorldGenerationWorkspace workspace,
        ReservedRegion region,
        WorldGenerationLiquidKind kind,
        string role)
    {
        for (int x = region.Left; x <= region.Right; x++)
        {
            for (int y = region.Top; y <= region.Bottom; y++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    tile.LiquidAmount > 0 &&
                    tile.LiquidKind == kind)
                {
                    return;
                }
            }
        }

        throw new InvalidOperationException($"Optimized generator validation could not find required {role} liquid.");
    }

    private static void RequireBiome(
        IWorldGenerationWorkspace workspace,
        HorizontalBand band,
        ushort type,
        string role)
    {
        int found = 0;
        int needed = Math.Max(12, band.Width / 6);
        for (int x = band.Left; x <= band.Right; x++)
        {
            for (int y = 1; y < workspace.HeightTiles - 1; y++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    (tile.Flags & WorldGenerationTileFlags.Active) != 0 &&
                    tile.Type == type)
                {
                    found++;
                    if (found >= needed)
                        return;
                }
            }
        }

        throw new InvalidOperationException(
            $"Optimized generator validation found insufficient {role} biome material ({found}/{needed}).");
    }

    private static void RequireEdgeOcean(IWorldGenerationWorkspace workspace, GenerationState state, bool left)
    {
        int water = 0;
        int maxX = Math.Min(state.OceanWidth, workspace.WidthTiles);
        for (int local = 0; local < maxX; local++)
        {
            int x = left ? local : workspace.WidthTiles - 1 - local;
            for (int y = Math.Max(1, state.BaseSurface - 8); y < Math.Min(workspace.HeightTiles - 1, state.RockLayer); y++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    tile.LiquidAmount > 0 &&
                    tile.LiquidKind == WorldGenerationLiquidKind.Water)
                {
                    water++;
                    if (water >= 64)
                        break;
                }
            }
        }

        if (water < 64)
            throw new InvalidOperationException($"Optimized generator validation found insufficient {(left ? "left" : "right")} ocean water.");

        Geometry.RequireOceanBasin(
            workspace,
            left,
            state.OceanWidth,
            Math.Max(1, state.BaseSurface - 12),
            Math.Min(workspace.HeightTiles - 2, state.RockLayer + 32),
            minimumSolidDepth: 8);
    }

    private static void SetTile(
        IWorldGenerationWorkspace workspace,
        int x,
        int y,
        ushort type,
        ushort wall,
        WorldGenerationTileFlags flags,
        byte liquidAmount = 0,
        WorldGenerationLiquidKind liquidKind = WorldGenerationLiquidKind.Water,
        short frameX = 0,
        short frameY = 0)
    {
        var tile = new WorldGenerationTile(
            Type: type,
            Wall: wall,
            FrameX: frameX,
            FrameY: frameY,
            Flags: flags,
            LiquidAmount: liquidAmount,
            TileColor: 0,
            WallColor: 0,
            Shape: 0,
            LiquidKind: liquidAmount == 0 ? WorldGenerationLiquidKind.Water : liquidKind);
        if (!workspace.TrySetTile(x, y, in tile))
        {
            throw new InvalidOperationException(
                $"Optimized generator could not write tile ({x}, {y}) inside {workspace.WidthTiles}x{workspace.HeightTiles}.");
        }
    }

    private static ushort Tile(TileTypeId id) => checked((ushort)id.Value);

    private static void RequireLayout(GenerationState state)
    {
        if (!state.LayoutReady)
            throw new InvalidOperationException("Optimized world-generation layout has not been prepared.");
    }

    private static int NextRange(IWorldGenerationRandom random, int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            return minInclusive;
        return minInclusive + random.NextInt32(maxExclusive - minInclusive);
    }

    private static double NextUnit(IWorldGenerationRandom random) =>
        random.NextUInt32() / ((double)uint.MaxValue + 1d);

    private static double FractalNoise1D(ulong seed, int x, double baseScale, int octaves)
    {
        double value = 0d;
        double amplitude = 1d;
        double total = 0d;
        double scale = baseScale;
        for (int octave = 0; octave < octaves; octave++)
        {
            value += ValueNoise1D(seed + (ulong)octave * 0x9E3779B97F4A7C15UL, x / scale) * amplitude;
            total += amplitude;
            amplitude *= 0.5d;
            scale *= 0.5d;
        }

        return total == 0d ? 0d : value / total;
    }

    private static double ValueNoise1D(ulong seed, double position)
    {
        int left = (int)Math.Floor(position);
        int right = left + 1;
        double fraction = position - left;
        double t = fraction * fraction * (3d - 2d * fraction);
        double a = HashSigned(seed, left);
        double b = HashSigned(seed, right);
        return a + (b - a) * t;
    }

    private static double HashSigned(ulong seed, int coordinate) =>
        Hash01(seed, coordinate) * 2d - 1d;

    private static double Hash01(ulong seed, int coordinate)
    {
        ulong value = unchecked(seed ^ (unchecked((ulong)(long)coordinate) * 0x9E3779B97F4A7C15UL));
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return (value >> 11) * (1d / (1UL << 53));
    }

    private static double SmoothStep(double value)
    {
        value = Math.Clamp(value, 0d, 1d);
        return value * value * (3d - 2d * value);
    }
}
