using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWallOfFleshBehaviorTests
{
    [Fact]
    public void First_authoritative_tick_plans_two_eyes_and_eleven_hungry_with_root_links()
    {
        var store = new RuntimeNpcStore(capacity: 32);
        NpcSnapshot wall = SpawnWall(store);
        var stepper = CreateStepper();
        stepper.SetCandidates([Target(0, 1400f, 35_800f)]);
        stepper.SetNpcPeers([wall]);

        Assert.True(stepper.TryStepState(in wall, out NpcStateUpdate proposed));
        Span<NpcAiSpawnIntent> intents = stackalloc NpcAiSpawnIntent[13];
        int count = stepper.PlanNpcSpawns(in wall, in proposed, intents);

        Assert.Equal(13, count);
        Assert.Equal(VanillaNpcIds.WallOfFleshEye, intents[0].Type);
        Assert.Equal(1f, intents[0].InitialAi.Ai0);
        Assert.Equal(wall.Handle.Slot, intents[0].InitialAi.Ai3);
        Assert.Equal(VanillaNpcIds.WallOfFleshEye, intents[1].Type);
        Assert.Equal(-1f, intents[1].InitialAi.Ai0);
        Assert.Equal(wall.Handle.Slot, intents[1].InitialAi.Ai3);
        for (int index = 2; index < count; index++)
        {
            Assert.Equal(VanillaNpcIds.TheHungry, intents[index].Type);
            Assert.Equal(wall.Handle.Slot, intents[index].InitialAi.Ai3);
            Assert.Equal((index - 2) * 0.1f - 0.05f, intents[index].InitialAi.Ai0, 5);
        }
        Assert.Equal(2f, proposed.Simulation.LocalAi.Ai0);
    }

    [Fact]
    public void Eye_volley_plans_source_projectile_83_with_root_life_damage_and_speed()
    {
        var store = new RuntimeNpcStore(capacity: 8);
        NpcSnapshot wall = SpawnWall(store);
        var rootUpdate = new NpcStateUpdate(
            wall.Type,
            wall.NetId,
            wall.PositionX,
            wall.PositionY,
            2f,
            0f,
            0,
            wall.Ai,
            wall.Simulation with
            {
                DirectionX = 1,
                LocalAi = new NpcAiState(2f, 0f, 35_400f, 36_200f)
            });
        Assert.True(store.TryUpdate(wall.Handle, in rootUpdate, out wall));

        var eyeUpdate = new NpcStateUpdate(
            VanillaNpcIds.WallOfFleshEye.Value,
            checked((short)VanillaNpcIds.WallOfFleshEye.Value),
            wall.PositionX,
            35_600f,
            0f,
            0f,
            0,
            new NpcAiState(1f, 0f, 0f, wall.Handle.Slot),
            NpcSimulationState.Initial with { LocalAi = new NpcAiState(0f, 45f, 1f, 0f) });
        Assert.True(store.TrySpawnVanilla(in eyeUpdate, out NpcSnapshot eye));

        var stepper = CreateStepper();
        stepper.SetCandidates([Target(0, 1800f, 35_800f)]);
        stepper.SetNpcPeers([wall, eye]);

        Assert.True(stepper.TryStepState(in eye, out NpcStateUpdate proposed));
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[1];
        int count = stepper.PlanProjectileSpawns(in eye, in proposed, intents);

        Assert.Equal(1, count);
        Assert.Equal(VanillaProjectileIds.WallOfFleshEyeLaser, intents[0].Type);
        Assert.Equal(11, intents[0].Damage);
        Assert.Equal(600, intents[0].TimeLeftOverride);
        Assert.Equal(9f, MathF.Sqrt(intents[0].VelocityX * intents[0].VelocityX + intents[0].VelocityY * intents[0].VelocityY), 4);
    }

    [Theory]
    [InlineData(false, 140)]
    [InlineData(true, 347)]
    public void Death_brick_box_uses_world_evil_brick_and_drains_liquid(bool crimson, int expectedBrick)
    {
        var tiles = new WorldTileStore(new WorldDimensions(200, 200));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.WallOfFlesh, out VanillaNpcDefinition definition));
        float positionX = 100 * 16f - definition.Width * 0.5f;
        float positionY = 100 * 16f - definition.Height * 0.5f;
        int radius = definition.Width / 2 / 16 + 1;

        WorldTile center = tiles.Get(100, 100);
        center.LiquidAmount = 255;
        center.LiquidKind = WorldLiquidKind.Lava;
        tiles.Set(100, 100, in center);

        int changed = VanillaWallOfFleshDeathWorldMutation.Apply(
            tiles,
            positionX,
            positionY,
            definition.Width,
            definition.Height,
            crimson);

        Assert.True(changed > 0);
        WorldTile perimeter = tiles.Get(100 + radius, 100);
        Assert.True(perimeter.IsActive);
        Assert.Equal(expectedBrick, perimeter.Type);
        WorldTile drained = tiles.Get(100, 100);
        Assert.Equal(0, drained.LiquidAmount);
        Assert.Equal(WorldLiquidKind.Water, drained.LiquidKind);
    }

    private static VanillaNpcTargetingAiStepper CreateStepper()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: new ZeroRandom());
        stepper.SetWallOfFleshEnvironment(new TestEnvironment());
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        return stepper;
    }

    private static NpcSnapshot SpawnWall(RuntimeNpcStore store)
    {
        var update = new NpcStateUpdate(
            VanillaNpcIds.WallOfFlesh.Value,
            checked((short)VanillaNpcIds.WallOfFlesh.Value),
            PositionX: 1000f,
            PositionY: 35_700f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial);
        Assert.True(store.TrySpawnVanilla(in update, out NpcSnapshot wall));
        return wall;
    }

    private static VanillaNpcTargetCandidate Target(byte slot, float x, float y) =>
        new(slot, x, y, Aggro: 0, Active: true, Dead: false, Ghost: false, NoAggro: false);

    private sealed class TestEnvironment : IVanillaWallOfFleshEnvironment
    {
        public int WorldWidthTiles => 8400;
        public int WorldHeightTiles => 2400;
        public int UnderworldLayerTiles => 2200;

        public bool TryResolveCorridor(float positionX, float positionY, int width, int height, out float topPixels, out float bottomPixels)
        {
            topPixels = 35_400f;
            bottomPixels = 36_200f;
            return true;
        }

        public bool CanHit(float sourceX, float sourceY, int sourceWidth, int sourceHeight, float targetX, float targetY, int targetWidth, int targetHeight) => true;

        public bool TryFindGroundSpawn(int tileX, int startTileY, out int bottomX, out int bottomY)
        {
            bottomX = 0;
            bottomY = 0;
            return false;
        }

        public bool TryFindTeleportSpot(int targetTileX, int targetTileY, int npcWidth, int npcHeight, out int tileX, out int tileY)
        {
            tileX = 0;
            tileY = 0;
            return false;
        }
    }

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }

    private sealed class ZeroRandom : IVanillaNpcRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) => inclusiveMin;
    }
}
