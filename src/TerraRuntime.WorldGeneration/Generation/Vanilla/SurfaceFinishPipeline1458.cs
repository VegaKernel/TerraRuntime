using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Eighth source-backed Terraria 1.4.5.8 world-generation overlay. It advances the ordinary canonical pipeline from
/// Quick Cleanup through Grass Wall. Guide is deliberately left as the next boundary because it requires generated
/// NPC persistence instead of another tile-only approximation.
/// </summary>
public sealed class SourceBackedSurfaceFinish1458 : IWorldGenerationProvider
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
    private readonly SourceBackedLateStructures1458 baseline = new();

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

        var state = new SurfaceFinishState1458();
        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id == MetadataId)
            {
                builder.Add(entry.Descriptor, new SpawnPreservingMetadataPass1458(entry.Pass, state));
                continue;
            }

            if (entry.Descriptor.Id != SecretSeedsId)
            {
                builder.Add(entry.Descriptor, entry.Pass);
                continue;
            }

            Add(builder, QuickCleanupId, SourceBackedLateStructures1458.FloatingIslandHousesId,
                new SurfaceFinishPass1458(SurfaceFinishStage1458.QuickCleanup, state));
            Add(builder, PotsId, QuickCleanupId,
                new SurfaceFinishPass1458(SurfaceFinishStage1458.Pots, state));
            Add(builder, HellforgeId, PotsId,
                new SurfaceFinishPass1458(SurfaceFinishStage1458.Hellforge, state));
            Add(builder, SpreadingGrassId, HellforgeId,
                new SurfaceFinishPass1458(SurfaceFinishStage1458.SpreadingGrass, state));
            Add(builder, SurfaceOreAndStoneId, SpreadingGrassId,
                new SurfaceFinishPass1458(SurfaceFinishStage1458.SurfaceOreAndStone, state));
            Add(builder, PlaceFallenLogId, SurfaceOreAndStoneId,
                new SurfaceFinishPass1458(SurfaceFinishStage1458.PlaceFallenLog, state));
            Add(builder, TrapsId, PlaceFallenLogId,
                new SurfaceFinishPass1458(SurfaceFinishStage1458.Traps, state));
            Add(builder, PilesId, TrapsId,
                new SurfaceFinishPass1458(SurfaceFinishStage1458.Piles, state));
            Add(builder, SpawnPointId, PilesId,
                new SurfaceFinishPass1458(SurfaceFinishStage1458.SpawnPoint, state));
            Add(builder, GrassWallId, SpawnPointId,
                new SurfaceFinishPass1458(SurfaceFinishStage1458.GrassWall, state));

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

internal enum SurfaceFinishStage1458 : byte
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

internal sealed class SurfaceFinishState1458
{
    public VanillaWorldGenerationBootstrapState1458? Bootstrap { get; private set; }
    public double WorldSurface { get; private set; }
    public double WorldSurfaceHigh { get; private set; }
    public double RockLayer { get; private set; }
    public int UnderworldTop { get; private set; }
    public WorldGenerationPoint? SpawnPoint { get; set; }

    public void EnsureInitialized(IWorldGenerationContext context, Workspace workspace)
    {
        if (Bootstrap is not null)
            return;

        Bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Surface-finish vanilla generation requires Reset bootstrap state.");
        if (context.Metadata is null || !context.Metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Surface-finish vanilla generation requires source-backed Terrain layers.");

        WorldSurface = layers.WorldSurface;
        WorldSurfaceHigh = workspace.VanillaTerrainState?.WorldSurfaceHigh ?? layers.WorldSurface;
        RockLayer = layers.RockLayer;
        UnderworldTop = Math.Clamp(workspace.HeightTiles - 200, (int)RockLayer + 120, workspace.HeightTiles - 90);
    }
}

internal sealed class SpawnPreservingMetadataPass1458 : IWorldGenerationPass
{
    private readonly IWorldGenerationPass fallback;
    private readonly SurfaceFinishState1458 state;

    public SpawnPreservingMetadataPass1458(
        IWorldGenerationPass fallback,
        SurfaceFinishState1458 state)
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

internal sealed class SurfaceFinishPass1458 : IWorldGenerationPass
{
    private const ushort Dirt = 0;
    private const ushort Stone = 1;
    private const ushort Grass = 2;
    private const ushort Ash = 57;
    private const ushort PressurePlate = 135;
    private const ushort Trap = 137;
    private const ushort SmallPile = 185;
    private const ushort FallenLog = 488;

    private readonly SurfaceFinishStage1458 stage;
    private readonly SurfaceFinishState1458 state;

