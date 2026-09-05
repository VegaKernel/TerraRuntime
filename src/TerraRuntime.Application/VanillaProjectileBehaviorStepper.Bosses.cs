using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

internal static partial class VanillaProjectileBehaviorStepper
{
    private static bool TryStepHallowBossRainbowStreak(
        in ProjectileSnapshot current,
        in VanillaProjectileDefinition definition,
        in VanillaProjectileBehaviorContext context,
        out VanillaProjectileBehaviorResult next)
    {
        // TerrariaServer 1.4.5.8 AI_171_HallowBossRainbowStreak, hostile type 873. Presentation-only
        // opacity/rotation are omitted; velocity phases and player homing remain authoritative.
        float velocityX = current.VelocityX;
        float velocityY = current.VelocityY;
        int timeLeft = context.CurrentTimeLeft;
        const float freeDriftUntil = 140f;
        const float homingEndsAt = 30f;

        if (timeLeft > freeDriftUntil)
        {
            float phase = current.Handle.Slot % 6f / 6f + current.PositionX / 320f + current.PositionY / 160f;
            float bend = MathF.Cos(phase) * (MathF.PI * 2f) * 0.125f / 30f;
            velocityX *= 0.98f;
            velocityY *= 0.98f;
            Rotate(ref velocityX, ref velocityY, bend);
        }
        else if (timeLeft > homingEndsAt && context.HostilePlayerTargets is not null)
        {
            int rawTarget = (int)current.Ai.Ai0;
            if ((uint)rawTarget < byte.MaxValue &&
                context.HostilePlayerTargets.TryGetActiveTargetCenter(
                    new PlayerSlotId(checked((byte)rawTarget)), out float targetCenterX, out float targetCenterY))
            {
                float centerX = current.PositionX + definition.Width * 0.5f;
                float centerY = current.PositionY + definition.Height * 0.5f;
                float dx = targetCenterX - centerX;
                float dy = targetCenterY - centerY;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                if (distance > 0f && float.IsFinite(distance))
                {
                    float desiredX = dx / distance * 30f;
                    float desiredY = dy / distance * 30f;
                    float progress = Math.Clamp((freeDriftUntil - timeLeft) / (freeDriftUntil - homingEndsAt), 0f, 1f);
                    float amount = 0.05f + (0.1f - 0.05f) * progress;
                    float smoothAmount = amount * amount * (3f - 2f * amount);
                    velocityX += (desiredX - velocityX) * smoothAmount;
                    velocityY += (desiredY - velocityY) * smoothAmount;
                }
            }
        }

        next = new VanillaProjectileBehaviorResult(velocityX, velocityY, current.Ai.Ai0);
        return true;
    }


    private static bool TryStepPhantasmalDeathray(
        in ProjectileSnapshot current,
        in VanillaProjectileDefinition definition,
        in VanillaProjectileBehaviorContext context,
        out VanillaProjectileBehaviorResult next)
    {
        // TerrariaServer 1.4.5.8 aiStyle 84, type 455. ai[1] is the live Moon Lord eye/head slot, while
        // Projectile.localAI[0] owns the 180-update beam lifetime and localAI[1] owns tile-scanned beam length.
        int sourceSlot = (int)current.Ai.Ai1;
        if (context.NpcTargets is null ||
            !context.NpcTargets.TryGetActiveNpc(sourceSlot, out NpcSnapshot sourceNpc) ||
            (sourceNpc.TypeIdentity != VanillaNpcIds.MoonLordHead &&
             sourceNpc.TypeIdentity != VanillaNpcIds.MoonLordFreeEye) ||
            !TryResolveNpcCenter(in sourceNpc, out float sourceCenterX, out float sourceCenterY))
        {
            next = new VanillaProjectileBehaviorResult(
                current.VelocityX, current.VelocityY, current.Ai.Ai0, Kill: true,
                LocalAiOverride: context.LocalAi);
            return true;
        }

        if (sourceNpc.TypeIdentity == VanillaNpcIds.MoonLordHead && sourceNpc.Ai.Ai0 == -2f)
        {
            next = new VanillaProjectileBehaviorResult(
                current.VelocityX, current.VelocityY, current.Ai.Ai0, Kill: true,
                LocalAiOverride: context.LocalAi);
            return true;
        }

        float ellipseWidth = sourceNpc.TypeIdentity == VanillaNpcIds.MoonLordHead ? 27f : 30f;
        float ellipseHeight = sourceNpc.TypeIdentity == VanillaNpcIds.MoonLordHead ? 59f : 30f;
        ResolveEllipseOffset(
            sourceNpc.Simulation.LocalAi.Ai0,
            ellipseWidth * sourceNpc.Simulation.LocalAi.Ai1,
            ellipseHeight * sourceNpc.Simulation.LocalAi.Ai1,
            out float offsetX,
            out float offsetY);

        float velocityX = current.VelocityX;
        float velocityY = current.VelocityY;
        float velocityLength = MathF.Sqrt(velocityX * velocityX + velocityY * velocityY);
        if (!(velocityLength > 0f) || !float.IsFinite(velocityLength))
        {
            velocityX = 0f;
            velocityY = -1f;
        }

        float localAi0 = context.LocalAi.Ai0 + 1f;
        if (localAi0 >= 180f)
        {
            next = new VanillaProjectileBehaviorResult(
                velocityX, velocityY, current.Ai.Ai0, Kill: true,
                LocalAiOverride: context.LocalAi with { Ai0 = localAi0 });
            return true;
        }

        float rotation = MathF.Atan2(velocityY, velocityX) + current.Ai.Ai0;
        velocityX = MathF.Cos(rotation);
        velocityY = MathF.Sin(rotation);

        next = new VanillaProjectileBehaviorResult(
            velocityX,
            velocityY,
            current.Ai.Ai0,
            PositionXOverride: sourceCenterX + offsetX - definition.Width * 0.5f,
            PositionYOverride: sourceCenterY + offsetY - definition.Height * 0.5f,
            LocalAiOverride: context.LocalAi with { Ai0 = localAi0 });
        return true;
    }


