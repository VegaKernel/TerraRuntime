using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Tenth source-backed Terraria 1.4.5.8 world-generation overlay. It advances the ordinary canonical pipeline from
/// Sunflowers through Mushrooms. The stage is deliberately bounded before Gems In Ice Biome: this block owns late
/// vegetation and ambient plant decoration only and therefore needs no additional persistence side table.
/// </summary>
public sealed class SourceBackedVegetation1458 : IWorldGenerationProvider
{
    internal static readonly WorldGenerationPassId SunflowersId = new("terraria:1.4.5.8/Sunflowers");
    internal static readonly WorldGenerationPassId PlantingTreesId = new("terraria:1.4.5.8/PlantingTrees");
    internal static readonly WorldGenerationPassId HerbsId = new("terraria:1.4.5.8/Herbs");
    internal static readonly WorldGenerationPassId DyePlantsId = new("terraria:1.4.5.8/DyePlants");
    internal static readonly WorldGenerationPassId WebsAndHoneyId = new("terraria:1.4.5.8/WebsAndHoney");
    internal static readonly WorldGenerationPassId WeedsId = new("terraria:1.4.5.8/Weeds");
    internal static readonly WorldGenerationPassId GlowingMushroomsAndJunglePlantsId = new("terraria:1.4.5.8/GlowingMushroomsAndJunglePlants");
    internal static readonly WorldGenerationPassId JunglePlantsId = new("terraria:1.4.5.8/JunglePlants");
    internal static readonly WorldGenerationPassId VinesId = new("terraria:1.4.5.8/Vines");
    internal static readonly WorldGenerationPassId FlowersId = new("terraria:1.4.5.8/Flowers");
    internal static readonly WorldGenerationPassId MushroomsId = new("terraria:1.4.5.8/Mushrooms");

    private static readonly WorldGenerationPassId SecretSeedsId = new("terraria:1.4.5.8/SecretSeeds");
    private readonly SourceBackedStartingNpc1458 baseline = new();

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

        var state = new VegetationState1458();
        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id != SecretSeedsId)
            {
                builder.Add(entry.Descriptor, entry.Pass);
                continue;
            }

            Add(builder, SunflowersId, SourceBackedStartingNpc1458.GuideId,
                new VegetationPass1458(VegetationStage1458.Sunflowers, state));
            Add(builder, PlantingTreesId, SunflowersId,
                new VegetationPass1458(VegetationStage1458.PlantingTrees, state));
            Add(builder, HerbsId, PlantingTreesId,
                new VegetationPass1458(VegetationStage1458.Herbs, state));
            Add(builder, DyePlantsId, HerbsId,
                new VegetationPass1458(VegetationStage1458.DyePlants, state));
            Add(builder, WebsAndHoneyId, DyePlantsId,
                new VegetationPass1458(VegetationStage1458.WebsAndHoney, state));
            Add(builder, WeedsId, WebsAndHoneyId,
                new VegetationPass1458(VegetationStage1458.Weeds, state));
            Add(builder, GlowingMushroomsAndJunglePlantsId, WeedsId,
                new VegetationPass1458(VegetationStage1458.GlowingMushroomsAndJunglePlants, state));
            Add(builder, JunglePlantsId, GlowingMushroomsAndJunglePlantsId,
                new VegetationPass1458(VegetationStage1458.JunglePlants, state));
            Add(builder, VinesId, JunglePlantsId,
                new VegetationPass1458(VegetationStage1458.Vines, state));
            Add(builder, FlowersId, VinesId,
                new VegetationPass1458(VegetationStage1458.Flowers, state));
            Add(builder, MushroomsId, FlowersId,
                new VegetationPass1458(VegetationStage1458.Mushrooms, state));

            builder.Add(CloneDescriptor(entry.Descriptor, [MushroomsId]), entry.Pass);
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

internal enum VegetationStage1458 : byte
{
    Sunflowers,
    PlantingTrees,
    Herbs,
    DyePlants,
    WebsAndHoney,
    Weeds,
    GlowingMushroomsAndJunglePlants,
    JunglePlants,
    Vines,
    Flowers,
    Mushrooms
}

internal sealed class VegetationState1458
{
    public VanillaWorldGenerationBootstrapState1458? Bootstrap { get; private set; }
    public double WorldSurface { get; private set; }
    public double RockLayer { get; private set; }
    public int UnderworldTop { get; private set; }

