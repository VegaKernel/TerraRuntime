using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

internal static partial class VanillaProjectileBehaviorStepper
{
    private static bool TryStepControlledMagicMissile(
        in ProjectileSnapshot current,
        in VanillaProjectileDefinition definition,
        IVanillaProjectileNpcTargetResolver? npcTargets,
        out VanillaProjectileBehaviorResult next)
    {
        const float maximumSpeed = 32f;
        const float autoTargetRange = 800f;
        float velocityX = current.VelocityX;
        float velocityY = current.VelocityY;
        float ai0 = current.Ai.Ai0;
        float ai1 = current.Ai.Ai1;
        bool released = ai0 is -1f or -2f;
        float? targetX = null;
        float? targetY = null;
        float steeringAmount = 1f;

        if (ai0 > 0f && ai1 > 0f)
        {
            targetX = ai0;
            targetY = ai1;
        }
        else if (released)
        {
            if (ai1 >= 0f)
            {
                int targetSlot = (int)ai1;
                if (npcTargets is not null &&
                    npcTargets.TryGetChaseableTargetCenter(targetSlot, out float centerX, out float centerY))
                {
                    targetX = centerX;
                    targetY = centerY;
                    steeringAmount = ResolveReleasedTargetSteeringAmount(
                        in current, in definition, centerX, centerY);
                }
                else
                {
                    ai1 = -1f;
                }
            }

            if (ai1 < 0f && npcTargets is not null &&
                npcTargets.TryFindClosestTargetWithLineOfSight(
                    in current,
                    in definition,
                    autoTargetRange,
                    out int acquiredTargetSlot,
                    out float acquiredCenterX,
                    out float acquiredCenterY))
            {
                ai1 = acquiredTargetSlot;
                targetX = acquiredCenterX;
                targetY = acquiredCenterY;
                steeringAmount = ResolveReleasedTargetSteeringAmount(
                    in current, in definition, acquiredCenterX, acquiredCenterY);
            }
        }

        if (targetX.HasValue && targetY.HasValue)
        {
            float centerX = current.PositionX + definition.Width * 0.5f;
            float centerY = current.PositionY + definition.Height * 0.5f;
            float dx = targetX.Value - centerX;
            float dy = targetY.Value - centerY;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance >= 64f)
            {
                if (!(distance > 0f) || !float.IsFinite(distance))
                {
                    next = new VanillaProjectileBehaviorResult(velocityX, velocityY, ai0, Kill: true);
                    return true;
                }
                float desiredSpeed = MathF.Min(maximumSpeed, distance);
                float desiredX = dx / distance * desiredSpeed;
                float desiredY = dy / distance * desiredSpeed;
                velocityX += (desiredX - velocityX) * steeringAmount;
                velocityY += (desiredY - velocityY) * steeringAmount;
            }
            else
            {
                velocityX = velocityX * 0.3f + dx * 0.3f;
                velocityY = velocityY * 0.3f + dy * 0.3f;
            }

            next = new VanillaProjectileBehaviorResult(
                velocityX, velocityY, ai0, Ai1Override: ai1, MinimumTimeLeftOverride: 60);
            return true;
        }

        if (released && ai1 < 0f)
        {
            float length = MathF.Sqrt(velocityX * velocityX + velocityY * velocityY);
            float desiredX = 0f;
            float desiredY = maximumSpeed;
            if (length > 0f && float.IsFinite(length))
            {
                desiredX = velocityX / length * maximumSpeed;
                desiredY = velocityY / length * maximumSpeed;
            }
            MoveTowards(ref velocityX, ref velocityY, desiredX, desiredY, 4f);
            next = new VanillaProjectileBehaviorResult(
                velocityX, velocityY, ai0, Ai1Override: ai1, TimeLeftOverride: 300);
            return true;
        }

