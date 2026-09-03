namespace TerraRuntime.Gameplay.Npcs;

/// <summary>
/// TerrariaServer 1.4.5.8 NPC.AI_003_Fighters close-range grounded lunge shared by the admitted
/// Angry Bones / Armored Skeleton slice. The source requires a target within 100x50 px, grounded motion
/// toward the target at at least one pixel/tick, doubles horizontal speed clamped to +/-3, then jumps at -4.
/// </summary>
public static class VanillaGroundFighterCloseRangeLunge
{
    public static bool TryResolve(
        float npcCenterX,
        float npcCenterY,
        float targetCenterX,
        float targetCenterY,
        float velocityX,
        float velocityY,
        int directionX,
        out float nextVelocityX,
        out float nextVelocityY)
    {
        nextVelocityX = velocityX;
        nextVelocityY = velocityY;

        if (!float.IsFinite(npcCenterX) ||
            !float.IsFinite(npcCenterY) ||
            !float.IsFinite(targetCenterX) ||
            !float.IsFinite(targetCenterY) ||
            !float.IsFinite(velocityX) ||
            !float.IsFinite(velocityY) ||
            directionX is not (-1 or 1) ||
            velocityY != 0f ||
            MathF.Abs(npcCenterX - targetCenterX) >= 100f ||
            MathF.Abs(npcCenterY - targetCenterY) >= 50f ||
            (directionX > 0 ? velocityX < 1f : velocityX > -1f))
        {
            return false;
        }

        nextVelocityX = Math.Clamp(velocityX * 2f, -3f, 3f);
        nextVelocityY = -4f;
        return true;
    }
}
