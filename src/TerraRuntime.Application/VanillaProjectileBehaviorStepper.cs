using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime;

/// <summary>
/// Immutable world-independent inputs consumed by vanilla projectile behavior. Weather is supplied explicitly
/// by the runtime so AI code does not reach into world/global state.
/// </summary>
internal interface IVanillaProjectileNpcTargetResolver
{
    bool TryFindClosestTargetWithLineOfSight(
        in ProjectileSnapshot projectile,
        in VanillaProjectileDefinition projectileDefinition,
        float maxRange,
        out int npcSlot,
        out float targetCenterX,
        out float targetCenterY);

    bool TryGetChaseableTargetCenter(int npcSlot, out float targetCenterX, out float targetCenterY);
}

internal readonly record struct VanillaProjectileBehaviorContext(
    bool WindPhysics,
    float WindSpeedCurrent,
    float WindPhysicsStrength,
    IRuntimePlayerSlotSnapshotLookup? PlayerSnapshots = null,
    bool ExpertMode = false,
    IVanillaProjectileNpcTargetResolver? NpcTargets = null,
    VanillaProjectilePlayerTargetResolver? HostilePlayerTargets = null,
    int CurrentTimeLeft = int.MaxValue);

/// <summary>State produced by one supported vanilla projectile AI-family step before world motion/collision.</summary>
internal readonly record struct VanillaProjectileBehaviorResult(
    float VelocityX,
    float VelocityY,
    float Ai0,
    float? Ai1Override = null,
    float? PositionXOverride = null,
    float? PositionYOverride = null,
    bool Kill = false,
    bool? TileCollideOverride = null,
    int? TimeLeftOverride = null,
    int? MinimumTimeLeftOverride = null);

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

            case VanillaProjectileBehaviorFamily.HostileStraightArrow:
                // AI_001 no-gravity switch: WoF/Probe/Retinazer/Golem hostile beams do not advance ai[0]
                // and therefore never enter the common arrow gravity branch. Their first AI step only flips ai[1]
                // for a presentation sound, which still belongs to synchronized projectile AI state.
                if (current.Ai.Ai1 == 0f)
                    ai1Override = 1f;
                break;

            case VanillaProjectileBehaviorFamily.PlanteraSeed:
            {
                // Types 275/276 increment ai[0] through the common AI_001 gate but replace ordinary arrow gravity
                // with a much later 0.025f fall. Expert mode additionally steers toward Player.FindClosest,
                // disables tile collision and caps the remaining lifetime at 180 before Update decrements it.
                ai0 += 1f;
                if (current.Ai.Ai1 == 0f)
                    ai1Override = 1f;
                if (ai0 >= 35f)
                {
                    ai0 = 35f;
                    velocityY += 0.025f;
                }

                bool? tileCollideOverride = null;
                int? timeLeftOverride = null;
                if (context.ExpertMode)
                {
                    if (!TryFindClosestPlayer(in current, in definition, context.PlayerSnapshots, out float planteraTargetX, out float planteraTargetY))
                    {
                        next = default;
                        return false;
                    }

                    float centerX = current.PositionX + definition.Width * 0.5f;
                    float centerY = current.PositionY + definition.Height * 0.5f;
                    float dx = planteraTargetX - centerX;
                    float dy = planteraTargetY - centerY;
                    float distance = MathF.Sqrt(dx * dx + dy * dy);
                    if (distance > 0f)
                    {
                        const float desiredSpeed = 18f;
                        float desiredX = dx / distance * desiredSpeed;
                        float desiredY = dy / distance * desiredSpeed;
                        velocityX = (velocityX * 69f + desiredX) / 70f;
                        velocityY = (velocityY * 69f + desiredY) / 70f;

                        float planteraSpeed = MathF.Sqrt(velocityX * velocityX + velocityY * velocityY);
                        if (planteraSpeed is > 0f and < 14f)
                        {
                            velocityX = velocityX / planteraSpeed * 14f;
                            velocityY = velocityY / planteraSpeed * 14f;
                        }
                    }

                    tileCollideOverride = false;
                    timeLeftOverride = 180;
                }

                next = new VanillaProjectileBehaviorResult(
                    velocityX,
                    velocityY,
                    ai0,
                    ai1Override,
                    TileCollideOverride: tileCollideOverride,
                    TimeLeftOverride: timeLeftOverride);
                return true;
            }

            case VanillaProjectileBehaviorFamily.SpazmatismCursedFlame:
                // AI_008 type 96 is an explicit no-counter exception, so the common aiStyle-8 gravity gate
                // never opens. Presentation-only dust/sound/rotation are intentionally omitted.
                break;

            case VanillaProjectileBehaviorFamily.SpazmatismEyeFire:
                // aiStyle 23 increments ai[0] every update and caps timeLeft at 60 before the normal lifetime
                // decrement. Type 101 has no gameplay movement mutation in this AI family.
                ai0 += 1f;
                next = new VanillaProjectileBehaviorResult(
                    velocityX,
                    velocityY,
                    ai0,
                    TimeLeftOverride: Math.Min(context.CurrentTimeLeft, 60));
                return true;

            case VanillaProjectileBehaviorFamily.HostileStraightNoGravity:
                // AI_001 types 462/593 live in the no-common-counter switch. Their ai[1] transition is only
                // the synchronized one-shot presentation gate; velocity remains ballistic and gravity-free.
                if (current.Ai.Ai1 == 0f)
                    ai1Override = 1f;
                break;

            case VanillaProjectileBehaviorFamily.CultistFireball:
                return TryStepCultistFireball(
                    in current,
                    in definition,
                    context.HostilePlayerTargets,
                    out next);

            case VanillaProjectileBehaviorFamily.QueenSlimeGel:
                // AI_001 type 926 uses the early-fall family: ai[0] advances to five, then clamps and adds
                // 0.15 vertical velocity per AI update. ai[1] is the one-shot synchronized sound gate.
                ai0 += 1f;
                if (ai0 >= 5f)
                {
                    ai0 = 5f;
                    velocityY += 0.15f;
                }
                if (current.Ai.Ai1 == 0f)
                    ai1Override = 1f;
                break;

            case VanillaProjectileBehaviorFamily.GolemFireball:
                // AI_008 type 258 is an explicit no-gravity/no-counter exception. Its authoritative ai[0]
                // counter is collision-owned and advances only when the fireball actually hits tiles.
                break;

            case VanillaProjectileBehaviorFamily.Bomb:
                // TerrariaServer 1.4.5.8 Projectile.AI_016_Bombs for launcher projectile types 133..144.
                // Presentation-only rotation/dust/sound are intentionally omitted. Tile impact bounce/arming is
                // resolved by VanillaProjectileWorldMotionResolver because it depends on collision results.
                ai0 += 1f;
                switch ((current.Type.Value - 133) % 3)
                {
                    case 0: // Grenade I..IV.
                        if (ai0 > 15f)
                        {
                            if (velocityY == 0f)
                                velocityX *= 0.95f;
                            velocityY += 0.2f;
                        }
                        break;
                    case 1: // Rocket I..IV: straight flight until impact.
                        break;
                    case 2: // Proximity Mine I..IV.
                        velocityY += 0.2f;
                        velocityX *= 0.97f;
                        velocityY *= 0.97f;
                        if (velocityX is > -0.1f and < 0.1f)
                            velocityX = 0f;
                        if (velocityY is > -0.1f and < 0.1f)
                            velocityY = 0f;
                        break;
                }
                break;

            case VanillaProjectileBehaviorFamily.ControlledMagicMissile:
                return TryStepControlledMagicMissile(in current, in definition, context.NpcTargets, out next);

            case VanillaProjectileBehaviorFamily.Boomerang:
                return TryStepEnchantedBoomerang(in current, in definition, context.PlayerSnapshots, out next);

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

    private static float AngleLerp(float current, float target, float amount)
    {
        float delta = WrapAngle(target - current);
        return WrapAngle(current + delta * amount);
    }

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
