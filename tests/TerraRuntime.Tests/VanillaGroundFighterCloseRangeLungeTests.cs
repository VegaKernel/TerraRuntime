using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaGroundFighterCloseRangeLungeTests
{
    [Theory]
    [InlineData(1, 1.25f, 2.5f)]
    [InlineData(1, 2f, 3f)]
    [InlineData(-1, -1.25f, -2.5f)]
    [InlineData(-1, -2f, -3f)]
    public void Grounded_close_target_moving_toward_target_lunges_and_clamps(
        int directionX,
        float velocityX,
        float expectedVelocityX)
    {
        Assert.True(VanillaGroundFighterCloseRangeLunge.TryResolve(
            npcCenterX: 100f,
            npcCenterY: 100f,
            targetCenterX: directionX > 0 ? 150f : 50f,
            targetCenterY: 120f,
            velocityX,
            velocityY: 0f,
            directionX,
            out float nextVelocityX,
            out float nextVelocityY));

        Assert.Equal(expectedVelocityX, nextVelocityX, 5);
        Assert.Equal(-4f, nextVelocityY, 5);
    }

    [Theory]
    [InlineData(100f, 0f, 1, 1.5f, 0f)]
    [InlineData(99f, 50f, 1, 1.5f, 0f)]
    [InlineData(99f, 49f, 1, 0.99f, 0f)]
    [InlineData(99f, 49f, -1, 1.5f, 0f)]
    [InlineData(99f, 49f, 1, 1.5f, 0.01f)]
    public void Source_boundaries_and_invalid_motion_do_not_lunge(
        float targetDeltaX,
        float targetDeltaY,
        int directionX,
        float velocityX,
        float velocityY)
    {
        Assert.False(VanillaGroundFighterCloseRangeLunge.TryResolve(
            npcCenterX: 100f,
            npcCenterY: 100f,
            targetCenterX: 100f + targetDeltaX,
            targetCenterY: 100f + targetDeltaY,
            velocityX,
            velocityY,
            directionX,
            out float nextVelocityX,
            out float nextVelocityY));

        Assert.Equal(velocityX, nextVelocityX, 5);
        Assert.Equal(velocityY, nextVelocityY, 5);
    }
}
