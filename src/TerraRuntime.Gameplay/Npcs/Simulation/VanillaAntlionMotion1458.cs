using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

public enum VanillaAntlionShotKind1458 : byte
{
    None = 0,
    Player = 1,
    Conveyor = 2
}

/// <summary>Input to the authoritative TerrariaServer 1.4.5.8 AI_019 Antlion motion slice.</summary>
public readonly record struct VanillaAntlionMotionInput1458(
    float VelocityX,
    float VelocityY,
    float Rotation,
    float Ai0,
    float PositionX,
    float PositionY,
    int Width,
    int Height,
    int DirectionY,
    bool HasTarget,
    float TargetCenterX,
    float TargetTopY,
    bool PlayerShotReady,
    bool HasConveyorBelow,
    bool HasSolidFloor);

/// <summary>Server state written by the TerrariaServer 1.4.5.8 AI_019 Antlion slice.</summary>
public readonly record struct VanillaAntlionMotionResult1458(
    float VelocityX,
    float VelocityY,
    float Rotation,
    float Ai0,
    bool NoGravity,
    bool NoTileCollide,
    VanillaAntlionShotKind1458 ShotKind);

/// <summary>
/// TerrariaServer 1.4.5.8 NPC.AI_019. Dust and sound are presentation effects and are intentionally outside
/// this server-state transition; projectile allocation is committed by the NPC projectile-intent phase.
/// </summary>
public static class VanillaAntlionMotion1458
{
    public const float ProjectileSpeed = 12f;
    public const float ProjectileCooldown = 200f;

    public static bool TryStep(in VanillaAntlionMotionInput1458 input, out VanillaAntlionMotionResult1458 result)
    {
        if (!IsValid(in input))
        {
            result = default;
            return false;
        }

        float velocityX = input.VelocityX;
        float velocityY = input.VelocityY;
        float rotation = input.Rotation;
        float ai0 = input.Ai0;
        bool canShootPlayer = false;
        if (input.DirectionY < 0 && input.HasTarget)
        {
            float centerX = input.PositionX + input.Width * .5f;
            float centerY = input.PositionY + input.Height * .5f;
            float targetX = input.TargetCenterX - centerX;
            float targetY = input.TargetTopY - centerY;
            rotation = MathF.Atan2(targetY, targetX) + MathF.PI * .5f;
            canShootPlayer = rotation is >= -1.2f and <= 1.2f;
            rotation = Math.Clamp(rotation, -.8f, .8f);

            // This is intentionally the source's OR predicate. Once a non-zero horizontal velocity reaches
            // this branch it is cleared after the 0.9 multiplier, irrespective of its magnitude.
            if (velocityX != 0f)
            {
                velocityX *= .9f;
                if (velocityX > -.1f || velocityX < .1f)
                    velocityX = 0f;
            }
        }

        if (ai0 > 0f)
            ai0--;

        VanillaAntlionShotKind1458 shotKind = VanillaAntlionShotKind1458.None;
        if (ai0 == 0f)
        {
            if (canShootPlayer && input.PlayerShotReady)
            {
                ai0 = ProjectileCooldown;
                shotKind = VanillaAntlionShotKind1458.Player;
            }
            else if (!canShootPlayer && input.HasConveyorBelow)
            {
                ai0 = ProjectileCooldown;
                shotKind = VanillaAntlionShotKind1458.Conveyor;
            }
        }

        bool anchored = input.HasSolidFloor;
        if (anchored)
            velocityY = -.2f;

        result = new VanillaAntlionMotionResult1458(
            velocityX, velocityY, rotation, ai0, anchored, anchored, shotKind);
        return true;
    }

    private static bool IsValid(in VanillaAntlionMotionInput1458 input) =>
        float.IsFinite(input.VelocityX) &&
        float.IsFinite(input.VelocityY) &&
        float.IsFinite(input.Rotation) &&
        float.IsFinite(input.Ai0) &&
        float.IsFinite(input.PositionX) &&
        float.IsFinite(input.PositionY) &&
        float.IsFinite(input.TargetCenterX) &&
        float.IsFinite(input.TargetTopY) &&
        input.Width > 0 &&
        input.Height > 0 &&
        input.DirectionY is >= -1 and <= 1;
}
