using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaBossTargetingStepperTests
{
    [Fact]
    public void Eye_of_cthulhu_refreshes_target_and_enters_classic_hover_state()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(
                Slot: 7,
                CenterX: 250f,
                CenterY: 355f,
                Aggro: 0,
                Active: true,
                Dead: false,
                Ghost: false,
                NoAggro: false)
        ]);
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.EyeOfCthulhu, lifeMax: 2800);

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal((ushort)7, next.Target);
        Assert.Equal(0.04f, next.VelocityX, 5);
        Assert.Equal(0f, next.VelocityY, 5);
        Assert.Equal(1f, next.Ai.Ai2, 5);
        Assert.True(next.Simulation.NoGravity);
        Assert.True(next.Simulation.NoTileCollide);
    }

    [Fact]
    public void Eye_of_cthulhu_receives_expert_phase_one_world_condition()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false, expertMode: true);
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(
                Slot: 7,
                CenterX: 250f,
                CenterY: 355f,
                Aggro: 0,
                Active: true,
                Dead: false,
                Ghost: false,
                NoAggro: false)
        ]);
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.EyeOfCthulhu, lifeMax: 2800);

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal(0.15f, next.VelocityX, 5);
        Assert.Equal(0f, next.VelocityY, 5);
        Assert.Equal(1f, next.Ai.Ai2, 5);
    }

    [Fact]
    public void Daytime_eye_uses_source_backed_retreat_lifecycle()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.SetWorldConditions(dayTime: true, slimeRainActive: false);
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(
                Slot: 3,
                CenterX: 250f,
                CenterY: 155f,
                Aggro: 0,
                Active: true,
                Dead: false,
                Ghost: false,
                NoAggro: false)
        ]);
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.EyeOfCthulhu, lifeMax: 2800);

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal((ushort)3, next.Target);
        Assert.Equal(-0.04f, next.VelocityY, 5);
        Assert.Equal(10, next.Simulation.TimeLeft);
    }

    [Fact]
    public void Servant_of_cthulhu_uses_ai_style_five_steering_and_no_clip_flags()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(
                Slot: 9,
                CenterX: 210f,
                CenterY: 110f,
                Aggro: 0,
                Active: true,
                Dead: false,
                Ghost: false,
                NoAggro: false)
        ]);
        NpcSnapshot npc = CreateNpc(VanillaNpcIds.ServantOfCthulhu, lifeMax: 8);

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal((ushort)9, next.Target);
        Assert.Equal(0.03f, next.VelocityX, 5);
        Assert.Equal(0f, next.VelocityY, 5);
        Assert.True(next.Simulation.NoGravity);
        Assert.True(next.Simulation.NoTileCollide);
    }

    private static NpcSnapshot CreateNpc(NpcTypeId type, int lifeMax) =>
        new(
            Handle: new NpcHandle(1, new NpcGeneration(1)),
            Revision: new NpcRevision(1),
            Type: type.Value,
            NetId: checked((short)type.Value),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                Life = lifeMax,
                LifeMax = lifeMax,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                NoGravity = true,
                NoTileCollide = true
            });
}
