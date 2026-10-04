using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Final ordinary-world TerrariaServer 1.4.5.8 world-generation overlay. It replaces the eight native passes after
/// Micro Biomes and before the compatibility SecretSeeds barrier, completing source-pinned pass identity coverage for
/// the ordinary canonical pipeline without changing the fallback used by secret seeds or synthetic sizes.
/// The source catalog has 109 registrations, including two conditional Skyblock entries; ordinary coverage is 107.
/// Registration coverage is not proof of pass geometry or complete-world equality.
/// </summary>
public sealed class SourceBackedFinal1458 : IWorldGenerationProvider
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
    private readonly SourceBackedMicroBiomes1458 baseline = new();

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

        var state = new FinalState1458();
        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id != SecretSeedsId)
            {
                builder.Add(entry.Descriptor, entry.Pass);
                continue;
            }

            Add(builder, SettleLiquidsAgainId, SourceBackedMicroBiomes1458.MicroBiomesId,
                new FinalPass1458(FinalStage1458.SettleLiquidsAgain, state));
            Add(builder, CactusPalmTreesCoralId, SettleLiquidsAgainId,
                new FinalPass1458(FinalStage1458.CactusPalmTreesCoral, state));
            Add(builder, TileCleanupId, CactusPalmTreesCoralId,
                new FinalPass1458(FinalStage1458.TileCleanup, state));
            Add(builder, LihzahrdAltarsId, TileCleanupId,
                new FinalPass1458(FinalStage1458.LihzahrdAltars, state));
            Add(builder, WaterPlantsId, LihzahrdAltarsId,
                new FinalPass1458(FinalStage1458.WaterPlants, state));
            Add(builder, StalacId, WaterPlantsId,
                new FinalPass1458(FinalStage1458.Stalac, state));
            Add(builder, RemoveBrokenTrapsId, StalacId,
                new FinalPass1458(FinalStage1458.RemoveBrokenTraps, state));
            Add(builder, FinalCleanupId, RemoveBrokenTrapsId,
                new FinalPass1458(FinalStage1458.FinalCleanup, state));
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

internal enum FinalStage1458 : byte
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

internal sealed class FinalState1458
{
    public VanillaWorldGenerationBootstrapState1458? Bootstrap { get; private set; }
    public WorldGenerationLayers Layers { get; private set; }

    public void EnsureInitialized(IWorldGenerationContext context, Workspace workspace)
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

internal sealed class FinalPass1458 : IWorldGenerationPass
{
    private const ushort Grass = 2;
    private const ushort Plants = 3;
    private const ushort JungleGrass = 60;
    private const ushort JunglePlants = 61;
    private const ushort MushroomGrass = 70;
    private const ushort MushroomPlants = 71;
    private const ushort Plants2 = 73;
    private const ushort JunglePlants2 = 74;
    private const ushort Sand = 53;
    private const ushort Coral = 81;
    private const ushort PressurePlate = 135;
    private const ushort Trap = 137;
    private const ushort Stalactite = 165;
    private const ushort LihzahrdBrick = 226;
    private const ushort LihzahrdAltar = 237;
    private const ushort LilyPad = 518;
    private const ushort Cattail = 519;
    private static readonly HashSet<ushort> SlowlyDiesInWater =
    [
        3, 20, 24, 27, 73, 80, 110, 201, 529, 530, 590, 595, 615, 637
    ];

    private readonly FinalStage1458 stage;
    private readonly FinalState1458 state;

