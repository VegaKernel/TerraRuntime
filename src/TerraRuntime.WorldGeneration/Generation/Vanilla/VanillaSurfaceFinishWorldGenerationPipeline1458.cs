using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration;

/// <summary>
/// Eighth source-backed Terraria 1.4.5.8 world-generation overlay. It advances the ordinary canonical pipeline from
/// Quick Cleanup through Grass Wall. Guide is deliberately left as the next boundary because it requires generated
/// NPC persistence instead of another tile-only approximation.
/// </summary>
public sealed class SourceBackedVanillaWorldGenerationSurfaceFinish1458 : IWorldGenerationProvider
{
    internal static readonly WorldGenerationPassId QuickCleanupId = new("terraria:1.4.5.8/QuickCleanup");
    internal static readonly WorldGenerationPassId PotsId = new("terraria:1.4.5.8/Pots");
    internal static readonly WorldGenerationPassId HellforgeId = new("terraria:1.4.5.8/Hellforge");
    internal static readonly WorldGenerationPassId SpreadingGrassId = new("terraria:1.4.5.8/SpreadingGrass");
    internal static readonly WorldGenerationPassId SurfaceOreAndStoneId = new("terraria:1.4.5.8/SurfaceOreAndStone");
    internal static readonly WorldGenerationPassId PlaceFallenLogId = new("terraria:1.4.5.8/PlaceFallenLog");
    internal static readonly WorldGenerationPassId TrapsId = new("terraria:1.4.5.8/Traps");
    internal static readonly WorldGenerationPassId PilesId = new("terraria:1.4.5.8/Piles");
    internal static readonly WorldGenerationPassId SpawnPointId = new("terraria:1.4.5.8/SpawnPoint");
    internal static readonly WorldGenerationPassId GrassWallId = new("terraria:1.4.5.8/GrassWall");

    private static readonly WorldGenerationPassId SecretSeedsId = new("terraria:1.4.5.8/SecretSeeds");
    private static readonly WorldGenerationPassId MetadataId = new("terraria:1.4.5.8/Metadata");
    private readonly SourceBackedVanillaWorldGenerationLateStructures1458 baseline = new();

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

        var state = new VanillaSurfaceFinishWorldGenerationState1458();
        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id == MetadataId)
            {
                builder.Add(entry.Descriptor, new VanillaSpawnPreservingMetadataPass1458(entry.Pass, state));
                continue;
            }

            if (entry.Descriptor.Id != SecretSeedsId)
            {
                builder.Add(entry.Descriptor, entry.Pass);
                continue;
            }

            Add(builder, QuickCleanupId, SourceBackedVanillaWorldGenerationLateStructures1458.FloatingIslandHousesId,
                new VanillaSurfaceFinishWorldGenerationPass1458(VanillaSurfaceFinishWorldGenerationStage1458.QuickCleanup, state));
            Add(builder, PotsId, QuickCleanupId,
                new VanillaSurfaceFinishWorldGenerationPass1458(VanillaSurfaceFinishWorldGenerationStage1458.Pots, state));
            Add(builder, HellforgeId, PotsId,
                new VanillaSurfaceFinishWorldGenerationPass1458(VanillaSurfaceFinishWorldGenerationStage1458.Hellforge, state));
            Add(builder, SpreadingGrassId, HellforgeId,
                new VanillaSurfaceFinishWorldGenerationPass1458(VanillaSurfaceFinishWorldGenerationStage1458.SpreadingGrass, state));
            Add(builder, SurfaceOreAndStoneId, SpreadingGrassId,
                new VanillaSurfaceFinishWorldGenerationPass1458(VanillaSurfaceFinishWorldGenerationStage1458.SurfaceOreAndStone, state));
            Add(builder, PlaceFallenLogId, SurfaceOreAndStoneId,
                new VanillaSurfaceFinishWorldGenerationPass1458(VanillaSurfaceFinishWorldGenerationStage1458.PlaceFallenLog, state));
            Add(builder, TrapsId, PlaceFallenLogId,
                new VanillaSurfaceFinishWorldGenerationPass1458(VanillaSurfaceFinishWorldGenerationStage1458.Traps, state));
            Add(builder, PilesId, TrapsId,
                new VanillaSurfaceFinishWorldGenerationPass1458(VanillaSurfaceFinishWorldGenerationStage1458.Piles, state));
            Add(builder, SpawnPointId, PilesId,
                new VanillaSurfaceFinishWorldGenerationPass1458(VanillaSurfaceFinishWorldGenerationStage1458.SpawnPoint, state));
            Add(builder, GrassWallId, SpawnPointId,
                new VanillaSurfaceFinishWorldGenerationPass1458(VanillaSurfaceFinishWorldGenerationStage1458.GrassWall, state));

            builder.Add(CloneDescriptor(entry.Descriptor, [GrassWallId]), entry.Pass);
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