    private static void ResolveEllipseOffset(
        float angle,
        float ellipseWidth,
        float ellipseHeight,
        out float offsetX,
        out float offsetY)
    {
        if (ellipseWidth == 0f && ellipseHeight == 0f)
        {
            offsetX = 0f;
            offsetY = 0f;
            return;
        }

        float angleX = MathF.Cos(angle);
        float angleY = MathF.Sin(angle);
        float sizesLength = MathF.Sqrt(ellipseWidth * ellipseWidth + ellipseHeight * ellipseHeight);
        if (!(sizesLength > 0f) || !float.IsFinite(sizesLength))
        {
            offsetX = 0f;
            offsetY = 0f;
            return;
        }

        float normalizedSizeX = ellipseWidth / sizesLength;
        float normalizedSizeY = ellipseHeight / sizesLength;
        if (normalizedSizeX == 0f || normalizedSizeY == 0f)
        {
            offsetX = 0f;
            offsetY = 0f;
            return;
        }

        angleX /= normalizedSizeX;
        angleY /= normalizedSizeY;
        float correctedLength = MathF.Sqrt(angleX * angleX + angleY * angleY);
        if (!(correctedLength > 0f) || !float.IsFinite(correctedLength))
        {
            offsetX = 0f;
            offsetY = 0f;
            return;
        }

        angleX /= correctedLength;
        angleY /= correctedLength;
        offsetX = angleX * ellipseWidth * 0.5f;
        offsetY = angleY * ellipseHeight * 0.5f;
    }


    private static bool TryStepPhantasmalEye(
        in ProjectileSnapshot current,
        in VanillaProjectileDefinition definition,
        in VanillaProjectileBehaviorContext context,
        out VanillaProjectileBehaviorResult next)
    {
        float velocityX = current.VelocityX;
        float velocityY = current.VelocityY;
        float ai0 = current.Ai.Ai0;
        float ai1 = current.Ai.Ai1;
        float ai2 = current.Ai.Ai2;
        float localAi0 = context.LocalAi.Ai0;

        if (ai0 == 0f || ai0 == 1f)
        {
            localAi0 += 1f;
            float gate = ai0 == 0f ? 45f : 90f;
            if (localAi0 >= gate)
            {
                localAi0 = 0f;
                if (ai0 == 0f)
                {
                    ai0 = 1f;
                    ai1 = -ai1;
                }
                else
                {
                    if (!TryFindClosestPlayer(
                            in current, in definition, context.PlayerSnapshots,
                            out PlayerSlotId targetSlot, out _, out _))
                    {
                        next = default;
                        return false;
                    }
                    ai0 = 2f;
                    ai1 = targetSlot.Value;
                }
            }

            // Source assigns only the rotated X component and retains the pre-rotation Y component.
            float rotatedX = velocityX * MathF.Cos(ai1) - velocityY * MathF.Sin(ai1);
            velocityX = Math.Clamp(rotatedX, -6f, 6f);
            velocityY -= 0.08f;
            if (velocityY > 0f)
                velocityY -= 0.2f;
            if (velocityY < -7f)
                velocityY = -7f;

            next = new VanillaProjectileBehaviorResult(
                velocityX, velocityY, ai0, Ai1Override: ai1, Ai2Override: ai2,
                LocalAiOverride: context.LocalAi with { Ai0 = localAi0 });
            return true;
        }

        if (ai0 != 2f || context.PlayerSnapshots is null ||
            !float.IsFinite(ai1) || ai1 < 0f || ai1 >= byte.MaxValue ||
            !context.PlayerSnapshots.TryGetPlayer(new PlayerSlotId((byte)ai1), out PlayerStateSnapshot target))
        {
            next = default;
            return false;
        }

        ai2 += 1f;
        float centerX = current.PositionX + definition.Width * 0.5f;
        float centerY = current.PositionY + definition.Height * 0.5f;
        float targetX = target.PositionX + 10f;
        float targetY = target.PositionY + 21f;
        float dx = targetX - centerX;
        float dy = targetY - centerY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance < 30f)
        {
            next = new VanillaProjectileBehaviorResult(
                velocityX, velocityY, ai0, Ai1Override: ai1, Ai2Override: ai2, Kill: true,
                LocalAiOverride: context.LocalAi with { Ai0 = localAi0 });
            return true;
        }

