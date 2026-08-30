using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldGroundFighterSpecialDoorPressureTests
{
    [Fact]
    public void GetGoodWorld_restricted_zombie_resets_blood_moon_progress()
    {
        WorldTileStore tiles = CreateDoorWorld();
        VanillaZombieDoorContactResult result = Resolve(
            tiles,
            VanillaNpcIds.Zombie,
            new NpcAiState(0f, 5f, 59f, 0f),
            new VanillaGroundFighterDoorEnvironment(
                BloodMoonActive: true,
                HasTarget: false,
                TargetCenterX: 0f,
                TargetCenterY: 0f,
                GetGoodWorld: true));

        Assert.True(result.StruckDoor);
        Assert.False(result.OpeningProgressAllowed);
        Assert.Equal(5f, result.Ai.Ai1, 5);
        Assert.Null(result.OpeningIntent);
    }

    [Fact]
    public void Inside_unbreakable_walls_adds_six_clamps_to_ten_and_opens()
    {
        WorldTileStore tiles = CreateDoorWorld();
        VanillaZombieDoorContactResult result = Resolve(
            tiles,
            VanillaNpcIds.Zombie,
            new NpcAiState(0f, 0f, 59f, 0f),
            new VanillaGroundFighterDoorEnvironment(
                BloodMoonActive: false,
                HasTarget: true,
                TargetCenterX: 100f,
                TargetCenterY: 100f,
                GetGoodWorld: true,
                TargetInsideUnbreakableWalls: true));

        Assert.True(result.OpeningProgressAllowed);
        Assert.Equal(10f, result.Ai.Ai1, 5);
        Assert.NotNull(result.OpeningIntent);
    }

    [Theory]
    [InlineData(27, 6f)]
    [InlineData(31, 10f)]
    public void Special_type_bonus_is_applied_by_the_contact_primitive(int rawType, float expectedProgress)
    {
        WorldTileStore tiles = CreateDoorWorld();
        VanillaZombieDoorContactResult result = Resolve(
            tiles,
            new NpcTypeId(rawType),
            new NpcAiState(0f, 0f, 59f, 0f),
            default);

        Assert.Equal(expectedProgress, result.Ai.Ai1, 5);
        Assert.Equal(rawType == 31, result.OpeningIntent.HasValue);
    }

    [Fact]
    public void Type460_forces_open_below_threshold_while_type26_never_masquerades_as_open()
    {
        WorldTileStore forceTiles = CreateDoorWorld();
        VanillaZombieDoorContactResult force = Resolve(
            forceTiles,
            new NpcTypeId(460),
            new NpcAiState(0f, 0f, 59f, 0f),
            default);

        WorldTileStore destroyTiles = CreateDoorWorld();
        VanillaZombieDoorContactResult destroy = Resolve(
            destroyTiles,
            new NpcTypeId(26),
            new NpcAiState(0f, 9f, 59f, 0f),
            default);

        Assert.Equal(5f, force.Ai.Ai1, 5);
        Assert.NotNull(force.OpeningIntent);
        Assert.Equal(10f, destroy.Ai.Ai1, 5);
        Assert.Null(destroy.OpeningIntent);
    }

    private static VanillaZombieDoorContactResult Resolve(
        WorldTileStore tiles,
        NpcTypeId npcType,
        NpcAiState ai,
        VanillaGroundFighterDoorEnvironment environment) =>
        VanillaWorldZombieDoorContact.Resolve(
            tiles,
            positionX: 96f,
            positionY: 80f,
            velocityX: 0.5f,
            velocityY: 0f,
            width: 18,
            height: 40,
            directionX: 1,
            ai,
            npcType,
            environment,
            doorRandom: null);

    private static WorldTileStore CreateDoorWorld()
    {
        var tiles = new WorldTileStore(new WorldDimensions(40, 40));
        tiles.Set(6, 7, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        tiles.Set(7, 5, new WorldTile
        {
            Type = checked((ushort)VanillaTileIds.ClosedDoor.Value),
            Flags = WorldTileFlags.Active
        });
        return tiles;
    }
}
