using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaMoonEventJumpingFighterAiTests
{
    [Fact]
    public void Type_341_defaults_and_coverage_match_source_ai25_entry()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(341), out VanillaNpcDefinition definition));
        Assert.True(VanillaNpcAiCoverageCatalog.TryGet(new NpcTypeId(341), out _));

        Assert.Equal(new NpcAiStyleId(25), definition.AiStyle);
        Assert.Equal(VanillaNpcBehaviorFamily.MoonEventJumpingFighter, definition.BehaviorFamily);
        Assert.Equal(24, definition.BaseWidth);
        Assert.Equal(24, definition.BaseHeight);
        Assert.Equal(100, definition.Damage);
        Assert.Equal(32, definition.Defense);
        Assert.Equal(900, definition.LifeMax);
        Assert.Equal(.25f, definition.KnockBackResist);
    }

    [Fact]
    public void First_grounded_wait_uses_source_short_jump()
    {
        VanillaNpcTargetingAiStepper stepper = CreateStepper(300f, 100f);
        NpcSnapshot npc = CreateNpc(new NpcAiState(1f, 0f, 11f, 1f));

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal((ushort)3, next.Target);
        Assert.Equal(1, next.Simulation.DirectionX);
        Assert.Equal(1, next.Simulation.SpriteDirection);
        Assert.Equal(3.5f, next.VelocityX, 5);
        Assert.Equal(-4f, next.VelocityY, 5);
        Assert.Equal(1f, next.Ai.Ai0, 5);
        Assert.Equal(1f, next.Ai.Ai1, 5);
        Assert.Equal(0f, next.Ai.Ai2, 5);
        Assert.Equal(1f, next.Ai.Ai3, 5);
    }

    [Fact]
    public void Second_grounded_wait_uses_source_high_jump_and_resets_cycle()
    {
        VanillaNpcTargetingAiStepper stepper = CreateStepper(20f, 100f);
        NpcSnapshot npc = CreateNpc(new NpcAiState(1f, 1f, 19f, 1f));

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(-1, next.Simulation.DirectionX);
        Assert.Equal(-1, next.Simulation.SpriteDirection);
        Assert.Equal(-2.5f, next.VelocityX, 5);
        Assert.Equal(-8f, next.VelocityY, 5);
        Assert.Equal(0f, next.Ai.Ai1, 5);
        Assert.Equal(0f, next.Ai.Ai2, 5);
    }

    [Fact]
    public void Source_activation_rectangle_uses_live_player_hitbox_edges()
    {
        // With a 20px player hitbox this overlaps the source rectangle by two pixels. A center-distance
        // approximation would reject it because the centres are 120px apart on X.
        VanillaNpcTargetingAiStepper stepper = CreateStepper(232f, 112f);
        NpcSnapshot npc = CreateNpc(default) with
        {
            Simulation = CreateNpc(default).Simulation with { Life = 900, LifeMax = 900 }
        };

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(1f, next.Ai.Ai0, 5);
        Assert.Equal(1f, next.Ai.Ai3, 5);
    }

    [Fact]
    public void Airborne_fighter_accelerates_toward_its_selected_direction()
    {
        VanillaNpcTargetingAiStepper stepper = CreateStepper(300f, 100f);
        NpcSnapshot npc = CreateNpc(new NpcAiState(1f, 0f, 0f, 1f)) with { VelocityY = -1f };

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(.1f, next.VelocityX, 5);
        Assert.Equal(-1f, next.VelocityY, 5);
    }

    private static VanillaNpcTargetingAiStepper CreateStepper(float centerX, float centerY)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.EnableZombieMotion(100d);
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(3, centerX, centerY, 0, true, false, false, false)
        ]);
        return stepper;
    }

    private static NpcSnapshot CreateNpc(NpcAiState ai) =>
        new(
            Handle: new NpcHandle(1, new NpcGeneration(1)),
            Revision: new NpcRevision(1),
            Type: 341,
            NetId: 341,
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: ai,
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = 1,
                SpriteDirection = 1,
                Life = 900,
                LifeMax = 900,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft
            });

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }
}
