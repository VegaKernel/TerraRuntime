using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration;

/// <summary>
/// Migration layer for the built-in Terraria 1.4.5.8 generator. It preserves the existing compatibility passes while
/// allowing source-backed vanilla passes to replace them one at a time under the same terraruntime:vanilla identity.
/// </summary>
public sealed class SourceBackedVanillaWorldGenerationProvider1458 : IWorldGenerationProvider
{
    internal static readonly WorldGenerationPassId ResetPassId = new("terraria:1.4.5.8/Reset");
    internal static readonly WorldGenerationPassId TerrainPassId = new("terraria:1.4.5.8/Terrain");
    internal static readonly WorldGenerationPassId MetadataPassId = new("terraria:1.4.5.8/Metadata");

    private readonly VanillaWorldGenerationProvider1458 compatibility = new();

    public WorldGeneratorId Id => VanillaWorldGenerationProvider1458.GeneratorId;

    public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var state = new VanillaWorldGenerationParityState1458();
        builder.Add(
            new WorldGenerationPassDescriptor(ResetPassId, WorldGenerationRngMode.VanillaSharedRng),
            new VanillaWorldGenerationBootstrapPass1458(state));
        compatibility.BuildPlan(in request, new OverlayPlanBuilder(builder, state));
    }

    private sealed class OverlayPlanBuilder : IWorldGenerationPlanBuilder
    {
        private readonly IWorldGenerationPlanBuilder inner;
        private readonly VanillaWorldGenerationParityState1458 state;

        public OverlayPlanBuilder(
            IWorldGenerationPlanBuilder inner,
            VanillaWorldGenerationParityState1458 state)
        {
            this.inner = inner;
            this.state = state;
        }

        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            ArgumentNullException.ThrowIfNull(pass);

            if (descriptor.Id == TerrainPassId)
            {
                inner.Add(RequireAfter(descriptor, ResetPassId), new VanillaTerrainPass1458(pass, state));
                return;
            }

            if (descriptor.Id == MetadataPassId)
            {
                inner.Add(descriptor, new VanillaMetadataParityPass1458(pass, state));
                return;
            }

            inner.Add(descriptor, pass);
        }

        private static WorldGenerationPassDescriptor RequireAfter(
            WorldGenerationPassDescriptor descriptor,
            WorldGenerationPassId dependency)
        {
            WorldGenerationPassId[] required = descriptor.RequiredAfter.ToArray();
            if (!required.Contains(dependency))
                required = [.. required, dependency];

            return new WorldGenerationPassDescriptor(
                descriptor.Id,
                descriptor.RngMode,
                required,
                descriptor.OptionalAfter.ToArray(),
                descriptor.OptionalBefore.ToArray());
        }
    }
}

internal sealed class VanillaWorldGenerationParityState1458
{
    public VanillaWorldGenerationBootstrapState1458? Bootstrap { get; set; }
    public WorldGenerationLayers? TerrainLayers { get; set; }
}

internal readonly record struct VanillaTerrainGenerationState1458(
    double WorldSurface,
    double RockLayer,
    double CurrentWorldSurface,
    double CurrentRockLayer,
    double WorldSurfaceLow,
    double WorldSurfaceHigh,
    double RockLayerLow,
    double RockLayerHigh);

/// <summary>
/// Clean-room port of the ordinary-world and pure Don't Dig Up TerrainPass branches from TerrariaServer 1.4.5.8.
/// The source-backed slice is used only for Terraria's three canonical dimensions. Its pre-Terrain WorldGen.Reset
/// RNG state and beach bounds are supplied by <see cref="VanillaWorldGenerationBootstrapPass1458"/>.
/// </summary>
internal sealed class VanillaTerrainPass1458 : IWorldGenerationPass
{
    internal const int FlatBeachPadding = 5;
    internal const int SurfaceHistoryLength = 500;

    private readonly IWorldGenerationPass fallback;
    private readonly VanillaWorldGenerationParityState1458 state;

