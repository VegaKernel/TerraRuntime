using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaZombieStepUpWiringTests
{
    [Fact]
    public void Step_up_adjusted_position_is_captured_before_collision_motion()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(7, 7, new WorldTile
        {
            Type = 1,
            Flags = WorldTileFlags.Active
        });
        var stepper = new VanillaNpcWorldMotionAiStepper(new ForwardGroundedStepper(), tiles);
        NpcSnapshot npc = CreateZombie();

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(72f, next.Simulation.OldPositionY, 5);
    }

    private static NpcSnapshot CreateZombie() =>
        new(
            Handle: new NpcHandle(1, new NpcGeneration(1)),
            Revision: new NpcRevision(1),
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
                DirectionY = 1,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft
            });

    private sealed class ForwardGroundedStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = new NpcStateUpdate(
                npc.Type,
                npc.NetId,
                npc.PositionX,
                npc.PositionY,
                0.5f,
                0f,
                npc.Target,
                npc.Ai,
                npc.Simulation with { NoGravity = true });
            return true;
        }
    }
}
