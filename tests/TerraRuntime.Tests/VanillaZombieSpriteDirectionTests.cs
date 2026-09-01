using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaZombieSpriteDirectionTests
{
    [Fact]
    public void Discouraged_idle_turn_updates_sprite_direction_with_direction()
    {
        var input = new VanillaZombieMotionInput(
            PositionX: 100f,
            OldPositionX: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            DirectionX: 1,
            DirectionY: 1,
            Target: 4,
            Ai: new NpcAiState(1f, 0f, 0f, 60f),
            Scale: 1f,
            TargetOverlaps: false,
            ClosestTarget: default)
        {
            PursuitAllowed = false,
            TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
            SpriteDirection = 1
        };

        Assert.True(VanillaZombieMotion.TryStep(in input, out VanillaZombieMotionResult result));

        Assert.Equal(-1, result.DirectionX);
        Assert.Equal(-1, result.SpriteDirection);
        Assert.Equal(0f, result.Ai.Ai0);
    }

    [Fact]
    public void Pursuit_direction_change_does_not_change_sprite_direction()
    {
        var input = new VanillaZombieMotionInput(
            PositionX: 100f,
            OldPositionX: 99f,
            VelocityX: 0f,
            VelocityY: 0f,
            DirectionX: 1,
            DirectionY: 1,
            Target: 4,
            Ai: default,
            Scale: 1f,
            TargetOverlaps: false,
            ClosestTarget: new VanillaZombieTargetRefresh(true, 5, -1, 1))
        {
            PursuitAllowed = true,
            TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft,
            SpriteDirection = 1
        };

        Assert.True(VanillaZombieMotion.TryStep(in input, out VanillaZombieMotionResult result));

        Assert.Equal(-1, result.DirectionX);
        Assert.Equal(1, result.SpriteDirection);
        Assert.Equal(1, result.TargetRefreshes);
    }
}