    public void EnsureInitialized(IWorldGenerationContext context, Workspace workspace)
    {
        if (Bootstrap is not null)
            return;

        Bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Vegetation generation requires Reset bootstrap state.");
        if (context.Metadata is null || !context.Metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Vegetation generation requires source-backed Terrain layers.");

        WorldSurface = layers.WorldSurface;
        RockLayer = layers.RockLayer;
        UnderworldTop = Math.Clamp(workspace.HeightTiles - 200, (int)RockLayer + 120, workspace.HeightTiles - 90);
    }
}

internal sealed class VegetationPass1458 : IWorldGenerationPass
{
    private const ushort Grass = 2;
    private const ushort Plants = 3;
    private const ushort Sunflower = 27;
    private const ushort Cobweb = 51;
    private const ushort Vines = 52;
    private const ushort Mud = 59;
    private const ushort JungleGrass = 60;
    private const ushort JunglePlants = 61;
    private const ushort JungleVines = 62;
    private const ushort MushroomGrass = 70;
    private const ushort MushroomPlants = 71;
    private const ushort Plants2 = 73;
    private const ushort JunglePlants2 = 74;
    private const ushort Herbs = 82;
    private const ushort SnowBlock = 147;
    private const ushort DyePlants = 227;
    private const ushort PlantDetritus = 233;

    private static readonly int[] FlowerStyles = [6, 7, 9, 10, 12, 14, 19];

    private readonly VegetationStage1458 stage;
    private readonly VegetationState1458 state;