internal enum VanillaSurfaceFinishWorldGenerationStage1458 : byte
{
    QuickCleanup,
    Pots,
    Hellforge,
    SpreadingGrass,
    SurfaceOreAndStone,
    PlaceFallenLog,
    Traps,
    Piles,
    SpawnPoint,
    GrassWall
}

internal sealed class VanillaSurfaceFinishWorldGenerationState1458
{
    public VanillaWorldGenerationBootstrapState1458? Bootstrap { get; private set; }
    public double WorldSurface { get; private set; }
    public double RockLayer { get; private set; }
    public int UnderworldTop { get; private set; }
    public WorldGenerationPoint? SpawnPoint { get; set; }

    public void EnsureInitialized(IWorldGenerationContext context, RuntimeWorldGenerationWorkspace workspace)
    {
        if (Bootstrap is not null)
            return;

        Bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Surface-finish vanilla generation requires Reset bootstrap state.");
        if (context.Metadata is null || !context.Metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Surface-finish vanilla generation requires source-backed Terrain layers.");

        WorldSurface = layers.WorldSurface;
        RockLayer = layers.RockLayer;
        UnderworldTop = Math.Clamp(workspace.HeightTiles - 200, (int)RockLayer + 120, workspace.HeightTiles - 90);
    }
}

internal sealed class VanillaSpawnPreservingMetadataPass1458 : IWorldGenerationPass
{
    private readonly IWorldGenerationPass fallback;
    private readonly VanillaSurfaceFinishWorldGenerationState1458 state;

    public VanillaSpawnPreservingMetadataPass1458(
        IWorldGenerationPass fallback,
        VanillaSurfaceFinishWorldGenerationState1458 state)
    {
        this.fallback = fallback;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        fallback.Execute(context);
        if (state.SpawnPoint is not WorldGenerationPoint spawn)
            return;

        IWorldGenerationMetadataWorkspace metadata = context.Metadata ??
            throw new InvalidOperationException("Spawn-preserving metadata wrapper requires world metadata storage.");
        if (!metadata.TrySetSpawn(spawn.X, spawn.Y))
            throw new InvalidOperationException("Could not preserve source-backed Terraria spawn point.");
    }
}

internal sealed class VanillaSurfaceFinishWorldGenerationPass1458 : IWorldGenerationPass
{
    private const ushort Dirt = 0;
    private const ushort Stone = 1;
    private const ushort Grass = 2;
    private const ushort Pot = 28;
    private const ushort Ash = 57;
    private const ushort Mud = 59;
    private const ushort JungleGrass = 60;
    private const ushort Hellforge = 77;
    private const ushort PressurePlate = 135;
    private const ushort Trap = 137;
    private const ushort SmallPile = 185;
    private const ushort FallenLog = 488;
    private const ushort GrassUnsafeWall = 63;

    private readonly VanillaSurfaceFinishWorldGenerationStage1458 stage;
    private readonly VanillaSurfaceFinishWorldGenerationState1458 state;

