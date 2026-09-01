using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcPhysicalSpawn1458Tests
{
    [Fact]
    public void Safe_home_tile_is_used_without_fallback_search()
    {
        WorldTileStore tiles = CreateSurfaceWorld();
        var resolver = new RuntimeTownNpcPhysicalSpawnResolver1458(tiles);

        RuntimeTownNpcPhysicalSpawn1458 spawn = resolver.Resolve(600, 60, []);

        Assert.Equal((600, 60), (spawn.TileX, spawn.TileY));
        Assert.Equal(0, spawn.DirectionX);
        Assert.True(spawn.SafeFromPlayers);
        Assert.False(spawn.UsedFallbackSearch);
    }

    [Fact]
    public void Unsafe_surface_home_searches_right_before_left_and_faces_home()
    {
        WorldTileStore tiles = CreateSurfaceWorld();
        PlaceSolid(tiles, 602, 58);
        PlaceSolid(tiles, 598, 58);
        var resolver = new RuntimeTownNpcPhysicalSpawnResolver1458(tiles);
        RuntimeTownPlayerBounds1458 blocker = BlockHomeFromLeftEdge(600, 60);

        RuntimeTownNpcPhysicalSpawn1458 spawn = resolver.Resolve(600, 60, [blocker]);

        Assert.Equal((602, 58), (spawn.TileX, spawn.TileY));
        Assert.Equal(-1, spawn.DirectionX);
        Assert.True(spawn.SafeFromPlayers);
        Assert.True(spawn.UsedFallbackSearch);
    }

    [Fact]
    public void First_solid_floor_with_blocked_clearance_ends_that_column_scan()
    {
        WorldTileStore tiles = CreateSurfaceWorld();
        PlaceSolid(tiles, 602, 58);
        PlaceSolid(tiles, 602, 57); // Collision.SolidTiles blocker above the first right-side floor.
        PlaceSolid(tiles, 602, 60); // Would be usable only if vanilla incorrectly continued down this column.
        PlaceSolid(tiles, 598, 58);
        var resolver = new RuntimeTownNpcPhysicalSpawnResolver1458(tiles);
        RuntimeTownPlayerBounds1458 blocker = BlockHomeFromRightEdge(600, 60);

        RuntimeTownNpcPhysicalSpawn1458 spawn = resolver.Resolve(600, 60, [blocker]);

        Assert.Equal((598, 58), (spawn.TileX, spawn.TileY));
        Assert.Equal(1, spawn.DirectionX);
        Assert.True(spawn.SafeFromPlayers);
    }

    [Fact]
    public void Unsafe_underground_home_does_not_run_surface_fallback_search()
    {
        WorldTileStore tiles = CreateSurfaceWorld();
        var resolver = new RuntimeTownNpcPhysicalSpawnResolver1458(tiles);
        RuntimeTownPlayerBounds1458 blocker = BlockHomeFromLeftEdge(600, 120);

        RuntimeTownNpcPhysicalSpawn1458 spawn = resolver.Resolve(600, 120, [blocker]);

        Assert.Equal((600, 120), (spawn.TileX, spawn.TileY));
        Assert.False(spawn.SafeFromPlayers);
        Assert.False(spawn.UsedFallbackSearch);
        Assert.Equal(0, spawn.DirectionX);
    }

    [Fact]
    public void Exhausted_surface_search_preserves_vanilla_final_scanned_fallback_coordinates()
    {
        WorldTileStore tiles = CreateSurfaceWorld();
        var resolver = new RuntimeTownNpcPhysicalSpawnResolver1458(tiles);
        RuntimeTownPlayerBounds1458 blocker = BlockHomeFromLeftEdge(600, 60);

        RuntimeTownNpcPhysicalSpawn1458 spawn = resolver.Resolve(600, 60, [blocker]);

        Assert.Equal((101, 99), (spawn.TileX, spawn.TileY));
        Assert.Equal(1, spawn.DirectionX);
        Assert.False(spawn.SafeFromPlayers);
        Assert.True(spawn.UsedFallbackSearch);
    }

    [Fact]
    public void Safety_rectangle_uses_xna_strict_edge_intersection()
    {
        WorldTileStore tiles = CreateSurfaceWorld();
        var resolver = new RuntimeTownNpcPhysicalSpawnResolver1458(tiles);
        int left = 600 * 16 + 8 - RuntimeTownNpcPhysicalSpawnResolver1458.ScreenWidth1458 / 2 -
            RuntimeTownNpcPhysicalSpawnResolver1458.SafeRangeX1458;
        int top = 60 * 16 + 8 - RuntimeTownNpcPhysicalSpawnResolver1458.ScreenHeight1458 / 2 -
            RuntimeTownNpcPhysicalSpawnResolver1458.SafeRangeY1458;

        var touchingEdge = new RuntimeTownPlayerBounds1458(left - 20, top + 100, 20, 42);
        var overlappingOnePixel = new RuntimeTownPlayerBounds1458(left - 19, top + 100, 20, 42);

        Assert.True(resolver.IsSafeSpawnTile(600, 60, [touchingEdge]));
        Assert.False(resolver.IsSafeSpawnTile(600, 60, [overlappingOnePixel]));
    }

    [Fact]
    public void Resident_materialization_uses_newnpc_bottom_anchor_and_spawn_facing()
    {
        WorldTileStore tiles = CreateSurfaceWorld();
        var town = new RuntimeTownNpcStateStore(new WorldNpcPersistence([], [], []), [], tiles.Dimensions);
        var npcs = new RuntimeNpcStore();
        var placement = new VanillaHousingPlacement(600, 60, VanillaHousingValidationResult.Valid);
        var physical = new RuntimeTownNpcPhysicalSpawn1458(602, 58, -1, true, true);

        Assert.True(town.TryAddResident(
            VanillaNpcIds.Merchant,
            in placement,
            in physical,
            npcs,
            out NpcSnapshot snapshot,
            out RuntimeTownNpcHomeCommit home));
        Assert.True(VanillaTownNpcFacts1458.TryGetDefinition(VanillaNpcIds.Merchant, out VanillaNpcDefinition definition));

        Assert.Equal(602 * 16f, snapshot.PositionX + definition.BaseWidth / 2f);
        Assert.Equal(58 * 16f, snapshot.PositionY + definition.BaseHeight);
        Assert.Equal(-1, snapshot.Simulation.DirectionX);
        Assert.Equal((600, 60), (home.HomeTileX, home.HomeTileY));
        WorldTownNpc persisted = Assert.Single(town.CaptureNpcPersistence().TownNpcs);
        Assert.Equal(snapshot.PositionX, persisted.X);
        Assert.Equal(snapshot.PositionY, persisted.Y);
    }

    private static WorldTileStore CreateSurfaceWorld()
    {
        var tiles = new WorldTileStore(new WorldDimensions(1200, 300));
        Assert.True(tiles.TryAttachWorldSurface(100d));
        return tiles;
    }

    private static RuntimeTownPlayerBounds1458 BlockHomeFromLeftEdge(int homeTileX, int homeTileY)
    {
        int left = homeTileX * 16 + 8 - RuntimeTownNpcPhysicalSpawnResolver1458.ScreenWidth1458 / 2 -
            RuntimeTownNpcPhysicalSpawnResolver1458.SafeRangeX1458;
        int top = homeTileY * 16 + 8 - RuntimeTownNpcPhysicalSpawnResolver1458.ScreenHeight1458 / 2 -
            RuntimeTownNpcPhysicalSpawnResolver1458.SafeRangeY1458;
        return new RuntimeTownPlayerBounds1458(left, top + 100, 20, 42);
    }

    private static RuntimeTownPlayerBounds1458 BlockHomeFromRightEdge(int homeTileX, int homeTileY)
    {
        int left = homeTileX * 16 + 8 - RuntimeTownNpcPhysicalSpawnResolver1458.ScreenWidth1458 / 2 -
            RuntimeTownNpcPhysicalSpawnResolver1458.SafeRangeX1458;
        int top = homeTileY * 16 + 8 - RuntimeTownNpcPhysicalSpawnResolver1458.ScreenHeight1458 / 2 -
            RuntimeTownNpcPhysicalSpawnResolver1458.SafeRangeY1458;
        int width = RuntimeTownNpcPhysicalSpawnResolver1458.ScreenWidth1458 +
            RuntimeTownNpcPhysicalSpawnResolver1458.SafeRangeX1458 * 2;
        return new RuntimeTownPlayerBounds1458(left + width - 20, top + 100, 20, 42);
    }

    private static void PlaceSolid(WorldTileStore tiles, int x, int y) =>
        tiles.Set(x, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
}
