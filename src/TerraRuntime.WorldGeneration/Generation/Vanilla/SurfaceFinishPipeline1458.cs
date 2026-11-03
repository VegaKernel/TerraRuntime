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
    public double WorldSurfaceLow { get; private set; }
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
        WorldSurfaceLow = workspace.VanillaTerrainState?.WorldSurfaceLow ?? layers.WorldSurface;
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
                int forges = HellforgePlacement1458.Generate(workspace.TileStore, context.VanillaRandom!, context.CancellationToken,workspace);
                context.ReportProgress(1d, $"Placing Hellforges ({forges}/{workspace.WidthTiles / 200})");
                break;
            case SurfaceFinishStage1458.SpreadingGrass:
                ApplySpreadingGrass(context, workspace);
                break;
            case SurfaceFinishStage1458.SurfaceOreAndStone:
                ApplySurfaceOreAndStone(context, workspace, grid, random);
                break;
            case SurfaceFinishStage1458.PlaceFallenLog:
                ApplyFallenLog(context, workspace);
                break;
            case SurfaceFinishStage1458.Traps:
                ApplyTraps(context, workspace);
                break;
            case SurfaceFinishStage1458.Piles:
                ApplyPiles(context, workspace);
                break;
            case SurfaceFinishStage1458.SpawnPoint:
                ApplySpawnPoint(context, workspace);
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

        if(workspace.HasVanillaGenerationSolidity){workspace.SetVanillaGenerationSolid(new TileTypeId(137),false);workspace.SetVanillaGenerationSolid(new TileTypeId(130),false);}
        var pass = new QuickCleanupPass1458(
            workspace.TileStore, random, state.WorldSurface, state.RockLayer,
            DungeonGenerationCatalog1458.BeachDistance, context.CancellationToken,
            workspace.HasVanillaGenerationSolidity?workspace.IsVanillaGenerationSolid:null);
        pass.Apply();
        if(workspace.HasVanillaGenerationSolidity){workspace.SetVanillaGenerationSolid(new TileTypeId(137),true);workspace.SetVanillaGenerationSolid(new TileTypeId(130),true);}
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
            DungeonGenerationCatalog1458.BeachDistance, context.CancellationToken,
            workspace.HasVanillaGenerationSolidity?workspace.IsVanillaGenerationSolid:null);
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
            -1, -1, context.CancellationToken, workspace);
        pass.Apply();
        context.ReportProgress(1d,
            $"Spreading surface grass ({pass.Grown} grown, {pass.Reskinned} re-skinned)");
    }

    private void ApplySurfaceOreAndStone(IWorldGenerationContext context, Workspace workspace, RuntimeGrid grid, IRandom random)
    {
        WorldGenerationRequest request = context.Request;
        if (WorldSeedResolver1458.Resolve(in request).Has(VanillaSpecialWorldSeed1458.Skyblock))
            return; // WorldGen.Skyblock.denyAllGeneration leaves this PassLegacy delegate RNG-neutral.
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        var pass = new SurfaceOreStone1458(
            grid.Store,
            context.VanillaRandom ?? throw new InvalidOperationException("Surface ore and stone requires shared UnifiedRandom semantics."),
            state.WorldSurfaceLow,
            state.WorldSurface,
            bootstrap.CopperOre,
            bootstrap.IronOre,
            context.CancellationToken,
            workspace.VanillaTerrainState?.CurrentWorldSurface ?? state.WorldSurface,
            workspace);
        pass.Apply();
        context.ReportProgress(1d, $"Adding surface ore and stone ({pass.Changed} blocks)");
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

    private void ApplyTraps(IWorldGenerationContext context, Workspace workspace)
    {
        WorldGenerationRequest request=context.Request;
        if(!WorldSeedResolver1458.Resolve(in request).IsDefault)
            throw new NotSupportedException("Special-seed trap generation requires its source-qualified trap branches.");
        IWorldGenerationVanillaRandom random=context.VanillaRandom??throw new InvalidOperationException("Trap generation requires the shared vanilla RNG owner.");
        VanillaWorldGenerationBootstrapState1458 bootstrap=RequireBootstrap();
        VanillaLiquidLines1458 lines=workspace.VanillaLiquidLines??throw new InvalidOperationException("Trap generation requires retained lava depth.");
        WorldGenerationPoint shimmer=workspace.VanillaShimmerPosition??throw new InvalidOperationException("Trap generation requires retained shimmer position.");
        var placement=new GenerationTrapPlacement1458(workspace,random,state.WorldSurface,lines.LavaLine,shimmer,bootstrap.DungeonSide);
        workspace.SetVanillaGenerationSolid(new TileTypeId(379),false);
        int placed=0;
        for(int offer=0;offer<workspace.WidthTiles*.05;offer++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            for(int attempt=0;attempt<1150;attempt++)
            {
                int x=random.Next(200,workspace.WidthTiles-200),y=random.Next((int)state.WorldSurface,workspace.HeightTiles-210);
                while(y<=(state.WorldSurface+state.RockLayer)/2+40&&(x<380||x>workspace.WidthTiles-380))
                {x=random.Next(200,workspace.WidthTiles-200);y=random.Next((int)state.WorldSurface,workspace.HeightTiles-210);}
                if(workspace.TileStore.Get(x,y).Wall==0&&placement.Place(x,y)){placed++;break;}
            }
        }
        for(int offer=0;offer<workspace.WidthTiles*.003;offer++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            for(int attempt=0;attempt<20000;attempt++)
            {
                int x=random.Next((int)(workspace.WidthTiles*.15),(int)(workspace.WidthTiles*.85));
                int y=random.Next((int)state.WorldSurface+20,workspace.HeightTiles-210);
                if(workspace.TileStore.Get(x,y).Wall==187&&placement.PlaceSand(x,y)){placed++;break;}
            }
        }
        workspace.SetVanillaGenerationSolid(new TileTypeId(379),true);
        context.ReportProgress(1d,$"Placing traps ({placed} successful offers)");
    }

    /// <summary>
    /// Source <c>GenPassNameID.Piles</c>, delegated to <see cref="PilePass1458"/>. Seven sample-and-cascade
    /// stages with their own bands, style tables and budgets, which between them cover almost every floor in
    /// the world.
    /// </summary>
    private void ApplyPiles(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Piles require shared UnifiedRandom semantics.");

        var pass = new PilePass1458(
            workspace.TileStore,
            random,
            state.WorldSurface,
            state.RockLayer,
            DungeonGenerationCatalog1458.BeachDistance,
            context.CancellationToken, workspace);
        pass.Apply();

        context.ReportProgress(1d, $"Placing ambient piles ({pass.Placed})");
    }

    private void ApplySpawnPoint(IWorldGenerationContext context, Workspace workspace)
    {
        WorldGenerationRequest request=context.Request;
        if(!WorldSeedResolver1458.Resolve(in request).IsDefault)
            throw new NotSupportedException("Special-seed spawn generation requires its source-qualified branches.");
        IWorldGenerationVanillaRandom random=context.VanillaRandom??throw new InvalidOperationException("Spawn generation requires the shared vanilla RNG owner.");
        var placement=new GenerationSpawnPoint1458(workspace,random,state.WorldSurface,context.CancellationToken);
        WorldGenerationPoint spawn=placement.Apply();state.SpawnPoint=spawn;
        context.ReportProgress(1d,$"Selecting spawn point ({spawn.X}, {spawn.Y})");
    }

    private void ApplyGrassWall(IWorldGenerationContext context, Workspace workspace)
    {
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Surface grass walls require shared UnifiedRandom semantics.");

        var pass = new GrassWallPass1458(
            workspace.TileStore, random, state.WorldSurface, context.CancellationToken, workspace);
        pass.Apply();
        context.ReportProgress(1d,
            $"Adding Grass Walls ({pass.Painted} cells in {pass.Pockets} pockets, {pass.Grown} grown)");
    }

    private VanillaWorldGenerationBootstrapState1458 RequireBootstrap() =>
        state.Bootstrap ?? throw new InvalidOperationException("Surface-finish pass executed before bootstrap initialization.");

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
        public WorldTileStore Store => store;

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
