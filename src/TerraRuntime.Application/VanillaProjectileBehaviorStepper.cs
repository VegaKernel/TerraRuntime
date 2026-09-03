using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

/// <summary>
/// Immutable world-independent inputs consumed by vanilla projectile behavior. Weather is supplied explicitly
/// by the runtime so AI code does not reach into world/global state.
/// </summary>
internal readonly record struct VanillaProjectileBehaviorContext(
    bool WindPhysics,
    float WindSpeedCurrent,
    float WindPhysicsStrength,
    IRuntimePlayerSlotSnapshotLookup? PlayerSnapshots = null);

/// <summary>State produced by one supported vanilla projectile AI-family step before world motion/collision.</summary>
internal readonly record struct VanillaProjectileBehaviorResult(
    float VelocityX,
    float VelocityY,
    float Ai0,
    float? Ai1Override = null,
    float? PositionXOverride = null,
    float? PositionYOverride = null,
    bool Kill = false,
    bool? TileCollideOverride = null);

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 projectile behavior that is independent of tile/world queries.
/// Runtime behavior-family selection is explicit in <see cref="VanillaProjectileBehaviorProfileCatalog"/> so
/// equal aiStyle values never silently opt unrelated projectile types into the same implementation.
/// World collision, liquids, post-AI wind and lifetime/kill handling remain owned by the world-motion layer.
/// </summary>
internal static class VanillaProjectileBehaviorStepper
{
    private const float MaximumThrownFallSpeed = 32f;
    private const float MaximumArrowFallSpeed = 16f;

    public static bool TryStep(
        in ProjectileSnapshot current,
        in VanillaProjectileDefinition definition,
        in VanillaProjectileBehaviorContext context,
        out VanillaProjectileBehaviorResult next)
    {
        if (!VanillaProjectileBehaviorProfileCatalog.TryGet(current.Type, out VanillaProjectileBehaviorProfile profile))
        {
            next = default;
            return false;
        }

        return TryStep(in current, in definition, in profile, in context, out next);
    }