    public VegetationPass1458(
        VegetationStage1458 stage,
        VegetationState1458 state)
    {
        this.stage = stage;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Workspace workspace = context.Workspace as Workspace ??
            throw new InvalidOperationException("Vegetation generation requires Workspace.");
        state.EnsureInitialized(context, workspace);
        var grid = new RuntimeGrid(workspace);
        var random = new VanillaRandom(
            context.VanillaRandom ??
            throw new InvalidOperationException("Vegetation generation requires shared UnifiedRandom semantics."));

        switch (stage)
        {
            case VegetationStage1458.Sunflowers:
                ApplySunflowers(context, grid, random);
                break;
            case VegetationStage1458.PlantingTrees:
                ApplyPlantingTrees(context, grid, random);
                break;
            case VegetationStage1458.Herbs:
                ApplyHerbs(context, grid, random);
                break;
            case VegetationStage1458.DyePlants:
                ApplyDyePlants(context, grid, random);
                break;
            case VegetationStage1458.WebsAndHoney:
                ApplyWebsAndHoney(context, grid, random);
                break;
            case VegetationStage1458.Weeds:
                ApplyWeeds(context, grid, random);
                break;
            case VegetationStage1458.GlowingMushroomsAndJunglePlants:
                ApplyGlowingMushroomsAndJunglePlants(context, grid, random);
                break;
            case VegetationStage1458.JunglePlants:
                ApplyJunglePlants(context, grid, random);
                break;
            case VegetationStage1458.Vines:
                ApplyVines(context, grid, random);
                break;
            case VegetationStage1458.Flowers:
                ApplyFlowers(context, grid, random);
                break;
            case VegetationStage1458.Mushrooms:
                ApplyMushrooms(context, grid, random);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void ApplySunflowers(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int target = grid.Width switch
        {
            <= 4200 => 32,
            <= 6400 => 48,
            _ => 64
        };
        int minX = Math.Max(bootstrap.LeftBeachEnd + 50, 10);
        int maxX = Math.Min(bootstrap.RightBeachStart - 50, grid.Width - 12);
        int minY = Math.Max(12, (int)state.WorldSurface - 160);
        int maxY = Math.Min(grid.Height - 8, (int)state.WorldSurface + 150);
        int placed = 0;

        for (int attempt = 0; attempt < target * 120 && placed < target; attempt++)
        {
            if ((attempt & 127) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int left = random.Next(minX, maxX - 1);
            int floor = grid.FindFirstActiveY(left, minY, maxY);
            if (floor >= maxY || grid.At(left, floor).Type != Grass || grid.At(left + 1, floor).Type != Grass)
                continue;
            int top = floor - 4;
            if (!grid.IsEmptyRectangle(left, top, 2, 4))
                continue;

            for (int dx = 0; dx < 2; dx++)
                for (int dy = 0; dy < 4; dy++)
                {
                    ref WorldTile tile = ref grid.At(left + dx, top + dy);
                    SetPlant(ref tile, Sunflower, dx * 18, dy * 18);
                }
            placed++;
        }

        context.ReportProgress(1d, $"Planting sunflowers ({placed}/{target})");
    }

    private void ApplyPlantingTrees(IWorldGenerationContext context, RuntimeGrid grid, VanillaRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int target = grid.Width switch
        {
            <= 4200 => 120,
            <= 6400 => 180,
            _ => 240
        };
        int minX = Math.Max(bootstrap.LeftBeachEnd + 25, 8);
        int maxX = Math.Min(bootstrap.RightBeachStart - 25, grid.Width - 8);
        int minY = Math.Max(18, (int)state.WorldSurface - 180);
        int maxY = Math.Min(grid.Height - 10, (int)state.WorldSurface + 175);
        int placed = 0;

        for (int attempt = 0; attempt < target * 80 && placed < target; attempt++)
        {
            if ((attempt & 127) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(minX, maxX);
            int floor = grid.FindFirstActiveY(x, minY, maxY);
            if (floor >= maxY || floor < 22)
                continue;

            ushort ground = grid.At(x, floor).Type;
            if (ground is not (Grass or JungleGrass or SnowBlock))
                continue;
            if (grid.HasFrameImportantNearby(x, floor, 5, 3))
                continue;

            if (TreeGrower1458.TryGrow(grid.Store, x, floor, random.Source))
                placed++;
        }

        context.ReportProgress(1d, $"Planting framed surface trees ({placed}/{target})");
    }

    private void ApplyHerbs(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int target = Math.Max(80, grid.Width / 18);
        int minY = Math.Max(8, (int)state.WorldSurface - 160);
        int maxY = Math.Min(state.UnderworldTop - 20, grid.Height - 4);
        int placed = 0;

        for (int attempt = 0; attempt < target * 90 && placed < target; attempt++)
        {
            if ((attempt & 255) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(5, grid.Width - 5);
            int probe = random.Next(minY, maxY);
            int floor = grid.FindFirstActiveY(x, probe, Math.Min(grid.Height - 2, probe + 80));
            if (floor >= grid.Height - 2 || !CanPlaceSinglePlant(grid, x, floor - 1))
                continue;

            ushort ground = grid.At(x, floor).Type;
            int herbStyle = SelectHerbStyle(ground, random);
            if (herbStyle < 0)
                continue;

            SetPlant(ref grid.At(x, floor - 1), Herbs, herbStyle * 18, 0);
            placed++;
        }

        context.ReportProgress(1d, $"Planting herbs ({placed}/{target})");
    }

    private void ApplyDyePlants(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int target = grid.Width switch
        {
            <= 4200 => 18,
            <= 6400 => 27,
            _ => 36
        };
        int minY = Math.Max(8, (int)state.WorldSurface - 180);
        int maxY = Math.Min(state.UnderworldTop - 20, grid.Height - 4);
        int placed = 0;

        for (int attempt = 0; attempt < target * 220 && placed < target; attempt++)
        {
            if ((attempt & 127) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(8, grid.Width - 8);
            int probe = random.Next(minY, maxY);
            int floor = grid.FindFirstActiveY(x, probe, Math.Min(grid.Height - 2, probe + 90));
            if (floor >= grid.Height - 2 || !CanPlaceSinglePlant(grid, x, floor - 1))
                continue;
            ushort ground = grid.At(x, floor).Type;
            int style = SelectDyePlantStyle(ground, random);
            if (style < 0 || grid.HasNearbyType(x, floor - 1, DyePlants, 12, 8))
                continue;

            SetPlant(ref grid.At(x, floor - 1), DyePlants, style * 18, 0);
            placed++;
        }

        context.ReportProgress(1d, $"Placing dye plants ({placed}/{target})");
    }

    private void ApplyWebsAndHoney(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int webAttempts = Math.Max(300, grid.Width / 3);
        int minY = Math.Clamp((int)state.RockLayer + 20, 20, state.UnderworldTop - 80);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 25);
        int webs = 0;
        int honeyCells = 0;

        for (int i = 0; i < webAttempts; i++)
        {
            if ((i & 511) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(3, grid.Width - 3);
            int y = random.Next(minY, maxY);
            ref WorldTile tile = ref grid.At(x, y);
            if (tile.IsActive || tile.LiquidAmount != 0 || !grid.HasSolidNeighbor(x, y))
                continue;
            SetPlant(ref tile, Cobweb, 0, 0);
            webs++;
        }

        int jungleHalfWidth = Math.Max(250, grid.Width / 10);
        int left = Math.Max(15, bootstrap.JungleOriginX - jungleHalfWidth);
        int right = Math.Min(grid.Width - 15, bootstrap.JungleOriginX + jungleHalfWidth);
        int pools = grid.Width switch
        {
            <= 4200 => 14,
            <= 6400 => 20,
            _ => 28
        };
        for (int pool = 0; pool < pools; pool++)
        {
            int cx = random.Next(left, right);
            int cy = random.Next(minY, maxY);
            int rx = random.Next(3, 7);
            int ry = random.Next(2, 5);
            for (int x = cx - rx; x <= cx + rx; x++)
                for (int y = cy - ry; y <= cy + ry; y++)
                {
                    if (!grid.Contains(x, y) || (x - cx) * (x - cx) * ry * ry + (y - cy) * (y - cy) * rx * rx > rx * rx * ry * ry)
                        continue;
                    ref WorldTile tile = ref grid.At(x, y);
                    if (tile.IsActive || tile.LiquidAmount != 0)
                        continue;
                    tile.LiquidAmount = 255;
                    tile.LiquidKind = WorldLiquidKind.Honey;
                    honeyCells++;
                }
        }

        context.ReportProgress(1d, $"Adding webs and honey ({webs} webs, {honeyCells} honey cells)");
    }

    private void ApplyWeeds(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int target = Math.Max(180, grid.Width / 9);
        int minY = Math.Max(8, (int)state.WorldSurface - 170);
        int maxY = Math.Min(grid.Height - 3, (int)state.WorldSurface + 180);
        int placed = 0;

        for (int attempt = 0; attempt < target * 30 && placed < target; attempt++)
        {
            if ((attempt & 511) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(4, grid.Width - 4);
            int floor = grid.FindFirstActiveY(x, minY, maxY);
            if (floor >= maxY || grid.At(x, floor).Type != Grass || !CanPlaceSinglePlant(grid, x, floor - 1))
                continue;

            int style = random.Next(6);
            SetPlant(ref grid.At(x, floor - 1), Plants, style * 18, 0);
            placed++;
        }

        context.ReportProgress(1d, $"Planting surface weeds ({placed}/{target})");
    }

    private void ApplyGlowingMushroomsAndJunglePlants(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int attempts = Math.Max(1000, grid.Width * 2);
        int minY = Math.Clamp((int)state.WorldSurface + 20, 20, state.UnderworldTop - 80);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 20);
        int mushrooms = 0;
        int jungle = 0;

        for (int i = 0; i < attempts; i++)
        {
            if ((i & 1023) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(4, grid.Width - 4);
            int probe = random.Next(minY, maxY);
            int floor = grid.FindFirstActiveY(x, probe, Math.Min(grid.Height - 2, probe + 70));
            if (floor >= grid.Height - 2 || !CanPlaceSinglePlant(grid, x, floor - 1))
                continue;

            ushort ground = grid.At(x, floor).Type;
            if (ground == MushroomGrass)
            {
                SetPlant(ref grid.At(x, floor - 1), MushroomPlants, random.Next(5) * 18, 0);
                mushrooms++;
            }
            else if (ground == JungleGrass)
            {
                SetPlant(ref grid.At(x, floor - 1), JunglePlants, random.Next(6) * 18, 0);
                jungle++;
            }
        }

        context.ReportProgress(1d, $"Growing glowing mushrooms and jungle plants ({mushrooms}/{jungle})");
    }

    private void ApplyJunglePlants(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int target = Math.Max(100, grid.Width / 20);
        int halfWidth = Math.Max(260, grid.Width / 9);
        int left = Math.Max(5, bootstrap.JungleOriginX - halfWidth);
        int right = Math.Min(grid.Width - 5, bootstrap.JungleOriginX + halfWidth);
        int minY = Math.Clamp((int)state.WorldSurface + 10, 15, state.UnderworldTop - 60);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 20);
        int placed = 0;

        for (int attempt = 0; attempt < target * 80 && placed < target; attempt++)
        {
            if ((attempt & 255) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(left, right);
            int probe = random.Next(minY, maxY);
            int floor = grid.FindFirstActiveY(x, probe, Math.Min(grid.Height - 2, probe + 80));
            if (floor >= grid.Height - 2 || grid.At(x, floor).Type != JungleGrass || !CanPlaceSinglePlant(grid, x, floor - 1))
                continue;

            int roll = random.Next(12);
            ushort type;
            int style;
            if (roll == 0)
            {
                type = PlantDetritus;
                style = random.Next(8);
            }
            else if (roll < 4)
            {
                type = JunglePlants2;
                style = random.Next(8);
            }
            else
            {
                type = JunglePlants;
                style = random.Next(10, 23);
            }

            // Tall/detritus identities have richer framing in vanilla. Keep placement one-cell and source-shaped until
            // their exact framing helpers are ported; this preserves section validity without inventing adjacent cells.
            SetPlant(ref grid.At(x, floor - 1), type, style * 18, 0);
            placed++;
        }

        context.ReportProgress(1d, $"Decorating underground jungle plants ({placed}/{target})");
    }

    private void ApplyVines(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int attempts = Math.Max(700, grid.Width);
        int maxSurfaceY = Math.Min(grid.Height - 12, (int)state.WorldSurface + 100);
        int maxJungleY = Math.Min(state.UnderworldTop - 10, grid.Height - 12);
        int grown = 0;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            if ((attempt & 511) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(3, grid.Width - 3);
            int upperBound = random.Next(3) == 0 ? maxJungleY : maxSurfaceY;
            int floor = grid.FindFirstActiveY(x, 8, upperBound);
            if (floor >= upperBound)
                continue;

            ushort ground = grid.At(x, floor).Type;
            ushort vine = ground switch
            {
                Grass => Vines,
                JungleGrass => JungleVines,
                _ => 0
            };
            if (vine == 0)
                continue;

            int y = floor + 1;
            if (y >= grid.Height - 2 || grid.At(x, y).IsActive)
            {
                // For exposed ceilings, the supporting grass is above an air cell rather than below it.
                int ceilingY = grid.FindLastActiveYBeforeAir(x, 8, upperBound);
                if (ceilingY < 1 || ceilingY + 1 >= grid.Height - 2)
                    continue;
                ground = grid.At(x, ceilingY).Type;
                vine = ground switch
                {
                    Grass => Vines,
                    JungleGrass => JungleVines,
                    _ => 0
                };
                if (vine == 0 || grid.At(x, ceilingY + 1).IsActive)
                    continue;
                y = ceilingY + 1;
            }

            int length = random.Next(2, 9);
            for (int step = 0; step < length && y + step < grid.Height - 2; step++)
            {
                ref WorldTile tile = ref grid.At(x, y + step);
                if (tile.IsActive || tile.LiquidAmount > 0)
                    break;
                SetPlant(ref tile, vine, 0, 0);
                grown++;
            }
        }

        context.ReportProgress(1d, $"Growing vines ({grown} cells)");
    }

    private void ApplyFlowers(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int target = Math.Max(120, grid.Width / 16);
        int minY = Math.Max(8, (int)state.WorldSurface - 170);
        int maxY = Math.Min(grid.Height - 3, (int)state.WorldSurface + 170);
        int placed = 0;

        for (int attempt = 0; attempt < target * 40 && placed < target; attempt++)
        {
            if ((attempt & 255) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(4, grid.Width - 4);
            int floor = grid.FindFirstActiveY(x, minY, maxY);
            if (floor >= maxY || grid.At(x, floor).Type != Grass || !CanPlaceSinglePlant(grid, x, floor - 1))
                continue;
            int style = FlowerStyles[random.Next(FlowerStyles.Length)];
            SetPlant(ref grid.At(x, floor - 1), Plants, style * 18, 0);
            placed++;
        }

        context.ReportProgress(1d, $"Planting surface flowers ({placed}/{target})");
    }

    private void ApplyMushrooms(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int target = Math.Max(60, grid.Width / 35);
        int minY = Math.Max(8, (int)state.WorldSurface - 170);
        int maxY = Math.Min(grid.Height - 3, (int)state.WorldSurface + 170);
        int placed = 0;

        for (int attempt = 0; attempt < target * 80 && placed < target; attempt++)
        {
            if ((attempt & 255) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(4, grid.Width - 4);
            int floor = grid.FindFirstActiveY(x, minY, maxY);
            if (floor >= maxY || grid.At(x, floor).Type != Grass || !CanPlaceSinglePlant(grid, x, floor - 1))
                continue;
            SetPlant(ref grid.At(x, floor - 1), Plants, 8 * 18, 0);
            placed++;
        }

        context.ReportProgress(1d, $"Planting surface mushrooms ({placed}/{target})");
    }

    private static int SelectHerbStyle(ushort ground, IRandom random) =>
        ground switch
        {
            Grass => 0,                  // Daybloom
            JungleGrass => 1,           // Moonglow
            Mud => 2,                   // Blinkroot-compatible soil
            SnowBlock => 6,             // Shiverthorn
            53 => 4,                    // Sand -> Waterleaf
            57 => 5,                    // Ash -> Fireblossom
            25 or 203 or 199 => 3,      // evil stone/grass families -> Deathweed
            _ => -1
        };

    private static int SelectDyePlantStyle(ushort ground, IRandom random) =>
        ground switch
        {
            Grass => random.Next(2, 5),
            JungleGrass => random.Next(0, 2),
            53 => random.Next(5, 7),
            57 => 7,
            _ => -1
        };

    private static bool CanPlaceSinglePlant(RuntimeGrid grid, int x, int y)
    {
        if (!grid.Contains(x, y) || y + 1 >= grid.Height)
            return false;
        WorldTile tile = grid.At(x, y);
        if (tile.IsActive || tile.LiquidAmount != 0)
            return false;
        return !VanillaWorldFrameImportance326.IsFrameImportant(grid.At(x, y + 1).Type) || grid.At(x, y + 1).IsActive;
    }

    private static void SetPlant(ref WorldTile tile, ushort type, int frameX, int frameY)
    {
        tile.Type = type;
        tile.Flags |= WorldTileFlags.Active;
        tile.FrameX = checked((short)frameX);
        tile.FrameY = checked((short)frameY);
        tile.Shape = 0;
        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
    }

    private VanillaWorldGenerationBootstrapState1458 RequireBootstrap() =>
        state.Bootstrap ?? throw new InvalidOperationException("Vegetation pass executed before bootstrap initialization.");

    private interface IRandom
    {
        int Next();
        int Next(int max);
        int Next(int min, int max);
        double NextDouble();
    }

    private sealed class VanillaRandom(IWorldGenerationVanillaRandom inner) : IRandom
    {
        public IWorldGenerationVanillaRandom Source => inner;
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
        public WorldTileStore Store => store;

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

        public int FindLastActiveYBeforeAir(int x, int minY, int maxExclusive)
        {
            int max = Math.Min(Height - 1, maxExclusive);
            for (int y = Math.Max(1, minY); y < max; y++)
            {
                if (At(x, y).IsActive && !At(x, y + 1).IsActive)
                    return y;
            }
            return -1;
        }

        public bool IsEmptyRectangle(int left, int top, int width, int height)
        {
            if (left < 1 || top < 1 || left + width >= Width - 1 || top + height >= Height - 1)
                return false;
            for (int x = left; x < left + width; x++)
                for (int y = top; y < top + height; y++)
                {
                    WorldTile tile = At(x, y);
                    if (tile.IsActive || tile.LiquidAmount != 0)
                        return false;
                }
            return true;
        }

        public bool HasFrameImportantNearby(int centerX, int centerY, int radiusX, int radiusY)
        {
            int left = Math.Max(1, centerX - radiusX);
            int right = Math.Min(Width - 2, centerX + radiusX);
            int top = Math.Max(1, centerY - radiusY);
            int bottom = Math.Min(Height - 2, centerY + radiusY);
            for (int x = left; x <= right; x++)
                for (int y = top; y <= bottom; y++)
                {
                    WorldTile tile = At(x, y);
                    if (tile.IsActive && VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                        return true;
                }
            return false;
        }

        public bool HasSolidNeighbor(int x, int y) =>
            At(x - 1, y).IsActive || At(x + 1, y).IsActive || At(x, y - 1).IsActive || At(x, y + 1).IsActive;

        public bool HasNearbyType(int centerX, int centerY, ushort type, int radiusX, int radiusY)
        {
            int left = Math.Max(1, centerX - radiusX);
            int right = Math.Min(Width - 2, centerX + radiusX);
            int top = Math.Max(1, centerY - radiusY);
            int bottom = Math.Min(Height - 2, centerY + radiusY);
            for (int x = left; x <= right; x++)
                for (int y = top; y <= bottom; y++)
                {
                    WorldTile tile = At(x, y);
                    if (tile.IsActive && tile.Type == type)
                        return true;
                }
            return false;
        }
    }
}
