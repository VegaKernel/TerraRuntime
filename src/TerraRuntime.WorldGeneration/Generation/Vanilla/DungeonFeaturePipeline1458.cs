using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-shaped ordinary-dungeon decoration stage pinned to TerrariaServer 1.4.5.8 DungeonCrawler.MakeDungeon.
/// Layout owns rooms/halls/entrance; this stage owns the post-layout feature order and keeps consuming the same
/// shared UnifiedRandom stream. It deliberately does not reuse Optimized/DungeonV2 heuristics.
/// </summary>
internal static class DungeonFeaturePipeline1458
{
    internal readonly record struct Result(
        int Doors,
        int Platforms,
        int Spikes,
        int BiomeChests,
        int BasicChests,
        int Bookshelves,
        int Lights,
        int Traps,
        int Furniture,
        int Paintings,
        int Banners);

    public static Result Apply(
        Workspace workspace,
        DungeonGraph1458 graph,
        IWorldGenerationVanillaRandom random,
        double worldSurface,
        double rockLayer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(random);

        DungeonSetupProfile1458 setup = workspace.VanillaDungeonSetupProfile ??
            throw new InvalidOperationException("Dungeon feature generation requires the Dunes-owned dungeon setup profile.");
        VanillaWorldGenerationBootstrapState1458 bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Dungeon feature generation requires Reset bootstrap state.");

        var grid = new Grid(workspace.TileStore);
        DungeonBounds1458 samplingBounds = graph.FeatureBounds ?? throw new InvalidOperationException("Dungeon graph lacks crawler sampling bounds.");
        DungeonBounds1458 bounds = ClampBounds(samplingBounds, grid.Width, grid.Height, padding: 25);

        int[] wallVariants = ResolveWallVariants(setup.Palette.BrickWallType);

        // TerrariaServer 1.4.5.8 DungeonCrawler.MakeDungeon order:
        // CalculatePlatformsAndDoors -> early pits -> spikes -> doors -> wall variants -> platforms -> biome chests ->
        // bookshelves -> basic chests -> lights -> traps -> furniture -> paintings -> banners.
        var candidates = DungeonFeatureCandidates1458.Collect(workspace.TileStore, graph);
        // Crawler inflates its feature area only AFTER basic chests; spikes/walls must not use that padding.
        DungeonBounds1458 earlyBounds = ClampBounds(samplingBounds, grid.Width, grid.Height, padding: 0);
        DungeonBounds1458? protectedEntrance = setup.PrecalculatesEntrance
            ? graph.Components.Single(static c => c.Kind == DungeonComponentKind1458.Entrance).Bounds : null;
        var pits = new DungeonPitTraps1458(workspace.TileStore, random, cancellationToken).Place(earlyBounds,
            graph.BrickTileType, setup.Palette.BrickWallType, setup.Palette.CrackedBrickTileType, worldSurface, graph.Anchor.Y);
        int spikes = new DungeonSpikes1458(workspace.TileStore, random, cancellationToken, pits)
            .Place(earlyBounds, setup.Palette.BrickWallType, setup.Palette.CrackedBrickTileType, worldSurface, protectedEntrance);
        int doors = new DungeonDoors1458(workspace.TileStore, random, cancellationToken)
            .Place(candidates.Doors, graph.BrickTileType, setup.Palette.BrickWallType, setup.Palette.Color);
        new DungeonWallVariants1458(workspace.TileStore, random, cancellationToken)
            .Apply(earlyBounds, setup.Palette.BrickWallType, wallVariants, worldSurface);
        int platforms = new DungeonPlatforms1458(workspace.TileStore, random, worldSurface, rockLayer, cancellationToken)
            .Place(candidates.Platforms, setup.Palette.Color);
        var chests = new DungeonChests1458(workspace, random, bootstrap, worldSurface, rockLayer, cancellationToken, pits);
        int biomeChests = chests.PlaceBiome(earlyBounds, graph.Components.Single(static c => c.Kind == DungeonComponentKind1458.Entrance).Bounds);
        int bookshelves = new DungeonBookshelves1458(workspace.TileStore, random, worldSurface, rockLayer, cancellationToken, pits)
            .Place(earlyBounds, wallVariants, graph.Decoration, protectedEntrance);
        int basicChests = chests.PlaceBasic(graph.Components);
        int lights = new DungeonLights1458(workspace.TileStore, random, cancellationToken, pits).Place(bounds, wallVariants,
            setup.Palette.Color, graph.Decoration, graph.Components.Single(static c => c.Kind == DungeonComponentKind1458.Entrance).Bounds);
        // This pass PRECEDES Shimmer. WorldGen.clearWorld sets genVars.shimmerPosition to zero;
        // Workspace's nullable position separately records whether the later Shimmer pass completed.
        int traps = new DungeonTraps1458(workspace.TileStore, random, new(0, 0), cancellationToken).Place(bounds, worldSurface);
        int furniture = new DungeonFurniture1458(workspace, random, cancellationToken, pits).Place(bounds, setup.Palette.BrickWallType, worldSurface);
        int paintings = new DungeonPaintings1458(workspace.TileStore, random, cancellationToken)
            .Place(bounds, setup.Palette.BrickWallType, worldSurface, protectedEntrance);
        int banners = new DungeonBanners1458(workspace.TileStore, random, cancellationToken, pits).Place(bounds, wallVariants);
        // LegacyDungeonLayout replaces outerProgressionBounds[0] with the SAME dungeonBounds object.
        // Its subsequent entrance updates and post-chest inflation therefore also govern late doors.
        new DungeonLateDoors1458(workspace.TileStore, random, cancellationToken).Apply(bounds);

        return new Result(
            doors,
            platforms,
            spikes,
            biomeChests,
            basicChests,
            bookshelves,
            lights,
            traps,
            furniture,
            paintings,
            banners);
    }



