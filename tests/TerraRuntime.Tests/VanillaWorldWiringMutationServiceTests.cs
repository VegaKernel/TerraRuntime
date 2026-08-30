using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldWiringMutationServiceTests
{
    [Fact]
    public void Wire_mutations_preserve_independent_tile_wall_and_liquid_state()
    {
        var tiles = new WorldTileStore(new WorldDimensions(20, 20));
        var original = new WorldTile
        {
            Type = checked((ushort)VanillaTileIds.Stone.Value),
            Wall = checked((ushort)VanillaWallIds.Glass.Value),
            Flags = WorldTileFlags.Active | WorldTileFlags.WireBlue,
            LiquidAmount = 41,
            LiquidKind = WorldLiquidKind.Honey
        };
        tiles.Set(5, 6, in original);
        tiles.DirtySections.Clear();
        tiles.PersistenceDirtySections.Clear();
        var service = new VanillaWorldWiringMutationService(tiles);

        var place = new WorldWiringMutationRequest(
            WorldWiringMutationKind.PlaceWire,
            5,
            6,
            WorldWireChannel.Red);
        WorldWiringMutationResult placed = service.Apply(in place);

        Assert.True(placed.Applied);
        Assert.Equal(VanillaTileIds.Stone, placed.After.TileType);
        Assert.Equal(VanillaWallIds.Glass, placed.After.WallType);
        Assert.Equal((byte)41, placed.After.LiquidAmount);
        Assert.True((placed.After.Flags & WorldTileFlags.WireBlue) != 0);
        Assert.True((placed.After.Flags & WorldTileFlags.WireRed) != 0);
        Assert.Equal(1, tiles.DirtySections.DirtyCount);
        Assert.Equal(1, tiles.PersistenceDirtySections.DirtyCount);

        var kill = place with { Kind = WorldWiringMutationKind.KillWire };
        Assert.True(service.Apply(in kill).Applied);
        Assert.False((tiles.Get(5, 6).Flags & WorldTileFlags.WireRed) != 0);
        Assert.True((tiles.Get(5, 6).Flags & WorldTileFlags.WireBlue) != 0);
    }

    [Fact]
    public void Actuation_requires_an_active_tile_and_installed_actuator()
    {
        var tiles = new WorldTileStore(new WorldDimensions(10, 10));
        var service = new VanillaWorldWiringMutationService(tiles);
        var actuate = new WorldWiringMutationRequest(WorldWiringMutationKind.Actuate, 2, 2);

        Assert.Equal(WorldWiringMutationStatus.MissingTile, service.Apply(in actuate).Status);

        var stone = new WorldTile
        {
            Type = checked((ushort)VanillaTileIds.Stone.Value),
            Flags = WorldTileFlags.Active
        };
        tiles.Set(2, 2, in stone);
        Assert.Equal(WorldWiringMutationStatus.MissingActuator, service.Apply(in actuate).Status);

        var placeActuator = actuate with { Kind = WorldWiringMutationKind.PlaceActuator };
        Assert.True(service.Apply(in placeActuator).Applied);
        Assert.True(service.Apply(in actuate).Applied);
        Assert.True(tiles.Get(2, 2).IsActuated);

        var killActuator = actuate with { Kind = WorldWiringMutationKind.KillActuator };
        Assert.True(service.Apply(in killActuator).Applied);
        Assert.False(tiles.Get(2, 2).HasActuator);
        Assert.False(tiles.Get(2, 2).IsActuated);
    }

    [Fact]
    public void Invalid_channel_out_of_bounds_and_duplicate_state_are_side_effect_free()
    {
        var tiles = new WorldTileStore(new WorldDimensions(10, 10));
        var service = new VanillaWorldWiringMutationService(tiles);
        var invalid = new WorldWiringMutationRequest(
            WorldWiringMutationKind.PlaceWire,
            3,
            3,
            (WorldWireChannel)99);
        var outside = invalid with { X = -1, Channel = WorldWireChannel.Green };

        Assert.Equal(WorldWiringMutationStatus.InvalidChannel, service.Apply(in invalid).Status);
        Assert.Equal(WorldWiringMutationStatus.OutOfBounds, service.Apply(in outside).Status);

        var place = invalid with { Channel = WorldWireChannel.Green };
        Assert.True(service.Apply(in place).Applied);
        tiles.DirtySections.Clear();
        tiles.PersistenceDirtySections.Clear();
        Assert.Equal(WorldWiringMutationStatus.NoChange, service.Apply(in place).Status);
        Assert.Equal(0, tiles.DirtySections.DirtyCount);
        Assert.Equal(0, tiles.PersistenceDirtySections.DirtyCount);
    }
}
