using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaSkeletonAiTests
{
    [Fact]
    public void Skeleton_uses_source_backed_faster_fighter_speed_band()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.EnableZombieMotion(worldSurfaceTiles: 200d);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        stepper.SetCandidates([
            new VanillaNpcTargetCandidate(
                Slot: 3,
                CenterX: 500f,
                CenterY: 120f,
                Aggro: 0,
                Active: true,
                Dead: false,
                Ghost: false,
                NoAggro: false)
        ]);
        NpcSnapshot skeleton = CreateFighter(VanillaNpcIds.Skeleton, velocityX: 1.2f);

        Assert.True(stepper.TryStepState(in skeleton, out NpcStateUpdate next));

        Assert.Equal((ushort)3, next.Target);
        Assert.Equal(1.27f, next.VelocityX, 5);
    }

    [Fact]
    public void Zombie_keeps_its_distinct_one_pixel_speed_band()
    {
        Assert.True(VanillaGroundFighterBehaviorCatalog.TryGet(
            VanillaNpcIds.Zombie,
            out VanillaGroundFighterBehaviorParameters zombie));
        Assert.True(VanillaGroundFighterBehaviorCatalog.TryGet(
            VanillaNpcIds.Skeleton,
            out VanillaGroundFighterBehaviorParameters skeleton));

        Assert.Equal(1f, zombie.BaseMaximumHorizontalSpeed);
        Assert.Equal(1.5f, skeleton.BaseMaximumHorizontalSpeed);
    }

    private static NpcSnapshot CreateFighter(NpcTypeId type, float velocityX) =>
        new(
            Handle: new NpcHandle(1, new NpcGeneration(1)),
            Revision: new NpcRevision(1),
            Type: type.Value,
            NetId: checked((short)type.Value),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: velocityX,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = 1,
                OldPositionX = 99f,
                Life = 60,
                LifeMax = 60,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                Scale = 1f
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
