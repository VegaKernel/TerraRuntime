using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>World LOS query consumed by source-backed ordinary AI_005 projectile attacks.</summary>
public interface IVanillaNpcProjectileEnvironment
{
    bool CanHit(
        float sourcePositionX,
        float sourcePositionY,
        int sourceWidth,
        int sourceHeight,
        float targetPositionX,
        float targetPositionY,
        int targetWidth,
        int targetHeight);
}

/// <summary>
/// TerrariaServer 1.4.5.8 NPC.AI_GlobalFiringDistanceCheck. The source uses Main.MaxWorldViewSize 1920x1200,
/// centers that rectangle on the target Point, then inflates it by -50 pixels on both axes. Rectangle right and
/// bottom edges are exclusive, matching XNA Rectangle.Contains.
/// </summary>
public static class VanillaNpcGlobalFiringDistance
{
    public const int MaxWorldViewWidth = 1920;
    public const int MaxWorldViewHeight = 1200;
    public const int EdgeInset = 50;
    public const int HorizontalReach = MaxWorldViewWidth / 2 - EdgeInset;
    public const int VerticalReach = MaxWorldViewHeight / 2 - EdgeInset;

    public static bool Contains(float shootX, float shootY, float targetX, float targetY)
    {
        if (!float.IsFinite(shootX) ||
            !float.IsFinite(shootY) ||
            !float.IsFinite(targetX) ||
            !float.IsFinite(targetY))
        {
            return false;
        }

        int sx = (int)shootX;
        int sy = (int)shootY;
        int tx = (int)targetX;
        int ty = (int)targetY;
        return sx >= tx - HorizontalReach &&
               sx < tx + HorizontalReach &&
               sy >= ty - VerticalReach &&
               sy < ty + VerticalReach;
    }
}

public readonly record struct VanillaFlyerProjectileAttackResult(
    NpcAiState LocalAi,
    float VelocityX,
    float VelocityY,
    bool ProjectileReady);

/// <summary>
/// Source-backed server-state portion of ordinary TerrariaServer 1.4.5.8 AI_005 projectile attacks. Probe and
/// Blood Squid use localAI[0] as a server-only timer; the returned state is folded into the same NpcStateUpdate
/// revision as movement. Projectile publication remains a separate post-commit intent.
/// </summary>
public static class VanillaFlyerProjectileAttack
{
    public const float ProbeAttackThreshold = 120f;
    public const float BloodSquidAttackThreshold = 120f;
    public const float BloodSquidRetryTimer = 50f;
    public const float BloodSquidMaximumShotDistance = 400f;
    public const float BloodSquidRecoilSpeed = 5f;
    public const float BloodSquidProjectileSpeed = 15f;

    public static bool IsSupportedShooter(NpcTypeId type) =>
        type == VanillaNpcIds.Probe || type == VanillaNpcIds.BloodSquid;

