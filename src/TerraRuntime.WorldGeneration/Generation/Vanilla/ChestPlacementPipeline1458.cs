using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Sixth source-backed Terraria 1.4.5.8 world-generation overlay. It owns the four chest placement passes immediately
/// after Statues, including the structural Underground Houses and Buried Chests slice, and couples every 2x2 chest tile
/// object to the candidate workspace chest side table. Admitted loot is generated at placement time through the shared
/// vanilla RNG; unported chest families remain outside this overlay rather than using a late fallback filler.
/// </summary>
public sealed class SourceBackedChestPlacement1458 : IWorldGenerationProvider
{
    internal static readonly WorldGenerationPassId BuriedChestsId = new("terraria:1.4.5.8/BuriedChests");
    internal static readonly WorldGenerationPassId SurfaceChestsId = new("terraria:1.4.5.8/SurfaceChests");
    internal static readonly WorldGenerationPassId JungleChestsPlacementId =
        new("terraria:1.4.5.8/JungleChestsPlacement");
    internal static readonly WorldGenerationPassId WaterChestsId = new("terraria:1.4.5.8/WaterChests");

    private static readonly WorldGenerationPassId SecretSeedsId = new("terraria:1.4.5.8/SecretSeeds");
    private readonly SourceBackedPostSettle1458 baseline = new();

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

