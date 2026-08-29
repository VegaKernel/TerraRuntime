using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Runtime-owned Terraria 1.4.5.8 vanilla generation profile. This is deliberately pass-shaped from the start:
/// source-verified passes can replace the current clean-room implementations one coherent slice at a time without
/// changing the selectable generator identity or the isolated candidate/persistence transaction.
/// </summary>
public sealed class VanillaWorldGenerationProvider1458 : IWorldGenerationProvider
{
    public static readonly WorldGeneratorId GeneratorId = new("terraruntime:vanilla");

    internal static readonly WorldGenerationPassId TerrainPassId = new("terraruntime:vanilla/terrain");
    internal static readonly WorldGenerationPassId CavesPassId = new("terraruntime:vanilla/caves");
    internal static readonly WorldGenerationPassId OceansPassId = new("terraruntime:vanilla/oceans");
    internal static readonly WorldGenerationPassId UnderworldPassId = new("terraruntime:vanilla/underworld");
    internal static readonly WorldGenerationPassId MetadataPassId = new("terraruntime:vanilla/metadata");

    private const ushort Dirt = 0;
    private const ushort Stone = 1;

    public WorldGeneratorId Id => GeneratorId;

    public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        request.Validate();
        if (request.WidthTiles < 96 || request.HeightTiles < 64)
            throw new ArgumentOutOfRangeException(nameof(request), "The vanilla generator requires at least 96x64 tiles.");

        VanillaWorldSeedProfile1458 profile = VanillaWorldSeedProfile1458.Parse(request.SeedText, request.Seed);

