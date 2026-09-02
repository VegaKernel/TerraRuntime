using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration;

/// <summary>
/// Final ordinary-world TerrariaServer 1.4.5.8 world-generation overlay. It replaces the eight native passes after
/// Micro Biomes and before the compatibility SecretSeeds barrier, completing source-pinned pass identity coverage for
/// the ordinary canonical 109-pass pipeline without changing the fallback used by secret seeds or synthetic sizes.
/// </summary>
public sealed class SourceBackedVanillaWorldGenerationFinal1458 : IWorldGenerationProvider
{
    internal static readonly WorldGenerationPassId SettleLiquidsAgainId = new("terraria:1.4.5.8/SettleLiquidsAgain");
    internal static readonly WorldGenerationPassId CactusPalmTreesCoralId = new("terraria:1.4.5.8/CactusPalmTreesCoral");
    internal static readonly WorldGenerationPassId TileCleanupId = new("terraria:1.4.5.8/TileCleanup");
    internal static readonly WorldGenerationPassId LihzahrdAltarsId = new("terraria:1.4.5.8/LihzahrdAltars");
    internal static readonly WorldGenerationPassId WaterPlantsId = new("terraria:1.4.5.8/WaterPlants");
    internal static readonly WorldGenerationPassId StalacId = new("terraria:1.4.5.8/Stalac");
    internal static readonly WorldGenerationPassId RemoveBrokenTrapsId = new("terraria:1.4.5.8/RemoveBrokenTraps");
    internal static readonly WorldGenerationPassId FinalCleanupId = new("terraria:1.4.5.8/FinalCleanup");

    private static readonly WorldGenerationPassId SecretSeedsId = new("terraria:1.4.5.8/SecretSeeds");
    private readonly SourceBackedVanillaWorldGenerationMicroBiomes1458 baseline = new();

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

        var state = new VanillaFinalWorldGenerationState1458();
        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id != SecretSeedsId)
            {
                builder.Add(entry.Descriptor, entry.Pass);
                continue;
            }

            Add(builder, SettleLiquidsAgainId, SourceBackedVanillaWorldGenerationMicroBiomes1458.MicroBiomesId,
                new VanillaFinalWorldGenerationPass1458(VanillaFinalWorldGenerationStage1458.SettleLiquidsAgain, state));
            Add(builder, CactusPalmTreesCoralId, SettleLiquidsAgainId,
                new VanillaFinalWorldGenerationPass1458(VanillaFinalWorldGenerationStage1458.CactusPalmTreesCoral, state));
            Add(builder, TileCleanupId, CactusPalmTreesCoralId,
                new VanillaFinalWorldGenerationPass1458(VanillaFinalWorldGenerationStage1458.TileCleanup, state));
            Add(builder, LihzahrdAltarsId, TileCleanupId,
                new VanillaFinalWorldGenerationPass1458(VanillaFinalWorldGenerationStage1458.LihzahrdAltars, state));
            Add(builder, WaterPlantsId, LihzahrdAltarsId,
                new VanillaFinalWorldGenerationPass1458(VanillaFinalWorldGenerationStage1458.WaterPlants, state));
            Add(builder, StalacId, WaterPlantsId,
                new VanillaFinalWorldGenerationPass1458(VanillaFinalWorldGenerationStage1458.Stalac, state));
            Add(builder, RemoveBrokenTrapsId, StalacId,
                new VanillaFinalWorldGenerationPass1458(VanillaFinalWorldGenerationStage1458.RemoveBrokenTraps, state));
            Add(builder, FinalCleanupId, RemoveBrokenTrapsId,
                new VanillaFinalWorldGenerationPass1458(VanillaFinalWorldGenerationStage1458.FinalCleanup, state));

            builder.Add(CloneDescriptor(entry.Descriptor, [FinalCleanupId]), entry.Pass);
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
        new(source.Id, source.RngMode, requiredAfter, source.OptionalAfter.ToArray(), source.OptionalBefore.ToArray());

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

internal enum VanillaFinalWorldGenerationStage1458 : byte
{
    SettleLiquidsAgain,
    CactusPalmTreesCoral,
    TileCleanup,
    LihzahrdAltars,
    WaterPlants,
    Stalac,
    RemoveBrokenTraps,
    FinalCleanup
}

