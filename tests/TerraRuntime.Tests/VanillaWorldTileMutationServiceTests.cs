using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldTileMutationServiceTests
{
    [Fact]
    public void Place_and_kill_simple_tile_preserve_independent_wall_wire_and_liquid_state()
    {
        var tiles = new WorldTileStore(new WorldDimensions(400, 300));
        var original = new WorldTile
        {
            Wall = checked((ushort)VanillaWallIds.Stone.Value),
            Flags = WorldTileFlags.WireRed,
            LiquidAmount = 77,
            LiquidKind = WorldLiquidKind.Water,
            WallColor = 3
        };
        tiles.Set(210, 160, in original);
        ClearDirty(tiles);
        var service = new VanillaWorldTileMutationService(tiles);

        var place = new WorldTileMutationRequest(
            WorldTileMutationKind.PlaceTile,
            210,
            160,
            TileType: VanillaTileIds.Stone);
        WorldTileMutationResult placed = service.Apply(in place);

        Assert.True(placed.Applied);
        Assert.True(placed.After.IsActive);
        Assert.Equal(VanillaTileIds.Stone, placed.After.TileType);
        Assert.Equal(VanillaWallIds.Stone, placed.After.WallType);
        Assert.True(placed.After.HasAnyWire);
        Assert.Equal((byte)77, placed.After.LiquidAmount);
        Assert.Equal((byte)3, placed.After.WallColor);

        var kill = new WorldTileMutationRequest(WorldTileMutationKind.KillTile, 210, 160);
        WorldTileMutationResult killed = service.Apply(in kill);

        Assert.True(killed.Applied);
        Assert.False(killed.After.IsActive);
        Assert.Equal(VanillaWallIds.Stone, killed.After.WallType);
        Assert.True(killed.After.HasAnyWire);
        Assert.Equal((byte)77, killed.After.LiquidAmount);
        Assert.Equal((byte)3, killed.After.WallColor);
    }

    [Fact]
    public void Place_and_kill_wall_preserve_tile_and_dirty_frame_neighborhood_sections()
    {
        var tiles = new WorldTileStore(new WorldDimensions(400, 300));
        var stone = new WorldTile
        {
            Type = checked((ushort)VanillaTileIds.Stone.Value),
            Flags = WorldTileFlags.Active
        };
        tiles.Set(199, 149, in stone);
        ClearDirty(tiles);
        var service = new VanillaWorldTileMutationService(tiles);

        var placeWall = new WorldTileMutationRequest(
            WorldTileMutationKind.PlaceWall,
            199,
            149,
            WallType: VanillaWallIds.Glass);
        WorldTileMutationResult placed = service.Apply(in placeWall);

        Assert.True(placed.Applied);
        Assert.Equal(VanillaTileIds.Stone, placed.After.TileType);
        Assert.Equal(VanillaWallIds.Glass, placed.After.WallType);
        Assert.Equal(4, tiles.DirtySections.DirtyCount);
        Assert.Equal(4, tiles.PersistenceDirtySections.DirtyCount);
        Assert.Equal(198, placed.FrameMinX);
        Assert.Equal(148, placed.FrameMinY);
        Assert.Equal(200, placed.FrameMaxX);
        Assert.Equal(150, placed.FrameMaxY);

        ClearDirty(tiles);
        var killWall = new WorldTileMutationRequest(WorldTileMutationKind.KillWall, 199, 149);
        WorldTileMutationResult killed = service.Apply(in killWall);

        Assert.True(killed.Applied);
        Assert.Equal(VanillaTileIds.Stone, killed.After.TileType);
        Assert.Equal(VanillaWallIds.None, killed.After.WallType);
        Assert.Equal(4, tiles.DirtySections.DirtyCount);
    }

    [Fact]
    public void Shape_mutation_accepts_solid_simple_tile_and_rejects_frame_important_object()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var service = new VanillaWorldTileMutationService(tiles);
        var stone = new WorldTile
        {
            Type = checked((ushort)VanillaTileIds.Stone.Value),
            Flags = WorldTileFlags.Active
        };
        tiles.Set(20, 20, in stone);

        var slope = new WorldTileMutationRequest(
            WorldTileMutationKind.SetShape,
            20,
            20,
            Shape: 4);
        WorldTileMutationResult slopeResult = service.Apply(in slope);

        Assert.True(slopeResult.Applied);
        Assert.Equal((byte)4, tiles.Get(20, 20).Shape);

        var chest = new WorldTile
        {
            Type = checked((ushort)VanillaTileIds.Containers.Value),
            Flags = WorldTileFlags.Active
        };
        tiles.Set(30, 30, in chest);
        var shapeChest = new WorldTileMutationRequest(
            WorldTileMutationKind.SetShape,
            30,
            30,
            Shape: 1);
        WorldTileMutationResult chestResult = service.Apply(in shapeChest);

        Assert.Equal(WorldTileMutationStatus.UnsupportedState, chestResult.Status);
        Assert.Equal((byte)0, tiles.Get(30, 30).Shape);
    }

    [Fact]
    public void Simple_tile_framing_canonicalizes_neighbor_without_touching_frame_important_neighbor()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var ordinary = new WorldTile
        {
            Type = checked((ushort)VanillaTileIds.Stone.Value),
            FrameX = 18,
            FrameY = 36,
            Flags = WorldTileFlags.Active
        };
        var chest = new WorldTile
        {
            Type = checked((ushort)VanillaTileIds.Containers.Value),
            FrameX = 36,
            FrameY = 18,
            Flags = WorldTileFlags.Active
        };
        tiles.Set(19, 20, in ordinary);
        tiles.Set(21, 20, in chest);
        ClearDirty(tiles);
        var service = new VanillaWorldTileMutationService(tiles);

        var place = new WorldTileMutationRequest(
            WorldTileMutationKind.PlaceTile,
            20,
            20,
            TileType: VanillaTileIds.Dirt);
        WorldTileMutationResult result = service.Apply(in place);

        Assert.True(result.Applied);
        Assert.Equal(2, result.ChangedTiles);
        Assert.Equal((short)0, tiles.Get(19, 20).FrameX);
        Assert.Equal((short)0, tiles.Get(19, 20).FrameY);
        Assert.Equal((short)36, tiles.Get(21, 20).FrameX);
        Assert.Equal((short)18, tiles.Get(21, 20).FrameY);
    }

    [Fact]
    public void Invalid_content_out_of_bounds_and_frame_important_placement_are_side_effect_free()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var service = new VanillaWorldTileMutationService(tiles);

        var invalid = new WorldTileMutationRequest(
            WorldTileMutationKind.PlaceTile,
            20,
            20,
            TileType: new TileTypeId(VanillaTileIds.Count));
        var frameImportant = new WorldTileMutationRequest(
            WorldTileMutationKind.PlaceTile,
            20,
            20,
            TileType: VanillaTileIds.Containers);
        var outOfBounds = new WorldTileMutationRequest(
            WorldTileMutationKind.PlaceWall,
            -1,
            20,
            WallType: VanillaWallIds.Stone);

        Assert.Equal(WorldTileMutationStatus.InvalidContent, service.Apply(in invalid).Status);
        Assert.Equal(WorldTileMutationStatus.FrameImportantUnsupported, service.Apply(in frameImportant).Status);
        Assert.Equal(WorldTileMutationStatus.OutOfBounds, service.Apply(in outOfBounds).Status);
        Assert.Equal(default, tiles.Get(20, 20));
        Assert.Equal(0, tiles.DirtySections.DirtyCount);
        Assert.Equal(0, tiles.PersistenceDirtySections.DirtyCount);
    }

    [Fact]
    public void Dirt_compatibility_facade_uses_shared_mutation_semantics_without_weakening_isolation_rules()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        Assert.True(VanillaDirtRules1458.TryPlaceOnEmpty(tiles, 20, 20));
        Assert.True(VanillaDirtRules1458.CanKillIsolated(tiles, 20, 20));
        Assert.True(VanillaDirtRules1458.TryKillIsolatedWithoutDrop(tiles, 20, 20));
        Assert.Equal(default, tiles.Get(20, 20));

        var wallOnly = new WorldTile { Wall = checked((ushort)VanillaWallIds.Stone.Value) };
        tiles.Set(30, 30, in wallOnly);
        Assert.False(VanillaDirtRules1458.TryPlaceOnEmpty(tiles, 30, 30));
    }

    private static void ClearDirty(WorldTileStore tiles)
    {
        tiles.DirtySections.Clear();
        tiles.PersistenceDirtySections.Clear();
    }
}
