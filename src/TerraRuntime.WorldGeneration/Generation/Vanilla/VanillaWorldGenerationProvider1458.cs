using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration;

/// <summary>
/// Runtime-owned clean-room Terraria 1.4.5.8 world generator. The pass boundaries intentionally mirror the vanilla
/// pipeline so source-pinned passes can replace compatibility implementations independently as parity work advances.
/// </summary>
public sealed class VanillaWorldGenerationProvider1458 : IWorldGenerationProvider
{
    public static readonly WorldGeneratorId GeneratorId = new("terraruntime:vanilla");

    private static readonly WorldGenerationPassId TerrainId = new("terraria:1.4.5.8/Terrain");
    private static readonly WorldGenerationPassId BiomesId = new("terraria:1.4.5.8/Biomes");
    private static readonly WorldGenerationPassId CavesId = new("terraria:1.4.5.8/Caves");
    private static readonly WorldGenerationPassId OresId = new("terraria:1.4.5.8/Ores");
    private static readonly WorldGenerationPassId DungeonsId = new("terraria:1.4.5.8/Dungeon");
    private static readonly WorldGenerationPassId SecretSeedsId = new("terraria:1.4.5.8/SecretSeeds");
    private static readonly WorldGenerationPassId MetadataId = new("terraria:1.4.5.8/Metadata");

    public WorldGeneratorId Id => GeneratorId;

    public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        request.Validate();
        if (request.WidthTiles < 64 || request.HeightTiles < 64)
            throw new ArgumentOutOfRangeException(nameof(request), "Vanilla generation requires at least a 64x64 candidate workspace.");