    public VanillaSurfaceFinishWorldGenerationPass1458(
        VanillaSurfaceFinishWorldGenerationStage1458 stage,
        VanillaSurfaceFinishWorldGenerationState1458 state)
    {
        this.stage = stage;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RuntimeWorldGenerationWorkspace workspace = context.Workspace as RuntimeWorldGenerationWorkspace ??
            throw new InvalidOperationException("Surface-finish Terraria generation requires RuntimeWorldGenerationWorkspace.");
        state.EnsureInitialized(context, workspace);
        var grid = new RuntimeGrid(workspace);
        var random = new VanillaRandom(
            context.VanillaRandom ??
            throw new InvalidOperationException("Surface-finish Terraria generation requires shared UnifiedRandom semantics."));

        switch (stage)
        {
            case VanillaSurfaceFinishWorldGenerationStage1458.QuickCleanup:
                ApplyQuickCleanup(context, grid);
                break;
            case VanillaSurfaceFinishWorldGenerationStage1458.Pots:
                ApplyPots(context, grid, random);
                break;
            case VanillaSurfaceFinishWorldGenerationStage1458.Hellforge:
                ApplyHellforge(context, grid, random);
                break;
            case VanillaSurfaceFinishWorldGenerationStage1458.SpreadingGrass:
                ApplySpreadingGrass(context, grid);
                break;
            case VanillaSurfaceFinishWorldGenerationStage1458.SurfaceOreAndStone:
                ApplySurfaceOreAndStone(context, grid, random);
                break;
            case VanillaSurfaceFinishWorldGenerationStage1458.PlaceFallenLog:
                ApplyFallenLog(context, grid, random);
                break;
            case VanillaSurfaceFinishWorldGenerationStage1458.Traps:
                ApplyTraps(context, grid, random);
                break;
            case VanillaSurfaceFinishWorldGenerationStage1458.Piles:
                ApplyPiles(context, grid, random);
                break;
            case VanillaSurfaceFinishWorldGenerationStage1458.SpawnPoint:
                ApplySpawnPoint(context, grid);
                break;
            case VanillaSurfaceFinishWorldGenerationStage1458.GrassWall:
                ApplyGrassWall(context, grid, random);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static void ApplyQuickCleanup(IWorldGenerationContext context, RuntimeGrid grid)
    {
        int normalized = 0;
        for (int x = 1; x < grid.Width - 1; x++)
        {
            if ((x & 127) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            for (int y = 1; y < grid.Height - 1; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (!tile.IsActive)
                {
                    if (tile.Shape != 0)
                    {
                        tile.Shape = 0;
                        normalized++;
                    }
                    continue;
                }

                if (!VanillaWorldFrameImportance326.IsFrameImportant(tile.Type) &&
                    (tile.FrameX != 0 || tile.FrameY != 0))
                {
                    tile.FrameX = 0;
                    tile.FrameY = 0;
                    normalized++;
                }
            }
        }

        context.ReportProgress(1d, $"Quick Cleanup normalized {normalized} tile states");
    }

    private void ApplyPots(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int target = grid.Width switch
        {
            <= 4200 => 95,
            <= 6400 => 145,
            _ => 195
        };
        int minY = Math.Clamp((int)state.WorldSurface + 30, 20, grid.Height - 80);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 25);
        int placed = 0;

        for (int attempt = 0; attempt < target * 80 && placed < target; attempt++)
        {
            if ((attempt & 255) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int left = random.Next(8, grid.Width - 10);
            int probe = random.Next(minY, maxY);
            int floor = grid.FindFirstActiveY(left, probe, Math.Min(maxY + 40, grid.Height - 2));
            int top = floor - 2;
            if (!CanPlaceObject(grid, left, top, width: 2, height: 2))
                continue;

            int style = random.Next(4);
            PlaceFramedObject(grid, left, top, width: 2, height: 2, Pot, styleWidthPixels: 36, style);
            placed++;
        }

        context.ReportProgress(1d, $"Placing cavern pots ({placed}/{target})");
    }

    private void ApplyHellforge(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int target = grid.Width switch
        {
            <= 4200 => 12,
            <= 6400 => 18,
            _ => 24
        };
        int minY = Math.Clamp(state.UnderworldTop + 10, 20, grid.Height - 20);
        int placed = 0;

        for (int attempt = 0; attempt < target * 160 && placed < target; attempt++)
        {
            if ((attempt & 127) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int left = random.Next(10, grid.Width - 13);
            int floor = grid.FindFirstActiveY(left + 1, minY, grid.Height - 3);
            int top = floor - 2;
            if (!CanPlaceObject(grid, left, top, width: 3, height: 2))
                continue;

            ushort support = grid.At(left + 1, floor).Type;
            if (support is not (Ash or Stone or 75 or 76))
                continue;

            PlaceFramedObject(grid, left, top, width: 3, height: 2, Hellforge, styleWidthPixels: 54, style: 0);
            placed++;
        }

        context.ReportProgress(1d, $"Placing Hellforges ({placed}/{target})");
    }

    private void ApplySpreadingGrass(IWorldGenerationContext context, RuntimeGrid grid)
    {
        int converted = 0;
        int maxY = Math.Clamp((int)state.WorldSurface + 85, 20, grid.Height - 2);
        for (int x = 2; x < grid.Width - 2; x++)
        {
            if ((x & 127) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            for (int y = 2; y < maxY; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (!tile.IsActive || !grid.HasOpenNeighbor(x, y))
                    continue;

                if (tile.Type == Dirt)
                {
                    tile.Type = Grass;
                    converted++;
                }
                else if (tile.Type == Mud)
                {
                    tile.Type = JungleGrass;
                    converted++;
                }
            }
        }

        context.ReportProgress(1d, $"Spreading surface grass ({converted} blocks)");
    }

    private void ApplySurfaceOreAndStone(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        ushort[] ores =
        [
            checked((ushort)bootstrap.CopperOre),
            checked((ushort)bootstrap.IronOre),
            checked((ushort)bootstrap.SilverOre),
            checked((ushort)bootstrap.GoldOre)
        ];

        int patches = grid.Width switch
        {
            <= 4200 => 70,
            <= 6400 => 105,
            _ => 140
        };
        int minY = Math.Clamp((int)state.WorldSurface - 5, 10, grid.Height - 20);
        int maxY = Math.Clamp((int)state.RockLayer + 25, minY + 1, grid.Height - 10);
        int changed = 0;

        for (int patch = 0; patch < patches; patch++)
        {
            if ((patch & 15) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int cx = random.Next(25, grid.Width - 25);
            int cy = random.Next(minY, maxY);
            int radius = random.Next(3, 8);
            bool orePatch = random.Next(4) == 0;
            ushort replacement = orePatch ? ores[random.Next(ores.Length)] : Stone;

            for (int x = cx - radius; x <= cx + radius; x++)
            for (int y = cy - radius; y <= cy + radius; y++)
            {
                int dx = x - cx;
                int dy = y - cy;
                if (dx * dx + dy * dy > radius * radius)
                    continue;
                ref WorldTile tile = ref grid.At(x, y);
                if (!tile.IsActive || tile.Type != Dirt)
                    continue;
                tile.Type = replacement;
                tile.FrameX = 0;
                tile.FrameY = 0;
                tile.Shape = 0;
                changed++;
            }
        }

        context.ReportProgress(1d, $"Adding surface ore and stone ({changed} blocks)");
    }

    private void ApplyFallenLog(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int target = grid.Width switch
        {
            <= 4200 => 6,
            <= 6400 => 9,
            _ => 12
        };
        int placed = 0;
        int minX = Math.Max(bootstrap.LeftBeachEnd + 40, 20);
        int maxX = Math.Min(bootstrap.RightBeachStart - 40, grid.Width - 20);
        int minY = Math.Max(20, (int)state.WorldSurface - 140);
        int maxY = Math.Min(grid.Height - 5, (int)state.WorldSurface + 130);

        for (int attempt = 0; attempt < target * 180 && placed < target; attempt++)
        {
            if ((attempt & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int left = random.Next(minX, maxX - 3);
            int floor = grid.FindFirstActiveY(left + 1, minY, maxY);
            int top = floor - 2;
            if (floor >= maxY || !CanPlaceObject(grid, left, top, width: 3, height: 2))
                continue;
            if (grid.At(left + 1, floor).Type != Grass)
                continue;

            PlaceFramedObject(grid, left, top, width: 3, height: 2, FallenLog, styleWidthPixels: 54, style: 0);
            placed++;
        }

        context.ReportProgress(1d, $"Placing Fallen Logs ({placed}/{target})");
    }

    private void ApplyTraps(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int target = grid.Width switch
        {
            <= 4200 => 28,
            <= 6400 => 42,
            _ => 56
        };
        int minY = Math.Clamp((int)state.RockLayer + 20, 30, state.UnderworldTop - 60);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 20);
        int placed = 0;

        for (int attempt = 0; attempt < target * 200 && placed < target; attempt++)
        {
            if ((attempt & 127) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int plateX = random.Next(12, grid.Width - 12);
            int probe = random.Next(minY, maxY);
            int floor = grid.FindFirstActiveY(plateX, probe, Math.Min(maxY + 25, grid.Height - 3));
            int plateY = floor - 1;
            if (plateY < 3 || grid.At(plateX, plateY).IsActive || !grid.At(plateX, floor).IsActive)
                continue;

            int direction = random.Next(2) == 0 ? -1 : 1;
            int trapX = plateX + direction * random.Next(4, 9);
            int trapY = Math.Max(2, plateY - random.Next(0, 3));
            if (trapX < 2 || trapX >= grid.Width - 2 || grid.At(trapX, trapY).IsActive)
                continue;

            ref WorldTile plate = ref grid.At(plateX, plateY);
            plate.Type = PressurePlate;
            plate.Flags |= WorldTileFlags.Active | WorldTileFlags.WireRed;
            plate.FrameX = 36; // gray pressure-plate style
            plate.FrameY = 0;
            plate.Shape = 0;

            ref WorldTile trap = ref grid.At(trapX, trapY);
            trap.Type = Trap;
            trap.Flags |= WorldTileFlags.Active | WorldTileFlags.WireRed;
            trap.FrameX = checked((short)(direction < 0 ? 18 : 0));
            trap.FrameY = 0;
            trap.Shape = 0;
            trap.LiquidAmount = 0;

            WireBetween(grid, plateX, plateY, trapX, trapY);
            placed++;
        }

        context.ReportProgress(1d, $"Placing wired cavern traps ({placed}/{target})");
    }

    private void ApplyPiles(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int target = grid.Width switch
        {
            <= 4200 => 140,
            <= 6400 => 210,
            _ => 280
        };
        int minY = Math.Clamp((int)state.WorldSurface - 60, 15, grid.Height - 50);
        int maxY = Math.Max(minY + 1, state.UnderworldTop - 20);
        int placed = 0;

        for (int attempt = 0; attempt < target * 80 && placed < target; attempt++)
        {
            if ((attempt & 255) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(5, grid.Width - 5);
            int probe = random.Next(minY, maxY);
            int floor = grid.FindFirstActiveY(x, probe, Math.Min(grid.Height - 2, maxY + 30));
            int y = floor - 1;
            if (y < 2 || grid.At(x, y).IsActive || !grid.At(x, floor).IsActive)
                continue;

            ref WorldTile pile = ref grid.At(x, y);
            pile.Type = SmallPile;
            pile.Flags |= WorldTileFlags.Active;
            pile.FrameX = checked((short)(random.Next(9) * 18));
            pile.FrameY = 0;
            pile.Shape = 0;
            pile.LiquidAmount = 0;
            placed++;
        }

        context.ReportProgress(1d, $"Placing ambient piles ({placed}/{target})");
    }

    private void ApplySpawnPoint(IWorldGenerationContext context, RuntimeGrid grid)
    {
        int center = grid.Width / 2;
        int minY = Math.Max(10, (int)state.WorldSurface - 170);
        int maxY = Math.Min(grid.Height - 10, (int)state.WorldSurface + 190);
        WorldGenerationPoint? selected = null;

        for (int radius = 0; radius <= 180 && selected is null; radius++)
        {
            int[] xs = radius == 0 ? [center] : [center - radius, center + radius];
            foreach (int x in xs)
            {
                if (x < 12 || x >= grid.Width - 12)
                    continue;
                int floor = grid.FindFirstActiveY(x, minY, maxY);
                if (floor >= maxY || floor < 6)
                    continue;
                if (!grid.IsPlayerClearanceAvailable(x, floor - 4, floor - 1))
                    continue;
                selected = new WorldGenerationPoint(x, floor - 1);
                break;
            }
        }

        if (selected is not WorldGenerationPoint spawn)
            throw new InvalidOperationException("Could not locate a safe source-backed Terraria spawn point near world center.");

        for (int x = spawn.X - 2; x <= spawn.X + 2; x++)
        for (int y = spawn.Y - 4; y <= spawn.Y; y++)
        {
            if (y == spawn.Y && grid.At(x, y + 1).IsActive)
                continue;
            ref WorldTile tile = ref grid.At(x, y);
            if (tile.IsActive && !VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
            {
                tile.Flags &= ~WorldTileFlags.Active;
                tile.Shape = 0;
                tile.LiquidAmount = 0;
            }
        }

        IWorldGenerationMetadataWorkspace metadata = context.Metadata ??
            throw new InvalidOperationException("Spawn Point pass requires world metadata storage.");
        if (!metadata.TrySetSpawn(spawn.X, spawn.Y))
            throw new InvalidOperationException("Could not publish source-backed Terraria spawn point.");
        state.SpawnPoint = spawn;
        context.ReportProgress(1d, $"Selecting spawn point ({spawn.X}, {spawn.Y})");
    }

    private void ApplyGrassWall(IWorldGenerationContext context, RuntimeGrid grid, IRandom random)
    {
        int patches = grid.Width switch
        {
            <= 4200 => 55,
            <= 6400 => 80,
            _ => 110
        };
        int minY = Math.Max(12, (int)state.WorldSurface - 80);
        int maxY = Math.Min(grid.Height - 12, (int)state.WorldSurface + 110);
        int painted = 0;

        for (int patch = 0; patch < patches; patch++)
        {
            if ((patch & 15) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int cx = random.Next(20, grid.Width - 20);
            int cy = random.Next(minY, maxY);
            int rx = random.Next(3, 9);
            int ry = random.Next(2, 6);

            for (int x = cx - rx; x <= cx + rx; x++)
            for (int y = cy - ry; y <= cy + ry; y++)
            {
                if ((x - cx) * (x - cx) * ry * ry + (y - cy) * (y - cy) * rx * rx > rx * rx * ry * ry)
                    continue;
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive || tile.Wall != 0 || !grid.HasNearbySurfaceSoil(x, y))
                    continue;
                tile.Wall = GrassUnsafeWall;
                painted++;
            }
        }

        context.ReportProgress(1d, $"Adding unsafe Grass Wall patches ({painted} cells)");
    }

    private VanillaWorldGenerationBootstrapState1458 RequireBootstrap() =>
        state.Bootstrap ?? throw new InvalidOperationException("Surface-finish pass executed before bootstrap initialization.");

    private static bool CanPlaceObject(RuntimeGrid grid, int left, int top, int width, int height)
    {
        if (left < 1 || top < 1 || left + width >= grid.Width - 1 || top + height >= grid.Height - 1)
            return false;
        for (int x = left; x < left + width; x++)
        for (int y = top; y < top + height; y++)
        {
            if (grid.At(x, y).IsActive || grid.At(x, y).LiquidAmount != 0)
                return false;
        }
        for (int x = left; x < left + width; x++)
        {
            if (!grid.At(x, top + height).IsActive)
                return false;
        }
        return true;
    }

    private static void PlaceFramedObject(
        RuntimeGrid grid,
        int left,
        int top,
        int width,
        int height,
        ushort type,
        int styleWidthPixels,
        int style)
    {
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < height; dy++)
        {
            ref WorldTile tile = ref grid.At(left + dx, top + dy);
            tile.Type = type;
            tile.Flags |= WorldTileFlags.Active;
            tile.FrameX = checked((short)(style * styleWidthPixels + dx * 18));
            tile.FrameY = checked((short)(dy * 18));
            tile.Shape = 0;
            tile.LiquidAmount = 0;
            tile.LiquidKind = WorldLiquidKind.Water;
        }
    }

    private static void WireBetween(RuntimeGrid grid, int x0, int y0, int x1, int y1)
    {
        int x = x0;
        int stepX = Math.Sign(x1 - x0);
        while (x != x1)
        {
            grid.At(x, y0).Flags |= WorldTileFlags.WireRed;
            x += stepX;
        }
        grid.At(x1, y0).Flags |= WorldTileFlags.WireRed;

        int y = y0;
        int stepY = Math.Sign(y1 - y0);
        while (y != y1)
        {
            grid.At(x1, y).Flags |= WorldTileFlags.WireRed;
            y += stepY;
        }
        grid.At(x1, y1).Flags |= WorldTileFlags.WireRed;
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

        public bool IsPlayerClearanceAvailable(int x, int top, int bottom)
        {
            if (top < 1 || bottom >= Height - 1)
                return false;
            for (int px = x - 1; px <= x + 1; px++)
            for (int py = top; py <= bottom; py++)
            {
                WorldTile tile = At(px, py);
                if (tile.IsActive && VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                    return false;
                if (tile.LiquidAmount > 80)
                    return false;
            }
            return true;
        }

        public bool HasNearbySurfaceSoil(int x, int y)
        {
            for (int dx = -2; dx <= 2; dx++)
            for (int dy = -2; dy <= 2; dy++)
            {
                WorldTile tile = At(x + dx, y + dy);
                if (tile.IsActive && tile.Type is Dirt or Grass)
                    return true;
            }
            return false;
        }
    }
}
