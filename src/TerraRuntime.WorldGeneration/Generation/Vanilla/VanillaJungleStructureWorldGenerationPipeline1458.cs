using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration;

/// <summary>
/// Fourth source-backed Terraria 1.4.5.8 world-generation overlay. It extends the ordinary canonical pipeline from
/// Pyramids through the first Settle Liquids pass. The old compatibility Biomes pass becomes a no-op barrier because
/// source-backed Beaches already owns the remaining ocean geometry.
/// </summary>
public sealed class SourceBackedVanillaWorldGenerationJungleStructures1458 : IWorldGenerationProvider
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

    private readonly SourceBackedVanillaWorldGenerationDungeonPipeline1458 baseline = new();

    public WorldGeneratorId Id => baseline.Id;

    public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var capture = new CapturePlanBuilder();
        baseline.BuildPlan(in request, capture);

        WorldGenerationRequest requestCopy = request;
        VanillaWorldSeedProfile1458 profile = VanillaWorldSeedResolver1458.Resolve(in requestCopy);
        if (!profile.IsDefault || !VanillaTerrainPass1458.IsCanonicalWorldSize(request.WidthTiles, request.HeightTiles))
        {
            capture.Replay(builder);
            return;
        }

        var state = new VanillaJungleStructureWorldGenerationState1458();

        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id == BiomesId)
            {
                builder.Add(
                    CloneDescriptor(entry.Descriptor, WorldGenerationRngMode.IsolatedDeterministic),
                    VanillaSourceBackedBiomesCompatibilityBarrier1458.Instance);
                continue;
            }

            if (entry.Descriptor.Id == SecretSeedsId)
            {
                Add(builder, DirtRockWallRunnerId, SourceBackedVanillaWorldGenerationDungeonPipeline1458.PyramidsId,
                    new VanillaJungleStructureWorldGenerationPass1458(
                        VanillaJungleStructureWorldGenerationStage1458.DirtRockWallRunner, state));
                Add(builder, LivingTreesId, DirtRockWallRunnerId,
                    new VanillaJungleStructureWorldGenerationPass1458(
                        VanillaJungleStructureWorldGenerationStage1458.LivingTrees, state));
                Add(builder, WoodTreeWallsId, LivingTreesId,
                    new VanillaJungleStructureWorldGenerationPass1458(
                        VanillaJungleStructureWorldGenerationStage1458.WoodTreeWalls, state));
                Add(builder, AltarsId, WoodTreeWallsId,
                    new VanillaJungleStructureWorldGenerationPass1458(
                        VanillaJungleStructureWorldGenerationStage1458.Altars, state));
                Add(builder, WetJungleId, AltarsId,
                    new VanillaJungleStructureWorldGenerationPass1458(
                        VanillaJungleStructureWorldGenerationStage1458.WetJungle, state));
                Add(builder, JungleTempleId, WetJungleId,
                    new VanillaJungleStructureWorldGenerationPass1458(
                        VanillaJungleStructureWorldGenerationStage1458.JungleTemple, state));
                Add(builder, HivesId, JungleTempleId,
                    new VanillaJungleStructureWorldGenerationPass1458(
                        VanillaJungleStructureWorldGenerationStage1458.Hives, state));
                Add(builder, JungleChestsId, HivesId,
                    new VanillaJungleStructureWorldGenerationPass1458(
                        VanillaJungleStructureWorldGenerationStage1458.JungleChests, state));
                Add(builder, SettleLiquidsId, JungleChestsId,
                    new VanillaJungleStructureWorldGenerationPass1458(
                        VanillaJungleStructureWorldGenerationStage1458.SettleLiquids, state));

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

internal enum VanillaJungleStructureWorldGenerationStage1458 : byte
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

internal sealed class VanillaJungleStructureWorldGenerationState1458
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

    public void EnsureInitialized(IWorldGenerationContext context, RuntimeWorldGenerationWorkspace workspace)
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

internal sealed class VanillaJungleStructureWorldGenerationPass1458 : IWorldGenerationPass
{
    private const ushort Dirt = 0;
    private const ushort Stone = 1;
    private const ushort Grass = 2;
    private const ushort DemonAltar = 26;
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

    private readonly VanillaJungleStructureWorldGenerationStage1458 stage;
    private readonly VanillaJungleStructureWorldGenerationState1458 state;

    public VanillaJungleStructureWorldGenerationPass1458(
        VanillaJungleStructureWorldGenerationStage1458 stage,
        VanillaJungleStructureWorldGenerationState1458 state)
    {
        this.stage = stage;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RuntimeWorldGenerationWorkspace workspace = context.Workspace as RuntimeWorldGenerationWorkspace ??
            throw new InvalidOperationException(
                "Source-backed jungle-structure Terraria generation requires RuntimeWorldGenerationWorkspace.");
        state.EnsureInitialized(context, workspace);
        var grid = new RuntimeGrid(workspace);
        var random = new VanillaRandom(
            context.VanillaRandom ??
            throw new InvalidOperationException(
                "Source-backed jungle-structure Terraria generation requires shared UnifiedRandom semantics."));

        switch (stage)
        {
            case VanillaJungleStructureWorldGenerationStage1458.DirtRockWallRunner:
                ApplyDirtRockWallRunner(context, grid, random);
                break;
            case VanillaJungleStructureWorldGenerationStage1458.LivingTrees:
                ApplyLivingTrees(context, grid, random);
                break;
            case VanillaJungleStructureWorldGenerationStage1458.WoodTreeWalls:
                ApplyWoodTreeWalls(context, grid);
                break;
            case VanillaJungleStructureWorldGenerationStage1458.Altars:
                ApplyAltars(context, grid, random);
                break;
            case VanillaJungleStructureWorldGenerationStage1458.WetJungle:
                ApplyWetJungle(context, grid, random);
                break;
            case VanillaJungleStructureWorldGenerationStage1458.JungleTemple:
                ApplyJungleTemple(context, grid, random);
                break;
            case VanillaJungleStructureWorldGenerationStage1458.Hives:
                ApplyHives(context, grid, random);
                break;
            case VanillaJungleStructureWorldGenerationStage1458.JungleChests:
                ApplyJungleChests(context, grid, random);
                break;
            case VanillaJungleStructureWorldGenerationStage1458.SettleLiquids:
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

    private void ApplyLivingTrees(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int target = grid.Width switch
        {
            <= 4200 => 3,
            <= 6400 => 4,
            _ => 5
        };
        int placed = 0;
        int attempts = target * 80;

        for (int attempt = 0; attempt < attempts && placed < target; attempt++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(bootstrap.LeftBeachEnd + 140, bootstrap.RightBeachStart - 140);
            if (!IsLivingTreeSiteAllowed(grid, bootstrap, x))
                continue;

            int surface = grid.FindFirstActiveY(
                x,
                25,
                Math.Min(grid.Height, Math.Max((int)state.WorldSurface + 100, (int)state.RockLayer)));
            if (surface >= grid.Height - 20 || surface < 50)
                continue;

            ref WorldTile ground = ref grid.At(x, surface);
            if (!ground.IsActive || !IsNatural(ground.Type))
                continue;

            int trunkHeight = random.Next(28, 48);
            int trunkHalfWidth = random.Next(2, 4);
            if (surface - trunkHeight - 18 < 4)
                continue;

            BuildLivingTree(grid, random, x, surface, trunkHeight, trunkHalfWidth);
            placed++;
        }

        context.ReportProgress(1d, $"Growing source-shaped living trees ({placed}/{target})");
    }

    private bool IsLivingTreeSiteAllowed(RuntimeGrid grid, VanillaWorldGenerationBootstrapState1458 bootstrap, int x)
    {
        int spawn = grid.Width / 2;
        if (Math.Abs(x - spawn) < 220)
            return false;
        if (Math.Abs(x - bootstrap.JungleOriginX) < Math.Max(320, grid.Width / 11))
            return false;
        if (x > bootstrap.SnowOriginLeft - 120 && x < bootstrap.SnowOriginRight + 120)
            return false;
        if (Math.Abs(x - bootstrap.DungeonLocation) < 220)
            return false;
        return true;
    }

    private static void BuildLivingTree(
        RuntimeGrid grid,
        IRandom random,
        int centerX,
        int surface,
        int height,
        int halfWidth)
    {
        int top = surface - height;
        for (int y = top; y <= surface + 3; y++)
        {
            int taper = y < top + height / 3 ? 1 : 0;
            int left = centerX - Math.Max(1, halfWidth - taper);
            int right = centerX + Math.Max(1, halfWidth - taper);
            for (int x = left; x <= right; x++)
            {
                if (!grid.Contains(x, y))
                    continue;
                ref WorldTile tile = ref grid.At(x, y);
                SetType(ref tile, LivingWood);
                tile.Wall = LivingWoodUnsafeWall;
            }
        }

        for (int branch = -1; branch <= 1; branch += 2)
        {
            int branchY = top + random.Next(height / 4, Math.Max(height / 4 + 1, height / 2));
            int branchLength = random.Next(9, 16);
            for (int step = 0; step < branchLength; step++)
            {
                int x = centerX + branch * (halfWidth + step);
                int y = branchY - step / 3;
                if (!grid.Contains(x, y))
                    break;
                SetType(ref grid.At(x, y), LivingWood);
            }
        }

        int canopyY = top - 4;
        int canopyRx = random.Next(13, 19);
        int canopyRy = random.Next(8, 12);
        FillEllipse(grid, centerX, canopyY, canopyRx, canopyRy, LeafBlock, overwriteAir: true, wall: 0);
        FillEllipse(grid, centerX, canopyY + 4, Math.Max(4, canopyRx / 3), Math.Max(3, canopyRy / 2),
            LivingWood, overwriteAir: true, wall: LivingWoodUnsafeWall);

        for (int direction = -1; direction <= 1; direction += 2)
        {
            int rootLength = random.Next(7, 13);
            for (int step = 0; step < rootLength; step++)
            {
                int x = centerX + direction * (halfWidth + step / 2);
                int y = surface + step / 2;
                if (!grid.Contains(x, y))
                    break;
                ref WorldTile tile = ref grid.At(x, y);
                if (!tile.IsActive || IsNatural(tile.Type))
                    SetType(ref tile, LivingWood);
            }
        }
    }

    private static void ApplyWoodTreeWalls(IWorldGenerationContext context, RuntimeGrid grid)
    {
        long walls = 0;
        for (int x = 1; x < grid.Width - 1; x++)
        {
            if ((x & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            for (int y = 1; y < grid.Height - 1; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.Type == LivingWood && tile.IsActive)
                {
                    if (tile.Wall == 0)
                    {
                        tile.Wall = LivingWoodUnsafeWall;
                        walls++;
                    }
                    continue;
                }

                if (tile.IsActive || tile.Wall != 0)
                    continue;
                if (!grid.HasNeighborType(x, y, LivingWood))
                    continue;

                tile.Wall = LivingWoodUnsafeWall;
                walls++;
            }
        }

        context.ReportProgress(1d, $"Filling living-tree background walls ({walls} cells)");
    }

    private void ApplyAltars(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int target = grid.Width switch
        {
            <= 4200 => 3,
            <= 6400 => 4,
            _ => 5
        };
        int placed = 0;
        int minY = Math.Clamp((int)state.RockLayer + 30, 10, state.UnderworldTop - 100);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 70);
        int styleX = context.Request.Options.Evil == WorldGenerationEvil.Crimson ? 54 : 0;

        for (int attempt = 0; attempt < target * 120 && placed < target; attempt++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(80, grid.Width - 84);
            int startY = random.Next(minY, maxY);
            int floorY = grid.FindFirstActiveY(x + 1, startY, state.UnderworldTop);
            if (floorY <= 3 || floorY >= state.UnderworldTop)
                continue;
            int top = floorY - 2;
            if (!CanPlaceObject(grid, x, top, 3, 2, requireFloor: true))
                continue;

            for (int dx = 0; dx < 3; dx++)
            for (int dy = 0; dy < 2; dy++)
            {
                ref WorldTile tile = ref grid.At(x + dx, top + dy);
                SetType(ref tile, DemonAltar);
                tile.FrameX = checked((short)(styleX + dx * 18));
                tile.FrameY = checked((short)(dy * 18));
            }
            placed++;
        }

        context.ReportProgress(1d, $"Placing evil altars ({placed}/{target})");
    }

    private void ApplyWetJungle(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int halfWidth = Math.Max(260, grid.Width / 9);
        int left = Math.Max(30, bootstrap.JungleOriginX - halfWidth);
        int right = Math.Min(grid.Width - 30, bootstrap.JungleOriginX + halfWidth);
        int top = Math.Clamp((int)state.RockLayer + 20, 30, state.UnderworldTop - 120);
        int bottom = Math.Max(top + 1, state.UnderworldTop - 80);
        int pools = Math.Max(10, grid.Width / 260);

        for (int i = 0; i < pools; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(left, right);
            int y = random.Next(top, bottom);
            int rx = random.Next(7, 16);
            int ry = random.Next(4, 9);
            WorldLiquidKind liquid = random.Next(8) == 0 ? WorldLiquidKind.Honey : WorldLiquidKind.Water;

            CarveEllipse(grid, x, y, rx, ry, JungleUnsafeWall);
            FillLiquidEllipse(grid, x, y + 1, Math.Max(3, rx - 2), Math.Max(2, ry - 2), liquid);
            MudRing(grid, x, y, rx + 3, ry + 3);
        }

        context.ReportProgress(1d, $"Adding wet-jungle water and honey pockets ({pools} basins)");
    }

    private void ApplyJungleTemple(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int width = grid.Width switch
        {
            <= 4200 => random.Next(88, 112),
            <= 6400 => random.Next(112, 142),
            _ => random.Next(138, 174)
        };
        int height = grid.Height switch
        {
            <= 1200 => random.Next(58, 74),
            <= 1800 => random.Next(72, 92),
            _ => random.Next(88, 112)
        };

        int centerX = Math.Clamp(
            bootstrap.JungleOriginX + random.Next(-Math.Max(90, grid.Width / 28), Math.Max(91, grid.Width / 28)),
            width / 2 + 30,
            grid.Width - width / 2 - 31);
        int top = Math.Clamp(
            (int)state.RockLayer + random.Next(130, 210),
            (int)state.RockLayer + 80,
            state.UnderworldTop - height - 45);
        int left = centerX - width / 2;
        int right = left + width;
        int bottom = top + height;

        state.TempleLeft = left;
        state.TempleRight = right;
        state.TempleTop = top;
        state.TempleBottom = bottom;

        for (int x = left; x <= right; x++)
        {
            if ((x & 31) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            for (int y = top; y <= bottom; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                bool shell = x <= left + 2 || x >= right - 2 || y <= top + 2 || y >= bottom - 2;
                if (shell)
                {
                    SetType(ref tile, LihzahrdBrick);
                    tile.Wall = LihzahrdBrickUnsafeWall;
                }
                else
                {
                    ClearTile(ref tile, preserveWall: false);
                    tile.Wall = LihzahrdBrickUnsafeWall;
                }
            }
        }

        int roomCount = Math.Max(3, height / 18);
        for (int room = 1; room < roomCount; room++)
        {
            int y = top + room * height / roomCount;
            int openingCenter = random.Next(left + 15, right - 15);
            for (int x = left + 4; x < right - 4; x++)
            {
                if (Math.Abs(x - openingCenter) < 5)
                    continue;
                ref WorldTile tile = ref grid.At(x, y);
                SetType(ref tile, LihzahrdBrick);
                tile.Wall = LihzahrdBrickUnsafeWall;
            }
        }

        int stairX = centerX;
        for (int y = top + 5; y < bottom - 5; y++)
        {
            if ((y - top) % 14 == 0)
                stairX = Math.Clamp(stairX + random.Next(-12, 13), left + 9, right - 9);
            for (int dx = -2; dx <= 2; dx++)
            {
                ref WorldTile tile = ref grid.At(stairX + dx, y);
                ClearTile(ref tile, preserveWall: true);
                tile.Wall = LihzahrdBrickUnsafeWall;
            }
        }

        context.ReportProgress(1d, $"Building jungle temple shell ({width}x{height})");
    }

    private void ApplyHives(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int count = grid.Width switch
        {
            <= 4200 => 4,
            <= 6400 => 6,
            _ => 8
        };
        int halfWidth = Math.Max(280, grid.Width / 9);
        int left = Math.Max(40, bootstrap.JungleOriginX - halfWidth);
        int right = Math.Min(grid.Width - 40, bootstrap.JungleOriginX + halfWidth);
        int top = Math.Clamp((int)state.RockLayer + 90, 30, state.UnderworldTop - 120);
        int bottom = Math.Max(top + 1, state.UnderworldTop - 70);
        int placed = 0;

        for (int attempt = 0; attempt < count * 40 && placed < count; attempt++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(left, right);
            int y = random.Next(top, bottom);
            int rx = random.Next(12, 22);
            int ry = random.Next(9, 16);
            if (IntersectsTemple(x - rx - 8, x + rx + 8, y - ry - 8, y + ry + 8))
                continue;

            FillEllipse(grid, x, y, rx, ry, Hive, overwriteAir: true, wall: HiveUnsafeWall);
            CarveEllipse(grid, x, y, Math.Max(5, rx - 4), Math.Max(4, ry - 4), HiveUnsafeWall);
            FillLiquidEllipse(grid, x, y + Math.Max(1, ry / 3), Math.Max(4, rx - 6), Math.Max(2, ry / 3),
                WorldLiquidKind.Honey);
            placed++;
        }

        context.ReportProgress(1d, $"Generating jungle hives ({placed}/{count})");
    }

    private void ApplyJungleChests(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        state.JungleChestCandidates.Clear();
        int target = grid.Width switch
        {
            <= 4200 => 5,
            <= 6400 => 7,
            _ => 9
        };
        int halfWidth = Math.Max(260, grid.Width / 9);
        int left = Math.Max(30, bootstrap.JungleOriginX - halfWidth);
        int right = Math.Min(grid.Width - 30, bootstrap.JungleOriginX + halfWidth);
        int top = Math.Clamp((int)state.RockLayer + 45, 20, state.UnderworldTop - 100);
        int bottom = Math.Max(top + 1, state.UnderworldTop - 60);

        for (int attempt = 0; attempt < target * 120 && state.JungleChestCandidates.Count < target; attempt++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(left, right);
            int y = random.Next(top, bottom);
            if (IntersectsTemple(x - 6, x + 6, y - 5, y + 5))
                continue;
            if (grid.At(x, y).IsActive || grid.At(x, y - 1).IsActive)
                continue;
            int floor = grid.FindFirstActiveY(x, y + 1, Math.Min(bottom + 35, grid.Height));
            if (floor >= grid.Height || floor - y > 18)
                continue;

            var candidate = new WorldGenerationPoint(x, floor - 1);
            bool nearExisting = state.JungleChestCandidates.Any(existing =>
                Math.Abs(existing.X - candidate.X) < 70 && Math.Abs(existing.Y - candidate.Y) < 45);
            if (nearExisting)
                continue;

            state.JungleChestCandidates.Add(candidate);
            BuildJungleChestPedestal(grid, candidate.X, candidate.Y + 1);
        }

        context.ReportProgress(
            1d,
            $"Reserving jungle chest sites ({state.JungleChestCandidates.Count}/{target}); object placement remains a later vanilla pass");
    }

    private static void BuildJungleChestPedestal(RuntimeGrid grid, int x, int floorY)
    {
        for (int dx = -2; dx <= 2; dx++)
        {
            if (!grid.Contains(x + dx, floorY))
                continue;
            ref WorldTile tile = ref grid.At(x + dx, floorY);
            if (!tile.IsActive || IsNatural(tile.Type))
                SetType(ref tile, Mud);
        }
    }

    private void ApplySettleLiquids(IWorldGenerationContext context, RuntimeGrid grid)
    {
        int top = Math.Clamp((int)state.WorldSurface - 20, 1, grid.Height - 2);
        const int sweeps = 6;
        long moved = 0;

        for (int sweep = 0; sweep < sweeps; sweep++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            long sweepMoved = 0;
            for (int y = grid.Height - 2; y >= top; y--)
            {
                if ((y & 31) == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();

                for (int x = 1; x < grid.Width - 1; x++)
                {
                    ref WorldTile source = ref grid.At(x, y);
                    if (source.LiquidAmount == 0 || source.IsActive)
                        continue;

                    ref WorldTile below = ref grid.At(x, y + 1);
                    if (below.IsActive || (below.LiquidAmount > 0 && below.LiquidKind != source.LiquidKind))
                        continue;

                    int capacity = byte.MaxValue - below.LiquidAmount;
                    if (capacity <= 0)
                        continue;

                    int transfer = Math.Min(capacity, source.LiquidAmount);
                    below.LiquidKind = source.LiquidKind;
                    below.LiquidAmount = checked((byte)(below.LiquidAmount + transfer));
                    source.LiquidAmount = checked((byte)(source.LiquidAmount - transfer));
                    if (source.LiquidAmount == 0)
                        source.LiquidKind = WorldLiquidKind.Water;
                    sweepMoved += transfer;
                }
            }

            moved += sweepMoved;
            context.ReportProgress((sweep + 1d) / sweeps, $"Settling liquids sweep {sweep + 1}/{sweeps}");
            if (sweepMoved == 0)
                break;
        }

        context.ReportProgress(1d, $"Completed first liquid-settling stage ({moved} liquid units moved)");
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

        public RuntimeGrid(RuntimeWorldGenerationWorkspace workspace) => store = workspace.TileStore;

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
internal sealed class VanillaSourceBackedBiomesCompatibilityBarrier1458 : IWorldGenerationPass
{
    public static VanillaSourceBackedBiomesCompatibilityBarrier1458 Instance { get; } = new();

    private VanillaSourceBackedBiomesCompatibilityBarrier1458()
    {
    }

    public void Execute(IWorldGenerationContext context) =>
        context.ReportProgress(1d, "Compatibility Biomes replaced by source-backed biome and beach passes");
}