    private static ushort Tile(TileTypeId type) => checked((ushort)type.Value);
    private static ushort Wall(WallTypeId type) => checked((ushort)type.Value);

    private static int[] ResolveWallVariants(ushort baseWall) => baseWall switch
    {
        var wall when wall == Wall(VanillaWallIds.BlueDungeonUnsafe) =>
            [Wall(VanillaWallIds.BlueDungeonUnsafe), Wall(VanillaWallIds.BlueDungeonSlabUnsafe), Wall(VanillaWallIds.BlueDungeonTileUnsafe)],
        var wall when wall == Wall(VanillaWallIds.GreenDungeonUnsafe) =>
            [Wall(VanillaWallIds.GreenDungeonUnsafe), Wall(VanillaWallIds.GreenDungeonSlabUnsafe), Wall(VanillaWallIds.GreenDungeonTileUnsafe)],
        var wall when wall == Wall(VanillaWallIds.PinkDungeonUnsafe) =>
            [Wall(VanillaWallIds.PinkDungeonUnsafe), Wall(VanillaWallIds.PinkDungeonSlabUnsafe), Wall(VanillaWallIds.PinkDungeonTileUnsafe)],
        _ => throw new InvalidOperationException("Unsupported ordinary dungeon base wall."),
    };


    private static bool IsAnyDungeonWall(ushort wall, IReadOnlyList<int> variants) =>
        variants.Contains(wall);


    private static DungeonBounds1458 ClampBounds(DungeonBounds1458 value, int width, int height, int padding) =>
        new(
            Math.Clamp(value.Left - padding, 10, width - 10),
            Math.Clamp(value.Top - padding, 10, height - 10),
            Math.Clamp(value.Right + padding, 10, width - 10),
            Math.Clamp(value.Bottom + padding, 10, height - 10));


    private sealed class Grid(WorldTileStore store)
    {
        public int Width => store.Dimensions.WidthTiles;
        public int Height => store.Dimensions.HeightTiles;
        public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;
        public ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
    }
}
