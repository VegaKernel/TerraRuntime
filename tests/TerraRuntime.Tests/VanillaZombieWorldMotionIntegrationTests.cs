using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaZombieWorldMotionIntegrationTests
{
    [Fact]
    public void Obstacle_jump_is_applied_before_gravity_and_collision_capture()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 7, SolidTile());
        tiles.Set(7, 5, SolidTile());
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate initial = new(
            Type: 3,
            NetId: 3,
            PositionX: 96f,
            PositionY: 80f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = -1,
                NoGravity = false,
                OldPositionX = 95f,
                OldPositionY = 80f
            });
        Assert.True(store.TrySpawn(0, in initial, out NpcSnapshot spawned));
        var executor = new RuntimeNpcAiStateExecutor(store);
        var stepper = new VanillaNpcWorldMotionAiStepper(
            new FixedZombieVelocityStepper(0.5f, 0f),
            tiles);

        executor.Tick(stepper);

        Assert.True(store.TryGet(spawned.Handle, out NpcSnapshot updated));
        Assert.Equal(-5.925f, updated.Simulation.OldVelocityY, 5);
        Assert.True(updated.Simulation.OldVelocityY < -5f);
    }

    private static WorldTile SolidTile() => new()
    {
        Type = 1,
        Flags = WorldTileFlags.Active
    };

    private sealed class FixedZombieVelocityStepper(float velocityX, float velocityY) : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = new NpcStateUpdate(
                npc.Type,
                npc.NetId,
                npc.PositionX,
                npc.PositionY,
                velocityX,
                velocityY,
                npc.Target,
                npc.Ai,
                npc.Simulation with
                {
                    DirectionX = 1,
                    DirectionY = -1,
                    NoGravity = false
                });
            return true;
        }
    }
}