    public static bool TryStep(
        NpcTypeId type,
        in NpcSnapshot npc,
        in VanillaNpcHitboxSize hitbox,
        in VanillaNpcTargetCandidate target,
        float postMotionVelocityX,
        float postMotionVelocityY,
        IVanillaNpcProjectileEnvironment? environment,
        out VanillaFlyerProjectileAttackResult result)
    {
        if (!IsSupportedShooter(type) ||
            !float.IsFinite(postMotionVelocityX) ||
            !float.IsFinite(postMotionVelocityY) ||
            !npc.Simulation.LocalAi.IsFinite ||
            !float.IsFinite(target.CenterX) ||
            !float.IsFinite(target.CenterY))
        {
            result = default;
            return false;
        }

        float localAi0 = npc.Simulation.LocalAi.Ai0;
        float sourceCenterX = npc.PositionX + hitbox.Width * 0.5f;
        float sourceCenterY = npc.PositionY + hitbox.Height * 0.5f;
        float targetPositionX = target.CenterX - VanillaPlayerHitboxFacts.BaseWidth * 0.5f;
        float targetPositionY = target.CenterY - VanillaPlayerHitboxFacts.BaseHeight * 0.5f;
        bool targetUsable = target.Active && !target.Dead && !target.Ghost;

        if (type == VanillaNpcIds.Probe)
        {
            // ai[3] != 0 belongs to the Mech Queen attachment path. TerraRuntime does not claim that composite
            // encounter yet, so ordinary Probe attack state remains fail-closed instead of applying its different
            // 360-tick cadence with incomplete parent state.
            if (npc.Ai.Ai3 != 0f)
            {
                result = new(
                    npc.Simulation.LocalAi,
                    postMotionVelocityX,
                    postMotionVelocityY,
                    ProjectileReady: false);
                return true;
            }

            if (npc.Simulation.JustHit)
            {
                localAi0 = 0f;
            }
            else
            {
                localAi0 += 1f;
            }

            bool ready = false;
            if (localAi0 >= ProbeAttackThreshold)
            {
                localAi0 = 0f;
                ready = targetUsable &&
                    VanillaNpcGlobalFiringDistance.Contains(
                        sourceCenterX,
                        sourceCenterY,
                        target.CenterX,
                        target.CenterY) &&
                    environment?.CanHit(
                        npc.PositionX,
                        npc.PositionY,
                        hitbox.Width,
                        hitbox.Height,
                        targetPositionX,
                        targetPositionY,
                        (int)VanillaPlayerHitboxFacts.BaseWidth,
                        (int)VanillaPlayerHitboxFacts.BaseHeight) == true;
            }

            result = new(
                new NpcAiState(
                    localAi0,
                    npc.Simulation.LocalAi.Ai1,
                    npc.Simulation.LocalAi.Ai2,
                    npc.Simulation.LocalAi.Ai3),
                postMotionVelocityX,
                postMotionVelocityY,
                ready);
            return true;
        }

        if (!targetUsable)
        {
            result = new(
                npc.Simulation.LocalAi,
                postMotionVelocityX,
                postMotionVelocityY,
                ProjectileReady: false);
            return true;
        }

        if (npc.Simulation.JustHit)
            localAi0 += 10f;
        localAi0 += 1f;

        bool bloodReady = false;
        float velocityX = postMotionVelocityX;
        float velocityY = postMotionVelocityY;
        if (localAi0 >= BloodSquidAttackThreshold)
        {
            float deltaCenterX = target.CenterX - sourceCenterX;
            float deltaCenterY = target.CenterY - sourceCenterY;
            bool withinShotDistance =
                deltaCenterX * deltaCenterX + deltaCenterY * deltaCenterY <
                BloodSquidMaximumShotDistance * BloodSquidMaximumShotDistance;
            bool canFire =
                VanillaNpcGlobalFiringDistance.Contains(
                    sourceCenterX,
                    sourceCenterY,
                    target.CenterX,
                    target.CenterY) &&
                environment?.CanHit(
                    npc.PositionX,
                    npc.PositionY,
                    hitbox.Width,
                    hitbox.Height,
                    targetPositionX,
                    targetPositionY,
                    (int)VanillaPlayerHitboxFacts.BaseWidth,
                    (int)VanillaPlayerHitboxFacts.BaseHeight) == true &&
                withinShotDistance;

            if (canFire)
            {
                float recoilX = target.CenterX - sourceCenterX;
                float recoilY = targetPositionY - sourceCenterY;
                Normalize(ref recoilX, ref recoilY, BloodSquidRecoilSpeed);
                velocityX = -recoilX;
                velocityY = -recoilY;
                localAi0 = 0f;
                bloodReady = true;
            }
            else
            {
                localAi0 = BloodSquidRetryTimer;
            }
        }

        result = new(
            new NpcAiState(
                localAi0,
                npc.Simulation.LocalAi.Ai1,
                npc.Simulation.LocalAi.Ai2,
                npc.Simulation.LocalAi.Ai3),
            velocityX,
            velocityY,
            bloodReady);
        return true;
    }

    public static void Normalize(ref float x, ref float y, float speed)
    {
        float lengthSquared = x * x + y * y;
        if (!float.IsFinite(lengthSquared) || lengthSquared <= 0f)
        {
            x = 0f;
            y = 0f;
            return;
        }

        float scale = speed / MathF.Sqrt(lengthSquared);
        x *= scale;
        y *= scale;
    }
}