        next = new VanillaProjectileBehaviorResult(velocityX, velocityY, ai0, Ai1Override: ai1);
        return true;
    }


    private static float ResolveReleasedTargetSteeringAmount(
        in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition definition,
        float targetCenterX,
        float targetCenterY)
    {
        float centerX = projectile.PositionX + definition.Width * 0.5f;
        float centerY = projectile.PositionY + definition.Height * 0.5f;
        float dx = targetCenterX - centerX;
        float dy = targetCenterY - centerY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        float envelope = GetLerpValue(0f, 100f, distance, clamped: true) *
            GetLerpValue(600f, 400f, distance, clamped: true);
        return 0.2f * GetLerpValue(200f, 20f, 1f - envelope, clamped: true);
    }


    private static float GetLerpValue(float from, float to, float value, bool clamped)
    {
        if (clamped)
        {
            if (from < to)
            {
                if (value < from)
                    return 0f;
                if (value > to)
                    return 1f;
            }
            else
            {
                if (value < to)
                    return 1f;
                if (value > from)
                    return 0f;
            }
        }
        return (value - from) / (to - from);
    }


    private static void MoveTowards(ref float x, ref float y, float targetX, float targetY, float maxDistanceDelta)
    {
        float dx = targetX - x;
        float dy = targetY - y;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (!(distance > maxDistanceDelta) || !float.IsFinite(distance))
        {
            x = targetX;
            y = targetY;
            return;
        }
        float scale = maxDistanceDelta / distance;
        x += dx * scale;
        y += dy * scale;
    }


    private static bool TryStepEnchantedBoomerang(
        in ProjectileSnapshot current,
        in VanillaProjectileDefinition definition,
        IRuntimePlayerSlotSnapshotLookup? players,
        out VanillaProjectileBehaviorResult next)
    {
        if (current.Type != VanillaProjectileIds.EnchantedBoomerang ||
            players is null ||
            !VanillaProjectileOwnership.IsPlayerOwned(current.Spawner) ||
            !players.TryGetPlayer(new PlayerSlotId(current.Spawner), out PlayerStateSnapshot owner) ||
            !owner.Player.IsAssigned ||
            owner.Player.Slot.Value != current.Spawner ||
            owner.IsDead)
        {
            next = default;
            return false;
        }

        float ai0 = current.Ai.Ai0;
        float ai1 = current.Ai.Ai1;
        float velocityX = current.VelocityX;
        float velocityY = current.VelocityY;

        // Projectile.AI_003_Boomerang, type 6. While outbound ai[1] counts to 30. The tick that flips
        // ai[0] to the return phase still uses outbound tile collision; homing begins on the next update.
        if (ai0 == 0f)
        {
            ai1 += 1f;
            if (ai1 >= 30f)
            {
                ai0 = 1f;
                ai1 = 0f;
            }

            next = new VanillaProjectileBehaviorResult(
                velocityX,
                velocityY,
                ai0,
                Ai1Override: ai1);
            return true;
        }

        // The generic return path disables tile collision and accelerates toward the owning player. Vanilla's
        // melee-speed scaling is intentionally not guessed here; until authoritative meleeSpeed exists the verified
        // baseline is the ResetEffects value of 1, yielding speed 9 and acceleration 0.4 for type 6.
        const float returnSpeed = 9f;
        const float returnAcceleration = 0.4f;
        float centerX = current.PositionX + definition.Width * 0.5f;
        float centerY = current.PositionY + definition.Height * 0.5f;
        float ownerCenterX = owner.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float ownerCenterY = owner.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float dx = ownerCenterX - centerX;
        float dy = ownerCenterY - centerY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);

        if (distance > 3000f)
        {
            next = new VanillaProjectileBehaviorResult(
                velocityX, velocityY, ai0, Ai1Override: ai1, Kill: true, TileCollideOverride: false);
            return true;
        }

        if (distance > 0f)
        {
            float scale = returnSpeed / distance;
            float desiredX = dx * scale;
            float desiredY = dy * scale;
            AccelerateAxis(ref velocityX, desiredX, returnAcceleration);
            AccelerateAxis(ref velocityY, desiredY, returnAcceleration);
        }

        bool intersectsOwner =
            current.PositionX < owner.PositionX + PlayerAuthority.VanillaBasePlayerWidth &&
            current.PositionX + definition.Width > owner.PositionX &&
            current.PositionY < owner.PositionY + PlayerAuthority.VanillaBasePlayerHeight &&
            current.PositionY + definition.Height > owner.PositionY;

        next = new VanillaProjectileBehaviorResult(
            velocityX,
            velocityY,
            ai0,
            Ai1Override: ai1,
            Kill: intersectsOwner,
            TileCollideOverride: false);
        return true;
    }


    private static void AccelerateAxis(ref float velocity, float desired, float acceleration)
    {
        if (velocity < desired)
        {
            velocity += acceleration;
            if (velocity < 0f && desired > 0f)
                velocity += acceleration;
        }
        else if (velocity > desired)
        {
            velocity -= acceleration;
            if (velocity > 0f && desired < 0f)
                velocity -= acceleration;
        }
    }
}