        Add(builder, TerrainId, VanillaTerrainPass.Instance);
        Add(builder, BiomesId, VanillaBiomesPass.Instance, TerrainId);
        Add(builder, CavesId, VanillaCavesPass.Instance, BiomesId);
        Add(builder, OresId, VanillaOresPass.Instance, CavesId);
        Add(builder, DungeonsId, VanillaDungeonPass.Instance, OresId);
        Add(builder, SecretSeedsId, VanillaSecretSeedPass.Instance, DungeonsId);
        Add(builder, MetadataId, VanillaMetadataPass.Instance, SecretSeedsId);
    }

    private static void Add(
        IWorldGenerationPlanBuilder builder,
        WorldGenerationPassId id,
        IWorldGenerationPass pass,
        params WorldGenerationPassId[] requiredAfter) =>
        builder.Add(
            new WorldGenerationPassDescriptor(
                id,
                WorldGenerationRngMode.VanillaSharedRng,
                requiredAfter.Length == 0 ? null : requiredAfter),
            pass);

    private sealed class VanillaTerrainPass : IWorldGenerationPass
    {
        public static VanillaTerrainPass Instance { get; } = new();

        public void Execute(IWorldGenerationContext context)
        {
            IWorldGenerationVanillaRandom random = RequireVanillaRandom(context);
            VanillaWorldSeedProfile1458 seeds = ResolveSeedProfile(context);
            int width = context.Workspace.WidthTiles;
            int height = context.Workspace.HeightTiles;

            if (seeds.Has(VanillaSpecialWorldSeed1458.Skyblock))
            {
                GenerateSkyblock(context, random, width, height);
                context.ReportProgress(1d, "Generating Skyblock terrain");
                return;
            }

            int baseSurface = Math.Clamp((int)Math.Round(height * 0.28d), 12, height - 36);
            if (seeds.Has(VanillaSecretWorldSeed1458.SuchGreatHeights))
                baseSurface = Math.Max(8, baseSurface - Math.Max(4, height / 12));
            if (seeds.Has(VanillaSpecialWorldSeed1458.Remix))
                baseSurface = Math.Clamp((int)Math.Round(height * 0.50d), 12, height - 28);

            int rockLayer = Math.Clamp((int)Math.Round(height * 0.48d), baseSurface + 12, height - 20);
            int surface = baseSurface;
            int drift = 0;
            int progressStride = Math.Max(1, width / 100);

            for (int x = 0; x < width; x++)
            {
                if ((x & 63) == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();

                if ((x & 3) == 0)
                {
                    drift += random.Next(-1, 2);
                    drift = Math.Clamp(drift, -8, 8);
                    surface += drift == 0 ? random.Next(-1, 2) : Math.Sign(drift);
                    surface = Math.Clamp(surface, baseSurface - 14, baseSurface + 14);
                }

                int columnRock = Math.Clamp(rockLayer + random.Next(-4, 5), surface + 8, height - 16);
                for (int y = surface; y < height; y++)
                    SetSolid(context.Workspace, x, y, y < columnRock ? (ushort)0 : (ushort)1);

                if (x % progressStride == 0 || x == width - 1)
                    context.ReportProgress((x + 1d) / width, "Generating terrain");
            }
        }
    }

    private sealed class VanillaBiomesPass : IWorldGenerationPass
    {
        public static VanillaBiomesPass Instance { get; } = new();

        public void Execute(IWorldGenerationContext context)
        {
            IWorldGenerationVanillaRandom random = RequireVanillaRandom(context);
            VanillaWorldSeedProfile1458 seeds = ResolveSeedProfile(context);
            if (seeds.Has(VanillaSpecialWorldSeed1458.Skyblock))
                return;

            int width = context.Workspace.WidthTiles;
            int height = context.Workspace.HeightTiles;
            int edge = Math.Max(12, width / 14);
            int snowWidth = Math.Max(24, width / (seeds.Has(VanillaSecretWorldSeed1458.WinterIsComing) ? 3 : 7));
            int desertWidth = Math.Max(24, width / (seeds.Has(VanillaSecretWorldSeed1458.SandyBritches) ? 3 : 8));
            int jungleWidth = Math.Max(24, width / (seeds.Has(VanillaSecretWorldSeed1458.SaveTheRainforest) ? 3 : 7));
            int evilWidth = Math.Max(20, width / 11);

            int leftInterior = Math.Min(width - 1, edge + 4);
            int rightInterior = Math.Max(leftInterior + 1, width - edge - 4);
            int snowStart = PickBandStart(random, leftInterior, rightInterior, snowWidth);
            int jungleStart = PickNonOverlappingBandStart(random, leftInterior, rightInterior, jungleWidth, snowStart, snowWidth);
            int desertStart = PickNonOverlappingBandStart(random, leftInterior, rightInterior, desertWidth, jungleStart, jungleWidth);
            int evilStart = PickNonOverlappingBandStart(random, leftInterior, rightInterior, evilWidth, snowStart, snowWidth);

            PaintBiome(context, snowStart, snowWidth, 147, 161, topType: 147);
            PaintBiome(context, jungleStart, jungleWidth, 59, 59, topType: 60);
            PaintBiome(context, desertStart, desertWidth, 53, 53, topType: 53);

            bool crimson = context.Request.Options.Evil == WorldGenerationEvil.Crimson;
            ushort evilStone = crimson ? (ushort)203 : (ushort)25;
            ushort evilGrass = crimson ? (ushort)199 : (ushort)23;
            PaintBiome(context, evilStart, evilWidth, evilStone, evilStone, evilGrass);

            GenerateOcean(context, left: true, edge);
            GenerateOcean(context, left: false, edge);
            GenerateUnderworld(context, random, seeds, height);

            if (seeds.Has(VanillaSpecialWorldSeed1458.NotTheBees))
                ApplyNotTheBees(context, random);
            if (seeds.Has(VanillaSecretWorldSeed1458.Toadstool))
                PaintBiome(context, width / 2 - Math.Max(10, width / 16), Math.Max(20, width / 8), 59, 59, 70);

            context.ReportProgress(1d, "Generating biomes and oceans");
        }
    }

    private sealed class VanillaCavesPass : IWorldGenerationPass
    {
        public static VanillaCavesPass Instance { get; } = new();

        public void Execute(IWorldGenerationContext context)
        {
            IWorldGenerationVanillaRandom random = RequireVanillaRandom(context);
            VanillaWorldSeedProfile1458 seeds = ResolveSeedProfile(context);
            if (seeds.Has(VanillaSpecialWorldSeed1458.Skyblock))
                return;

            int width = context.Workspace.WidthTiles;
            int height = context.Workspace.HeightTiles;
            int caves = Math.Max(8, checked(width * height) / 2800);
            if (seeds.Has(VanillaSecretWorldSeed1458.MolePeople))
                caves *= 2;

            for (int i = 0; i < caves; i++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                int x = random.Next(8, width - 8);
                int y = random.Next(Math.Max(12, height / 4), Math.Max(13, height - height / 9));
                int length = random.Next(18, Math.Max(19, Math.Min(90, width / 8 + 18)));
                double angle = random.NextDouble() * Math.PI * 2d;
                double velocity = 0.7d + random.NextDouble() * 1.4d;
                double radius = 2.0d + random.NextDouble() * 3.5d;

                for (int step = 0; step < length; step++)
                {
                    CarveCircle(context.Workspace, (int)x, (int)y, radius);
                    angle += (random.NextDouble() - 0.5d) * 0.45d;
                    x = Math.Clamp((int)Math.Round(x + Math.Cos(angle) * velocity), 4, width - 5);
                    y = Math.Clamp((int)Math.Round(y + Math.Sin(angle) * velocity), 8, height - 10);
                    radius = Math.Clamp(radius + (random.NextDouble() - 0.5d) * 0.5d, 1.5d, 6d);
                }
            }

            context.ReportProgress(1d, "Carving caves");
        }
    }

    private sealed class VanillaOresPass : IWorldGenerationPass
    {
        public static VanillaOresPass Instance { get; } = new();

        public void Execute(IWorldGenerationContext context)
        {
            IWorldGenerationVanillaRandom random = RequireVanillaRandom(context);
            VanillaWorldSeedProfile1458 seeds = ResolveSeedProfile(context);
            if (seeds.Has(VanillaSpecialWorldSeed1458.Skyblock))
                return;

            PlaceOre(context, random, 7, densityDivisor: 4200, minDepthPercent: 36);
            PlaceOre(context, random, 6, densityDivisor: 4800, minDepthPercent: 42);
            PlaceOre(context, random, 9, densityDivisor: 5600, minDepthPercent: 48);
            PlaceOre(context, random, 8, densityDivisor: 6400, minDepthPercent: 54);
            context.ReportProgress(1d, "Placing ore tiers");
        }
    }

    private sealed class VanillaDungeonPass : IWorldGenerationPass
    {
        public static VanillaDungeonPass Instance { get; } = new();

        public void Execute(IWorldGenerationContext context)
        {
            IWorldGenerationVanillaRandom random = RequireVanillaRandom(context);
            VanillaWorldSeedProfile1458 seeds = ResolveSeedProfile(context);
            if (seeds.Has(VanillaSpecialWorldSeed1458.Skyblock))
                return;

            int width = context.Workspace.WidthTiles;
            int primaryX = random.Next(2) == 0 ? Math.Max(8, width / 10) : Math.Min(width - 9, width - width / 10);
            BuildDungeonShaft(context, primaryX);
            if (seeds.Has(VanillaSecretWorldSeed1458.DoubleDaringDangers))
                BuildDungeonShaft(context, width - 1 - primaryX);
            context.ReportProgress(1d, "Generating dungeon anchors");
        }
    }

    private sealed class VanillaSecretSeedPass : IWorldGenerationPass
    {
        public static VanillaSecretSeedPass Instance { get; } = new();

        public void Execute(IWorldGenerationContext context)
        {
            IWorldGenerationVanillaRandom random = RequireVanillaRandom(context);
            VanillaWorldSeedProfile1458 seeds = ResolveSeedProfile(context);

            if (seeds.Has(VanillaSecretWorldSeed1458.Planetoids) || seeds.Has(VanillaSecretWorldSeed1458.BeamMeUp))
                GenerateFloatingIslands(context, random, seeds.Has(VanillaSecretWorldSeed1458.Planetoids) ? 8 : 3);
            if (seeds.Has(VanillaSecretWorldSeed1458.Waterpark))
                FloodCaves(context, random, WorldGenerationLiquidKind.Water, 18);
            if (seeds.Has(VanillaSecretWorldSeed1458.FishMox))
                FloodCaves(context, random, WorldGenerationLiquidKind.Water, 8);
            if (seeds.Has(VanillaSpecialWorldSeed1458.NotTheBees))
                FloodCaves(context, random, WorldGenerationLiquidKind.Honey, 10);
            if (seeds.Has(VanillaSecretWorldSeed1458.DoesThatSparkle))
                FloodCaves(context, random, WorldGenerationLiquidKind.Shimmer, 2);

            if (seeds.Has(VanillaSecretWorldSeed1458.InvisiblePlane))
                MarkSparseBlocksInvisible(context, random);
            if (seeds.Has(VanillaSecretWorldSeed1458.Monochrome))
                PaintActiveTiles(context, 29);
            if (seeds.Has(VanillaSecretWorldSeed1458.RainbowRoad))
                PaintRainbowBand(context);

            context.ReportProgress(1d, "Applying secret seed modifiers");
        }
    }

    private sealed class VanillaMetadataPass : IWorldGenerationPass
    {
        public static VanillaMetadataPass Instance { get; } = new();

        public void Execute(IWorldGenerationContext context)
        {
            IWorldGenerationVanillaRandom random = RequireVanillaRandom(context);
            IWorldGenerationMetadataWorkspace metadata = context.Metadata ??
                throw new InvalidOperationException("Vanilla generation requires the runtime metadata workspace.");
            VanillaWorldSeedProfile1458 seeds = ResolveSeedProfile(context);
            int width = context.Workspace.WidthTiles;
            int height = context.Workspace.HeightTiles;

            int spawnX;
            if (seeds.Has(VanillaSecretWorldSeed1458.HowDidIGetHere))
                spawnX = random.Next(Math.Max(4, width / 8), Math.Max(5, width - width / 8));
            else
                spawnX = width / 2;

            int spawnSurface = FindSurface(context.Workspace, spawnX);
            int spawnY = Math.Clamp(spawnSurface - 1, 1, height - 2);
            if (seeds.Has(VanillaSpecialWorldSeed1458.Remix) && !seeds.Has(VanillaSpecialWorldSeed1458.Skyblock))
                spawnY = Math.Clamp(height - Math.Max(12, height / 10), 1, height - 2);

            int dungeonX = FindDungeonAnchor(context.Workspace);
            int dungeonY = Math.Clamp(FindSurface(context.Workspace, dungeonX) - 1, 1, height - 2);
            int surface = EstimateWorldSurface(context.Workspace);
            int rock = Math.Clamp(Math.Max(surface + 8, (int)Math.Round(height * 0.48d)), surface + 1, height - 2);

            if (!metadata.TrySetSpawn(spawnX, spawnY) ||
                !metadata.TrySetDungeon(dungeonX, dungeonY) ||
                !metadata.TrySetLayers(surface, rock))
            {
                throw new InvalidOperationException("Vanilla generator produced invalid world anchors.");
            }

            if (context.Workspace is RuntimeWorldGenerationWorkspace runtimeWorkspace)
                runtimeWorkspace.SetVanillaSeedProfile(seeds);

            context.ReportProgress(1d, "Finalizing vanilla world anchors");
        }
    }

    private static IWorldGenerationVanillaRandom RequireVanillaRandom(IWorldGenerationContext context) =>
        context.VanillaRandom ?? throw new InvalidOperationException("Vanilla pass executed without Terraria UnifiedRandom semantics.");

    private static VanillaWorldSeedProfile1458 ResolveSeedProfile(IWorldGenerationContext context)
    {
        WorldGenerationRequest request = context.Request;
        return VanillaWorldSeedResolver1458.Resolve(in request);
    }

    private static void GenerateSkyblock(IWorldGenerationContext context, IWorldGenerationVanillaRandom random, int width, int height)
    {
        // Compatibility fallback for the vanilla generator when the seed contains "skyblock".
        // The rich terraruntime:skyblock profile is the production skyblock path; this fallback ensures the
        // vanilla compatibility path still produces a valid, non-empty world with deterministic floating islands
        // instead of a single tiny patch that would leave spawn/dungeon heuristics with degraded inputs.
        int centerX = width / 2;
        int centerY = Math.Clamp(height / 4, 12, height - 24);
        int radiusX = Math.Clamp(width / 22, 6, 18);
        int radiusY = Math.Clamp(height / 28, 4, 10);
        for (int x = centerX - radiusX; x <= centerX + radiusX; x++)
        {
            double nx = (x - centerX) / (double)radiusX;
            int half = Math.Max(1, (int)Math.Round(radiusY * Math.Sqrt(Math.Max(0d, 1d - nx * nx))));
            for (int y = centerY - half; y <= centerY + half; y++)
            {
                ushort type = y <= centerY - half + 1 ? (ushort)2 : (ushort)(random.Next(5) == 0 ? 1 : 0);
                SetSolid(context.Workspace, x, y, type);
            }
        }

        // Two additional side islands ensure the compatibility world is not degenerate and
        // that density checks for skyblock lowTiles remain meaningful even on small synthetic worlds.
        int sideRadius = Math.Max(4, radiusX - 2);
        int sideHalf = Math.Max(2, radiusY - 1);
        int leftX = Math.Clamp(width / 4, sideRadius + 2, width - sideRadius - 2);
        int rightX = Math.Clamp(width * 3 / 4, sideRadius + 2, width - sideRadius - 2);
        int sideY = Math.Clamp(centerY + height / 10, 12, height - 24);
        for (int island = 0; island < 2; island++)
        {
            int islandX = island == 0 ? leftX : rightX;
            ushort stoneType = island == 0 ? (ushort)53 : (ushort)147;
            for (int x = islandX - sideRadius; x <= islandX + sideRadius; x++)
            {
                double nx = (x - islandX) / (double)sideRadius;
                int half = Math.Max(1, (int)Math.Round(sideHalf * Math.Sqrt(Math.Max(0d, 1d - nx * nx))));
                for (int y = sideY - half; y <= sideY + half; y++)
                    SetSolid(context.Workspace, x, y, stoneType);
            }
        }

        int lowerY = Math.Clamp(height * 3 / 4, centerY + 20, height - 12);
        for (int x = centerX - 4; x <= centerX + 4; x++)
            for (int y = lowerY; y < lowerY + 5; y++)
                SetSolid(context.Workspace, x, y, y == lowerY ? (ushort)57 : (ushort)1);
    }

    private static int PickBandStart(IWorldGenerationVanillaRandom random, int min, int max, int width)
    {
        int upper = Math.Max(min + 1, max - width);
        return upper <= min ? min : random.Next(min, upper);
    }

    private static int PickNonOverlappingBandStart(
        IWorldGenerationVanillaRandom random,
        int min,
        int max,
        int width,
        int avoidStart,
        int avoidWidth)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            int value = PickBandStart(random, min, max, width);
            if (value + width < avoidStart || value > avoidStart + avoidWidth)
                return value;
        }
        return PickBandStart(random, min, max, width);
    }

    private static void PaintBiome(IWorldGenerationContext context, int startX, int width, ushort dirtType, ushort stoneType, ushort topType)
    {
        int end = Math.Min(context.Workspace.WidthTiles, startX + width);
        for (int x = Math.Max(0, startX); x < end; x++)
        {
            int surface = FindSurface(context.Workspace, x);
            for (int y = surface; y < context.Workspace.HeightTiles - 8; y++)
            {
                if (!context.Workspace.TryGetTile(x, y, out WorldGenerationTile tile) || !IsActive(tile))
                    continue;
                ushort type = y == surface ? topType : (y < context.Workspace.HeightTiles * 0.48d ? dirtType : stoneType);
                SetSolid(context.Workspace, x, y, type, tile.TileColor);
            }
        }
    }

    private static void GenerateOcean(IWorldGenerationContext context, bool left, int edge)
    {
        int width = context.Workspace.WidthTiles;
        int height = context.Workspace.HeightTiles;
        int start = left ? 0 : width - edge;
        int end = left ? edge : width;
        int waterLine = Math.Clamp((int)Math.Round(height * 0.25d), 8, height - 20);
        for (int x = start; x < end; x++)
        {
            double t = left ? (edge - x) / (double)Math.Max(1, edge) : (x - start) / (double)Math.Max(1, edge);
            int floor = Math.Clamp(waterLine + 6 + (int)Math.Round((1d - t) * 12d), waterLine + 3, height - 12);
            for (int y = waterLine; y < floor; y++)
                SetLiquid(context.Workspace, x, y, WorldGenerationLiquidKind.Water, 255);
            for (int y = floor; y < Math.Min(height, floor + 10); y++)
                SetSolid(context.Workspace, x, y, 53);
        }
    }

    private static void GenerateUnderworld(IWorldGenerationContext context, IWorldGenerationVanillaRandom random, VanillaWorldSeedProfile1458 seeds, int height)
    {
        int start = Math.Clamp(height - Math.Max(14, height / 10), 1, height - 2);
        for (int x = 0; x < context.Workspace.WidthTiles; x++)
        {
            for (int y = start; y < height; y++)
            {
                if (random.Next(14) == 0 && y < height - 4)
                    SetLiquid(context.Workspace, x, y, WorldGenerationLiquidKind.Lava, 255);
                else
                    SetSolid(context.Workspace, x, y, random.Next(24) == 0 ? (ushort)58 : (ushort)57);
            }
        }
    }

    private static void ApplyNotTheBees(IWorldGenerationContext context, IWorldGenerationVanillaRandom random)
    {
        int start = context.Workspace.WidthTiles / 5;
        int end = context.Workspace.WidthTiles - start;
        for (int x = start; x < end; x++)
        {
            int surface = FindSurface(context.Workspace, x);
            for (int y = surface; y < Math.Min(context.Workspace.HeightTiles - 10, surface + context.Workspace.HeightTiles / 3); y++)
            {
                if (!context.Workspace.TryGetTile(x, y, out WorldGenerationTile tile) || !IsActive(tile))
                    continue;
                SetSolid(context.Workspace, x, y, y == surface ? (ushort)60 : (ushort)59);
                if (random.Next(180) == 0)
                    SetLiquid(context.Workspace, x, Math.Max(1, y - 1), WorldGenerationLiquidKind.Honey, 255);
            }
        }
    }

    private static void CarveCircle(IWorldGenerationWorkspace workspace, int centerX, int centerY, double radius)
    {
        int r = (int)Math.Ceiling(radius);
        double square = radius * radius;
        for (int x = centerX - r; x <= centerX + r; x++)
        {
            for (int y = centerY - r; y <= centerY + r; y++)
            {
                double dx = x - centerX;
                double dy = y - centerY;
                if (dx * dx + dy * dy <= square)
                    SetAir(workspace, x, y);
            }
        }
    }

    private static void PlaceOre(IWorldGenerationContext context, IWorldGenerationVanillaRandom random, ushort oreType, int densityDivisor, int minDepthPercent)
    {
        int width = context.Workspace.WidthTiles;
        int height = context.Workspace.HeightTiles;
        int count = Math.Max(3, checked(width * height) / densityDivisor);
        int minY = Math.Clamp(height * minDepthPercent / 100, 8, height - 10);
        for (int i = 0; i < count; i++)
        {
            int x = random.Next(6, width - 6);
            int y = random.Next(minY, height - 8);
            int radius = random.Next(2, 5);
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (dx * dx + dy * dy > radius * radius + random.Next(3))
                        continue;
                    int tx = x + dx;
                    int ty = y + dy;
                    if (context.Workspace.TryGetTile(tx, ty, out WorldGenerationTile tile) && IsActive(tile) && tile.Type is 0 or 1 or 25 or 203)
                        SetSolid(context.Workspace, tx, ty, oreType, tile.TileColor);
                }
            }
        }
    }

    private static void BuildDungeonShaft(IWorldGenerationContext context, int centerX)
    {
        int height = context.Workspace.HeightTiles;
        int surface = FindSurface(context.Workspace, centerX);
        int bottom = Math.Min(height - Math.Max(12, height / 10), surface + Math.Max(24, height / 3));
        for (int y = Math.Max(4, surface - 6); y < bottom; y++)
        {
            for (int x = centerX - 4; x <= centerX + 4; x++)
            {
                bool wall = x is var _ && (x == centerX - 4 || x == centerX + 4 || y % 18 is 0 or 1);
                if (wall)
                    SetSolid(context.Workspace, x, y, 41);
                else
                    SetAir(context.Workspace, x, y);
            }
        }
    }

    private static void GenerateFloatingIslands(IWorldGenerationContext context, IWorldGenerationVanillaRandom random, int count)
    {
        int width = context.Workspace.WidthTiles;
        int height = context.Workspace.HeightTiles;
        for (int i = 0; i < count; i++)
        {
            int cx = random.Next(12, width - 12);
            int cy = random.Next(8, Math.Max(9, height / 4));
            int rx = random.Next(5, Math.Max(6, Math.Min(15, width / 20)));
            int ry = Math.Max(3, rx / 3);
            for (int x = cx - rx; x <= cx + rx; x++)
            {
                double nx = (x - cx) / (double)rx;
                int half = Math.Max(1, (int)Math.Round(ry * Math.Sqrt(Math.Max(0d, 1d - nx * nx))));
                for (int y = cy - half; y <= cy + half; y++)
                    SetSolid(context.Workspace, x, y, y <= cy - half + 1 ? (ushort)2 : (ushort)0);
            }
        }
    }

    private static void FloodCaves(IWorldGenerationContext context, IWorldGenerationVanillaRandom random, WorldGenerationLiquidKind liquid, int attempts)
    {
        int width = context.Workspace.WidthTiles;
        int height = context.Workspace.HeightTiles;
        for (int i = 0; i < attempts; i++)
        {
            int cx = random.Next(6, width - 6);
            int cy = random.Next(Math.Max(8, height / 3), height - 10);
            int radius = random.Next(2, 6);
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                for (int y = cy - radius; y <= cy + radius; y++)
                {
                    if (context.Workspace.TryGetTile(x, y, out WorldGenerationTile tile) && !IsActive(tile))
                        SetLiquid(context.Workspace, x, y, liquid, 255);
                }
            }
        }
    }

    private static void MarkSparseBlocksInvisible(IWorldGenerationContext context, IWorldGenerationVanillaRandom random)
    {
        for (int i = 0; i < Math.Max(8, context.Workspace.WidthTiles / 20); i++)
        {
            int x = random.Next(2, context.Workspace.WidthTiles - 2);
            int y = random.Next(2, context.Workspace.HeightTiles - 2);
            if (!context.Workspace.TryGetTile(x, y, out WorldGenerationTile tile) || !IsActive(tile))
                continue;
            WorldGenerationTile updated = tile with { Flags = tile.Flags | WorldGenerationTileFlags.InvisibleBlock };
            context.Workspace.TrySetTile(x, y, in updated);
        }
    }

    private static void PaintActiveTiles(IWorldGenerationContext context, byte paint)
    {
        for (int x = 0; x < context.Workspace.WidthTiles; x++)
        {
            for (int y = 0; y < context.Workspace.HeightTiles; y++)
            {
                if (!context.Workspace.TryGetTile(x, y, out WorldGenerationTile tile) || !IsActive(tile))
                    continue;
                WorldGenerationTile updated = tile with { TileColor = paint };
                context.Workspace.TrySetTile(x, y, in updated);
            }
        }
    }

    private static void PaintRainbowBand(IWorldGenerationContext context)
    {
        byte[] paints = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
        for (int x = 0; x < context.Workspace.WidthTiles; x++)
        {
            int surface = FindSurface(context.Workspace, x);
            if (!context.Workspace.TryGetTile(x, surface, out WorldGenerationTile tile) || !IsActive(tile))
                continue;
            WorldGenerationTile updated = tile with { TileColor = paints[x % paints.Length] };
            context.Workspace.TrySetTile(x, surface, in updated);
        }
    }

    private static int EstimateWorldSurface(IWorldGenerationWorkspace workspace)
    {
        int[] samples = new int[Math.Min(33, workspace.WidthTiles)];
        for (int i = 0; i < samples.Length; i++)
        {
            int x = samples.Length == 1 ? 0 : i * (workspace.WidthTiles - 1) / (samples.Length - 1);
            samples[i] = FindSurface(workspace, x);
        }
        Array.Sort(samples);
        return Math.Clamp(samples[samples.Length / 2], 1, workspace.HeightTiles - 3);
    }

    private static int FindDungeonAnchor(IWorldGenerationWorkspace workspace)
    {
        int left = Math.Max(4, workspace.WidthTiles / 10);
        int right = Math.Min(workspace.WidthTiles - 5, workspace.WidthTiles - workspace.WidthTiles / 10);
        for (int x = 1; x < workspace.WidthTiles - 1; x++)
        {
            for (int y = 1; y < workspace.HeightTiles - 1; y++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) && tile.Type == 41 && IsActive(tile))
                    return x < workspace.WidthTiles / 2 ? left : right;
            }
        }
        return left;
    }

    private static int FindSurface(IWorldGenerationWorkspace workspace, int x)
    {
        x = Math.Clamp(x, 0, workspace.WidthTiles - 1);
        for (int y = 1; y < workspace.HeightTiles - 1; y++)
        {
            if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) && IsActive(tile))
                return y;
        }
        return Math.Clamp(workspace.HeightTiles / 2, 1, workspace.HeightTiles - 2);
    }

    private static bool IsActive(in WorldGenerationTile tile) =>
        (tile.Flags & WorldGenerationTileFlags.Active) != 0;

    private static void SetSolid(IWorldGenerationWorkspace workspace, int x, int y, ushort type, byte tileColor = 0)
    {
        if ((uint)x >= (uint)workspace.WidthTiles || (uint)y >= (uint)workspace.HeightTiles)
            return;
        var tile = new WorldGenerationTile(
            type,
            0,
            0,
            0,
            WorldGenerationTileFlags.Active,
            0,
            tileColor,
            0,
            0,
            WorldGenerationLiquidKind.Water);
        if (!workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Vanilla generator could not write tile ({x}, {y}).");
    }

    private static void SetAir(IWorldGenerationWorkspace workspace, int x, int y)
    {
        if ((uint)x >= (uint)workspace.WidthTiles || (uint)y >= (uint)workspace.HeightTiles)
            return;
        var tile = new WorldGenerationTile(0, 0, 0, 0, WorldGenerationTileFlags.None, 0, 0, 0, 0, WorldGenerationLiquidKind.Water);
        if (!workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Vanilla generator could not clear tile ({x}, {y}).");
    }

    private static void SetLiquid(IWorldGenerationWorkspace workspace, int x, int y, WorldGenerationLiquidKind liquid, byte amount)
    {
        if ((uint)x >= (uint)workspace.WidthTiles || (uint)y >= (uint)workspace.HeightTiles)
            return;
        var tile = new WorldGenerationTile(0, 0, 0, 0, WorldGenerationTileFlags.None, amount, 0, 0, 0, liquid);
        if (!workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Vanilla generator could not place liquid at ({x}, {y}).");
    }
}