internal sealed class VanillaFinalWorldGenerationState1458
{
    public VanillaWorldGenerationBootstrapState1458? Bootstrap { get; private set; }
    public WorldGenerationLayers Layers { get; private set; }

    public void EnsureInitialized(IWorldGenerationContext context, RuntimeWorldGenerationWorkspace workspace)
    {
        if (Bootstrap is not null)
            return;

        Bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Final vanilla world generation requires Reset bootstrap state.");
        if (context.Metadata is null || !context.Metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Final vanilla world generation requires source-backed Terrain layers.");
        Layers = layers;
    }
}

internal sealed class VanillaFinalWorldGenerationPass1458 : IWorldGenerationPass
{
    private const ushort Sand = 53;
    private const ushort Cactus = 80;
    private const ushort Coral = 81;
    private const ushort PressurePlate = 135;
    private const ushort Trap = 137;
    private const ushort Stalactite = 165;
    private const ushort LihzahrdBrick = 226;
    private const ushort LihzahrdAltar = 237;
    private const ushort PalmTree = 323;
    private const ushort LilyPad = 518;
    private const ushort Cattail = 519;

    private readonly VanillaFinalWorldGenerationStage1458 stage;
    private readonly VanillaFinalWorldGenerationState1458 state;

    public VanillaFinalWorldGenerationPass1458(
        VanillaFinalWorldGenerationStage1458 stage,
        VanillaFinalWorldGenerationState1458 state)
    {
        this.stage = stage;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RuntimeWorldGenerationWorkspace workspace = context.Workspace as RuntimeWorldGenerationWorkspace ??
            throw new InvalidOperationException("Final vanilla world generation requires RuntimeWorldGenerationWorkspace.");
        state.EnsureInitialized(context, workspace);
        var grid = new RuntimeGrid(workspace);
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Final vanilla world generation requires shared UnifiedRandom semantics.");

        switch (stage)
        {
            case VanillaFinalWorldGenerationStage1458.SettleLiquidsAgain:
                ApplySettleLiquidsAgain(context, grid);
                break;
            case VanillaFinalWorldGenerationStage1458.CactusPalmTreesCoral:
                ApplyCactusPalmTreesCoral(context, grid, random);
                break;
            case VanillaFinalWorldGenerationStage1458.TileCleanup:
                ApplyTileCleanup(context, grid);
                break;
            case VanillaFinalWorldGenerationStage1458.LihzahrdAltars:
                ApplyLihzahrdAltars(context, grid, random);
                break;
            case VanillaFinalWorldGenerationStage1458.WaterPlants:
                ApplyWaterPlants(context, grid, random);
                break;
            case VanillaFinalWorldGenerationStage1458.Stalac:
                ApplyStalac(context, grid, random);
                break;
            case VanillaFinalWorldGenerationStage1458.RemoveBrokenTraps:
                ApplyRemoveBrokenTraps(context, grid);
                break;
            case VanillaFinalWorldGenerationStage1458.FinalCleanup:
                ApplyFinalCleanup(context, grid);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static void ApplySettleLiquidsAgain(IWorldGenerationContext context, RuntimeGrid grid)
    {
        long moved = 0;
        for (int x = 0; x < grid.Width; x++)
        {
            if ((x & 31) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int top = 0;
            while (top < grid.Height)
            {
                while (top < grid.Height && IsLiquidBarrier(in grid.At(x, top)))
                    top++;
                if (top >= grid.Height)
                    break;

                int bottom = top;
                while (bottom + 1 < grid.Height && !IsLiquidBarrier(in grid.At(x, bottom + 1)))
                    bottom++;

                moved += CompactLiquidSegment(grid, x, top, bottom);
                top = bottom + 1;
            }
        }

        context.ReportProgress(1d, $"Settle Liquids Again complete; compacted {moved} liquid units");
    }

    private static long CompactLiquidSegment(RuntimeGrid grid, int x, int top, int bottom)
    {
        var runs = new List<LiquidRun>(4);
        for (int y = bottom; y >= top; y--)
        {
            ref WorldTile tile = ref grid.At(x, y);
            if (tile.LiquidAmount == 0)
                continue;

            if (runs.Count != 0 && runs[^1].Kind == tile.LiquidKind)
                runs[^1] = runs[^1] with { Amount = runs[^1].Amount + tile.LiquidAmount };
            else
                runs.Add(new LiquidRun(tile.LiquidKind, tile.LiquidAmount));
        }

        if (runs.Count == 0)
            return 0;

        long originalWeighted = 0;
        for (int y = top; y <= bottom; y++)
        {
            ref WorldTile tile = ref grid.At(x, y);
            if (tile.LiquidAmount != 0)
                originalWeighted += (long)tile.LiquidAmount * y;
            tile.LiquidAmount = 0;
            tile.LiquidKind = WorldLiquidKind.Water;
        }

        int writeY = bottom;
        foreach (LiquidRun run in runs)
        {
            int remaining = run.Amount;
            while (remaining > 0 && writeY >= top)
            {
                int amount = Math.Min(byte.MaxValue, remaining);
                ref WorldTile tile = ref grid.At(x, writeY--);
                tile.LiquidAmount = checked((byte)amount);
                tile.LiquidKind = run.Kind;
                remaining -= amount;
            }
        }

        long settledWeighted = 0;
        for (int y = top; y <= bottom; y++)
        {
            WorldTile tile = grid.At(x, y);
            if (tile.LiquidAmount != 0)
                settledWeighted += (long)tile.LiquidAmount * y;
        }
        return Math.Max(0, settledWeighted - originalWeighted);
    }

    private void ApplyCactusPalmTreesCoral(
        IWorldGenerationContext context,
        RuntimeGrid grid,
        IWorldGenerationVanillaRandom random)
    {
        VanillaWorldGenerationBootstrapState1458 bootstrap = RequireBootstrap();
        int cactus = 0;
        int palms = 0;
        int coral = 0;
        int minSurface = Math.Max(2, (int)state.Layers.WorldSurface - 40);
        int maxSurface = Math.Min(grid.Height - 20, (int)state.Layers.WorldSurface + 120);

        for (int x = 3; x < grid.Width - 3; x++)
        {
            if ((x & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int floor = grid.FindFirstActiveY(x, minSurface, maxSurface);
            if (floor <= 2 || floor >= grid.Height - 2 || grid.At(x, floor).Type != Sand)
                continue;

            bool beach = x <= bootstrap.LeftBeachEnd + 90 || x >= bootstrap.RightBeachStart - 90;
            if (beach && random.Next(9) == 0 && TryGrowPalm(grid, x, floor, random))
            {
                palms++;
                continue;
            }

            if (!beach && random.Next(18) == 0 && TryGrowCactus(grid, x, floor, random))
                cactus++;
        }

        coral += PlaceCoralBand(context, grid, random, 3, Math.Min(grid.Width - 3, bootstrap.LeftBeachEnd + 110));
        coral += PlaceCoralBand(context, grid, random, Math.Max(3, bootstrap.RightBeachStart - 110), grid.Width - 3);
        context.ReportProgress(1d, $"Cactus, Palm Trees, & Coral complete; cactus={cactus}, palms={palms}, coral={coral}");
    }

    private static bool TryGrowCactus(RuntimeGrid grid, int x, int floor, IWorldGenerationVanillaRandom random)
    {
        int height = random.Next(2, 6);
        if (!grid.IsEmptyColumn(x, floor - height, floor - 1))
            return false;

        for (int y = floor - 1; y >= floor - height; y--)
            SetObjectTile(ref grid.At(x, y), Cactus, preserveLiquid: false);
        return true;
    }

    private static bool TryGrowPalm(RuntimeGrid grid, int x, int floor, IWorldGenerationVanillaRandom random)
    {
        int height = random.Next(8, 16);
        if (!grid.IsEmptyColumn(x, floor - height, floor - 1))
            return false;

        for (int y = floor - 1; y >= floor - height; y--)
            SetObjectTile(ref grid.At(x, y), PalmTree, preserveLiquid: false);
        return true;
    }

    private static int PlaceCoralBand(
        IWorldGenerationContext context,
        RuntimeGrid grid,
        IWorldGenerationVanillaRandom random,
        int left,
        int right)
    {
        int placed = 0;
        for (int x = Math.Max(2, left); x < Math.Min(grid.Width - 2, right); x++)
        {
            if ((x & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            if (random.Next(10) != 0)
                continue;

            for (int y = 2; y < Math.Min(grid.Height - 2, grid.Height / 3); y++)
            {
                ref WorldTile water = ref grid.At(x, y);
                WorldTile support = grid.At(x, y + 1);
                if (water.IsActive || water.LiquidAmount < 96 || !support.IsActive || support.Type != Sand)
                    continue;

                SetObjectTile(ref water, Coral, frameX: checked((short)(random.Next(6) * 18)), preserveLiquid: true);
                placed++;
                break;
            }
        }
        return placed;
    }

    private static void ApplyTileCleanup(IWorldGenerationContext context, RuntimeGrid grid)
    {
        long normalized = 0;
        for (int x = 0; x < grid.Width; x++)
        {
            if ((x & 31) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            for (int y = 0; y < grid.Height; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                WorldTile before = tile;
                if (tile.Shape > 5)
                    tile.Shape = 0;
                if (!tile.IsActive)
                {
                    tile.FrameX = 0;
                    tile.FrameY = 0;
                    tile.Shape = 0;
                    tile.Flags &= ~(WorldTileFlags.Inactive | WorldTileFlags.Actuator);
                }
                if (tile.LiquidAmount == 0)
                    tile.LiquidKind = WorldLiquidKind.Water;
                tile.Reserved = 0;
                if (!tile.Equals(before))
                    normalized++;
            }
        }
        context.ReportProgress(1d, $"Tile Cleanup complete; normalized={normalized}");
    }

    private static void ApplyLihzahrdAltars(
        IWorldGenerationContext context,
        RuntimeGrid grid,
        IWorldGenerationVanillaRandom random)
    {
        if (grid.ContainsTileType(LihzahrdAltar))
        {
            context.ReportProgress(1d, "Lihzahrd Altars complete; existing altar preserved");
            return;
        }

        if (!grid.TryFindBounds(LihzahrdBrick, out TileBounds bounds))
        {
            context.ReportProgress(1d, "Lihzahrd Altars complete; no temple bounds found");
            return;
        }

        int minX = Math.Max(bounds.Left + 3, 3);
        int maxX = Math.Min(bounds.Right - 3, grid.Width - 4);
        int minY = Math.Clamp(bounds.Top + Math.Max(4, bounds.Height / 2), 3, grid.Height - 5);
        int maxY = Math.Min(bounds.Bottom - 2, grid.Height - 4);
        bool placed = false;

        for (int attempt = 0; attempt < 5000 && minX <= maxX && minY <= maxY; attempt++)
        {
            if ((attempt & 127) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int left = random.Next(minX, maxX + 1) - 1;
            int top = random.Next(minY, maxY + 1) - 1;
            if (!grid.IsEmptyRectangle(left, top, 3, 2))
                continue;
            if (!grid.IsSolidTempleFloor(left, top + 2, 3))
                continue;

            PlaceFramedObject(grid, left, top, 3, 2, LihzahrdAltar);
            placed = true;
            break;
        }

        context.ReportProgress(1d, placed ? "Lihzahrd Altars complete; altar placed" : "Lihzahrd Altars complete; no legal altar site");
    }

    private static void ApplyWaterPlants(
        IWorldGenerationContext context,
        RuntimeGrid grid,
        IWorldGenerationVanillaRandom random)
    {
        int placed = 0;
        int maxY = Math.Min(grid.Height - 3, Math.Max((int)(grid.Height * 0.55d), (int)grid.Height / 3));
        for (int x = 2; x < grid.Width - 2; x++)
        {
            if ((x & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            if (random.Next(18) != 0)
                continue;

            for (int y = 2; y < maxY; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive || tile.LiquidAmount < 64)
                    continue;

                WorldTile above = grid.At(x, y - 1);
                if (above.IsActive || above.LiquidAmount != 0)
                    continue;

                ushort type = random.Next(3) == 0 ? Cattail : LilyPad;
                SetObjectTile(ref tile, type, frameX: checked((short)(random.Next(3) * 18)), preserveLiquid: true);
                placed++;
                break;
            }
        }
        context.ReportProgress(1d, $"Water Plants complete; placed={placed}");
    }

    private void ApplyStalac(
        IWorldGenerationContext context,
        RuntimeGrid grid,
        IWorldGenerationVanillaRandom random)
    {
        int minY = Math.Clamp((int)state.Layers.WorldSurface + 30, 3, grid.Height - 4);
        int maxY = Math.Clamp(grid.Height - 220, minY + 1, grid.Height - 3);
        int target = Math.Max(20, grid.Width / 7);
        int placed = 0;
        int attempts = target * 12;

        for (int attempt = 0; attempt < attempts && placed < target; attempt++)
        {
            if ((attempt & 127) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(3, grid.Width - 3);
            int y = random.Next(minY, maxY);
            ref WorldTile tile = ref grid.At(x, y);
            if (tile.IsActive || tile.LiquidAmount > 64)
                continue;

            bool ceiling = IsNaturalSolid(in grid.At(x, y - 1));
            bool floor = IsNaturalSolid(in grid.At(x, y + 1));
            if (!ceiling && !floor)
                continue;

            short frameX = checked((short)(random.Next(3) * 18));
            short frameY = checked((short)(floor && !ceiling ? 54 : 0));
            SetObjectTile(ref tile, Stalactite, frameX, frameY, preserveLiquid: true);
            placed++;
        }
        context.ReportProgress(1d, $"Stalac complete; placed={placed}");
    }

    private static void ApplyRemoveBrokenTraps(IWorldGenerationContext context, RuntimeGrid grid)
    {
        int removed = 0;
        for (int x = 1; x < grid.Width - 1; x++)
        {
            if ((x & 31) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            for (int y = 1; y < grid.Height - 1; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (!tile.IsActive || (tile.Type != Trap && tile.Type != PressurePlate))
                    continue;
                if (tile.HasAnyWire || grid.HasWireNearby(x, y, tile.Type == Trap ? 8 : 2))
                    continue;

                ClearTile(ref tile, preserveLiquid: true);
                removed++;
            }
        }
        context.ReportProgress(1d, $"Remove Broken Traps complete; removed={removed}");
    }

    private static void ApplyFinalCleanup(IWorldGenerationContext context, RuntimeGrid grid)
    {
        long normalized = 0;
        for (int x = 0; x < grid.Width; x++)
        {
            if ((x & 31) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            for (int y = 0; y < grid.Height; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive && (uint)tile.Type >= (uint)VanillaTileIds.Count)
                    throw new InvalidOperationException($"Final Cleanup found unsupported tile id {tile.Type} at ({x}, {y}).");
                if ((uint)tile.Wall >= (uint)VanillaWallIds.Count)
                    throw new InvalidOperationException($"Final Cleanup found unsupported wall id {tile.Wall} at ({x}, {y}).");
                if (!tile.HasOnlyKnownFlags)
                    throw new InvalidOperationException($"Final Cleanup found unknown tile flags at ({x}, {y}).");

                if (!tile.IsActive && (tile.FrameX != 0 || tile.FrameY != 0 || tile.Shape != 0))
                {
                    tile.FrameX = 0;
                    tile.FrameY = 0;
                    tile.Shape = 0;
                    normalized++;
                }
                if (tile.LiquidAmount == 0 && tile.LiquidKind != WorldLiquidKind.Water)
                {
                    tile.LiquidKind = WorldLiquidKind.Water;
                    normalized++;
                }
                tile.Reserved = 0;
            }
        }
        context.ReportProgress(1d, $"Final Cleanup complete; normalized={normalized}");
    }

    private VanillaWorldGenerationBootstrapState1458 RequireBootstrap() =>
        state.Bootstrap ?? throw new InvalidOperationException("Final vanilla world generation is not initialized.");

    private static bool IsLiquidBarrier(in WorldTile tile)
    {
        if (!tile.IsActive || tile.IsActuated)
            return false;
        return VanillaTileDefinitionCatalog.TryGet(tile.TileType, out VanillaTileDefinition definition) && definition.IsSolid;
    }

    private static bool IsNaturalSolid(in WorldTile tile)
    {
        if (!tile.IsActive || tile.IsActuated || VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
            return false;
        return VanillaTileDefinitionCatalog.TryGet(tile.TileType, out VanillaTileDefinition definition) && definition.IsSolid;
    }

    private static void SetObjectTile(
        ref WorldTile tile,
        ushort type,
        short frameX = 0,
        short frameY = 0,
        bool preserveLiquid = false)
    {
        byte liquidAmount = tile.LiquidAmount;
        WorldLiquidKind liquidKind = tile.LiquidKind;
        tile.Type = type;
        tile.Flags |= WorldTileFlags.Active;
        tile.Flags &= ~WorldTileFlags.Inactive;
        tile.FrameX = frameX;
        tile.FrameY = frameY;
        tile.Shape = 0;
        if (!preserveLiquid)
        {
            tile.LiquidAmount = 0;
            tile.LiquidKind = WorldLiquidKind.Water;
        }
        else
        {
            tile.LiquidAmount = liquidAmount;
            tile.LiquidKind = liquidKind;
        }
    }

    private static void PlaceFramedObject(RuntimeGrid grid, int left, int top, int width, int height, ushort type)
    {
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < height; dy++)
        {
            ref WorldTile tile = ref grid.At(left + dx, top + dy);
            SetObjectTile(ref tile, type, checked((short)(dx * 18)), checked((short)(dy * 18)));
        }
    }

    private static void ClearTile(ref WorldTile tile, bool preserveLiquid)
    {
        byte liquidAmount = tile.LiquidAmount;
        WorldLiquidKind liquidKind = tile.LiquidKind;
        tile.Type = 0;
        tile.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive | WorldTileFlags.Actuator);
        tile.FrameX = 0;
        tile.FrameY = 0;
        tile.Shape = 0;
        if (preserveLiquid)
        {
            tile.LiquidAmount = liquidAmount;
            tile.LiquidKind = liquidKind;
        }
        else
        {
            tile.LiquidAmount = 0;
            tile.LiquidKind = WorldLiquidKind.Water;
        }
    }

    private readonly record struct LiquidRun(WorldLiquidKind Kind, int Amount);
    private readonly record struct TileBounds(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left + 1;
        public int Height => Bottom - Top + 1;
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

        public bool IsEmptyColumn(int x, int top, int bottom)
        {
            if ((uint)x >= (uint)Width || top < 1 || bottom >= Height - 1 || top > bottom)
                return false;
            for (int y = top; y <= bottom; y++)
            {
                if (At(x, y).IsActive)
                    return false;
            }
            return true;
        }

        public bool IsEmptyRectangle(int left, int top, int width, int height)
        {
            if (left < 1 || top < 1 || left + width >= Width - 1 || top + height >= Height - 1)
                return false;
            for (int x = left; x < left + width; x++)
            for (int y = top; y < top + height; y++)
            {
                if (At(x, y).IsActive)
                    return false;
            }
            return true;
        }

        public bool ContainsTileType(ushort type)
        {
            for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                WorldTile tile = At(x, y);
                if (tile.IsActive && tile.Type == type)
                    return true;
            }
            return false;
        }

        public bool TryFindBounds(ushort type, out TileBounds bounds)
        {
            int left = Width;
            int right = -1;
            int top = Height;
            int bottom = -1;
            for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                WorldTile tile = At(x, y);
                if (!tile.IsActive || tile.Type != type)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }

            if (right < left || bottom < top)
            {
                bounds = default;
                return false;
            }
            bounds = new TileBounds(left, top, right, bottom);
            return true;
        }

        public bool IsSolidTempleFloor(int left, int y, int width)
        {
            if (left < 0 || left + width > Width || (uint)y >= (uint)Height)
                return false;
            for (int x = left; x < left + width; x++)
            {
                WorldTile tile = At(x, y);
                if (!tile.IsActive || tile.Type != LihzahrdBrick)
                    return false;
            }
            return true;
        }

        public bool HasWireNearby(int cx, int cy, int radius)
        {
            int left = Math.Max(0, cx - radius);
            int right = Math.Min(Width - 1, cx + radius);
            int top = Math.Max(0, cy - radius);
            int bottom = Math.Min(Height - 1, cy + radius);
            for (int x = left; x <= right; x++)
            for (int y = top; y <= bottom; y++)
            {
                if (At(x, y).HasAnyWire)
                    return true;
            }
            return false;
        }
    }
}
