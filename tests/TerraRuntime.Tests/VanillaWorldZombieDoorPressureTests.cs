using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldZombieDoorPressureTests
{
    [Fact]
    public void Blood_moon_accumulates_normal_door_progress_and_emits_opening_intent()
    {
        WorldTileStore tiles = CreateDoorWorld(VanillaTileIds.ClosedDoor);
        var random = new FixedDoorRandom(false);

        VanillaZombieDoorContactResult result = Resolve(
            tiles,
            new NpcAiState(0f, 5f, 59f, 0f),
            new VanillaGroundFighterDoorEnvironment(
                BloodMoonActive: true,
                HasTarget: false,
                TargetCenterX: 0f,
                TargetCenterY: 0f),
            random);

        Assert.True(result.StruckDoor);
        Assert.True(result.OpeningProgressAllowed);
        Assert.False(result.TargetInGraveyard);
        Assert.Equal(10f, result.Ai.Ai1, 5);
        VanillaGroundFighterDoorOpeningIntent intent = Assert.IsType<VanillaGroundFighterDoorOpeningIntent>(result.OpeningIntent);
        Assert.Equal(VanillaTileIds.ClosedDoor, intent.ClosedType);
        Assert.Equal(7, intent.TileX);
        Assert.Equal(5, intent.TileY);
        Assert.Equal(1, intent.DirectionX);
        Assert.Equal(0, random.Calls);
    }

    [Fact]
    public void Blood_moon_tall_gate_uses_two_point_progress_and_same_threshold()
    {
        WorldTileStore tiles = CreateDoorWorld(VanillaTileIds.TallGateClosed);

        VanillaZombieDoorContactResult result = Resolve(
            tiles,
            new NpcAiState(0f, 8f, 59f, 0f),
            new VanillaGroundFighterDoorEnvironment(true, false, 0f, 0f),
            new FixedDoorRandom(false));

        Assert.Equal(10f, result.Ai.Ai1, 5);
        Assert.NotNull(result.OpeningIntent);
        Assert.Equal(VanillaTileIds.TallGateClosed, result.OpeningIntent.Value.ClosedType);
    }

    [Fact]
    public void Graveyard_failed_roll_resets_previous_progress_before_current_strike()
    {
        WorldTileStore tiles = CreateDoorWorld(VanillaTileIds.ClosedDoor);
        FillGraveyard(tiles);
        var random = new FixedDoorRandom(false);

        VanillaZombieDoorContactResult result = Resolve(
            tiles,
            new NpcAiState(0f, 5f, 59f, 0f),
            TargetEnvironment(),
            random);

        Assert.True(result.TargetInGraveyard);
        Assert.False(result.OpeningProgressAllowed);
        Assert.Equal(5f, result.Ai.Ai1, 5);
        Assert.Null(result.OpeningIntent);
        Assert.Equal(1, random.Calls);
    }

    [Fact]
    public void Graveyard_successful_roll_accumulates_and_emits_opening_intent()
    {
        WorldTileStore tiles = CreateDoorWorld(VanillaTileIds.ClosedDoor);
        FillGraveyard(tiles);
        var random = new FixedDoorRandom(true);

        VanillaZombieDoorContactResult result = Resolve(
            tiles,
            new NpcAiState(0f, 5f, 59f, 0f),
            TargetEnvironment(),
            random);

        Assert.True(result.TargetInGraveyard);
        Assert.True(result.OpeningProgressAllowed);
        Assert.Equal(10f, result.Ai.Ai1, 5);
        Assert.NotNull(result.OpeningIntent);
        Assert.Equal(1, random.Calls);
    }

    [Fact]
    public void Graveyard_random_is_not_consumed_before_sixtieth_contact_tick()
    {
        WorldTileStore tiles = CreateDoorWorld(VanillaTileIds.ClosedDoor);
        FillGraveyard(tiles);
        var random = new FixedDoorRandom(true);

        VanillaZombieDoorContactResult result = Resolve(
            tiles,
            new NpcAiState(0f, 5f, 58f, 0f),
            TargetEnvironment(),
            random);

        Assert.False(result.StruckDoor);
        Assert.Equal(59f, result.Ai.Ai2, 5);
        Assert.Equal(0, random.Calls);
    }

    private static VanillaGroundFighterDoorEnvironment TargetEnvironment() =>
        new(
            BloodMoonActive: false,
            HasTarget: true,
            TargetCenterX: 100 * 16f,
            TargetCenterY: 100 * 16f);

    private static VanillaZombieDoorContactResult Resolve(
        WorldTileStore tiles,
        NpcAiState ai,
        VanillaGroundFighterDoorEnvironment environment,
        IVanillaGroundFighterDoorRandom random) =>
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
            environment,
            random);

    private static WorldTileStore CreateDoorWorld(TileTypeId doorType)
    {
        var tiles = new WorldTileStore(new WorldDimensions(240, 240));
        tiles.Set(6, 7, SolidTile());
        tiles.Set(7, 5, new WorldTile
        {
            Type = checked((ushort)doorType.Value),
            Flags = WorldTileFlags.Active
        });
        return tiles;
    }

    private static void FillGraveyard(WorldTileStore tiles)
    {
        for (int index = 0; index < VanillaWorldGraveyardScene.FunctionalTileThreshold; index++)
        {
            int x = 92 + index % 14;
            int y = 92 + index / 14;
            tiles.Set(x, y, new WorldTile
            {
                Type = checked((ushort)VanillaTileIds.Tombstones.Value),
                Flags = WorldTileFlags.Active
            });
        }
    }

    private static WorldTile SolidTile() => new()
    {
        Type = 1,
        Flags = WorldTileFlags.Active
    };

    private sealed class FixedDoorRandom(bool result) : IVanillaGroundFighterDoorRandom
    {
        public int Calls { get; private set; }

        public bool NextGraveyardProgress()
        {
            Calls++;
            return result;
        }
    }
}
