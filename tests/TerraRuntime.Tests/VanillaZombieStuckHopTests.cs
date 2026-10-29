using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaZombieStuckHopTests
{
    [Theory]
    [InlineData(0f, 1, 0f)]
    [InlineData(.07f, 1, -5f)]
    [InlineData(-.07f, 1, 0f)]
    [InlineData(-.07f, -1, -5f)]
    [InlineData(.07f, -1, 0f)]
    public void Grounded_stuck_hop_requires_post_ai_horizontal_motion_matching_sprite_direction(
        float postAiVelocityX, int spriteDirection, float expectedVelocityY)
    {
        WorldTileStore tiles = CreateSupportedWorld();
        var stepper = new VanillaNpcWorldMotionAiStepper(new Ai3OneStepper(postAiVelocityX), tiles);
        NpcSnapshot npc = CreateZombie(justHit: false, spriteDirection);

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        // NPC.AI_003_Fighters 1.4.5.8 places the ai3==1/flag6 launch
        // inside its strict velocity.X/spriteDirection gate. A custom inner
        // step returning zero has not performed vanilla horizontal acceleration.
        Assert.Equal(expectedVelocityY, next.Simulation.OldVelocityY, 5);
    }

    [Fact]
    public void Just_hit_suppresses_grounded_stuck_hop()
    {
        WorldTileStore tiles = CreateSupportedWorld();
        var stepper = new VanillaNpcWorldMotionAiStepper(new Ai3OneStepper(.07f), tiles);
        NpcSnapshot npc = CreateZombie(justHit: true);

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(0f, next.Simulation.OldVelocityY, 5);
    }

    [Fact]
    public void Missing_ground_support_suppresses_stuck_hop()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var stepper = new VanillaNpcWorldMotionAiStepper(new Ai3OneStepper(.07f), tiles);
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

    private static NpcSnapshot CreateZombie(bool justHit, int spriteDirection = 1) =>
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
                SpriteDirection = spriteDirection,
                JustHit = justHit,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft
            });

    private sealed class Ai3OneStepper(float velocityX) : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = new NpcStateUpdate(
                npc.Type,
                npc.NetId,
                npc.PositionX,
                npc.PositionY,
                velocityX,
                0f,
                npc.Target,
                new NpcAiState(0f, 0f, 0f, 1f),
                npc.Simulation with { NoGravity = true });
            return true;
        }
    }
}