    public static bool TryStep(
        in ProjectileSnapshot current,
        in VanillaProjectileDefinition definition,
        in VanillaProjectileBehaviorProfile profile,
        in VanillaProjectileBehaviorContext context,
        out VanillaProjectileBehaviorResult next)
    {
        if (!profile.BehaviorImplemented || definition.AiStyle != profile.ExpectedAiStyle)
        {
            next = default;
            return false;
        }

        if (profile.RejectServerOwned && VanillaProjectileOwnership.IsServerOwned(current.Spawner))
        {
            next = default;
            return false;
        }

        if (profile.RequiresDefaultAi2 && current.Ai.Ai2 != 0f)
        {
            next = default;
            return false;
        }

        float velocityX = current.VelocityX;
        float velocityY = current.VelocityY;
        float ai0 = current.Ai.Ai0;
        float? ai1Override = null;

        switch (profile.Family)
        {
            case VanillaProjectileBehaviorFamily.Thrown:
                // TerrariaServer 1.4.5.8 AI(), aiStyle == 2.
                if (context.WindPhysics)
                    velocityX += context.WindSpeedCurrent * context.WindPhysicsStrength;

                ai0 += 1f;
                if (ai0 >= 20f)
                {
                    velocityY += 0.4f;
                    velocityX *= 0.97f;
                }

                if (velocityY > MaximumThrownFallSpeed)
                    velocityY = MaximumThrownFallSpeed;
                break;

            case VanillaProjectileBehaviorFamily.BasicArrow:
                // TerrariaServer 1.4.5.8 Projectile.AI_001(), source-backed basic aiStyle-1 path.
                ai0 += 1f;
                if (ai0 >= 15f)
                {
                    ai0 = 15f;
                    velocityY += 0.1f;
                }

                if (velocityY > MaximumArrowFallSpeed)
                    velocityY = MaximumArrowFallSpeed;
                break;

            case VanillaProjectileBehaviorFamily.Boomerang:
                return TryStepEnchantedBoomerang(in current, in definition, context.PlayerSnapshots, out next);

            case VanillaProjectileBehaviorFamily.FallingStar:
                // aiStyle 5 (Falling Star / Star Cannon Star) has no gameplay velocity mutation. Its only
                // server-relevant AI state in the admitted slice is the ai[1] solid-collision arming transition,
                // which is resolved by the world-motion layer because it requires a tile query.
                next = new VanillaProjectileBehaviorResult(velocityX, velocityY, ai0);
                return true;

            case VanillaProjectileBehaviorFamily.SuperStar:
                // AI_151_SuperStar mutates alpha/rotation/sound/gore/dust only. None of those fields affect
                // authoritative movement, collision, damage, lifetime or spawn ordering, so gameplay is a no-op.
                next = new VanillaProjectileBehaviorResult(velocityX, velocityY, ai0);
                return true;

            case VanillaProjectileBehaviorFamily.SuperStarSlash:
                // Type 729 is spawned by SummonSuperStarSlash with ai[0] == 0. AI_152 then changes only
                // alpha/rotation/sound for this exact type; tileCollide=false and extraUpdates are SetDefaults facts.
                // Any nonzero ai[0] would enter the shared AI_152 curved-flight branch and remains fail-closed here.
                if (ai0 != 0f)
                {
                    next = default;
                    return false;
                }
                next = new VanillaProjectileBehaviorResult(velocityX, velocityY, ai0, TileCollideOverride: false);
                return true;

            case VanillaProjectileBehaviorFamily.SkeletronSkull:
                float ai1 = current.Ai.Ai1 + 1f;
                ai1Override = ai1;
                float speed = MathF.Sqrt(velocityX * velocityX + velocityY * velocityY);
                if (ai1 > 30f && ai1 < 110f && speed > 0f &&
                    TryFindClosestPlayer(in current, in definition, context.PlayerSnapshots, out float targetX, out float targetY))
                {
                    float centerX = current.PositionX + definition.Width * 0.5f;
                    float centerY = current.PositionY + definition.Height * 0.5f;
                    float dx = targetX - centerX;
                    float dy = targetY - centerY;
                    float distance = MathF.Sqrt(dx * dx + dy * dy);
                    if (distance > 0f)
                    {
                        float desiredX = dx / distance * speed;
                        float desiredY = dy / distance * speed;
                        velocityX = (velocityX * 24f + desiredX) / 25f;
                        velocityY = (velocityY * 24f + desiredY) / 25f;
                        float blendedSpeed = MathF.Sqrt(velocityX * velocityX + velocityY * velocityY);
                        if (blendedSpeed > 0f)
                        {
                            velocityX = velocityX / blendedSpeed * speed;
                            velocityY = velocityY / blendedSpeed * speed;
                        }
                    }
                }

                if (MathF.Sqrt(velocityX * velocityX + velocityY * velocityY) < 18f)
                {
                    velocityX *= 1.02f;
                    velocityY *= 1.02f;
                }
                break;

            case VanillaProjectileBehaviorFamily.DeerclopsIceSpike:
            {
                // Projectile.AI_157 uses ai[0] as the entire authoritative lifetime gate for type 961.
                // Alpha/scale/dust are presentation-only and deliberately remain outside server simulation.
                bool kill = ai0 >= 20f;
                if (!kill)
                    ai0 += 1f;
                next = new VanillaProjectileBehaviorResult(velocityX, velocityY, ai0, Kill: kill);
                return true;
            }

            case VanillaProjectileBehaviorFamily.DeerclopsRubble:
                // Type 962 is an aiStyle-1 exception: the common counter advances, then gravity begins at 5.
                ai0 += 1f;
                if (ai0 >= 5f)
                    velocityY += 0.15f;
                break;

            case VanillaProjectileBehaviorFamily.DeerclopsShadowHand:
                return TryStepDeerclopsShadowHand(in current, in definition, out next);

            default:
                next = default;
                return false;
        }

        next = new VanillaProjectileBehaviorResult(velocityX, velocityY, ai0, ai1Override);
        return true;
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

    private static bool TryStepDeerclopsShadowHand(
        in ProjectileSnapshot current,
        in VanillaProjectileDefinition definition,
        out VanillaProjectileBehaviorResult next)
    {
        float ai0 = current.Ai.Ai0;
        if (!TryResolveShadowHandPhase(ai0, out int variation, out int fakeCounter, out int counterMax))
        {
            next = new VanillaProjectileBehaviorResult(
                current.VelocityX, current.VelocityY, ai0, Kill: true);
            return true;
        }

        // AI_187 kills before applying the last movement step of each variation.
        if (fakeCounter >= counterMax - 1)
        {
            next = new VanillaProjectileBehaviorResult(
                current.VelocityX, current.VelocityY, ai0, Kill: true);
            return true;
        }

        float velocityX = current.VelocityX;
        float velocityY = current.VelocityY;
        float? positionX = null;
        float? positionY = null;
        float fromValue = fakeCounter / (float)counterMax;

        switch (variation)
        {
            case 0:
                velocityX *= 0.98f;
                velocityY *= 0.98f;
                break;

            case 1:
            {
                int direction = velocityX > 0f ? 1 : -1;
                if (MathF.Sqrt(velocityX * velocityX + velocityY * velocityY) > 0.1f)
                {
                    velocityX *= 0.95f;
                    velocityY *= 0.95f;
                }

                // Projectile.rotation is local-only and is not part of packet 27. For this source variation it is
                // fully derivable from the phase counter because SetDefaults starts it at zero and every delta is
                // a pure function of elapsed phase time and horizontal direction. Reconstructing it here avoids
                // smuggling a presentation-only rotation field into synchronized projectile AI.
                float rotationBefore = ComputeShadowOrbitRotation(fakeCounter, direction);
                float delta = ComputeShadowOrbitDelta(fromValue) * -direction;
                float rotationAfter = WrapAngle(rotationBefore + delta);
                float radius = 70f * direction;
                float centerX = current.PositionX + definition.Width * 0.5f;
                float centerY = current.PositionY + definition.Height * 0.5f;
                float anchorX = centerX - MathF.Cos(rotationBefore) * radius;
                float anchorY = centerY - MathF.Sin(rotationBefore) * radius;
                float nextCenterX = anchorX + MathF.Cos(rotationAfter) * radius;
                float nextCenterY = anchorY + MathF.Sin(rotationAfter) * radius;
                positionX = nextCenterX - definition.Width * 0.5f;
                positionY = nextCenterY - definition.Height * 0.5f;
                break;
            }

            case 2:
            {
                float speed =
                    Remap(fromValue, 0f, 0.4f, 1f, 0f) * 2f +
                    Remap(fromValue, 0.3f, 0.4f, 0f, 1f) *
                    Remap(fromValue, 0.4f, 1f, 1f, 0f) * 8f +
                    0.01f;
                velocityX = MathF.Cos(current.Ai.Ai1) * speed;
                velocityY = MathF.Sin(current.Ai.Ai1) * speed;
                break;
            }

            case 3:
                Rotate(ref velocityX, ref velocityY, current.Ai.Ai1);
                break;
        }

        next = new VanillaProjectileBehaviorResult(
            velocityX,
            velocityY,
            ai0 + 1f,
            PositionXOverride: positionX,
            PositionYOverride: positionY);
        return true;
    }

    private static bool TryResolveShadowHandPhase(
        float ai0,
        out int variation,
        out int fakeCounter,
        out int counterMax)
    {
        int counter = (int)ai0;
        if (counter is >= 0 and < 180)
        {
            variation = 0;
            fakeCounter = counter;
            counterMax = 180;
            return true;
        }
        if (counter is >= 180 and < 300)
        {
            variation = 1;
            fakeCounter = counter - 180;
            counterMax = 120;
            return true;
        }
        if (counter is >= 300 and < 390)
        {
            variation = 2;
            fakeCounter = counter - 300;
            counterMax = 90;
            return true;
        }
        if (counter is >= 390 and < 480)
        {
            variation = 3;
            fakeCounter = counter - 390;
            counterMax = 90;
            return true;
        }

        variation = default;
        fakeCounter = default;
        counterMax = default;
        return false;
    }

    private static float ComputeShadowOrbitRotation(int elapsedTicks, int direction)
    {
        float rotation = 0f;
        for (int tick = 0; tick < elapsedTicks; tick++)
        {
            float progress = tick / 120f;
            rotation = WrapAngle(rotation + ComputeShadowOrbitDelta(progress) * -direction);
        }
        return rotation;
    }

    private static float ComputeShadowOrbitDelta(float progress)
    {
        float forward = Remap(progress, 0.3f, 0.5f, 0f, 1f) *
                        Remap(progress, 0.45f, 0.5f, 1f, 0f);
        float reverse = Remap(progress, 0.5f, 0.55f, 0f, 1f) *
                        Remap(progress, 0.5f, 1f, 1f, 0f);
        return forward * MathF.PI / 60f - reverse * MathF.PI * 8f / 60f;
    }

    private static float Remap(float value, float fromMin, float fromMax, float toMin, float toMax)
    {
        float amount = Math.Clamp((value - fromMin) / (fromMax - fromMin), 0f, 1f);
        return toMin + (toMax - toMin) * amount;
    }

    private static float WrapAngle(float angle)
    {
        while (angle <= -MathF.PI)
            angle += MathF.PI * 2f;
        while (angle > MathF.PI)
            angle -= MathF.PI * 2f;
        return angle;
    }

    private static void Rotate(ref float x, ref float y, float radians)
    {
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        float nextX = x * cos - y * sin;
        y = x * sin + y * cos;
        x = nextX;
    }

    private static bool TryFindClosestPlayer(
        in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition definition,
        IRuntimePlayerSlotSnapshotLookup? players,
        out float centerX,
        out float centerY)
    {
        centerX = 0f;
        centerY = 0f;
        if (players is null)
            return false;

        float projectileCenterX = projectile.PositionX + definition.Width * 0.5f;
        float projectileCenterY = projectile.PositionY + definition.Height * 0.5f;
        float bestDistanceSquared = float.PositiveInfinity;
        bool found = false;
        for (int rawSlot = 0; rawSlot < byte.MaxValue; rawSlot++)
        {
            var slot = new PlayerSlotId(checked((byte)rawSlot));
            if (!players.TryGetPlayer(slot, out PlayerStateSnapshot player) || player.IsDead)
                continue;

            float playerCenterX = player.PositionX + 10f;
            float playerCenterY = player.PositionY + 21f;
            float dx = playerCenterX - projectileCenterX;
            float dy = playerCenterY - projectileCenterY;
            float distanceSquared = dx * dx + dy * dy;
            if (distanceSquared >= bestDistanceSquared)
                continue;

            bestDistanceSquared = distanceSquared;
            centerX = playerCenterX;
            centerY = playerCenterY;
            found = true;
        }
        return found;
    }
}
