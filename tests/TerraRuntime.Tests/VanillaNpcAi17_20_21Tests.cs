using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaNpcAi17_20_21Tests
{
    [Theory]
    [InlineData(61, 17, 36, 36, 15, 4, 40, 0.8f, 1f, false, false, false)]
    [InlineData(301, 17, 36, 26, 12, 2, 35, 0.85f, 1f, false, false, false)]
    [InlineData(70, 20, 34, 34, 32, 100, 100, 0f, 1.5f, true, true, true)]
    [InlineData(72, 21, 34, 34, 24, 100, 100, 0f, 1.2f, true, false, true)]
    public void Definitions_match_pinned_1458_defaults(
        int type,
        int aiStyle,
        int width,
        int height,
        int damage,
        int defense,
        int life,
        float knockback,
        float scale,
        bool noGravity,
        bool noTileCollide,
        bool dontTakeDamage)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(type), out VanillaNpcDefinition definition));
        Assert.Equal(aiStyle, definition.AiStyle.Value);
        Assert.Equal(width, definition.BaseWidth);
        Assert.Equal(height, definition.BaseHeight);
        Assert.Equal(damage, definition.Damage);
        Assert.Equal(defense, definition.Defense);
        Assert.Equal(life, definition.LifeMax);
        Assert.Equal(knockback, definition.KnockBackResist);
        Assert.Equal(scale, definition.Scale);
        Assert.Equal(noGravity, definition.NoGravityAtSpawn);
        Assert.Equal(noTileCollide, definition.NoTileCollideAtSpawn);
        Assert.Equal(dontTakeDamage, definition.DontTakeDamageAtSpawn);
    }

    [Fact]
    public void Vulture_resting_activation_uses_source_six_pixel_launch()
    {
        var input = new VanillaVultureMotionInput1458(
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            OldVelocityX: 0f,
            OldVelocityY: 0f,
            Width: 36,
            Height: 36,
            DirectionX: -1,
            DirectionY: -1,
            Target: byte.MaxValue,
            Ai: default,
            Wet: false,
            CollideX: false,
            CollideY: false,
            Life: 40,
            LifeMax: 40,
            CurrentTargetDead: false,
            ClosestTarget: new VanillaBlueSlimeTargetRefresh(true, 2, 1, 1),
            ClosestTargetCenterX: 150f,
            ClosestTargetCenterY: 150f);

        Assert.True(VanillaVultureMotion1458.TryStep(in input, out VanillaVultureMotionResult1458 result));
        Assert.Equal(1f, result.Ai.Ai0);
        Assert.Equal(-6f, result.VelocityY);
        Assert.False(result.NoGravity);
        Assert.Equal((ushort)2, result.Target);
        Assert.Equal(1, result.DirectionX);
    }

    [Fact]
    public void Vulture_flight_rebounds_before_retargeting_and_steering()
    {
        var input = new VanillaVultureMotionInput1458(
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: -1f,
            VelocityY: 0f,
            OldVelocityX: -4f,
            OldVelocityY: 0f,
            Width: 36,
            Height: 36,
            DirectionX: -1,
            DirectionY: 1,
            Target: 1,
            Ai: new NpcAiState(1f, 0f, 0f, 0f),
            Wet: false,
            CollideX: true,
            CollideY: false,
            Life: 40,
            LifeMax: 40,
            CurrentTargetDead: false,
            ClosestTarget: new VanillaBlueSlimeTargetRefresh(true, 2, 1, -1),
            ClosestTargetCenterX: 500f,
            ClosestTargetCenterY: 50f);

        Assert.True(VanillaVultureMotion1458.TryStep(in input, out VanillaVultureMotionResult1458 result));
        Assert.Equal(2.1f, result.VelocityX, 3);
        Assert.Equal(1, result.DirectionX);
        Assert.True(result.NoGravity);
    }

    [Fact]
    public void Spike_ball_initialization_preserves_source_rng_and_scaled_geometry()
    {
        var input = new VanillaSpikeBallMotionInput1458(
            PositionX: 100f,
            PositionY: 200f,
            VelocityX: 0f,
            VelocityY: 0f,
            Width: 51,
            Height: 51,
            DirectionX: 0,
            DirectionY: 0,
            Target: byte.MaxValue,
            Ai: default,
            ClosestTarget: new VanillaBlueSlimeTargetRefresh(true, 4, 1, 1));

        Assert.True(VanillaSpikeBallMotion1458.TryStep(in input, new FixedRandom(3), out VanillaSpikeBallMotionResult1458 result));
        Assert.Equal(233f, result.PositionY);
        Assert.Equal(-1, result.DirectionX);
        Assert.Equal(-1, result.DirectionY);
        Assert.Equal(1.3f, result.Ai.Ai3, 3);
        Assert.Equal(-7.8f, result.VelocityY, 3);
        Assert.Equal(1f, result.Ai.Ai0);
    }

    [Fact]
    public void Spike_ball_vertical_phase_turns_into_horizontal_phase_at_tick_15()
    {
        var input = new VanillaSpikeBallMotionInput1458(
            0f,
            0f,
            0f,
            6f,
            34,
            34,
            1,
            1,
            0,
            new NpcAiState(15f, 0f, 0f, 1f),
            default);
        Assert.True(VanillaSpikeBallMotion1458.TryStep(in input, new FixedRandom(0), out VanillaSpikeBallMotionResult1458 result));
        Assert.Equal(-1f, result.Ai.Ai0);
        Assert.Equal(6f, result.VelocityX);
        Assert.Equal(0f, result.VelocityY);
        Assert.Equal(-1, result.DirectionY);
    }

    [Fact]
    public void Blazing_wheel_switches_wall_axis_using_collision_history()
    {
        var initial = new VanillaBlazingWheelMotionInput1458(
            0f,
            0f,
            0,
            0,
            byte.MaxValue,
            default,
            CollideX: false,
            CollideY: true,
            new VanillaBlueSlimeTargetRefresh(true, 3, -1, 1));
        Assert.True(VanillaBlazingWheelMotion1458.TryStep(in initial, out VanillaBlazingWheelMotionResult1458 first));
        Assert.Equal(-6f, first.VelocityX);
        Assert.Equal(6f, first.VelocityY);
        Assert.Equal(2f, first.Ai.Ai0);

        var leaveFloor = new VanillaBlazingWheelMotionInput1458(
            first.VelocityX,
            first.VelocityY,
            first.DirectionX,
            first.DirectionY,
            first.Target,
            first.Ai,
            CollideX: false,
            CollideY: false,
            default);
        Assert.True(VanillaBlazingWheelMotion1458.TryStep(in leaveFloor, out VanillaBlazingWheelMotionResult1458 second));
        Assert.Equal(1, second.DirectionX);
        Assert.Equal(1f, second.Ai.Ai1);
        Assert.Equal(6f, second.VelocityX);
    }

    [Fact]
    public void Spike_ball_and_wheel_are_invulnerable_before_first_ai_tick()
    {
        foreach (NpcTypeId type in new[] { VanillaNpcIds.SpikeBall, VanillaNpcIds.BlazingWheel })
        {
            var store = new RuntimeNpcStore();
            var update = new NpcStateUpdate(
                type.Value,
                checked((short)type.Value),
                0f,
                0f,
                0f,
                0f,
                VanillaNpcDefinitionCatalog.DefaultTarget,
                default,
                NpcSimulationState.Initial);
            Assert.True(store.TrySpawnVanilla(in update, out NpcSnapshot spawned));
            Assert.True(spawned.Simulation.DontTakeDamage);
        }
    }

    private sealed class FixedRandom(int value) : IVanillaNpcRandom
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            return value;
        }
    }
}