        var state = new ChestPlacementState1458();
        foreach (CapturedPass entry in capture.Entries)
        {
            if (entry.Descriptor.Id != SecretSeedsId)
            {
                builder.Add(entry.Descriptor, entry.Pass);
                continue;
            }

            Add(builder, BuriedChestsId, SourceBackedPostSettle1458.StatuesId,
                new ChestPlacementPass1458(ChestPlacementStage1458.BuriedChests, state));
            Add(builder, SurfaceChestsId, BuriedChestsId,
                new ChestPlacementPass1458(ChestPlacementStage1458.SurfaceChests, state));
            Add(builder, JungleChestsPlacementId, SurfaceChestsId,
                new ChestPlacementPass1458(ChestPlacementStage1458.JungleChestsPlacement, state));
            Add(builder, WaterChestsId, JungleChestsPlacementId,
                new ChestPlacementPass1458(ChestPlacementStage1458.WaterChests, state));

            builder.Add(CloneDescriptor(entry.Descriptor, [WaterChestsId]), entry.Pass);
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

internal enum ChestPlacementStage1458 : byte
{
    BuriedChests,
    SurfaceChests,
    JungleChestsPlacement,
    WaterChests
}

internal sealed class ChestPlacementState1458
{
    public VanillaWorldGenerationBootstrapState1458? Bootstrap { get; private set; }
    public double WorldSurface { get; private set; }
    public double WorldSurfaceLow { get; private set; }
    public double WorldSurfaceHigh { get; private set; }
    public double RockLayer { get; private set; }
    public int UnderworldTop { get; private set; }
    public int LavaLine { get; private set; }
    public int JungleItemCount { get; set; }
    public bool LivingMahoganyWandsGenerated { get; set; }
    public List<CaveHouseRoom1458> ProtectedHouseRooms { get; } = [];

    public void EnsureInitialized(IWorldGenerationContext context, Workspace workspace)
    {
        if (Bootstrap is not null)
            return;

        Bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Chest-placement vanilla generation requires Reset bootstrap state.");
        if (context.Metadata is null || !context.Metadata.TryGetLayers(out WorldGenerationLayers layers))
            throw new InvalidOperationException("Chest-placement vanilla generation requires source-backed Terrain layers.");

        WorldSurface = layers.WorldSurface;
        WorldSurfaceLow = workspace.VanillaTerrainState?.WorldSurfaceLow ?? layers.WorldSurface;
        WorldSurfaceHigh = workspace.VanillaTerrainState?.WorldSurfaceHigh ?? layers.WorldSurface;
        RockLayer = layers.RockLayer;
        UnderworldTop = Math.Clamp(workspace.HeightTiles - 200, (int)RockLayer + 120, workspace.HeightTiles - 90);
        LavaLine = workspace.VanillaLiquidLines?.LavaLine ?? checked((int)Math.Round(RockLayer));

        // One context for the whole run. The shadow key, the ram rune, the living-mahogany wands and the
        // underworld's item cycle are all once-per-world facts, so they have to survive from one chest pass
        // to the next rather than reset with each of them.
        ChestContext = workspace.VanillaBuriedChestContext ?? new BuriedChestContext1458
        {
            Height = workspace.HeightTiles,
            WorldSurface = WorldSurface,
            RockLayer = RockLayer,
            LavaLine = LavaLine,
            CopperBar = Bootstrap.CopperBar,
            IronBar = Bootstrap.IronBar,
            SilverBar = Bootstrap.SilverBar,
            GoldBar = Bootstrap.GoldBar,
            // Source SavedOreTiers.Silver == 168 is the Tungsten Ore tile, which decides whether the
            // underworld's ammo stack is Cursed Bullets or Silver ones.
            TungstenIsSilverTier = Bootstrap.SilverOre == 168,
            DesertHiveLow = workspace.VanillaUndergroundDesertRegion?.Y ?? 0,
            DesertHiveHigh = workspace.VanillaUndergroundDesertRegion?.Bottom ?? 0,
            HellChestItem = Bootstrap.HellChestItems
        };
        workspace.SetVanillaBuriedChestContext(ChestContext);
    }

    /// <summary>The run-wide chest state every chest pass shares.</summary>
    public BuriedChestContext1458 ChestContext { get; private set; } = null!;
}

internal sealed class ChestPlacementPass1458 : IWorldGenerationPass
{
    private const ushort Containers = 21;
    private const int WoodenChestStyle = 0;
    private const int GoldChestStyle = 1;
    private const int ShadowChestStyle = 4;
    private const int IvyChestStyle = 10;
    private const int WaterChestStyle = 17;
    private const int BeachDistance = 380;

    private readonly ChestPlacementStage1458 stage;
    private readonly ChestPlacementState1458 state;

    public ChestPlacementPass1458(
        ChestPlacementStage1458 stage,
        ChestPlacementState1458 state)
    {
        this.stage = stage;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Workspace workspace = context.Workspace as Workspace ??
            throw new InvalidOperationException("Chest-placement Terraria generation requires Workspace.");
        state.EnsureInitialized(context, workspace);
        var grid = new RuntimeGrid(workspace);
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("Chest-placement Terraria generation requires shared UnifiedRandom semantics.");

        switch (stage)
        {
            case ChestPlacementStage1458.BuriedChests:
                ApplyBuriedChests(context, workspace, grid, random);
                break;
            case ChestPlacementStage1458.SurfaceChests:
                ApplySurfaceChests(context, workspace, grid, random);
                break;
            case ChestPlacementStage1458.JungleChestsPlacement:
                ApplyJungleChests(context, workspace, grid, random);
                break;
            case ChestPlacementStage1458.WaterChests:
                ApplyWaterChests(context, workspace, grid, random);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void ApplyBuriedChests(
        IWorldGenerationContext context,
        Workspace workspace,
        RuntimeGrid grid,
        IWorldGenerationVanillaRandom random)
    {
        // Source pass, in order: cave chests, underworld chests, cave houses, extra desert houses. The two
        // chest loops are ported; the two house loops are the cave-house row's and still use the runtime's
        // own placement, so their counts come from the ported pass rather than being invented here.
        var placer = CreatePlacer(workspace, random);
        var pass = new BuriedChestsPass1458(placer, workspace.TileStore, random, state.WorldSurfaceHigh,
            state.RockLayer, (state.WorldSurface + state.RockLayer) / 2d + 40d, BeachDistance,
            context.CancellationToken);
        pass.Apply();
        int chestsPlaced = placer.Chests.Count;
        int houseTarget = pass.CaveHouseCount;
        int additionalDesertTarget = pass.AdditionalDesertHouseCount;

        int housePlaced = 0;
        int houseMinimumY = Math.Clamp((int)(state.WorldSurfaceHigh + 20d), 30, grid.Height - 231);
        int houseMaximumY = Math.Max(houseMinimumY + 1, grid.Height - 230);
        for (int attempt = 0; attempt < 10_000 && housePlaced < houseTarget; attempt++)
        {
            if ((attempt & 63) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int x = random.Next(80, grid.Width - 80);
            int y = random.Next(houseMinimumY, houseMaximumY);
            // WorldGen's third UndergroundHousesAndBuriedChests loop rejects oceanDepths before
            // CaveHouseBiome.Place. Letting the builder inspect an ocean offer changes both its
            // acceptance and the shared RNG stream before the next sampled candidate.
            if (IsOrdinaryHouseOceanOffer1458(x, y, grid.Width, state.WorldSurface, state.RockLayer))
                continue;
            if (CaveHousePlacement1458.TryPlace(workspace, state, random, x, y, out _))
                housePlaced++;
        }

        int additionalDesertPlaced = 0;
        if (workspace.VanillaUndergroundDesertRegion is VanillaUndergroundDesertRegion1458 desert)
        {
            (int desertTop, int desertBottom) = GetAdditionalDesertHouseVerticalRange1458(
                desert.Y, desert.Bottom, state.WorldSurface);
            for (int attempt = 0;
                 attempt < 10_000 && additionalDesertPlaced < additionalDesertTarget && desertBottom > desertTop;
                 attempt++)
            {
                if ((attempt & 63) == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();

                int x = random.Next(desert.X, desert.Right);
                int y = random.Next(desertTop, desertBottom);
                if (CaveHousePlacement1458.TryPlace(workspace, state, random, x, y, out _))
                    additionalDesertPlaced++;
            }
        }
        workspace.SetVanillaCaveHouseCounts(housePlaced, additionalDesertPlaced);

        context.ReportProgress(
            1d,
            $"Placing underground houses and buried chests (chests {chestsPlaced}, " +
            $"houses {housePlaced}/{houseTarget}, additional desert {additionalDesertPlaced}/{additionalDesertTarget})");
    }

    internal static bool IsOrdinaryHouseOceanOffer1458(
        int x, int y, int width, double worldSurface, double rockLayer) =>
        y <= (worldSurface + rockLayer) / 2d + 40d &&
        (x < BeachDistance || x > width - BeachDistance);

    internal static (int Top, int Bottom) GetAdditionalDesertHouseVerticalRange1458(
        int desertTop, int desertBottom, double worldSurface) =>
        (Math.Max(desertTop, (int)worldSurface + 26), desertBottom);

    private void ApplySurfaceChests(
        IWorldGenerationContext context,
        Workspace workspace,
        RuntimeGrid grid,
        IWorldGenerationVanillaRandom random)
    {
        var placer = CreatePlacer(workspace, random);
        new SurfaceChestsPass1458(placer, workspace.TileStore, random, state.WorldSurface,
            state.WorldSurfaceLow, (state.WorldSurface + state.RockLayer) / 2d + 40d, BeachDistance,
            context.CancellationToken).Apply();

        context.ReportProgress(1d, $"Placing surface Wooden Chests ({placer.Chests.Count})");
    }

    private void ApplyJungleChests(
        IWorldGenerationContext context,
        Workspace workspace,
        RuntimeGrid grid,
        IWorldGenerationVanillaRandom random)
    {
        // TerrariaServer 1.4.5.8 GenPassNameID.ChestsInJungleShrines consumes
        // only the ordered GenVars.JChestX/Y entries published by JungleShrines.
        // It never searches a random area around JungleOriginX.
        ReadOnlySpan<WorldGenerationPoint> anchors = workspace.VanillaJungleShrineChestAnchors;
        var placer = CreatePlacer(workspace, random);
        var framer = new GenerationTileFraming1458(workspace.TileStore, random);
        foreach (WorldGenerationPoint anchor in anchors)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            int primary = NextJungleChestPrimary(random);
            if (placer.TryAdd(anchor.X + random.Next(2), anchor.Y, out _, out _,
                    primary, notNearOtherChests: false, chestStyle: IvyChestStyle,
                    trySlope: false, chestTileType: 0))
                continue;

            // Failed first offer: the source kills the 3x3 obstruction, flattens
            // the 3x4 repair region, then retries at the unshifted anchor.
            for (int x = anchor.X - 1; x <= anchor.X + 1; x++)
            for (int y = anchor.Y; y <= anchor.Y + 2; y++)
                framer.KillTile(x, y);
            for (int x = anchor.X - 1; x <= anchor.X + 1; x++)
            for (int y = anchor.Y; y <= anchor.Y + 3 && y < grid.Height; y++)
                if ((uint)x < (uint)grid.Width && (uint)y < (uint)grid.Height)
                    grid.At(x, y).Shape = 0;
            _ = placer.TryAdd(anchor.X, anchor.Y, out _, out _,
                primary, notNearOtherChests: false, chestStyle: IvyChestStyle,
                trySlope: false, chestTileType: 0);
        }
        context.ReportProgress(1d, $"Placed Ivy Chests at jungle shrine anchors ({placer.Chests.Count}/{anchors.Length})");
    }

    private void ApplyWaterChests(
        IWorldGenerationContext context,
        Workspace workspace,
        RuntimeGrid grid,
        IWorldGenerationVanillaRandom random)
    {
        // The two ocean-cave treasure anchors come from the Ocean Caves pass, in the slot order it recorded
        // them; the source's spiral search starts from each of them before the scattered half runs.
        ReadOnlySpan<WorldGenerationPoint> anchors = workspace.VanillaOceanCaveTreasure;
        var treasure = new (int X, int Y)[anchors.Length];
        for (int index = 0; index < anchors.Length; index++)
            treasure[index] = (anchors[index].X, anchors[index].Y);

        var placer = CreatePlacer(workspace, random);
        new UnderwaterChestsPass1458(placer, workspace.TileStore, random, state.WorldSurface, BeachDistance,
            treasure, context.CancellationToken).Apply();

        context.ReportProgress(1d, $"Placing Water Chests ({placer.Chests.Count})");
    }

    /// <summary>
    /// One <c>AddBuriedChest</c> placer bound to this run's shared chest state and to the workspace's chest
    /// side table, so a placement reserves a slot the way the source's <c>Chest.CreateChest</c> does.
    /// </summary>
    private BuriedChest1458 CreatePlacer(Workspace workspace, IWorldGenerationVanillaRandom random) =>
        new(workspace.TileStore, random, state.ChestContext,
            workspace.CanRegisterGeneratedChest,
            chest =>
            {
                if (!workspace.TryAddChest(chest.Left, chest.Top, string.Empty, chest.Items))
                    throw new InvalidOperationException(
                        $"Buried chest at {chest.Left},{chest.Top} passed placement but failed side-table registration.");
            });

    private static bool HasJungleMaterialNearby(
        RuntimeGrid grid,
        int centerX,
        int centerY,
        int radiusX,
        int radiusY)
    {
        int matches = 0;
        int samples = 0;
        int left = Math.Max(1, centerX - radiusX);
        int right = Math.Min(grid.Width - 2, centerX + radiusX);
        int top = Math.Max(1, centerY - radiusY);
        int bottom = Math.Min(grid.Height - 2, centerY + radiusY);
        for (int x = left; x <= right; x += 4)
        for (int y = top; y <= bottom; y += 4)
        {
            WorldTile tile = grid.At(x, y);
            if (!tile.IsActive)
                continue;
            samples++;
            if (tile.Type is 59 or 60 or 225 or 226)
                matches++;
        }
        return samples > 0 && matches * 100 / samples >= 20;
    }

    private static bool CanPlaceChest(
        RuntimeGrid grid,
        int left,
        int top,
        bool allowLiquid,
        int frameImportantRadius)
    {
        if (left < 1 || top < 1 || left + 1 >= grid.Width - 1 || top + 2 >= grid.Height - 1)
            return false;

        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            WorldTile tile = grid.At(left + dx, top + dy);
            if (tile.IsActive || (!allowLiquid && tile.LiquidAmount > 0))
                return false;
        }

        if (!grid.At(left, top + 2).IsActive || !grid.At(left + 1, top + 2).IsActive)
            return false;
        if (grid.HasFrameImportantNearby(left, top, frameImportantRadius, Math.Max(10, frameImportantRadius / 2)))
            return false;
        return true;
    }

    private static bool PlaceGeneratedChest(
        Workspace workspace,
        RuntimeGrid grid,
        int left,
        int top,
        int style,
        ReadOnlySpan<WorldGenerationChestItem> loot)
    {
        WorldTile a = grid.At(left, top);
        WorldTile b = grid.At(left + 1, top);
        WorldTile c = grid.At(left, top + 1);
        WorldTile d = grid.At(left + 1, top + 1);

        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            ref WorldTile tile = ref grid.At(left + dx, top + dy);
            tile.Type = Containers;
            tile.Flags |= WorldTileFlags.Active;
            tile.FrameX = checked((short)(style * 36 + dx * 18));
            tile.FrameY = checked((short)(dy * 18));
            tile.Shape = 0;
            tile.LiquidAmount = 0;
            tile.LiquidKind = WorldLiquidKind.Water;
        }

        if (workspace.TryAddChest(left, top, string.Empty, loot))
            return true;

        grid.At(left, top) = a;
        grid.At(left + 1, top) = b;
        grid.At(left, top + 1) = c;
        grid.At(left + 1, top + 1) = d;
        return false;
    }

    private int NextJungleChestPrimary(IWorldGenerationVanillaRandom random)
    {
        int primary = (state.JungleItemCount % 4) switch
        {
            0 => 211,
            1 => 212,
            2 => 213,
            _ => 964
        };
        if (random.Next(15) == 0)
            primary = 2292;
        else if (random.Next(20) == 0)
            primary = 3017;
        state.JungleItemCount++;
        return primary;
    }

    private VanillaWorldGenerationBootstrapState1458 RequireBootstrap() =>
        state.Bootstrap ?? throw new InvalidOperationException("Chest-placement pass executed before bootstrap initialization.");

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

        public bool HasFrameImportantNearby(int centerX, int centerY, int radiusX, int radiusY)
        {
            int left = Math.Max(0, centerX - radiusX);
            int right = Math.Min(Width - 1, centerX + radiusX);
            int top = Math.Max(0, centerY - radiusY);
            int bottom = Math.Min(Height - 1, centerY + radiusY);
            for (int x = left; x <= right; x++)
            for (int y = top; y <= bottom; y++)
            {
                WorldTile tile = At(x, y);
                if (tile.IsActive && VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                    return true;
            }
            return false;
        }
    }
}