    public FinalPass1458(
        FinalStage1458 stage,
        FinalState1458 state)
    {
        this.stage = stage;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Workspace workspace = context.Workspace as Workspace ??
            throw new InvalidOperationException("Final vanilla world generation requires Workspace.");
        state.EnsureInitialized(context, workspace);
        var grid = new RuntimeGrid(workspace);
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Final vanilla world generation requires shared UnifiedRandom semantics.");

        switch (stage)
        {
            case FinalStage1458.SettleLiquidsAgain:
                ApplySettleLiquidsAgain(context, grid);
                break;
            case FinalStage1458.CactusPalmTreesCoral:
                ApplyCactusPalmTreesCoral(context, grid, random);
                break;
            case FinalStage1458.TileCleanup:
                ApplyTileCleanup(context, grid, random);
                break;
            case FinalStage1458.LihzahrdAltars:
                ApplyLihzahrdAltars(context, workspace, grid);
                break;
            case FinalStage1458.WaterPlants:
                ApplyWaterPlants(context, grid, random);
                break;
            case FinalStage1458.Stalac:
                ApplyStalac(context, workspace, random);
                break;
            case FinalStage1458.RemoveBrokenTraps:
                ApplyRemoveBrokenTraps(context, grid);
                break;
            case FinalStage1458.FinalCleanup:
                ApplyFinalCleanup(context, grid, random);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static void ApplySettleLiquidsAgain(IWorldGenerationContext context, RuntimeGrid grid)
    {
        // TerrariaServer 1.4.5.8 GenPassNameID.SettleLiquidsPart2AndNotTheBees repeats the ordinary
        // world-generation QuickWater -> WaterCheck -> ten bounded quick-settle rounds -> WaterCheck
        // sequence. Column compaction is not equivalent: it skips reactions, WaterCheck destruction and
        // the source queue ordering that controls downstream liquid state.
        VanillaWaterCheckDiagnostic1458 waterCheck = new VanillaWorldLiquidSimulator1458(grid.Store)
            .SettleDuringWorldGeneration(context.CancellationToken);
        if (!waterCheck.IsApplied)
        {
            throw new InvalidOperationException(
                $"Settle Liquids Again WaterCheck cannot resolve tile {waterCheck.TileType.Value} at {waterCheck.X},{waterCheck.Y}.");
        }

        context.ReportProgress(1d, "Applied source-backed QuickWater/WaterCheck liquid settle again");
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
        // The ordinary source pass scans from the world top to worldSurface-1, not a narrow band
        // around that layer marker: dry beach surfaces can be well above the marker.
        const int minSurface = 1;
        int maxSurface = Math.Min(grid.Height - 1, (int)Math.Ceiling(state.Layers.WorldSurface - 1));

        for (int x = 3; x < grid.Width - 3; x++)
        {
            if ((x & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int floor = grid.FindFirstActiveY(x, minSurface, maxSurface);
            if (floor <= 2 || floor >= grid.Height - 2 || grid.At(x, floor).Type != Sand)
                continue;

            bool beach = x <= bootstrap.LeftBeachEnd + 90 || x >= bootstrap.RightBeachStart - 90;
            if (beach && random.Next(9) == 0 && PalmTreeGrower1458.TryGrow(grid.Store, x, floor, random))
            {
                palms++;
                continue;
            }

            if (!beach && random.Next(18) == 0 && CactusGrower1458.Plant(grid.Store, x, floor, random))
                cactus++;
        }

        coral += PlaceCoralBand(context, grid, random, 3, Math.Min(grid.Width - 3, bootstrap.LeftBeachEnd + 110));
        coral += PlaceCoralBand(context, grid, random, Math.Max(3, bootstrap.RightBeachStart - 110), grid.Width - 3);
        context.ReportProgress(1d, $"Cactus, Palm Trees, & Coral complete; cactus={cactus}, palms={palms}, coral={coral}");
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

    private static void ApplyTileCleanup(
        IWorldGenerationContext context,
        RuntimeGrid grid,
        IWorldGenerationVanillaRandom random)
    {
        // TerrariaServer 1.4.5.8 TileCleanup begins with this complete-map slope pass.  It does not
        // normalize inactive frames, liquid kinds, actuator bits or our snapshot-reserved byte; those writes
        // were invented by the former implementation and destroyed state the source deliberately preserves.
        // The later cleanup branches remain separately incomplete, but this shared first boundary must stay
        // source-shaped because they observe the shapes it leaves behind.
        long flattened = 0;
        for (int x = 0; x < grid.Width; x++)
        {
            if ((x & 31) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            for (int y = 0; y < grid.Height; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if ((tile.IsActive && TileCleanupKeepsSlope(tile.Type)) || tile.Shape == 0)
                    continue;
                tile.Shape = 0;
                flattened++;
            }
        }

        // The source's second scan begins at its 40-cell margin. Its first repair turns a top slope into a
        // half brick when it meets the facing half brick. Runtime shape 2 is source slope 1 (left-facing)
        // and shape 3 is source slope 2 (right-facing).
        int repairedTopSlopes = 0;
        int waterDeaths = 0;
        int drips = 0;
        for (int x = 40; x < grid.Width - 40; x++)
        {
            if ((x & 31) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();
            for (int y = 40; y < grid.Height - 40; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.IsActive && tile.Shape is (2 or 3))
                {
                    bool joinsFacingHalfBrick = tile.Shape == 2
                        ? grid.At(x + 1, y).IsActive && grid.At(x + 1, y).Shape == 1
                        : grid.At(x - 1, y).IsActive && grid.At(x - 1, y).Shape == 1;
                    if (joinsFacingHalfBrick)
                    {
                        tile.Shape = 1;
                        repairedTopSlopes++;
                    }
                }

                if (tile.IsActive && tile.LiquidAmount > 0 && SlowlyDiesInWater.Contains(tile.Type))
                {
                    KillSingleTileDuringCleanup(ref tile);
                    waterDeaths++;
                }

                // TerrariaServer 1.4.5.8 TileCleanup's two drip searches are intentionally inside the
                // inner scan and share genRand with all subsequent decoration.  Do not pre-scan liquid
                // columns: both the short-circuiting and the accepted-only draws are observable RNG order.
                if (!tile.IsActive && tile.LiquidAmount == 0 && random.Next(3) != 0 && IsSolidTile(grid.At(x, y - 1)))
                {
                    int ceilingRange = random.Next(15, 21);
                    for (int sourceY = y - 2; sourceY >= y - ceilingRange; sourceY--)
                    {
                        WorldTile source = grid.At(x, sourceY);
                        if (source.LiquidAmount < 128 || source.LiquidKind == WorldLiquidKind.Shimmer)
                            continue;
                        if (random.Next(y - sourceY) > 1)
                            continue;
                        PlaceCleanupDrip(ref tile, source.LiquidKind, tile.Wall == 86);
                        drips++;
                        break;
                    }

                    if (!tile.IsActive)
                    {
                        int floorRange = random.Next(3, 11);
                        for (int sourceY = y + 1; sourceY <= y + floorRange; sourceY++)
                        {
                            WorldTile source = grid.At(x, sourceY);
                            if (source.LiquidAmount < 200 || source.LiquidKind == WorldLiquidKind.Shimmer)
                                continue;
                            if (random.Next((sourceY - y) * 3) > 1)
                                continue;
                            PlaceCleanupDrip(ref tile, source.LiquidKind, forceHoney: false);
                            drips++;
                            break;
                        }
                    }

                    if (!tile.IsActive && random.Next(4) == 0 && grid.At(x, y - 1).Type is 396 or 397)
                    {
                        tile.Type = 461;
                        tile.FrameX = 0;
                        tile.FrameY = 0;
                        tile.Flags |= WorldTileFlags.Active;
                    }
                }

                // Ordinary worlds take TileCleanup's non-extraLiquid wall branches.  Wall 87 is an
                // anti-liquid boundary; the secret-seed conversion branch remains outside this ordinary pass.
                if (tile.Wall is 13 or 14 or 87)
                    tile.LiquidAmount = 0;

                // Source TileCleanup removes the sloped/half-brick tile touching specific trap frames;
                // it deliberately only clears active(), retaining the neighbouring tile's other packed state.
                if (tile.IsActive && tile.Type == Trap)
                {
                    int trapRow = tile.FrameY / 18;
                    if (trapRow <= 2 || trapRow == 5)
                    {
                        int neighbourX = tile.FrameX >= 18 ? x + 1 : x - 1;
                        ref WorldTile neighbour = ref grid.At(neighbourX, y);
                        if (neighbour.Shape != 0)
                            neighbour.Flags &= ~WorldTileFlags.Active;
                    }
                }

                if (tile.IsActive && tile.Type == 162 &&
                    !grid.At(x, y - 1).IsActive && !grid.At(x, y + 1).IsActive &&
                    grid.At(x, y + 1).LiquidAmount == 0 && CanKillCleanupTile162(grid, x, y))
                    tile.Flags &= ~WorldTileFlags.Active;

                // WorldGen.TileCleanup repairs every piece of a broken 2x2 shadow orb / crimson heart
                // independently of which surviving piece caused this branch.  The drunk-world wall override
                // is deliberately deferred with the other special-seed-only cleanup branches; this is the
                // ordinary 1.4.5.8 path selected by the source's `!drunkWorldGen` arm.
                if (tile.IsActive && tile.Type == 31)
                {
                    int frameColumn = tile.FrameX / 18;
                    int originX = x - frameColumn % 2;
                    int originY = y - (tile.FrameY / 18) % 2;
                    int variant = context.Request.Options.Evil == WorldGenerationEvil.Crimson ? 1 : 0;
                    RepairTwoByTwoObject(grid, originX, originY, 31, variant, tile.FrameY / 36);
                }

                // TerrariaServer 1.4.5.8 TileCleanup applies this same independent 2x2 recovery to
                // Life Crystals, boulder-resistant traps, and Shadow Orbs, then restores a terrain
                // support only where it was absent.  Retain an already-active support's material.
                if (tile.IsActive && tile.Type is 12 or 639 or 28)
                {
                    int frameColumn = tile.FrameX / 18;
                    int originX = x - frameColumn % 2;
                    int originY = y - (tile.FrameY / 18) % 2;
                    int styleX = tile.FrameX / 36;
                    int styleY = tile.FrameY / 36;
                    RepairTwoByTwoObject(grid, originX, originY, tile.Type, styleX, styleY);
                    EnsureTwoWideTerrainSupport(grid, originX, originY + 2);
                }

                if (tile.IsActive && tile.Type is 21 or 467)
                    RepairBasicChest(context, grid, x, y, tile.Type, tile.FrameX, tile.FrameY);

                if (tile.IsActive && tile.Type == 26)
                {
                    int frameColumn = tile.FrameX / 18;
                    int originX = x - frameColumn % 3;
                    int originY = y - tile.FrameY / 18;
                    int variant = context.Request.Options.Evil == WorldGenerationEvil.Crimson ? 1 : 0;
                    RepairThreeByTwoObject(grid, originX, originY, 26, variant);
                    EnsureThreeWideHeartSupport(grid, originX, originY + 2);
                    ClearAdjacentHeartFragments(grid, originX, originY);
                }

                if (tile.IsActive && tile.Type == 237 && grid.At(x, y + 1).Type == 232)
                    grid.At(x, y + 1).Type = 226;
            }
        }

        context.ReportProgress(1d, $"Tile Cleanup complete; flattened={flattened}, repairedTopSlopes={repairedTopSlopes}, waterDeaths={waterDeaths}, drips={drips}");
    }

    private static bool IsSolidTile(in WorldTile tile) =>
        // TileCleanup temporarily sets Main.tileSolid[379] = false around its complete scan. The mutation
        // affects its SolidTile ceiling gate for drip offers, then the global table is restored on return.
        tile.IsActive && tile.Type != 379 && VanillaTileCollisionCatalog.IsSolid(new TileTypeId(tile.Type));

    private static void RepairBasicChest(
        IWorldGenerationContext context, RuntimeGrid grid, int x, int y, ushort observedType, short frameX, short frameY)
    {
        int frameColumn = frameX / 18;
        int originX = x - frameColumn % 2;
        int originY = y - frameY / 18;
        int style = frameX / 36;
        ushort type = observedType == 467 ? (ushort)467 : (ushort)21;

        if (context.Workspace is Workspace workspace)
        {
            foreach (WorldChest chest in workspace.CaptureGeneratedChests())
            {
                if (chest.X != originX || chest.Y != originY || chest.Items.Length == 0)
                    continue;

                style = chest.Items[0].ItemType switch
                {
                    1156 => 23,
                    1571 => 24,
                    1569 => 25,
                    1260 => 26,
                    1572 => 27,
                    _ => style
                };
                break;
            }
        }

        RepairTwoByTwoObject(grid, originX, originY, type, style, 0);
        EnsureTwoWideTerrainSupport(grid, originX, originY + 2);
    }

    // The reachable WorldGen.CanKillTile prefix for TileCleanup's type-162 arm in TerrariaServer 1.4.5.8.
    // Type 162 is not a boulder/container, so its later object-specific branches cannot apply here.
    private static bool CanKillCleanupTile162(RuntimeGrid grid, int x, int y)
    {
        ref WorldTile tile = ref grid.At(x, y);
        if (!tile.IsActive || tile.Wall == 350)
            return false;

        ref WorldTile above = ref grid.At(x, y - 1);
        if (!above.IsActive)
            return true;

        if (IsTreeTrunk(above.Type) && above.Type != tile.Type &&
            (above.FrameX != 66 || above.FrameY is < 0 or > 44) &&
            (above.FrameX != 88 || above.FrameY is < 66 or > 110) && above.FrameY < 198)
            return false;

        return above.Type switch
        {
            323 or 21 or 26 or 72 or 77 or 88 or 467 or 488 => above.Type == tile.Type,
            80 => above.Type == tile.Type || (above.FrameX / 18 is not (0 or 1 or 4 or 5)),
            _ => true
        };
    }

    // TileID.Sets.IsATreeTrunk from TerrariaServer 1.4.5.8, independently read from the official table.
    private static bool IsTreeTrunk(ushort type) => type is 5 or 72 or 583 or 584 or 585 or 586 or 587 or 588 or 589 or 596 or 616 or 634;

    private static void PlaceCleanupDrip(ref WorldTile destination, WorldLiquidKind kind, bool forceHoney)
    {
        destination.Type = forceHoney
            ? (ushort)375
            : (ushort)(kind switch
            {
                WorldLiquidKind.Lava => 374,
                WorldLiquidKind.Honey => 375,
                WorldLiquidKind.Shimmer => 709,
                _ => 373
            });
        destination.FrameX = 0;
        destination.FrameY = 0;
        destination.Flags |= WorldTileFlags.Active;
    }

    private static void RepairTwoByTwoObject(
        RuntimeGrid grid,
        int originX,
        int originY,
        ushort type,
        int styleX,
        int styleY)
    {
        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            ref WorldTile piece = ref grid.At(originX + dx, originY + dy);
            piece.Flags |= WorldTileFlags.Active;
            piece.Shape = 0;
            piece.Type = type;
            piece.FrameX = checked((short)(dx * 18 + 36 * styleX));
            piece.FrameY = checked((short)(dy * 18 + 36 * styleY));
        }
    }

    private static void EnsureTwoWideTerrainSupport(RuntimeGrid grid, int originX, int supportY)
    {
        for (int dx = 0; dx < 2; dx++)
        {
            ref WorldTile support = ref grid.At(originX + dx, supportY);
            if (!support.IsActive)
            {
                support.Flags |= WorldTileFlags.Active;
                support.Type = TerrainTileForWall(support.Wall);
            }

            support.Shape = 0;
        }
    }

    private static void RepairThreeByTwoObject(RuntimeGrid grid, int originX, int originY, ushort type, int style)
    {
        for (int dx = 0; dx < 3; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            ref WorldTile piece = ref grid.At(originX + dx, originY + dy);
            piece.Flags |= WorldTileFlags.Active;
            piece.Shape = 0;
            piece.Type = type;
            piece.FrameX = checked((short)(dx * 18 + 54 * style));
            piece.FrameY = checked((short)(dy * 18));
        }
    }

    private static void EnsureThreeWideHeartSupport(RuntimeGrid grid, int originX, int supportY)
    {
        for (int dx = 0; dx < 3; dx++)
        {
            ref WorldTile support = ref grid.At(originX + dx, supportY);
            if (!support.IsActive)
            {
                support.Flags |= WorldTileFlags.Active;
                support.Type = TerrainTileForWall(support.Wall);
            }

            TileTypeId supportType = new(support.Type);
            if ((!VanillaTileCollisionCatalog.IsSolid(supportType) || VanillaTileCollisionCatalog.IsSolidTop(supportType)) &&
                !VanillaTileIds.IsPlatform(supportType))
                support.Type = TerrainTileForWall(support.Wall);

            support.Shape = 0;
            ref WorldTile below = ref grid.At(originX + dx, supportY + 1);
            if (below.Type == 28 && below.FrameY % 36 >= 18)
            {
                below.Type = 0;
                below.Flags &= ~WorldTileFlags.Active;
            }
        }
    }

    private static void ClearAdjacentHeartFragments(RuntimeGrid grid, int originX, int originY)
    {
        for (int dy = 0; dy < 3; dy++)
        {
            ref WorldTile left = ref grid.At(originX - 1, originY + dy);
            if ((left.Type is 28 or 12 or 639) && left.FrameX % 36 < 18)
            {
                left.Type = 0;
                left.Flags &= ~WorldTileFlags.Active;
            }

            ref WorldTile right = ref grid.At(originX + 3, originY + dy);
            // Preserve the official source's apparent left-neighbour type-639 predicate in this arm.
            if (((right.Type is 28 or 12) || left.Type == 639) && right.FrameX % 36 >= 18)
            {
                right.Type = 0;
                right.Flags &= ~WorldTileFlags.Active;
            }
        }
    }

    // Terraria.ID.WallID.Sets.WallTypeToTerrainTileType in TerrariaServer 1.4.5.8. The remaining
    // ordinary wall identities map to Dirt (0), as independently used by SurfaceLakes1458.
    private static ushort TerrainTileForWall(ushort wall) => wall switch
    {
        40 => 147,
        71 => 161,
        15 => 59,
        86 => 225,
        3 => 25,
        83 => 203,
        178 => 367,
        180 => 368,
        _ => 0
    };

    // Terraria.TileID.Sets.SaveSlopes, built from original solidity in PostSetupContent plus its eight
    // non-solid exceptions. It must not be derived from mutable generation-time collision overrides.
    private static bool TileCleanupKeepsSlope(ushort type) =>
        VanillaTileCollisionCatalog.IsSolid(new TileTypeId(type)) ||
        type is 131 or 351 or 336 or 340 or 342 or 341 or 343 or 344;

    // TileCleanup calls WorldGen.KillTile rather than clearing fluid; retain the liquid while applying the
    // single-cell state mutation shared with the verified generation/loading KillTile boundary.
    private static void KillSingleTileDuringCleanup(ref WorldTile tile)
    {
        tile.Type = 0;
        tile.FrameX = -1;
        tile.FrameY = -1;
        tile.TileColor = 0;
        tile.Shape = 0;
        tile.Flags &= ~(
            WorldTileFlags.Active |
            WorldTileFlags.Inactive |
            WorldTileFlags.InvisibleBlock |
            WorldTileFlags.FullbrightBlock);
    }

    private static void ApplyLihzahrdAltars(
        IWorldGenerationContext context,
        Workspace workspace,
        RuntimeGrid grid)
    {
        VanillaLihzahrdAltarState1458 altar = workspace.VanillaLihzahrdAltarState ??
            throw new InvalidOperationException(
                "Lihzahrd Altars requires the Jungle Temple GenVars.lAltarX/lAltarY anchor.");

        // TerrariaServer 1.4.5.8 GenPassNameID.LihzahrdAltar consumes makeTemple's exact GenVars anchor;
        // it neither discovers a temple nor draws RNG. SquareTileFrame follows these writes in vanilla, but
        // no runtime re-framer is needed for this fixed explicit 3x2 footprint.
        for (int dx = 0; dx < 3; dx++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            for (int dy = 0; dy < 2; dy++)
            {
                ref WorldTile tile = ref grid.At(altar.X + dx, altar.Y + dy);
                tile.Flags |= WorldTileFlags.Active;
                tile.Type = LihzahrdAltar;
                tile.FrameX = checked((short)(dx * 18));
                tile.FrameY = checked((short)(dy * 18));
            }

            ref WorldTile support = ref grid.At(altar.X + dx, altar.Y + 2);
            support.Flags |= WorldTileFlags.Active;
            support.Shape = 0;
            support.Type = LihzahrdBrick;
        }

        context.ReportProgress(1d, "Lihzahrd Altars complete; placed at retained temple anchor");
    }

    private void ApplyWaterPlants(
        IWorldGenerationContext context,
        RuntimeGrid grid,
        IWorldGenerationVanillaRandom random)
    {
        // TerrariaServer 1.4.5.8 GenPassNameID.LilypadsCattailsBambooAndSeaweed walks every ordinary
        // surface column. This ordering is important: the one-in-five offer is consumed before any
        // placement helper decides whether it can use the site.
        int surface = Math.Clamp((int)state.Layers.WorldSurface, 1, grid.Height - 1);
        int placed = 0;
        for (int x = 20; x < grid.Width - 20; x++)
        {
            if ((x & 31) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            for (int y = 1; y < surface; y++)
            {
                if (random.Next(5) != 0 || grid.At(x, y).LiquidAmount == 0)
                    continue;

                if (!grid.At(x, y).IsActive)
                {
                    if (random.Next(2) == 0)
                        placed += TryPlaceLilyPad(grid, random, x, y) ? 1 : 0;
                    else
                    {
                        VanillaCatTailAnchor1458? anchor = CatTail1458.TryPlace(grid.Store, random, x, y);
                        if (anchor is { } value)
                        {
                            placed++;
                            int growth = random.Next(14);
                            for (int k = 0; k < growth; k++)
                                CatTail1458.Grow(grid.Store, random, value.X, value.Y);
                        }
                    }
                }

                WorldTile current = grid.At(x, y);
                if ((!current.IsActive || current.Type is JunglePlants or JunglePlants2) &&
                    TryPlaceBamboo(grid, random, x, y, surface))
                {
                    placed++;
                    int growth = random.Next(10, 20);
                    for (int l = 0; l < growth && TryPlaceBamboo(grid, random, x, y - l, surface); l++)
                        placed++;
                }
            }

            // Main.UnderworldLayer is the source's top-of-underworld line (ordinary worlds use the
            // maxTilesY-200 convention); the seaweed scan must not inspect the lava layer below it.
            int underworldLayer = Math.Clamp(grid.Height - 200, surface + 1, grid.Height - 1);
            for (int y = underworldLayer; y > surface; y--)
            {
                WorldTile tile = grid.At(x, y);
                if (!tile.IsActive)
                    continue;
                if (tile.Type == Sand && random.Next(3) != 0)
                    GrowSeaweed(grid, random, x, y);
                else if (tile.Type == 549)
                    GrowSeaweed(grid, random, x, y);
            }
        }
        context.ReportProgress(1d, $"Water Plants complete; placed={placed}");
    }

    private static bool TryPlaceLilyPad(RuntimeGrid grid, IWorldGenerationVanillaRandom random, int x, int y)
    {
        if (x < 50 || x > grid.Width - 50 || y < 50 || y > grid.Height - 50)
            return false;
        WorldTile start = grid.At(x, y);
        if (start.IsActive || start.LiquidAmount == 0 || start.LiquidKind != WorldLiquidKind.Water)
            return false;
        while (grid.At(x, y).LiquidAmount > 0 && y > 50)
            y--;
        y++;
        WorldTile surface = grid.At(x, y);
        if (surface.IsActive || grid.At(x, y - 1).IsActive || surface.LiquidAmount == 0 ||
            surface.LiquidKind != WorldLiquidKind.Water ||
            (surface.Wall != 0 && surface.Wall != 15 && surface.Wall != 70 && (surface.Wall < 63 || surface.Wall > 68)))
            return false;

        int neighbours = 0;
        for (int i = x - 5; i <= x + 5; i++)
        for (int j = y - 5; j <= y + 5; j++)
            if (grid.At(i, j).IsActive && grid.At(i, j).Type == LilyPad)
                neighbours++;
        if (neighbours > 3)
            return false;

        int floor = y;
        while ((!grid.At(x, floor).IsActive || !VanillaTileCollisionCatalog.IsSolid(grid.At(x, floor).TileType) ||
                VanillaTileCollisionCatalog.IsSolidTop(grid.At(x, floor).TileType)) && floor < grid.Height - 50)
        {
            if (grid.At(x, floor).IsActive && grid.At(x, floor).Type == Cattail)
                return false;
            floor++;
        }
        if (floor - y is > 12 or < 3)
            return false;
        WorldTile ground = grid.At(x, floor);
        short frameY = ground.Type switch { 2 or 477 => 0, 109 or 492 or 116 => 18, 60 => 36, _ => -1 };
        if (frameY < 0)
            return false;

        ref WorldTile pad = ref grid.At(x, y);
        pad.Flags |= WorldTileFlags.Active;
        pad.Flags &= ~WorldTileFlags.Inactive;
        pad.Type = LilyPad;
        pad.TileColor = ground.TileColor;
        pad.Shape = 0;
        pad.FrameY = frameY;
        if (random.Next(2) == 0)
            pad.FrameX = checked((short)(18 * random.Next(3)));
        else if (random.Next(15) == 0)
            pad.FrameX = checked((short)(18 * random.Next(18)));
        else
        {
            int fifth = grid.Width / 5;
            int group = x < fifth ? random.Next(6, 9) : x < fifth * 2 ? random.Next(9, 12) :
                x < fifth * 3 ? random.Next(3, 6) : x < fifth * 4 ? random.Next(15, 18) : random.Next(12, 15);
            pad.FrameX = checked((short)(18 * group));
        }
        return true;
    }

    private static bool TryPlaceBamboo(
        RuntimeGrid grid,
        IWorldGenerationVanillaRandom random,
        int x,
        int y,
        int surface)
    {
        // WorldGen.PlaceBamboo draws this before every rejection, including malformed candidates.
        int densityRoll = random.Next(1, 21);
        if ((uint)x >= (uint)grid.Width || (uint)y >= (uint)(grid.Height - 1))
            return false;
        WorldTile tile = grid.At(x, y);
        if (tile.Wall > 0 && y <= surface || tile.IsActive && tile.Type == 314)
            return false;
        WorldTile below = grid.At(x, y + 1);
        if (!below.IsActive || below.Type is not (571 or JungleGrass))
            return false;
        int waterDepth = GetWaterDepth(grid, x, y);
        if (waterDepth is < 2 or > 5)
            return false;
        int plants = CountGrowingPlantTiles(grid, x, y, 5, 571);
        int distance = 1;
        if (below.Type == 571)
        {
            while (y + distance < grid.Height && !IsOrdinarySolid(grid.At(x, y + distance)))
                distance++;
            if (y + distance >= grid.Height || distance + plants / random.Next(1, 21) > densityRoll)
                return false;
        }
        else
            plants += 25;
        plants += distance * 2;
        if (plants > random.Next(40, 61))
            return false;

        ref WorldTile planted = ref grid.At(x, y);
        planted.Flags |= WorldTileFlags.Active;
        planted.Flags &= ~WorldTileFlags.Inactive;
        planted.Type = 571;
        planted.FrameX = 0;
        planted.FrameY = 0;
        planted.Shape = 0;
        planted.TileColor = below.TileColor;
        planted.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        planted.Flags |= below.Flags & (WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        return true;
    }

    private static int GetWaterDepth(RuntimeGrid grid, int x, int y)
    {
        int bottom = y;
        while (bottom < grid.Height && !IsOrdinarySolid(grid.At(x, bottom)))
            bottom++;
        if (bottom >= grid.Height)
            return 0;
        bottom--;
        int top = bottom;
        while (top >= 0 && grid.At(x, top).LiquidAmount > 0 && !IsOrdinarySolid(grid.At(x, top)))
            top--;
        return bottom - top;
    }

    private static int CountGrowingPlantTiles(RuntimeGrid grid, int x, int y, int range, ushort type)
    {
        int count = 0;
        for (int i = Math.Max(0, x - range); i <= Math.Min(grid.Width - 1, x + range); i++)
        for (int j = Math.Max(0, y - range * 3); j <= Math.Min(grid.Height - 1, y + range * 3); j++)
            if (grid.At(i, j).IsActive && grid.At(i, j).Type == type)
                count++;
        return count;
    }

    private static void GrowSeaweed(RuntimeGrid grid, IWorldGenerationVanillaRandom random, int x, int y)
    {
        WorldTile tile = grid.At(x, y);
        if (tile.Type == 549 && tile.LiquidAmount < 200 || grid.At(x, y - 1).LiquidAmount < 200)
        {
            if (tile.IsActive && tile.Type == 549 && random.Next(2) == 0)
                KillSingleTileDuringCleanup(ref grid.At(x, y));
            return;
        }
        if (grid.At(x, y - 1).IsActive || grid.At(x, y - 2).IsActive || random.Next(1) != 0 ||
            grid.At(x, y - 2).LiquidAmount != byte.MaxValue || grid.At(x, y - 3).LiquidAmount != byte.MaxValue)
            return;
        int neighbours = 0;
        for (int i = Math.Max(0, x - 4); i <= Math.Min(grid.Width - 1, x + 4); i++)
        for (int j = y; j <= Math.Min(grid.Height - 1, y + 12); j++)
        {
            if (grid.At(i, j).IsActive && grid.At(i, j).Type == 549 && ++neighbours > 30)
                return;
        }
        int floor = y;
        while (floor < grid.Height - 50 && !IsOrdinarySolid(grid.At(x, floor)))
            floor++;
        if (floor - y < 17 - random.Next(20))
        {
            ref WorldTile seaweed = ref grid.At(x, y - 1);
            seaweed.Flags |= WorldTileFlags.Active;
            seaweed.Flags &= ~WorldTileFlags.Inactive;
            seaweed.Type = 549;
            seaweed.FrameX = 0;
            seaweed.FrameY = 0;
            seaweed.Shape = 0;
        }
    }

    private static bool IsOrdinarySolid(in WorldTile tile) =>
        tile.IsActive && VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
        !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);

    /// <summary>
    /// Source <c>GenPassNameID.SpeleothemsAndGemTrees</c>, delegated to <see cref="SpeleothemPass1458"/>.
    /// </summary>
    /// <remarks>
    /// The runtime sampled a bounded number of random cells - <c>max(20, width / 7)</c> attempts - and grew no
    /// gem trees at all, which is why an official Large world had 1,185 gem-tree cells against this runtime's
    /// 70, all of those from the Shimmer pass. The source scans every column instead, offers a gem tree of a
    /// randomly chosen identity on a one-in-five draw and a speleothem on another, and runs a second pass above
    /// the surface for ice and the two evil stones.
    /// </remarks>
    private void ApplyStalac(IWorldGenerationContext context, Workspace workspace, IWorldGenerationVanillaRandom random)
    {
        const int beachDistance = 380;
        var pass = new SpeleothemPass1458(
            workspace.TileStore,
            random,
            state.Layers.WorldSurface,
            state.Layers.RockLayer,
            beachDistance,
            context.CancellationToken);

        pass.Apply();
        context.ReportProgress(
            1d,
            $"Speleothems and gem trees complete; speleothems={pass.Speleothems} gemTrees={pass.GemTrees}");
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

    private void ApplyFinalCleanup(IWorldGenerationContext context, RuntimeGrid grid, IWorldGenerationVanillaRandom random)
    {
        long normalized = FillWallHolesAboveSurface(grid, (int)state.Layers.WorldSurface);
        var framing = new GenerationTileFraming1458(grid.Store, random);
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

                if (GenerationObsidianDoorFraming1458.Check(grid.Store, x, y))
                    normalized++;
                // TerrariaServer 1.4.5.8 restores the entire 2x2 boulder footprint here, before
                // TileFrame reaches Check2x2 later in this same cell. Do not validate an incomplete
                // 484/485 fragment first: FinalCleanup repairs it rather than deleting it.
                if (tile.IsActive && GenerationObjectSupport1458.IsBoulder(tile.Type))
                {
                    RepairFinalCleanupBoulder(grid, x, y, tile.Type, tile.FrameX, tile.FrameY);
                    normalized++;
                }
                if (GenerationDesertObjectFraming1458.Check(grid.Store, x, y))
                    normalized++;

                // TerrariaServer 1.4.5.8 FinalCleanup first stabilizes unsupported surface sand-family
                // cells before its per-cell framing tail.  This deliberately changes only identity/activity/
                // shape; it does not normalize unrelated packed tile state.
                if (tile.IsActive && y + 1 < grid.Height && IsLooseSurfaceMaterial(tile.Type) &&
                    !IsOrdinarySolid(grid.At(x, y + 1)))
                {
                    ref WorldTile below = ref grid.At(x, y + 1);
                    if (y < state.Layers.WorldSurface + 10 && (!below.IsActive || VanillaProjectileTileCutFacts.IsCuttable(below.TileType)) &&
                        below.Wall != 191 && !IsOceanDepth(x, y, grid.Width))
                    {
                        StabilizeLooseSurfaceMaterial(grid, x, y);
                    }
                    else if (below.IsActive && IsOrdinarySolid(below))
                    {
                        below.Shape = 0;
                    }
                    else
                    {
                        tile.Type = LooseMaterialFallback(tile.Type);
                    }
                    // The source immediately frames an above type-323 cell after this terrain rewrite.
                    // It is not cosmetic: TileFrame may reject an invalid liquid plant before that cell's
                    // own turn in the scan.
                    if (y > 0 && grid.At(x, y - 1).Type == 323)
                        framing.TileFrame(x, y - 1);
                    normalized++;
                }

                if (tile.Wall is 187 or 216 && tile.LiquidAmount > 0)
                {
                    tile.LiquidAmount = byte.MaxValue;
                    tile.LiquidKind = WorldLiquidKind.Lava;
                    normalized++;
                }
                if (tile.Type == Trap && tile.Shape != 0)
                {
                    tile.Shape = 0;
                    normalized++;
                }
                if (tile.IsActive && tile.Type == 323 && tile.LiquidAmount > 0)
                {
                    KillSingleTileDuringCleanup(ref tile);
                    normalized++;
                }
                if (DungeonGenerationTiles1458.IsDungeonWall(tile.Wall))
                {
                    tile.LiquidKind = WorldLiquidKind.Water;
                    if (tile.Type == 374) tile.Type = 373;
                    if (tile.IsActive && tile.Type == 56)
                    {
                        KillSingleTileDuringCleanup(ref tile);
                        tile.LiquidAmount = byte.MaxValue;
                    }
                    normalized++;
                }
                if (tile.IsActive && tile.Type == 314)
                {
                    // TerrariaServer 1.4.5.8 clears the current row plus fourteen above it. Its
                    // `while (j - num14 < 15)` loop is not an inclusive fifteen-rows-above range.
                    for (int clearY = Math.Max(0, y - 14); clearY <= y; clearY++)
                        grid.At(x, clearY).LiquidAmount = 0;
                    normalized++;
                }
                if (tile.IsActive && tile.Type == 332 && y + 1 < grid.Height && !grid.At(x, y + 1).IsActive)
                {
                    ref WorldTile support = ref grid.At(x, y + 1);
                    support = default;
                    // The source first assigns 332, then its complete FinalCleanup tail (including the
                    // generation queue finalization) leaves the freshly filled support as plain stone.
                    // Keep the observable post-pass state rather than retaining the transient sandfall ID.
                    support.Type = 1;
                    support.Flags = WorldTileFlags.Active;
                    normalized++;
                }

                if (tile.IsActive && y + 1 < grid.Height && IsUnsupportedSingleTilePlant(in tile, in grid.At(x, y + 1)))
                {
                    ClearTile(ref tile, preserveLiquid: true);
                    normalized++;
                }
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
                // FinalCleanup removes isolated partial surface fluid after TileCleanup may have restored
                // terrain supports into it. Do not clear full pools, coastal fluid or cloud-supported fluid.
                if (x > FinalCleanupBeachMargin && x < grid.Width - FinalCleanupBeachMargin &&
                    y < state.Layers.WorldSurface && tile.LiquidAmount is > 0 and < byte.MaxValue &&
                    grid.At(x - 1, y).LiquidAmount < byte.MaxValue &&
                    grid.At(x + 1, y).LiquidAmount < byte.MaxValue &&
                    grid.At(x, y + 1).LiquidAmount < byte.MaxValue &&
                    !IsActiveCloud(grid.At(x - 1, y)) && !IsActiveCloud(grid.At(x + 1, y)) &&
                    !IsActiveCloud(grid.At(x, y + 1)))
                {
                    tile.LiquidAmount = 0;
                    tile.LiquidKind = WorldLiquidKind.Water;
                    normalized++;
                }
                if (tile.IsActive && IsPainting(tile.Type) && tile.Wall == 0 &&
                    x >= 2 && x < grid.Width - 2 && y >= 2 && y < grid.Height - 2)
                {
                    ushort adjacentWall = FirstAdjacentWall(grid, x, y);
                    if (adjacentWall != 0)
                    {
                        tile.Wall = adjacentWall;
                        normalized++;
                    }
                }
                framing.TileFrame(x, y);
                tile.Reserved = 0;
            }
        }
        PlaceFinalCleanupGrassOffers(grid, random);
        context.ReportProgress(1d, $"Final Cleanup complete; normalized={normalized}");
    }


    private const int FinalCleanupBeachMargin = 380; // WorldGen.beachDistance, TerrariaServer 1.4.5.8.

    // WorldGen.oceanDepths uses the generation ocean line, derived from the retained terrain layers.
    // FinalCleanup must leave unsupported surface material alone inside the pre-ocean band.
    private bool IsOceanDepth(int x, int y, int width) =>
        y <= (state.Layers.WorldSurface + state.Layers.RockLayer) / 2d + 40d &&
        (x < FinalCleanupBeachMargin || x > width - FinalCleanupBeachMargin);

    // WorldGen.FillWallHolesInArea(Rectangle(0, 0, maxTilesX, worldSurface)), TerrariaServer 1.4.5.8.
    // This is intentionally a bounded flood through gaps which touch an inactive neighbour; sealed one-cell
    // cavities and regions of 150 cells are retained by vanilla.
    private static int FillWallHolesAboveSurface(RuntimeGrid grid, int surface)
    {
        int filled = 0;
        int lastColumn = Math.Min(grid.Width - 1, grid.Width);
        int endY = Math.Min(surface, grid.Height - 2);
        for (int requestedX = 0; requestedX <= lastColumn; requestedX++)
        {
            int x = Math.Clamp(requestedX, 2, grid.Width - 3);
            bool sawWall = false;
            for (int y = 2; y < endY; y++)
            {
                if (grid.At(x, y).Wall == 0)
                {
                    if (!sawWall)
                        continue;
                    sawWall = false;
                    if (FillWallHole(grid, x, y))
                        filled++;
                }
                else
                    sawWall = true;
            }
        }
        return filled;
    }

    private static bool FillWallHole(RuntimeGrid grid, int originX, int originY)
    {
        const int maximumVisited = 150;
        var visited = new HashSet<(int X, int Y)>();
        var current = new List<(int X, int Y)> { (originX, originY) };
        var next = new List<(int X, int Y)>();
        var wallCounts = new Dictionary<ushort, int>();
        while (current.Count > 0)
        {
            next.Clear();
            foreach ((int x, int y) in current)
            {
                if (visited.Count >= maximumVisited)
                    return false;
                if (x < 1 || x >= grid.Width - 1 || y < 1 || y >= grid.Height - 1)
                    continue;
                if (!visited.Add((x, y)))
                    continue;
                ref WorldTile tile = ref grid.At(x, y);
                if (tile.Wall != 0)
                {
                    wallCounts[tile.Wall] = wallCounts.GetValueOrDefault(tile.Wall) + 1;
                    continue;
                }
                bool touchesOpenSpace = false;
                for (int checkX = x - 1; checkX <= x + 1 && !touchesOpenSpace; checkX++)
                    touchesOpenSpace = !grid.At(checkX, y).IsActive;
                for (int checkY = y - 1; checkY <= y + 1 && !touchesOpenSpace; checkY++)
                    touchesOpenSpace = !grid.At(x, checkY).IsActive;
                if (!touchesOpenSpace)
                    continue;
                next.Add((x - 1, y));
                next.Add((x + 1, y));
                next.Add((x, y - 1));
                next.Add((x, y + 1));
            }
            (current, next) = (next, current);
        }
        if (visited.Count == 1)
            return false;
        ushort dominantWall = 2;
        int dominantCount = -1;
        foreach ((ushort wall, int count) in wallCounts)
            if (count > dominantCount)
            {
                dominantWall = wall;
                dominantCount = count;
            }
        foreach ((int x, int y) in visited)
            if (grid.At(x, y).Wall == 0)
                grid.At(x, y).Wall = dominantWall;
        return true;
    }

    private static bool IsLooseSurfaceMaterial(ushort type) => type is 53 or 112 or 234 or 224 or 123;

    // TileID.Sets.Paintings in TerrariaServer 1.4.5.8; independently enumerated from the official runtime.
    private static bool IsPainting(ushort type) => type is 240 or 241 or 242 or 245 or 246;

    private static ushort FirstAdjacentWall(RuntimeGrid grid, int x, int y)
    {
        foreach ((int sampleX, int sampleY) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
        {
            ushort wall = grid.At(sampleX, sampleY).Wall;
            if (wall != 0)
                return wall;
        }
        return 0;
    }

    private static void PlaceFinalCleanupGrassOffers(RuntimeGrid grid, IWorldGenerationVanillaRandom random)
    {
        // WorldGen.FinalCleanup's ordinary final offer loop.  Secret-seed multipliers and Mud eligibility
        // belong to the explicit secret-seed barrier; the source-backed provider enters this pass only for
        // the default canonical profile.
        if (grid.Width <= 100 || grid.Height <= 250)
            return;
        int target = grid.Width switch { >= 8400 => 9, >= 6400 => 6, _ => 3 };
        int attempts = 3000;
        int placed = 0;
        while (placed < target && --attempts > 0)
        {
            int x = random.Next(50, grid.Width - 50);
            int y = random.Next(50, grid.Height - 200);
            ref WorldTile tile = ref grid.At(x, y);
            if (!tile.IsActive || tile.Type != 0)
                continue;
            tile = default;
            tile.Flags = WorldTileFlags.Active;
            tile.Type = 668;
            placed++;
        }
    }

    private static ushort LooseMaterialFallback(ushort type) => type switch
    {
        53 => 397, 112 => 398, 234 => 399, 224 => 147, 123 => 1, _ => type
    };

    private static void StabilizeLooseSurfaceMaterial(RuntimeGrid grid, int x, int y)
    {
        ref WorldTile source = ref grid.At(x, y);
        int destinationY = y + 1;
        int remaining = 10;
        while (destinationY < grid.Height - 50 && remaining > 0 &&
               (!grid.At(x, destinationY).IsActive || VanillaProjectileTileCutFacts.IsCuttable(grid.At(x, destinationY).TileType)))
        {
            source.Shape = 0;
            ref WorldTile destination = ref grid.At(x, destinationY);
            destination.Flags |= WorldTileFlags.Active;
            destination.Type = source.Type;
            destination.Shape = 0;
            destinationY++;
            remaining--;
        }
        if (remaining == 0 && !grid.At(x, destinationY).IsActive)
        {
            ref WorldTile fallback = ref grid.At(x, destinationY);
            fallback.Type = LooseMaterialFallback(source.Type);
            fallback.Flags |= WorldTileFlags.Active;
        }
        else if (grid.At(x, destinationY).IsActive && IsOrdinarySolid(grid.At(x, destinationY)))
            grid.At(x, destinationY).Shape = 0;
    }

    private static void RepairFinalCleanupBoulder(RuntimeGrid grid, int x, int y, ushort type, short frameX, short frameY)
    {
        int originX = x - frameX / 18;
        int originY = y - frameY / 18;
        bool blockedByHeart = false;
        for (int dx = 0; dx < 2; dx++)
        {
            if (originY > 0 && grid.At(originX + dx, originY - 1).IsActive && grid.At(originX + dx, originY - 1).Type == 26)
                blockedByHeart = true;
            for (int dy = 0; dy < 2; dy++)
            {
                ref WorldTile piece = ref grid.At(originX + dx, originY + dy);
                piece.Flags |= WorldTileFlags.Active;
                piece.Shape = 0;
                piece.Type = type;
                piece.FrameX = checked((short)(dx * 18));
                piece.FrameY = checked((short)(dy * 18));
            }
        }
        if (!blockedByHeart)
            return;
        ushort replacement = type == 484 ? (ushort)397 : (ushort)0;
        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            ref WorldTile piece = ref grid.At(originX + dx, originY + dy);
            piece.Flags |= WorldTileFlags.Active;
            piece.Shape = 0;
            piece.Type = replacement;
            piece.FrameX = 0;
            piece.FrameY = 0;
        }
    }

    // TileID.Sets.Clouds, including the six 1.4.5.8 identities (not MergesWithClouds).
    private static bool IsActiveCloud(in WorldTile tile) =>
        tile.IsActive && tile.Type is 189 or 196 or 460 or 717 or 718 or 719;

    private static bool IsUnsupportedSingleTilePlant(in WorldTile plant, in WorldTile support)
    {
        if (!support.IsActive || support.IsActuated)
            return plant.Type is Plants or Plants2 or JunglePlants or JunglePlants2 or MushroomPlants;

        return plant.Type switch
        {
            Plants or Plants2 => support.Type != Grass,
            JunglePlants or JunglePlants2 => support.Type != JungleGrass,
            MushroomPlants => support.Type != MushroomGrass,
            _ => false
        };
    }

    private VanillaWorldGenerationBootstrapState1458 RequireBootstrap() =>
        state.Bootstrap ?? throw new InvalidOperationException("Final vanilla world generation is not initialized.");

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

    private readonly record struct TileBounds(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left + 1;
        public int Height => Bottom - Top + 1;
    }

    private sealed class RuntimeGrid
    {
        private readonly WorldTileStore store;
        public RuntimeGrid(Workspace workspace) => store = workspace.TileStore;
        public WorldTileStore Store => store;
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