    public VanillaTerrainPass1458(IWorldGenerationPass fallback, VanillaWorldGenerationParityState1458 state)
    {
        this.fallback = fallback;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        WorldGenerationRequest request = context.Request;
        VanillaWorldSeedProfile1458 seedProfile = VanillaWorldSeedResolver1458.Resolve(in request);
        if (!seedProfile.SupportsSourceBackedResetAndTerrain || !IsCanonicalWorldSize(context.Workspace.WidthTiles, context.Workspace.HeightTiles))
        {
            fallback.Execute(context);
            return;
        }

        VanillaWorldGenerationBootstrapState1458 bootstrap = state.Bootstrap ??
            throw new InvalidOperationException("Source-backed Terrain executed without the required WorldGen.Reset bootstrap state.");
        IWorldGenerationVanillaRandom random = context.VanillaRandom ??
            throw new InvalidOperationException("The source-backed Terraria terrain pass requires shared UnifiedRandom semantics.");
        IWorldGenerationMetadataWorkspace metadata = context.Metadata ??
            throw new InvalidOperationException("The source-backed Terraria terrain pass requires world metadata storage.");

        int width = context.Workspace.WidthTiles;
        int height = context.Workspace.HeightTiles;
        int leftBeachEnd = bootstrap.LeftBeachEnd;
        int rightBeachStart = bootstrap.RightBeachStart;

        TerrainFeatureType feature = TerrainFeatureType.Plateau;
        int featureRemaining = leftBeachEnd + FlatBeachPadding;
        // The profile admission gate above admits only the pure Remix profile, never Zenith.
        bool isRemix = seedProfile.Special == VanillaSpecialWorldSeed1458.Remix;

        double surface = height * 0.3d;
        surface *= random.Next(90, 110) * 0.005d;
        double rock = surface + height * 0.2d;
        rock *= random.Next(90, 110) * 0.01d;
        if (isRemix)
        {
            rock = height * 0.5d;
            if (width > 2500)
                rock = height * 0.6d;
            rock *= random.Next(95, 106) * 0.01d;
        }

        double surfaceLow = surface;
        double surfaceHigh = surface;
        double rockLow = rock;
        double rockHigh = rock;
        double beachSurfaceLimit = height * 0.23d;
        var history = new SurfaceHistory(SurfaceHistoryLength);
        int progressStride = Math.Max(1, width / 1000);

        for (int x = 0; x < width; x++)
        {
            if ((x & 31) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            surfaceLow = Math.Min(surface, surfaceLow);
            surfaceHigh = Math.Max(surface, surfaceHigh);
            rockLow = Math.Min(rock, rockLow);
            rockHigh = Math.Max(rock, rockHigh);

            if (featureRemaining <= 0)
            {
                feature = (TerrainFeatureType)random.Next(0, 5);
                featureRemaining = random.Next(5, 40);
                if (feature == TerrainFeatureType.Plateau)
                    featureRemaining *= (int)(random.Next(5, 30) * 0.2d);
            }

            featureRemaining--;
            if (x > width * 0.45d && x < width * 0.55d &&
                (feature == TerrainFeatureType.Mountain || feature == TerrainFeatureType.Valley))
            {
                feature = (TerrainFeatureType)random.Next(3);
            }

            if (x > width * 0.48d && x < width * 0.52d)
                feature = TerrainFeatureType.Plateau;

            surface += GenerateWorldSurfaceOffset(random, feature, isRemix);

            double minimumSurfacePercent = 0.17d;
            const double maximumSurfacePercent = 0.26d;
            if (width == 4200)
                minimumSurfacePercent += 0.02d;

            if (x < leftBeachEnd + FlatBeachPadding || x > rightBeachStart - FlatBeachPadding)
            {
                surface = Math.Clamp(surface, height * minimumSurfacePercent, beachSurfaceLimit);
            }
            else if (surface < height * minimumSurfacePercent)
            {
                surface = height * minimumSurfacePercent;
                featureRemaining = 0;
            }
            else if (surface > height * maximumSurfacePercent)
            {
                surface = height * maximumSurfacePercent;
                featureRemaining = 0;
            }

            while (random.Next(0, 3) == 0)
                rock += random.Next(-2, 3);

            if (isRemix)
            {
                if (width > 2500)
                {
                    if (rock > height * 0.7d)
                        rock--;
                }
                else if (rock > height * 0.6d)
                {
                    rock--;
                }
            }
            else
            {
                if (rock < surface + height * 0.06d)
                    rock++;
                if (rock > surface + height * 0.35d)
                    rock--;
            }

            history.Record(surface);
            FillColumn(context.Workspace, x, surface, rock);

            if (x == rightBeachStart - FlatBeachPadding)
            {
                if (surface > beachSurfaceLimit)
                    RetargetSurfaceHistory(context.Workspace, history, x, beachSurfaceLimit);
                feature = TerrainFeatureType.Plateau;
                featureRemaining = width - x;
            }

            if (x % progressStride == 0 || x == width - 1)
                context.ReportProgress((x + 1d) / width, "Generating source-backed Terraria terrain");
        }

        double worldSurface = (int)(surfaceHigh + 25d);
        double rockLayer = rockHigh;
        double sixTileBand = (int)((rockLayer - worldSurface) / 6d) * 6d;
        rockLayer = (int)(worldSurface + sixTileBand);

        const int minimumLayerGap = 20;
        if (rockLow < surfaceHigh + minimumLayerGap)
        {
            double center = (rockLow + surfaceHigh) / 2d;
            double gap = Math.Abs(rockLow - surfaceHigh);
            if (gap < minimumLayerGap)
                gap = minimumLayerGap;
            rockLow = center + gap / 2d;
            surfaceHigh = center - gap / 2d;
        }

        if (!metadata.TrySetLayers(worldSurface, rockLayer))
        {
            throw new InvalidOperationException(
                $"Terraria terrain produced invalid world layers: surface={worldSurface}, rock={rockLayer}.");
        }

        state.TerrainLayers = new WorldGenerationLayers(worldSurface, rockLayer);
        if (context.Workspace is RuntimeWorldGenerationWorkspace runtimeWorkspace)
        {
            runtimeWorkspace.SetVanillaTerrainState(new VanillaTerrainGenerationState1458(
                worldSurface,
                rockLayer,
                surface,
                rock,
                surfaceLow,
                surfaceHigh,
                rockLow,
                rockHigh));
        }
    }

    internal static bool IsCanonicalWorldSize(int width, int height) =>
        (width == 4200 && height == 1200) ||
        (width == 6400 && height == 1800) ||
        (width == 8400 && height == 2400);

    private static double GenerateWorldSurfaceOffset(
        IWorldGenerationVanillaRandom random,
        TerrainFeatureType feature,
        bool isDrunkOrGoodOrRemix = false)
    {
        double offset = 0d;
        if (isDrunkOrGoodOrRemix && random.Next(2) == 0)
        {
            // Vanilla alternative distribution for Drunk/ForTheWorthy/Remix (see TerrainPass.GenerateWorldSurfaceOffset).
            switch (feature)
            {
                case TerrainFeatureType.Plateau:
                    while (random.Next(0, 6) == 0)
                        offset += random.Next(-1, 2);
                    break;
                case TerrainFeatureType.Hill:
                    while (random.Next(0, 3) == 0)
                        offset--;
                    while (random.Next(0, 10) == 0)
                        offset++;
                    break;
                case TerrainFeatureType.Dale:
                    while (random.Next(0, 3) == 0)
                        offset++;
                    while (random.Next(0, 10) == 0)
                        offset--;
                    break;
                case TerrainFeatureType.Mountain:
                    while (random.Next(0, 3) != 0)
                        offset--;
                    while (random.Next(0, 6) == 0)
                        offset++;
                    break;
                case TerrainFeatureType.Valley:
                    while (random.Next(0, 3) != 0)
                        offset++;
                    while (random.Next(0, 5) == 0)
                        offset--;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(feature));
            }
            return offset;
        }

        switch (feature)
        {
            case TerrainFeatureType.Plateau:
                while (random.Next(0, 7) == 0)
                    offset += random.Next(-1, 2);
                break;
            case TerrainFeatureType.Hill:
                while (random.Next(0, 4) == 0)
                    offset--;
                while (random.Next(0, 10) == 0)
                    offset++;
                break;
            case TerrainFeatureType.Dale:
                while (random.Next(0, 4) == 0)
                    offset++;
                while (random.Next(0, 10) == 0)
                    offset--;
                break;
            case TerrainFeatureType.Mountain:
                while (random.Next(0, 2) == 0)
                    offset--;
                while (random.Next(0, 6) == 0)
                    offset++;
                break;
            case TerrainFeatureType.Valley:
                while (random.Next(0, 2) == 0)
                    offset++;
                while (random.Next(0, 5) == 0)
                    offset--;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feature));
        }

        return offset;
    }

    private static void FillColumn(IWorldGenerationWorkspace workspace, int x, double surface, double rock)
    {
        int surfaceTile = (int)surface;
        for (int y = 0; y < surfaceTile; y++)
            SetAir(workspace, x, y);

        for (int y = surfaceTile; y < workspace.HeightTiles; y++)
            SetSolid(workspace, x, y, y < rock ? VanillaTileIds.Dirt : VanillaTileIds.Stone);
    }

    private static void RetargetSurfaceHistory(
        IWorldGenerationWorkspace workspace,
        SurfaceHistory history,
        int targetX,
        double targetHeight)
    {
        for (int i = 0; i < history.Length / 2; i++)
        {
            if (history[history.Length - 1] <= targetHeight)
                break;

            for (int j = 0; j < history.Length - i * 2; j++)
            {
                double height = history[history.Length - j - 1] - 1d;
                history[history.Length - j - 1] = height;
                if (height <= targetHeight)
                    break;
            }
        }

        for (int i = 0; i < history.Length; i++)
            RetargetColumn(workspace, targetX - i, history[history.Length - i - 1]);
    }

    private static void RetargetColumn(IWorldGenerationWorkspace workspace, int x, double surface)
    {
        int surfaceTile = (int)surface;
        for (int y = 0; y < surfaceTile; y++)
            SetAir(workspace, x, y);

        for (int y = surfaceTile; y < workspace.HeightTiles; y++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile existing))
                throw new InvalidOperationException($"Could not read terrain tile ({x}, {y}).");

            bool activeStone =
                (existing.Flags & WorldGenerationTileFlags.Active) != 0 &&
                existing.Type == VanillaTileIds.Stone.Value;
            if (!activeStone)
                SetSolid(workspace, x, y, VanillaTileIds.Dirt);
        }
    }

    private static void SetAir(IWorldGenerationWorkspace workspace, int x, int y)
    {
        var tile = new WorldGenerationTile(
            Type: (ushort)VanillaTileIds.Dirt.Value,
            Wall: 0,
            FrameX: -1,
            FrameY: -1,
            Flags: WorldGenerationTileFlags.None,
            LiquidAmount: 0,
            TileColor: 0,
            WallColor: 0,
            Shape: 0,
            LiquidKind: WorldGenerationLiquidKind.Water);
        if (!workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Could not clear terrain tile ({x}, {y}).");
    }

    private static void SetSolid(IWorldGenerationWorkspace workspace, int x, int y, TileTypeId type)
    {
        var tile = new WorldGenerationTile(
            Type: checked((ushort)type.Value),
            Wall: 0,
            FrameX: -1,
            FrameY: -1,
            Flags: WorldGenerationTileFlags.Active,
            LiquidAmount: 0,
            TileColor: 0,
            WallColor: 0,
            Shape: 0,
            LiquidKind: WorldGenerationLiquidKind.Water);
        if (!workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Could not write terrain tile ({x}, {y}).");
    }

    private enum TerrainFeatureType : byte
    {
        Plateau = 0,
        Hill = 1,
        Dale = 2,
        Mountain = 3,
        Valley = 4
    }

    private sealed class SurfaceHistory
    {
        private readonly double[] heights;
        private int index;

        public SurfaceHistory(int size)
        {
            heights = new double[size];
        }

        public int Length => heights.Length;

        public double this[int offset]
        {
            get => heights[(offset + index) % heights.Length];
            set => heights[(offset + index) % heights.Length] = value;
        }

        public void Record(double height)
        {
            heights[index] = height;
            index = (index + 1) % heights.Length;
        }
    }
}

internal sealed class VanillaMetadataParityPass1458 : IWorldGenerationPass
{
    private readonly IWorldGenerationPass fallback;
    private readonly VanillaWorldGenerationParityState1458 state;

    public VanillaMetadataParityPass1458(IWorldGenerationPass fallback, VanillaWorldGenerationParityState1458 state)
    {
        this.fallback = fallback;
        this.state = state;
    }

    public void Execute(IWorldGenerationContext context)
    {
        IWorldGenerationMetadataWorkspace metadata = context.Metadata ??
            throw new InvalidOperationException("Vanilla metadata pass requires world metadata storage.");

        WorldGenerationPoint? sourceBackedDungeon = null;
        if (state.Bootstrap is not null && metadata.TryGetDungeon(out WorldGenerationPoint dungeon))
            sourceBackedDungeon = dungeon;

        fallback.Execute(context);

        if (state.Bootstrap is VanillaWorldGenerationBootstrapState1458 bootstrap &&
            context.Workspace is RuntimeWorldGenerationWorkspace runtimeWorkspace)
        {
            runtimeWorkspace.SetVanillaBootstrapState(bootstrap);
        }

        if (sourceBackedDungeon is WorldGenerationPoint preservedDungeon &&
            !metadata.TrySetDungeon(preservedDungeon.X, preservedDungeon.Y))
        {
            throw new InvalidOperationException("Could not preserve source-backed Terraria dungeon anchor.");
        }

        if (state.TerrainLayers is not WorldGenerationLayers layers)
            return;

        if (!metadata.TrySetLayers(layers.WorldSurface, layers.RockLayer))
            throw new InvalidOperationException("Could not preserve source-backed Terraria terrain layers.");
    }
}