    public SurfaceFinishPass1458(
        SurfaceFinishStage1458 stage,
        SurfaceFinishState1458 state)
    {
        this.stage = stage;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Workspace workspace = context.Workspace as Workspace ??
            throw new InvalidOperationException("Surface-finish Terraria generation requires Workspace.");
        state.EnsureInitialized(context, workspace);
        var grid = new RuntimeGrid(workspace);
        var random = new VanillaRandom(
            context.VanillaRandom ??
            throw new InvalidOperationException("Surface-finish Terraria generation requires shared UnifiedRandom semantics."));

        switch (stage)
        {
            case SurfaceFinishStage1458.QuickCleanup:
                ApplyQuickCleanup(context, workspace);
                break;
            case SurfaceFinishStage1458.Pots:
                ApplyPots(context, workspace);
                break;
            case SurfaceFinishStage1458.Hellforge:
                int forges = HellforgePlacement1458.Generate(workspace.TileStore, context.VanillaRandom!, context.CancellationToken);
                context.ReportProgress(1d, $"Placing Hellforges ({forges}/{workspace.WidthTiles / 200})");
                break;
            case SurfaceFinishStage1458.SpreadingGrass:
                ApplySpreadingGrass(context, workspace);
                break;
            case SurfaceFinishStage1458.SurfaceOreAndStone:
                ApplySurfaceOreAndStone(context, grid, random);
                break;
            case SurfaceFinishStage1458.PlaceFallenLog:
                ApplyFallenLog(context, workspace);
                break;
            case SurfaceFinishStage1458.Traps:
                ApplyTraps(context, grid, random);
                break;
            case SurfaceFinishStage1458.Piles:
                ApplyPiles(context, grid, random);
                break;
            case SurfaceFinishStage1458.SpawnPoint:
                ApplySpawnPoint(context, grid);
                break;
            case SurfaceFinishStage1458.GrassWall:
                ApplyGrassWall(context, workspace);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void ApplyQuickCleanup(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Quick cleanup requires shared UnifiedRandom semantics.");

        var pass = new QuickCleanupPass1458(
            workspace.TileStore, random, state.WorldSurface, state.RockLayer,
            DungeonGenerationCatalog1458.BeachDistance, context.CancellationToken);
        pass.Apply();
        context.ReportProgress(1d,
            $"Quick cleanup ({pass.Dropped} dropped, {pass.Retyped} retyped, {pass.SandFill} sand fill, " +
            $"{pass.Freshened} freshened)");
    }

    private void ApplyPots(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Pots require shared UnifiedRandom semantics.");

        double surfaceHigh = workspace.VanillaTerrainState?.WorldSurfaceHigh ?? state.WorldSurface;
        double surfaceLow = workspace.VanillaTerrainState?.WorldSurfaceLow ?? state.WorldSurface;
        var pass = new PotScatterPass1458(
            workspace.TileStore, random, state.WorldSurface, surfaceHigh, surfaceLow, state.RockLayer,
            DungeonGenerationCatalog1458.BeachDistance, context.CancellationToken);
        pass.Apply();
        context.ReportProgress(1d, $"Placing Pots ({pass.Placed})");
    }

    private void ApplySpreadingGrass(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Surface grass spreading requires shared UnifiedRandom semantics.");

        // The jungle's own columns are still unmeasured here. WorldGen.Reset leaves them at -1 and the pass
        // that fills them in is registered twenty-two places after this one, so the re-decision the re-skin
        // keys off them cannot fire in an ordinary world - and the pass is handed the value the source
        // actually holds rather than a later one that would change its answer.
        var pass = new SurfaceGrassSpreadPass1458(
            workspace.TileStore, random, state.WorldSurface, state.WorldSurfaceHigh,
            -1, -1, context.CancellationToken);
        pass.Apply();
        context.ReportProgress(1d,
            $"Spreading surface grass ({pass.Grown} grown, {pass.Reskinned} re-skinned)");
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

    /// <summary>
    /// Source <c>GenPassNameID.FallenLogsAndWaterFeatures</c>, delegated to <see cref="FallenLogPass1458"/>.
    /// The anchor it may leave behind is retained: the Flowers pass moves its first patch onto it.
    /// </summary>
    private void ApplyFallenLog(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Fallen logs require shared UnifiedRandom semantics.");

        var pass = new FallenLogPass1458(
            workspace.TileStore,
            random,
            state.WorldSurface,
            DungeonGenerationCatalog1458.BeachDistance,
            context.CancellationToken);
        pass.Apply();
        if (pass.Anchor is { } anchor)
            workspace.SetVanillaFallenLogAnchor(anchor.X, anchor.Y);

        context.ReportProgress(1d, $"Placing Fallen Logs ({pass.Placed})");
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

    private void ApplyGrassWall(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Surface grass walls require shared UnifiedRandom semantics.");

        var pass = new GrassWallPass1458(
            workspace.TileStore, random, state.WorldSurface, context.CancellationToken);
        pass.Apply();
        context.ReportProgress(1d,
            $"Adding Grass Walls ({pass.Painted} cells in {pass.Pockets} pockets, {pass.Grown} grown)");
    }

    private VanillaWorldGenerationBootstrapState1458 RequireBootstrap() =>
        state.Bootstrap ?? throw new InvalidOperationException("Surface-finish pass executed before bootstrap initialization.");

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
    }
}
