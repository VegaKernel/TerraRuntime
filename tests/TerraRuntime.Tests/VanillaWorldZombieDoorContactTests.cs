using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldZombieDoorContactTests
{
    [Fact]
    public void Closed_door_increments_contact_timer_and_clears_stuck_counter()
    {
        WorldTileStore tiles = CreateSupportedWorld();
        tiles.Set(7, 5, DoorTile(type: 10));
        var ai = new NpcAiState(0f, 0f, 0f, 17f);

        VanillaZombieDoorContactResult result = Resolve(tiles, velocityX: 0.5f, ai);

        Assert.True(result.TouchingDoor);
        Assert.False(result.StruckDoor);
        Assert.Equal(0.5f, result.VelocityX, 5);
        Assert.Equal(1f, result.Ai.Ai2, 5);
        Assert.Equal(0f, result.Ai.Ai3, 5);
    }

    [Fact]
    public void Sixtieth_closed_door_tick_recoils_and_resets_progress_to_five()
    {
        WorldTileStore tiles = CreateSupportedWorld();
        tiles.Set(7, 5, DoorTile(type: 10));
        var ai = new NpcAiState(0f, 9f, 59f, 12f);

        VanillaZombieDoorContactResult result = Resolve(tiles, velocityX: 0.5f, ai);

        Assert.True(result.TouchingDoor);
        Assert.True(result.StruckDoor);
        Assert.Equal(-0.5f, result.VelocityX, 5);
        Assert.Equal(5f, result.Ai.Ai1, 5);
        Assert.Equal(0f, result.Ai.Ai2, 5);
        Assert.Equal(0f, result.Ai.Ai3, 5);
    }

    [Fact]
    public void Tall_gate_uses_two_point_non_blood_moon_strike_progress()
    {
        WorldTileStore tiles = CreateSupportedWorld();
        tiles.Set(7, 5, DoorTile(type: 388));
        var ai = new NpcAiState(0f, 8f, 59f, 0f);

        VanillaZombieDoorContactResult result = Resolve(tiles, velocityX: 0.5f, ai);

        Assert.True(result.StruckDoor);
        Assert.Equal(2f, result.Ai.Ai1, 5);
        Assert.Equal(0f, result.Ai.Ai2, 5);
    }

    [Fact]
    public void Leaving_door_contact_clears_door_progress_but_preserves_stuck_counter()
    {
        WorldTileStore tiles = CreateSupportedWorld();
        var ai = new NpcAiState(3f, 5f, 27f, 19f);

        VanillaZombieDoorContactResult result = Resolve(tiles, velocityX: 0.5f, ai);

        Assert.False(result.TouchingDoor);
        Assert.False(result.StruckDoor);
        Assert.Equal(0f, result.Ai.Ai1, 5);
        Assert.Equal(0f, result.Ai.Ai2, 5);
        Assert.Equal(19f, result.Ai.Ai3, 5);
        Assert.Equal(3f, result.Ai.Ai0, 5);
    }

    private static VanillaZombieDoorContactResult Resolve(
        WorldTileStore tiles,
        float velocityX,
        NpcAiState ai) =>
        VanillaWorldZombieDoorContact.Resolve(
            tiles,
            positionX: 96f,
            positionY: 80f,
            velocityX: velocityX,
            velocityY: 0f,
            width: 18,
            height: 40,
            directionX: 1,
            ai: ai);

    private static WorldTileStore CreateSupportedWorld()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 7, SolidTile());
        return tiles;
    }

    private static WorldTile SolidTile() => new()
    {
        Type = 1,
        Flags = WorldTileFlags.Active
    };

    private static WorldTile DoorTile(ushort type) => new()
    {
        Type = type,
        Flags = WorldTileFlags.Active
    };
}
