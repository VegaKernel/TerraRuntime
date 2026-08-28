using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaZombieStuckHopTests
{
    [Fact]
    public void Grounded_zero_horizontal_input_with_ai3_one_uses_vanilla_five_jump()
    {
        WorldTileStore tiles = CreateSupportedWorld();
        var stepper = new VanillaNpcWorldMotionAiStepper(new Ai3OneStepper(), tiles);
        NpcSnapshot npc = CreateZombie(justHit: false);

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(-5f, next.Simulation.OldVelocityY, 5);
    }

    [Fact]
    public void Just_hit_suppresses_grounded_stuck_hop()
    {
        WorldTileStore tiles = CreateSupportedWorld();
        var stepper = new VanillaNpcWorldMotionAiStepper(new Ai3OneStepper(), tiles);
        NpcSnapshot npc = CreateZombie(justHit: true);

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(0f, next.Simulation.OldVelocityY, 5);
    }

    [Fact]
    public void Missing_ground_support_suppresses_stuck_hop()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var stepper = new VanillaNpcWorldMotionAiStepper(new Ai3OneStepper(), tiles);
        NpcSnapshot npc = CreateZombie(justHit: false);

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(0f, next.Simulation.OldVelocityY, 5);
    }

    private static WorldTileStore CreateSupportedWorld()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 7, new WorldTile
        {
            Type = 1,
            Flags = WorldTileFlags.Active
        });
        return tiles;
    }

    private static NpcSnapshot CreateZombie(bool justHit) =>
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
                JustHit = justHit,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft
            });

    private sealed class Ai3OneStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = new NpcStateUpdate(
                npc.Type,
                npc.NetId,
                npc.PositionX,
                npc.PositionY,
                0f,
                0f,
                npc.Target,
                new NpcAiState(0f, 0f, 0f, 1f),
                npc.Simulation with { NoGravity = true });
            return true;
        }
    }
}
