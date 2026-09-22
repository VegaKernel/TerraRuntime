using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaNpcEngagementFactsTests
{
    [Fact]
    public void Damaged_day_surface_blue_slime_uses_engaged_jump_timer()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.EnableBlueSlimeMotion(worldSurfaceTiles: 100d);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false);
        NpcSnapshot npc = new(
            Handle: new NpcHandle(1, new NpcGeneration(1)),
            Revision: new NpcRevision(1),
            Type: VanillaNpcIds.BlueSlime.Value,
            NetId: checked((short)VanillaNpcIds.BlueSlime.Value),
            PositionX: 100f,
            PositionY: 80f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: new NpcAiState(-2f, 0f, 1f, 0f),
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = 1,
                Life = 10,
                LifeMax = 25
            });

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(-6f, next.VelocityY, 5);
        Assert.Equal(-1120f, next.Ai.Ai0);
    }

    [Fact]
    public void Healthy_lava_slime_stays_unengaged_in_remix_world()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.EnableBlueSlimeMotion(worldSurfaceTiles: 100d);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, remixWorld: true);
        NpcSnapshot npc = new(
            Handle: new NpcHandle(1, new NpcGeneration(1)),
            Revision: new NpcRevision(1),
            Type: VanillaNpcIds.LavaSlime.Value,
            NetId: checked((short)VanillaNpcIds.LavaSlime.Value),
            PositionX: 100f,
            PositionY: 80f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: new NpcAiState(-4f, 0f, 1f, 0f),
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = 1,
                Life = 50,
                LifeMax = 50
            });

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(-3f, next.Ai.Ai0);
        Assert.Equal(0f, next.VelocityY);
    }

    [Fact]
    public void Corrupt_and_crimson_slimes_use_their_source_combat_timer_rules()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.EnableBlueSlimeMotion(worldSurfaceTiles: 100d);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false);
        NpcSnapshot crimson = Slime(VanillaNpcIds.Crimslime, new NpcAiState(-3f, 0f, 1f, 0f));

        Assert.True(stepper.TryStepState(in crimson, out NpcStateUpdate crimsonNext));

        Assert.Equal(-1120f, crimsonNext.Ai.Ai0);
        Assert.Equal(-6f, crimsonNext.VelocityY);
    }

    [Fact]
    public void Rainbow_slime_advances_its_source_timer_while_airborne()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.EnableBlueSlimeMotion(worldSurfaceTiles: 100d);
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false);
        NpcSnapshot rainbow = Slime(VanillaNpcIds.RainbowSlime, new NpcAiState(5f, 0f, 1f, 0f)) with
        {
            VelocityY = -1f
        };

        Assert.True(stepper.TryStepState(in rainbow, out NpcStateUpdate next));

        Assert.Equal(7f, next.Ai.Ai0);
        Assert.Equal(-1f, next.VelocityY);
    }

    [Fact]
    public void Zombie_forces_upward_direction_when_player_center_is_above_npc_bottom()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.EnableZombieMotion(worldSurfaceTiles: 100d);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(
                Slot: 4,
                CenterX: 200f,
                CenterY: 110f,
                Aggro: 0,
                Active: true,
                Dead: false,
                Ghost: false,
                NoAggro: false)
        ]);
        NpcSnapshot npc = new(
            Handle: new NpcHandle(1, new NpcGeneration(1)),
            Revision: new NpcRevision(1),
            Type: VanillaNpcIds.Zombie.Value,
            NetId: checked((short)VanillaNpcIds.Zombie.Value),
            PositionX: 100f,
            PositionY: 80f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = 1,
                OldPositionX = 99f,
                OldPositionY = 80f,
                Life = 45,
                LifeMax = 45,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft
            });

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal((ushort)4, next.Target);
        Assert.Equal(-1, next.Simulation.DirectionY);
    }

    private static NpcSnapshot Slime(NpcTypeId type, NpcAiState ai) => new(
        Handle: new NpcHandle(1, new NpcGeneration(1)),
        Revision: new NpcRevision(1),
        Type: type.Value,
        NetId: checked((short)type.Value),
        PositionX: 100f,
        PositionY: 80f,
        VelocityX: 0f,
        VelocityY: 0f,
        Target: VanillaNpcDefinitionCatalog.DefaultTarget,
        Ai: ai,
        Simulation: NpcSimulationState.Initial with
        {
            DirectionX = 1,
            DirectionY = 1,
            Life = 100,
            LifeMax = 100
        });
}