        if (!(distance > 0f) || !float.IsFinite(distance))
        {
            next = default;
            return false;
        }

        float desiredX = dx / distance * 14f;
        float desiredY = dy / distance * 14f;
        desiredX = velocityX * 0.4f + desiredX * 0.6f;
        desiredY = velocityY * 0.4f + desiredY * 0.6f;
        if (desiredY < 6f)
            desiredY = 6f;

        float acceleration = 0.4f * Remap(ai2, 0f, 90f, 1f, 0f);
        ApproachPhantasmalComponent(ref velocityX, desiredX, acceleration);
        ApproachPhantasmalComponent(ref velocityY, desiredY, acceleration);

        next = new VanillaProjectileBehaviorResult(
            velocityX, velocityY, ai0, Ai1Override: ai1, Ai2Override: ai2,
            LocalAiOverride: context.LocalAi with { Ai0 = localAi0 });
        return true;
    }


    private static void ApproachPhantasmalComponent(ref float value, float target, float acceleration)
    {
        if (value < target)
        {
            value += acceleration;
            if (value < 0f && target > 0f)
                value += acceleration;
        }
        else if (value > target)
        {
            value -= acceleration;
            if (value > 0f && target < 0f)
                value -= acceleration;
        }
    }


    private static bool TryResolveNpcCenter(in NpcSnapshot npc, out float centerX, out float centerY)
    {
        centerX = 0f;
        centerY = 0f;
        if (!VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
        {
            return false;
        }
        centerX = npc.PositionX + hitbox.Width * 0.5f;
        centerY = npc.PositionY + hitbox.Height * 0.5f;
        return float.IsFinite(centerX) && float.IsFinite(centerY);
    }


    private static bool TryStepCultistFireball(
        in ProjectileSnapshot current,
        in VanillaProjectileDefinition definition,
        VanillaProjectilePlayerTargetResolver? targets,
        out VanillaProjectileBehaviorResult next)
    {
        float velocityX = current.VelocityX;
        float velocityY = current.VelocityY;
        float ai0 = current.Ai.Ai0;
        float ai1 = current.Ai.Ai1;

        if (ai1 == 0f)
        {
            ai1 = 1f;
        }
        else if (ai1 == 1f)
        {
            if (targets is null)
            {
                next = default;
                return false;
            }

            if (targets.TryFindClosestTargetWithLineOfSight(
                    in current, in definition, 2000f, out PlayerSlotId targetSlot,
                    out _, out _, out float distance))
            {
                if (distance < 20f)
                {
                    next = new VanillaProjectileBehaviorResult(velocityX, velocityY, ai0, Ai1Override: ai1, Kill: true);
                    return true;
                }

                ai0 = targetSlot.Value;
                ai1 = 21f;
            }
        }
        else if (ai1 > 20f && ai1 < 200f)
        {
            ai1 += 1f;
            int rawTarget = (int)ai0;
            if ((uint)rawTarget >= byte.MaxValue || targets is null ||
                !targets.TryGetActiveTargetCenter(new PlayerSlotId(checked((byte)rawTarget)), out float targetX, out float targetY))
            {
                ai1 = 1f;
                ai0 = 0f;
            }
            else
            {
                float centerX = current.PositionX + definition.Width * 0.5f;
                float centerY = current.PositionY + definition.Height * 0.5f;
                float dx = targetX - centerX;
                float dy = targetY - centerY;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                if (distance < 20f)
                {
                    next = new VanillaProjectileBehaviorResult(velocityX, velocityY, ai0, Ai1Override: ai1, Kill: true);
                    return true;
                }

                float speed = MathF.Sqrt(velocityX * velocityX + velocityY * velocityY);
                if (speed > 0f && float.IsFinite(speed) && distance > 0f && float.IsFinite(distance))
                {
                    float currentAngle = MathF.Atan2(velocityY, velocityX);
                    float targetAngle = MathF.Atan2(dy, dx);
                    float amount = current.Type == VanillaProjectileIds.CultistBossFireBall ? 0.008f : 0.01f;
                    float nextAngle = AngleLerp(currentAngle, targetAngle, amount);
                    velocityX = MathF.Cos(nextAngle) * speed;
                    velocityY = MathF.Sin(nextAngle) * speed;
                }
            }
        }

        if (ai1 >= 1f && ai1 < 20f)
        {
            ai1 += 1f;
            if (ai1 == 20f)
                ai1 = 1f;
        }

        next = new VanillaProjectileBehaviorResult(velocityX, velocityY, ai0, Ai1Override: ai1);
        return true;
    }
}
