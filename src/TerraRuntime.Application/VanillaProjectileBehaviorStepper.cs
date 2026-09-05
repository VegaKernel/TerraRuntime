using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Application;

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

    bool IsNpcSlotAddressable(int npcSlot);

    bool TryGetActiveNpc(int npcSlot, out NpcSnapshot npc);
}

internal readonly record struct VanillaProjectileBehaviorContext(
    bool WindPhysics,
    float WindSpeedCurrent,
    float WindPhysicsStrength,
    IRuntimePlayerSlotSnapshotLookup? PlayerSnapshots = null,
    bool ExpertMode = false,
    IVanillaProjectileNpcTargetResolver? NpcTargets = null,
    VanillaProjectilePlayerTargetResolver? HostilePlayerTargets = null,
    int CurrentTimeLeft = int.MaxValue,
    ProjectileLocalAiState LocalAi = default,
    bool Wet = false);

/// <summary>State produced by one supported vanilla projectile AI-family step before world motion/collision.</summary>
internal readonly record struct VanillaProjectileBehaviorResult(
    float VelocityX,
    float VelocityY,
    float Ai0,
    float? Ai1Override = null,
    float? Ai2Override = null,
    float? PositionXOverride = null,
    float? PositionYOverride = null,
    bool Kill = false,
    bool? TileCollideOverride = null,
    int? TimeLeftOverride = null,
    int? MinimumTimeLeftOverride = null,
    ProjectileLocalAiState? LocalAiOverride = null);

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 projectile behavior that is independent of tile/world queries.
/// Runtime behavior-family selection is explicit in <see cref="VanillaProjectileBehaviorProfileCatalog"/> so
/// equal aiStyle values never silently opt unrelated projectile types into the same implementation.
/// World collision, liquids, post-AI wind and lifetime/kill handling remain owned by the world-motion layer.
/// </summary>
internal static partial class VanillaProjectileBehaviorStepper
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

            case VanillaProjectileBehaviorFamily.CultistIceMist:
            {
                // TerrariaServer 1.4.5.8 aiStyle 86, CultistBossIceMist (#464). ai[1] == 1 is the
                // rotating emitter: ai[0] is its 150-update lifetime and rotation advances PI/30 each update.
                // Child mist copies use ai[1] == 0; AI subtracts velocity before the generic position update,
                // keeping their center stationary while six rotating 30x30 collision lobes expand outward.
                float mistAi0 = current.Ai.Ai0 + 1f;
                float localAi1 = context.LocalAi.Ai1 == 0f ? 1f : context.LocalAi.Ai1;
                float rotation = context.LocalAi.Ai2;
                if (current.Ai.Ai1 == 1f)
                {
                    if (mistAi0 >= 150f)
                    {
                        next = new VanillaProjectileBehaviorResult(
                            velocityX, velocityY, mistAi0, Kill: true,
                            LocalAiOverride: context.LocalAi with { Ai1 = localAi1, Ai2 = rotation });
                        return true;
                    }

                    rotation += MathF.PI / 30f;
                    next = new VanillaProjectileBehaviorResult(
                        velocityX, velocityY, mistAi0,
                        LocalAiOverride: context.LocalAi with { Ai1 = localAi1, Ai2 = rotation });
                    return true;
                }

                float stationaryX = current.PositionX - velocityX;
                float stationaryY = current.PositionY - velocityY;
                if (mistAi0 >= 45f)
                {
                    next = new VanillaProjectileBehaviorResult(
                        velocityX, velocityY, mistAi0,
                        PositionXOverride: stationaryX,
                        PositionYOverride: stationaryY,
                        Kill: true,
                        LocalAiOverride: context.LocalAi with { Ai1 = localAi1 });
                    return true;
                }

                next = new VanillaProjectileBehaviorResult(
                    velocityX, velocityY, mistAi0,
                    PositionXOverride: stationaryX,
                    PositionYOverride: stationaryY,
                    LocalAiOverride: context.LocalAi with { Ai1 = localAi1 });
                return true;
            }

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

            case VanillaProjectileBehaviorFamily.SkeletronPrimeBomb:
                // TerrariaServer 1.4.5.8 AI_016 type 102. The dedicated server is Main.myPlayer for NPC-owned
                // hostile projectiles, so a grounded bomb or a fuse at <=3 ticks reaches Kill() from AI itself.
                if (velocityY > 10f)
                    velocityY = 10f;
                if (velocityY == 0f || context.CurrentTimeLeft <= 3)
                {
                    next = new VanillaProjectileBehaviorResult(velocityX, velocityY, ai0, Kill: true);
                    return true;
                }
                ai0 += 1f;
                if (ai0 > 5f)
                {
                    ai0 = 10f;
                    velocityY += 0.2f;
                }
                break;

            case VanillaProjectileBehaviorFamily.PlanteraThornBall:
            {
                // TerrariaServer 1.4.5.8 aiStyle 14, type 277. Expert mode steers only X toward
                // Player.FindClosest and then caps the complete velocity vector to 16 px/update.
                if (context.ExpertMode)
                {
                    if (!TryFindClosestPlayer(in current, in definition, context.PlayerSnapshots, out float targetX, out float targetY))
                    {
                        next = default;
                        return false;
                    }
                    float centerX = current.PositionX + definition.Width * 0.5f;
                    float centerY = current.PositionY + definition.Height * 0.5f;
                    float dx = targetX - centerX;
                    float dy = targetY - centerY;
                    float distance = MathF.Sqrt(dx * dx + dy * dy);
                    if (distance > 0f)
                    {
                        float desiredX = dx / distance * 12f;
                        velocityX = (velocityX * 199f + desiredX) / 200f;
                    }
                    float speed = MathF.Sqrt(velocityX * velocityX + velocityY * velocityY);
                    if (speed > 16f)
                    {
                        velocityX = velocityX / speed * 16f;
                        velocityY = velocityY / speed * 16f;
                    }
                }

                ai0 += 1f;
                if (ai0 > 15f)
                {
                    ai0 = 15f;
                    if (velocityY == 0f && velocityX != 0f)
                    {
                        velocityX *= 0.97f;
                        if (velocityX is > -0.01f and < 0.01f)
                        {
                            next = new VanillaProjectileBehaviorResult(velocityX, velocityY, ai0, Kill: true);
                            return true;
                        }
                    }
                    velocityY += 0.2f;
                }
                break;
            }

            case VanillaProjectileBehaviorFamily.FairyQueenLance:
            {
                // TerrariaServer 1.4.5.8 AI_179_FairyQueenLance. localAI[0] is server-only: the lance
                // is harmless/stationary for 60 updates, then travels exactly along ai[0] at 40 px/update.
                float localAi0 = context.LocalAi.Ai0 + 1f;
                if (localAi0 >= 60f)
                {
                    velocityX = MathF.Cos(current.Ai.Ai0) * 40f;
                    velocityY = MathF.Sin(current.Ai.Ai0) * 40f;
                }

                bool kill = localAi0 >= 360f;
                next = new VanillaProjectileBehaviorResult(
                    velocityX,
                    velocityY,
                    ai0,
                    Kill: kill,
                    LocalAiOverride: context.LocalAi with { Ai0 = localAi0 });
                return true;
            }

            case VanillaProjectileBehaviorFamily.FairyQueenSunDance:
            {
                // AI_180_FairyQueenSunDance anchors the ray fan to the owning Empress and owns its lifetime
                // through localAI[0]. Rotation/scale are reconstructed by collision code from ai[0]/localAI[0].
                float localAi0 = context.LocalAi.Ai0 + 1f;
                if (localAi0 >= 180f)
                {
                    next = new VanillaProjectileBehaviorResult(
                        0f, 0f, ai0, Kill: true,
                        LocalAiOverride: context.LocalAi with { Ai0 = localAi0 });
                    return true;
                }

                int sourceSlot = (int)current.Ai.Ai1;
                if (context.NpcTargets is null || !context.NpcTargets.IsNpcSlotAddressable(sourceSlot))
                {
                    next = new VanillaProjectileBehaviorResult(
                        0f, 0f, ai0, Kill: true,
                        LocalAiOverride: context.LocalAi with { Ai0 = localAi0 });
                    return true;
                }

                // Vanilla only kills when ai[1] is outside Main.npc. An addressable but inactive/wrong-type
                // slot leaves the Sun Dance where it is and zeros velocity; it re-anchors only while that slot
                // still contains the owning Empress of Light.
                if (context.NpcTargets.TryGetActiveNpc(sourceSlot, out NpcSnapshot sourceNpc) &&
                    sourceNpc.TypeIdentity == VanillaNpcIds.EmpressOfLight &&
                    TryResolveNpcCenter(in sourceNpc, out float sourceCenterX, out float sourceCenterY))
                {
                    next = new VanillaProjectileBehaviorResult(
                        0f,
                        0f,
                        ai0,
                        PositionXOverride: sourceCenterX - definition.Width * 0.5f,
                        PositionYOverride: sourceCenterY - definition.Height * 0.5f,
                        LocalAiOverride: context.LocalAi with { Ai0 = localAi0 });
                    return true;
                }

                next = new VanillaProjectileBehaviorResult(
                    0f,
                    0f,
                    ai0,
                    LocalAiOverride: context.LocalAi with { Ai0 = localAi0 });
                return true;
            }

            case VanillaProjectileBehaviorFamily.Sharknado:
            {
                // TerrariaServer 1.4.5.8 aiStyle 64. Width/height are gameplay state because the tornado
                // segments are resized from ai[1]; packet 27 does not carry dimensions, so localAI[1..2]
                // retain the authoritative dynamic hitbox. Presentation alpha/frame state is omitted.
                bool cthulunado = current.Type == VanillaProjectileIds.Cthulunado;
                int startDelay = cthulunado ? 16 : 10;
                int segmentCount = cthulunado ? 16 : 15;
                float scaleMultiplier = cthulunado ? 1.5f : 1f;
                float baseWidth = 150f;
                float baseHeight = 42f;
                float segmentScale = current.Ai.Ai1 == -1f
                    ? (context.LocalAi.Ai1 > 0f ? context.LocalAi.Ai1 / baseWidth : 1f)
                    : ((startDelay + segmentCount) - current.Ai.Ai1) * scaleMultiplier / (startDelay + segmentCount);
                if (!(segmentScale > 0f) || !float.IsFinite(segmentScale))
                {
                    next = default;
                    return false;
                }

                float width = (int)(baseWidth * segmentScale);
                float height = (int)(baseHeight * segmentScale);
                float positionX = current.PositionX;
                float positionY = current.PositionY;
                float localAi0 = context.LocalAi.Ai0;
                if (localAi0 == 0f)
                {
                    localAi0 = 1f;
                    float centerX = current.PositionX + definition.Width / 2;
                    float centerY = current.PositionY + definition.Height / 2;
                    positionX = centerX - (int)width / 2;
                    positionY = centerY - (int)height / 2;
                }

                float tornadoAi0 = current.Ai.Ai0;
                if (tornadoAi0 > 0f)
                    tornadoAi0 -= 1f;

                if (tornadoAi0 <= 0f)
                {
                    float angularStep = MathF.PI / 30f;
                    float swayScale = width / 5f * (cthulunado ? 2f : 1f);
                    int direction = velocityX == 0f ? 1 : -Math.Sign(velocityX);
                    float offset = (MathF.Cos(angularStep * -tornadoAi0) - 0.5f) * swayScale;
                    positionX -= offset * -direction;
                    tornadoAi0 -= 1f;
                    offset = (MathF.Cos(angularStep * -tornadoAi0) - 0.5f) * swayScale;
                    positionX += offset * -direction;
                }

                next = new VanillaProjectileBehaviorResult(
                    velocityX, velocityY, tornadoAi0,
                    PositionXOverride: positionX,
                    PositionYOverride: positionY,
                    LocalAiOverride: context.LocalAi with { Ai0 = localAi0, Ai1 = width, Ai2 = height });
                return true;
            }

            case VanillaProjectileBehaviorFamily.SharknadoBolt:
            {
                // TerrariaServer 1.4.5.8 aiStyle 65. ai[1] > 0 homes on that physical player slot;
                // ai[1] <= 0 follows the vertical cosine wave. The Kill() tornado child is committed by the
                // termination-effect queue only after this exact generation is removed successfully.
                float boltAi0 = current.Ai.Ai0;
                float localAi0 = context.LocalAi.Ai0;
                if (current.Ai.Ai1 > 0f)
                {
                    int rawTarget = (int)current.Ai.Ai1 - 1;
                    if (rawTarget >= 0 && rawTarget < byte.MaxValue &&
                        context.HostilePlayerTargets is not null &&
                        context.HostilePlayerTargets.TryGetActiveTargetCenter(new PlayerSlotId((byte)rawTarget), out float targetX, out float targetY))
                    {
                        localAi0 += 1f;
                        float centerX = current.PositionX + definition.Width * 0.5f;
                        float centerY = current.PositionY + definition.Height * 0.5f;
                        float dx = targetX - centerX;
                        float dy = targetY - centerY;
                        float distance = MathF.Sqrt(dx * dx + dy * dy);
                        if (distance < 50f)
                        {
                            next = new VanillaProjectileBehaviorResult(
                                velocityX, velocityY, boltAi0, Kill: true,
                                LocalAiOverride: context.LocalAi with { Ai0 = localAi0 });
                            return true;
                        }

                        if (distance > 0f && float.IsFinite(distance))
                        {
                            float speed = 4f + (current.Ai.Ai2 == 1f ? 12f : 0f) + localAi0 / 20f;
                            velocityX = dx / distance * speed;
                            velocityY = dy / distance * speed;
                        }
                    }
                }
                else
                {
                    float angularStep = MathF.PI / 15f;
                    float delta = (MathF.Cos(angularStep * boltAi0) - 0.5f) * 4f;
                    velocityY -= delta;
                    boltAi0 += 1f;
                    delta = (MathF.Cos(angularStep * boltAi0) - 0.5f) * 4f;
                    velocityY += delta;
                    localAi0 += 1f;
                }

                if (context.Wet)
                {
                    next = new VanillaProjectileBehaviorResult(
                        velocityX, velocityY, boltAi0,
                        PositionYOverride: current.PositionY - 16f,
                        Kill: true,
                        LocalAiOverride: context.LocalAi with { Ai0 = localAi0 });
                    return true;
                }

                next = new VanillaProjectileBehaviorResult(
                    velocityX, velocityY, boltAi0,
                    LocalAiOverride: context.LocalAi with { Ai0 = localAi0 });
                return true;
            }

            case VanillaProjectileBehaviorFamily.PhantasmalEye:
                return TryStepPhantasmalEye(in current, in definition, in context, out next);

            case VanillaProjectileBehaviorFamily.PhantasmalSphere:
            {
                // TerrariaServer 1.4.5.8 aiStyle 83. ai[0] >= 0 is the charge timer. During the first 30
                // updates the sphere follows its source NPC; afterwards it coasts with 4% damping. ai[0] == -1
                // is the released state and is handled with the source dynamic extraUpdates fact.
                float sphereAi0 = current.Ai.Ai0;
                if (sphereAi0 >= 0f)
                    sphereAi0 += 1f;

                if (sphereAi0 >= 0f && sphereAi0 < 30f)
                {
                    int sourceSlot = (int)current.Ai.Ai1;
                    if (context.NpcTargets is null ||
                        !context.NpcTargets.TryGetActiveNpc(sourceSlot, out NpcSnapshot sourceNpc) ||
                        !TryResolveNpcCenter(in sourceNpc, out float sourceCenterX, out float sourceCenterY))
                    {
                        next = default;
                        return false;
                    }

                    next = new VanillaProjectileBehaviorResult(
                        velocityX, velocityY, sphereAi0,
                        PositionXOverride: sourceCenterX - definition.Width * 0.5f - velocityX,
                        PositionYOverride: sourceCenterY - definition.Height * 0.5f - velocityY);
                    return true;
                }

                if (sphereAi0 != -1f)
                {
                    velocityX *= 0.96f;
                    velocityY *= 0.96f;
                }

                next = new VanillaProjectileBehaviorResult(velocityX, velocityY, sphereAi0);
                return true;
            }

            case VanillaProjectileBehaviorFamily.PhantasmalDeathray:
                return TryStepPhantasmalDeathray(in current, in definition, in context, out next);

            case VanillaProjectileBehaviorFamily.HallowBossRainbowStreak:
                return TryStepHallowBossRainbowStreak(in current, in definition, in context, out next);

            case VanillaProjectileBehaviorFamily.HallowBossLastingRainbow:
            {
                // TerrariaServer 1.4.5.8 AI_173_HallowBossRainbowTrail. Presentation opacity/rotation are omitted.
                Rotate(ref velocityX, ref velocityY, current.Ai.Ai0);
                float trailAi0 = current.Ai.Ai0;
                const float maximumTurn = MathF.PI / 360f;
                if (trailAi0 < maximumTurn)
                    trailAi0 += maximumTurn / 30f;
                next = new VanillaProjectileBehaviorResult(velocityX, velocityY, trailAi0);
                return true;
            }

            case VanillaProjectileBehaviorFamily.HallowBossDeathAurora:
                // aiStyle 0 has no projectile-specific AI. Common runtime position/lifetime processing still runs.
                break;

            case VanillaProjectileBehaviorFamily.QueenSlimeSmash:
            {
                // TerrariaServer 1.4.5.8 AI_135_OgreStomp, type 922. The source grows a center-preserving square
                // from 80 toward 480 px across nine AI updates and kills on the tenth. Runtime-only localAI[1]
                // carries the dynamic width because packet 27 has no projectile-size field.
                float smashAi0 = current.Ai.Ai0 + 1f;
                if (smashAi0 > 9f)
                {
                    next = new VanillaProjectileBehaviorResult(0f, 0f, smashAi0, Kill: true);
                    return true;
                }

                float priorSize = context.LocalAi.Ai1 > 0f ? context.LocalAi.Ai1 : definition.Width;
                float centerX = current.PositionX + priorSize * 0.5f;
                float centerY = current.PositionY + priorSize * 0.5f;
                float progress = Math.Clamp(smashAi0 / 9f, 0f, 1f);
                float size = (int)(16f * (5f + (30f - 5f) * progress));
                next = new VanillaProjectileBehaviorResult(
                    0f,
                    0f,
                    smashAi0,
                    PositionXOverride: centerX - size * 0.5f,
                    PositionYOverride: centerY - size * 0.5f,
                    LocalAiOverride: context.LocalAi with { Ai1 = size });
                return true;
            }

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
            {
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
            }

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

}