        builder.Add(VanillaDescriptor(TerrainPassId), new TerrainPass(profile));
        builder.Add(
            VanillaDescriptor(CavesPassId, requiredAfter: [TerrainPassId]),
            new CavesPass(profile));
        builder.Add(
            VanillaDescriptor(OceansPassId, requiredAfter: [CavesPassId]),
            new OceansPass(profile));
        builder.Add(
            VanillaDescriptor(UnderworldPassId, requiredAfter: [OceansPassId]),
            new UnderworldPass(profile));
        builder.Add(
            VanillaDescriptor(MetadataPassId, requiredAfter: [UnderworldPassId]),
            new MetadataPass(profile));
    }

    private static WorldGenerationPassDescriptor VanillaDescriptor(
        WorldGenerationPassId id,
        WorldGenerationPassId[]? requiredAfter = null) =>
        new(id, WorldGenerationRngMode.VanillaSharedRng, requiredAfter: requiredAfter);

    private sealed class TerrainPass(VanillaWorldSeedProfile1458 profile) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            IWorldGenerationVanillaRandom random = RequireVanillaRandom(context);
            int width = context.Workspace.WidthTiles;
            int height = context.Workspace.HeightTiles;

            if (profile.HasFlag(VanillaWorldSeedFlags1458.SkyblockWorld))
            {
                GenerateSkyblockTerrain(context, random);
                return;
            }

            int baseSurface = Math.Clamp((int)Math.Round(height * 0.28d), 18, height - 36);
            int rockLayer = Math.Clamp((int)Math.Round(height * 0.48d), baseSurface + 12, height - 18);
            int surface = baseSurface;
            int drift = Math.Max(2, height / 80);
            int progressStride = Math.Max(1, width / 100);

            for (int x = 0; x < width; x++)
            {
                if ((x & 63) == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();

                if ((x & 3) == 0)
                {
                    surface += random.Next(-1, 2);
                    if (random.Next(7) == 0)
                        surface += random.Next(-drift, drift + 1);
                    surface = Math.Clamp(surface, baseSurface - drift * 2, baseSurface + drift * 2);
                }

                for (int y = surface; y < height; y++)
                {
                    ushort type = y < rockLayer ? Dirt : Stone;
                    SetSolid(context, x, y, type);
                }

                if (x % progressStride == 0 || x == width - 1)
                    context.ReportProgress((x + 1d) / width, "Vanilla terrain");
            }
        }

        private static void GenerateSkyblockTerrain(IWorldGenerationContext context, IWorldGenerationVanillaRandom random)
        {
            int width = context.Workspace.WidthTiles;
            int height = context.Workspace.HeightTiles;
            int centerX = width / 2;
            int centerY = Math.Clamp((int)Math.Round(height * 0.32d), 16, height - 32);
            int radiusX = Math.Clamp(width / 32, 12, 42);
            int radiusY = Math.Clamp(height / 28, 6, 18);

            for (int x = centerX - radiusX; x <= centerX + radiusX; x++)
            {
                double nx = (x - centerX) / (double)radiusX;
                double span = Math.Sqrt(Math.Max(0d, 1d - nx * nx));
                int top = centerY - Math.Max(2, (int)Math.Round(span * radiusY * 0.35d));
                int bottom = centerY + Math.Max(3, (int)Math.Round(span * radiusY));
                int jitter = random.Next(-1, 2);
                for (int y = Math.Max(0, top + jitter); y <= Math.Min(height - 1, bottom); y++)
                    SetSolid(context, x, y, y < centerY + 2 ? Dirt : Stone);
            }

            context.ReportProgress(1d, "Vanilla Skyblock terrain");
        }
    }

    private sealed class CavesPass(VanillaWorldSeedProfile1458 profile) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            if (profile.HasFlag(VanillaWorldSeedFlags1458.SkyblockWorld))
            {
                context.ReportProgress(1d, "Skyblock caves skipped");
                return;
            }

            IWorldGenerationVanillaRandom random = RequireVanillaRandom(context);
            int width = context.Workspace.WidthTiles;
            int height = context.Workspace.HeightTiles;
            int caveCount = Math.Max(12, width * height / 22_000);

            for (int cave = 0; cave < caveCount; cave++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                double x = random.Next(width / 12, width - width / 12);
                double y = random.Next(Math.Max(8, height / 3), Math.Max(9, height - height / 9));
                double vx = random.NextDouble() * 2d - 1d;
                double vy = random.NextDouble() - 0.15d;
                int steps = random.Next(18, Math.Max(19, Math.Min(120, width / 18 + height / 12)));
                double radius = random.Next(2, Math.Max(3, Math.Min(8, height / 80 + 3)));

                for (int step = 0; step < steps; step++)
                {
                    CarveEllipse(context, (int)x, (int)y, radius, Math.Max(1.5d, radius * 0.65d));
                    x += vx * 2.4d;
                    y += vy * 1.7d;
                    vx = Math.Clamp(vx + (random.NextDouble() - 0.5d) * 0.35d, -1.4d, 1.4d);
                    vy = Math.Clamp(vy + (random.NextDouble() - 0.5d) * 0.25d, -1d, 1d);
                    radius = Math.Clamp(radius + (random.NextDouble() - 0.5d) * 0.8d, 1.8d, 8d);
                    if (x < 8 || x >= width - 8 || y < height / 4 || y >= height - 8)
                        break;
                }

                context.ReportProgress((cave + 1d) / caveCount, "Vanilla caves");
            }
        }
    }

    private sealed class OceansPass(VanillaWorldSeedProfile1458 profile) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            if (profile.HasFlag(VanillaWorldSeedFlags1458.SkyblockWorld))
            {
                context.ReportProgress(1d, "Skyblock oceans skipped");
                return;
            }

            int width = context.Workspace.WidthTiles;
            int height = context.Workspace.HeightTiles;
            int oceanWidth = Math.Clamp(width / 18, 18, Math.Max(18, width / 8));
            int waterLine = Math.Clamp((int)Math.Round(height * 0.25d), 10, height - 30);
            int floor = Math.Clamp((int)Math.Round(height * 0.36d), waterLine + 6, height - 20);

            GenerateOcean(context, 0, oceanWidth, waterLine, floor, left: true);
            GenerateOcean(context, width - oceanWidth, width, waterLine, floor, left: false);
            context.ReportProgress(1d, "Vanilla oceans");
        }

        private static void GenerateOcean(
            IWorldGenerationContext context,
            int startX,
            int endX,
            int waterLine,
            int floor,
            bool left)
        {
            int span = Math.Max(1, endX - startX);
            for (int x = startX; x < endX; x++)
            {
                double edgeDistance = left ? x - startX : endX - 1 - x;
                double t = edgeDistance / Math.Max(1d, span - 1d);
                int localFloor = floor - (int)Math.Round(t * (floor - waterLine) * 0.65d);
                localFloor = Math.Max(waterLine + 3, localFloor);

                for (int y = 0; y < localFloor; y++)
                {
                    if (y >= waterLine)
                        SetLiquid(context, x, y, WorldGenerationLiquidKind.Water, byte.MaxValue);
                    else
                        SetAir(context, x, y);
                }
            }
        }
    }

    private sealed class UnderworldPass(VanillaWorldSeedProfile1458 profile) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            if (profile.HasFlag(VanillaWorldSeedFlags1458.SkyblockWorld))
            {
                context.ReportProgress(1d, "Skyblock underworld skipped");
                return;
            }

            IWorldGenerationVanillaRandom random = RequireVanillaRandom(context);
            int width = context.Workspace.WidthTiles;
            int height = context.Workspace.HeightTiles;
            int roof = Math.Clamp(height - Math.Max(28, height / 9), height / 2, height - 16);
            int lavaLine = Math.Clamp(height - Math.Max(12, height / 22), roof + 8, height - 4);

            for (int x = 0; x < width; x++)
            {
                if ((x & 63) == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();

                int localRoof = Math.Clamp(roof + random.Next(-3, 4), roof - 5, roof + 5);
                for (int y = localRoof; y < height - 2; y++)
                {
                    if (y >= lavaLine && random.Next(5) != 0)
                        SetLiquid(context, x, y, WorldGenerationLiquidKind.Lava, byte.MaxValue);
                    else
                        SetAir(context, x, y);
                }
            }

            context.ReportProgress(1d, "Vanilla underworld cavity");
        }
    }

    private sealed class MetadataPass(VanillaWorldSeedProfile1458 profile) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            IWorldGenerationMetadataWorkspace metadata = context.Metadata ??
                throw new InvalidOperationException("Vanilla generation requires the runtime metadata workspace.");
            IWorldGenerationVanillaRandom random = RequireVanillaRandom(context);

            int width = context.Workspace.WidthTiles;
            int height = context.Workspace.HeightTiles;
            int spawnX = width / 2;
            int spawnY = FindSurface(context, spawnX);

            if (profile.HasModifier(VanillaSecretSeedModifier1458.HowDidIGetHere))
            {
                spawnX = random.Next(Math.Max(8, width / 12), Math.Max(9, width - width / 12));
                spawnY = FindSurface(context, spawnX);
            }

            bool dungeonOnLeft = random.Next(2) == 0;
            int dungeonX = dungeonOnLeft ? Math.Max(8, width / 10) : Math.Min(width - 9, width - width / 10);
            int dungeonY = Math.Max(1, FindSurface(context, dungeonX));

            double worldSurface = Math.Clamp(height * 0.30d, 1d, height - 3d);
            double rockLayer = Math.Clamp(height * 0.50d, worldSurface + 1d, height - 2d);
            if (profile.HasFlag(VanillaWorldSeedFlags1458.SkyblockWorld))
            {
                worldSurface = Math.Clamp(height * 0.34d, 1d, height - 3d);
                rockLayer = Math.Clamp(height * 0.62d, worldSurface + 1d, height - 2d);
                dungeonX = Math.Max(1, width / 8);
                dungeonY = Math.Max(1, (int)worldSurface);
            }

            if (!metadata.TrySetSpawn(spawnX, spawnY) ||
                !metadata.TrySetDungeon(dungeonX, dungeonY) ||
                !metadata.TrySetLayers(worldSurface, rockLayer))
            {
                throw new InvalidOperationException("Vanilla generation produced invalid world anchors.");
            }

            if (context.Workspace is RuntimeWorldGenerationWorkspace runtimeWorkspace)
                runtimeWorkspace.SetVanillaSeedProfile(profile);

            context.ReportProgress(1d, "Vanilla metadata");
        }

        private static int FindSurface(IWorldGenerationContext context, int x)
        {
            for (int y = 1; y < context.Workspace.HeightTiles - 1; y++)
            {
                if (context.Workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    (tile.Flags & WorldGenerationTileFlags.Active) != 0)
                {
                    return Math.Max(1, y - 1);
                }
            }

            return Math.Clamp(context.Workspace.HeightTiles / 3, 1, context.Workspace.HeightTiles - 2);
        }
    }

    private static IWorldGenerationVanillaRandom RequireVanillaRandom(IWorldGenerationContext context) =>
        context.VanillaRandom ?? throw new InvalidOperationException(
            "Vanilla generation pass was not supplied the Terraria 1.4.5.8 RNG surface.");

    private static void SetSolid(IWorldGenerationContext context, int x, int y, ushort type)
    {
        var tile = new WorldGenerationTile(
            Type: type,
            Wall: 0,
            FrameX: 0,
            FrameY: 0,
            Flags: WorldGenerationTileFlags.Active,
            LiquidAmount: 0,
            TileColor: 0,
            WallColor: 0,
            Shape: 0,
            LiquidKind: WorldGenerationLiquidKind.Water);
        if (!context.Workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Vanilla generator could not write tile ({x}, {y}).");
    }

    private static void SetAir(IWorldGenerationContext context, int x, int y)
    {
        WorldGenerationTile tile = default;
        if (!context.Workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Vanilla generator could not clear tile ({x}, {y}).");
    }

    private static void SetLiquid(
        IWorldGenerationContext context,
        int x,
        int y,
        WorldGenerationLiquidKind kind,
        byte amount)
    {
        var tile = new WorldGenerationTile(
            Type: 0,
            Wall: 0,
            FrameX: 0,
            FrameY: 0,
            Flags: WorldGenerationTileFlags.None,
            LiquidAmount: amount,
            TileColor: 0,
            WallColor: 0,
            Shape: 0,
            LiquidKind: kind);
        if (!context.Workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Vanilla generator could not write liquid tile ({x}, {y}).");
    }

    private static void CarveEllipse(
        IWorldGenerationContext context,
        int centerX,
        int centerY,
        double radiusX,
        double radiusY)
    {
        int minX = Math.Max(0, (int)Math.Floor(centerX - radiusX));
        int maxX = Math.Min(context.Workspace.WidthTiles - 1, (int)Math.Ceiling(centerX + radiusX));
        int minY = Math.Max(0, (int)Math.Floor(centerY - radiusY));
        int maxY = Math.Min(context.Workspace.HeightTiles - 1, (int)Math.Ceiling(centerY + radiusY));

        double rx2 = radiusX * radiusX;
        double ry2 = radiusY * radiusY;
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                double dx = x - centerX;
                double dy = y - centerY;
                if (dx * dx / rx2 + dy * dy / ry2 <= 1d)
                    SetAir(context, x, y);
            }
        }
    }
}
