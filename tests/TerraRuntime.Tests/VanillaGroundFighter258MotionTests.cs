using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaGroundFighter258MotionTests
{
    [Theory]
    [InlineData(2f, 70f, -1, 1.8f)]
    [InlineData(-2f, 140f, 1, -1.8f)]
    [InlineData(-5f, 70f, -1, -5f)]
    [InlineData(5f, 140f, 1, 5f)]
    [InlineData(1f, 110f, -1, 1f)]
    public void Airborne_pursuit_damps_opposing_motion_then_accelerates_with_source_bounds(
        float velocityX,
        float targetCenterX,
        int directionX,
        float expectedVelocityX)
    {
        var input = new VanillaGroundFighter258AirborneInput(
            PositionX: 100f,
            Width: 30,
            VelocityX: velocityX,
            VelocityY: -2f,
            DirectionX: directionX,
            TargetCenterX: targetCenterX);

        Assert.True(VanillaGroundFighter258Motion.TryResolveAirborne(in input, out var result));

        Assert.Equal(expectedVelocityX, result.VelocityX, 5);
        Assert.Equal(directionX, result.SpriteDirection);
    }

    [Theory]
    [InlineData(100f, 49.9f, true, -7f, true)]
    [InlineData(100f, 50f, true, 0f, false)]
    [InlineData(100f, 49.9f, false, 0f, false)]
    [InlineData(100f, 49.9f, true, .01f, false)]
    public void Ground_leap_requires_strict_high_target_line_of_sight_and_grounded_motion(
        float positionY,
        float targetCenterY,
        bool canHit,
        float velocityY,
        bool expected)
    {
        bool leapt = VanillaGroundFighter258Motion.TryResolveGroundLeap(
            positionY, velocityY, targetCenterY, canHit, out float nextVelocityY);

        Assert.Equal(expected, leapt);
        Assert.Equal(expected ? -7f : velocityY, nextVelocityY, 5);
    }

    [Fact]
    public void Airborne_motion_rejects_non_airborne_or_invalid_inputs()
    {
        var grounded = new VanillaGroundFighter258AirborneInput(100f, 30, 0f, 0f, 1, 150f);
        var invalidDirection = grounded with { VelocityY = 1f, DirectionX = 0 };

        Assert.False(VanillaGroundFighter258Motion.TryResolveAirborne(in grounded, out _));
        Assert.False(VanillaGroundFighter258Motion.TryResolveAirborne(in invalidDirection, out _));
    }

    [Fact]
    public void Ai003_reacquires_the_airborne_target_and_commits_its_special_steering()
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        stepper.EnableZombieMotion(100d);
        stepper.SetWorldConditions(dayTime: false, slimeRainActive: false);
        stepper.SetCandidates(
        [
            new VanillaNpcTargetCandidate(3, 60f, 100f, 0, true, false, false, false),
            new VanillaNpcTargetCandidate(7, 400f, 100f, 0, true, false, false, false)
        ]);
        NpcSnapshot npc = new(
            new NpcHandle(1, new NpcGeneration(1)),
            new NpcRevision(1),
            Type: 258,
            NetId: 258,
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 2f,
            VelocityY: -1f,
            Target: 7,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = 1,
                SpriteDirection = 1,
                Life = 220,
                LifeMax = 220,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
                OldPositionX = 99f
            });

        Assert.True(stepper.TryStepState(in npc, out NpcStateUpdate next));

        Assert.Equal((ushort)3, next.Target);
        Assert.Equal(-1, next.Simulation.DirectionX);
        Assert.Equal(-1, next.Simulation.SpriteDirection);
        Assert.Equal(1.7145f, next.VelocityX, 5);
    }

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }
}
