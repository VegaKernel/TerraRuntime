using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Core.Npcs;

/// <summary>
/// Accepted-state AI_003 projectile tails for the admitted ground fighters. Terraria rolls their firing thresholds
/// and allocates shots after common fighter motion. Keeping those tails in the deferred commit phase prevents a
/// stale NPC revision from advancing the shared random stream or allocating a projectile.
/// </summary>
internal static class VanillaGroundFighterProjectileAttack
{
    private static readonly NpcTypeId Type243 = new(243);
    private static readonly NpcTypeId Type251 = new(251);
    private static readonly NpcTypeId Type350 = new(350);

    public static bool IsSupported(NpcTypeId type) => type == Type243 || type == Type251 || type == Type350;

    public static NpcSnapshot Complete(
        in NpcSnapshot before,
        in NpcSnapshot committed,
        VanillaNpcBehaviorContext context,
        IVanillaNpcRandom random,
        IVanillaNpcProjectileEnvironment? environment,
        INpcAiCommittedNpcMutationSink mutations)
    {
        if (before.TypeIdentity != committed.TypeIdentity || !IsSupported(before.TypeIdentity) ||
            !VanillaNpcDefinitionCatalog.TryGet(before.TypeIdentity, before.NetIdentity, out VanillaNpcDefinition definition) ||
            !definition.TryResolveHitbox(committed.Simulation, out VanillaNpcHitboxSize hitbox) ||
            committed.Simulation.LifeMax <= 0)
        {
            return committed;
        }

        if (before.TypeIdentity == Type350)
            return CompleteType350(in before, in committed, in hitbox, context, random, environment, mutations);

        float timer = committed.Ai.Ai2;
        if (before.TypeIdentity == Type243)
        {
            if (before.Simulation.JustHit && random.NextInt32(0, 3) == 0)
                timer -= random.NextInt32(0, 30);
            timer = MathF.Max(0f, timer);
            if (committed.Simulation.Confused)
                timer = 0f;
            timer += 1f;

            float threshold = random.NextInt32(30, 900) * (float)committed.Simulation.Life /
                committed.Simulation.LifeMax + 30f;
            if (!CanFire(in committed, in hitbox, context, environment, requireGlobalDistance: false, out VanillaNpcTargetCandidate target) ||
                timer < threshold)
            {
                return UpdateTimer(in committed, timer, mutations);
            }

            NpcSnapshot completed = UpdateTimer(in committed, 0f, mutations);
            if (completed.Revision == committed.Revision)
                return committed;
            SpawnType243Bolt(in completed, in target, in hitbox, random, mutations);
            return completed;
        }

        if (before.Simulation.JustHit)
            timer -= random.NextInt32(0, 30);
        timer = MathF.Max(0f, timer);
        if (committed.Simulation.Confused)
            timer = 0f;
        timer += 1f;

        float type251Threshold = random.NextInt32(60, 1800) * (float)committed.Simulation.Life /
            committed.Simulation.LifeMax + 15f;
        if (timer < type251Threshold)
            return UpdateTimer(in committed, timer, mutations);

        NpcSnapshot type251Completed = UpdateTimer(in committed, 0f, mutations);
        if (type251Completed.Revision == committed.Revision)
            return committed;
        if (CanFire(in type251Completed, in hitbox, context, environment, requireGlobalDistance: true, out VanillaNpcTargetCandidate type251Target))
            SpawnType251Bolt(in type251Completed, in type251Target, in hitbox, random, mutations);
        return type251Completed;
    }

    private static NpcSnapshot CompleteType350(
        in NpcSnapshot before,
        in NpcSnapshot committed,
        in VanillaNpcHitboxSize hitbox,
        VanillaNpcBehaviorContext context,
        IVanillaNpcRandom random,
        IVanillaNpcProjectileEnvironment? environment,
        INpcAiCommittedNpcMutationSink mutations)
    {
        // AI_003's type-350 branch follows its ordinary fighter motion. ai[1] is the 110-tick wind-up and ai[2]
        // is the source facing/animation category selected from the prepared or fired shot vector.
        float timer = committed.Ai.Ai1;
        float mode = committed.Ai.Ai2;
        float velocityX = committed.VelocityX;
        int spriteDirection = committed.Simulation.SpriteDirection;
        if (timer > 0f)
            timer -= 1f;
        if (before.Simulation.JustHit)
        {
            timer = 30f;
            mode = 0f;
        }
        if (committed.Simulation.Confused)
        {
            timer = 0f;
            mode = 0f;
        }

        bool hasShot = false;
        float shotX = 0f;
        float shotY = 0f;
        float shotVelocityX = 0f;
        float shotVelocityY = 0f;
        if (mode > 0f)
        {
            if (timer == 55f && TryGetTarget(in committed, context, out VanillaNpcTargetCandidate firingTarget))
            {
                float sourceCenterX = committed.PositionX + hitbox.Width * .5f;
                float sourceCenterY = committed.PositionY + hitbox.Height * .5f;
                float targetX = firingTarget.CenterX - sourceCenterX;
                float aimX = targetX + random.NextInt32(-40, 41);
                float aimY = firingTarget.CenterY - sourceCenterY - MathF.Abs(targetX) * .1f + random.NextInt32(-40, 41);
                float length = MathF.Sqrt(aimX * aimX + aimY * aimY);
                shotVelocityX = aimX / length * 11f;
                shotVelocityY = aimY / length * 11f;
                shotX = sourceCenterX + shotVelocityX - 5f;
                shotY = sourceCenterY + shotVelocityY - 5f;
                mode = AimCategory(aimX, aimY);
                hasShot = float.IsFinite(shotVelocityX) && float.IsFinite(shotVelocityY);
            }

            if (committed.VelocityY != 0f || timer <= 0f)
            {
                mode = 0f;
                timer = 0f;
            }
            else
            {
                velocityX *= .9f;
                spriteDirection = committed.Simulation.DirectionX;
            }
        }
        else if (committed.VelocityY == 0f && timer <= 0f &&
                 TryGetTarget(in committed, context, out VanillaNpcTargetCandidate preparationTarget) &&
                 !preparationTarget.Dead && environment is not null)
        {
            // Source tests Collision.CanHit first, then rejects an idle non-stealthed player. Its two random aim
            // offsets are consumed once visibility and the player-state gate pass, even when the target is >=700 px.
            bool canPrepare = environment.CanHit(
                committed.PositionX,
                committed.PositionY,
                hitbox.Width,
                hitbox.Height,
                preparationTarget.CenterX - preparationTarget.Width * .5f,
                preparationTarget.CenterY - preparationTarget.Height * .5f,
                (int)preparationTarget.Width,
                (int)preparationTarget.Height) &&
                !(preparationTarget.Stealth == 0f && preparationTarget.ItemAnimation == 0);
            if (canPrepare)
            {
                float sourceCenterX = committed.PositionX + hitbox.Width * .5f;
                float sourceCenterY = committed.PositionY + hitbox.Height * .5f;
                float targetX = preparationTarget.CenterX - sourceCenterX;
                float aimX = targetX + random.NextInt32(-40, 41);
                float aimY = preparationTarget.CenterY - sourceCenterY - MathF.Abs(targetX) * .1f + random.NextInt32(-40, 41);
                float distance = MathF.Sqrt(aimX * aimX + aimY * aimY);
                if (distance < 700f)
                {
                    velocityX *= .5f;
                    mode = AimCategory(aimX, aimY);
                    timer = 110f;
                }
            }
        }

        NpcAiState ai = committed.Ai with { Ai1 = timer, Ai2 = mode };
        NpcSimulationState simulation = committed.Simulation with { SpriteDirection = spriteDirection };
        if (ai == committed.Ai && simulation == committed.Simulation && velocityX == committed.VelocityX)
            return committed;

        var update = new NpcStateUpdate(
            committed.Type,
            committed.NetId,
            committed.PositionX,
            committed.PositionY,
            velocityX,
            committed.VelocityY,
            committed.Target,
            ai,
            simulation);
        if (!mutations.TryUpdateState(in committed, in update, out NpcSnapshot completed))
            return committed;

        if (hasShot)
        {
            var intent = new NpcAiProjectileIntent(
                VanillaProjectileIds.GroundFighter350Bolt,
                shotX,
                shotY,
                shotVelocityX,
                shotVelocityY,
                Damage: 45,
                KnockBack: 0f);
            mutations.TrySpawnProjectile(in completed, in intent, out _);
        }

        return completed;
    }

    private static bool TryGetTarget(
        in NpcSnapshot npc,
        VanillaNpcBehaviorContext context,
        out VanillaNpcTargetCandidate target)
    {
        target = default;
        return npc.Target < byte.MaxValue &&
            context.TryFindCandidate((byte)npc.Target, out target) &&
            target.Active;
    }

    private static float AimCategory(float x, float y)
    {
        if (MathF.Abs(y) > MathF.Abs(x) * 2f)
            return y > 0f ? 1f : 5f;
        if (MathF.Abs(x) > MathF.Abs(y) * 2f)
            return 3f;
        return y > 0f ? 2f : 4f;
    }

    private static NpcSnapshot UpdateTimer(
        in NpcSnapshot committed,
        float timer,
        INpcAiCommittedNpcMutationSink mutations) =>
        mutations.TryUpdateAi(in committed, committed.Ai with { Ai2 = timer }, out NpcSnapshot completed)
            ? completed
            : committed;

    private static bool CanFire(
        in NpcSnapshot npc,
        in VanillaNpcHitboxSize hitbox,
        VanillaNpcBehaviorContext context,
        IVanillaNpcProjectileEnvironment? environment,
        bool requireGlobalDistance,
        out VanillaNpcTargetCandidate target)
    {
        target = default;
        if (environment is null || npc.VelocityY != 0f || npc.Target >= byte.MaxValue ||
            !context.TryFindCandidate((byte)npc.Target, out target) ||
            !target.Active || target.Dead || target.Frozen)
        {
            return false;
        }

        float centerX = npc.PositionX + hitbox.Width * .5f;
        bool facingTarget = (npc.Simulation.DirectionX > 0 && centerX < target.CenterX) ||
            (npc.Simulation.DirectionX < 0 && centerX > target.CenterX);
        if (!facingTarget ||
            (requireGlobalDistance && !VanillaNpcGlobalFiringDistance.Contains(centerX, npc.PositionY + hitbox.Height * .5f,
                target.CenterX, target.CenterY)))
        {
            return false;
        }

        return environment.CanHit(
            npc.PositionX,
            npc.PositionY,
            hitbox.Width,
            hitbox.Height,
            target.CenterX - target.Width * .5f,
            target.CenterY - target.Height * .5f,
            (int)target.Width,
            (int)target.Height);
    }

    private static void SpawnType243Bolt(
        in NpcSnapshot source,
        in VanillaNpcTargetCandidate target,
        in VanillaNpcHitboxSize hitbox,
        IVanillaNpcRandom random,
        INpcAiCommittedNpcMutationSink mutations)
    {
        float x = source.PositionX + hitbox.Width * .5f + 10f * source.Simulation.DirectionX;
        float y = source.PositionY + 20f;
        float velocityX = target.CenterX - x + random.NextInt32(-40, 41);
        float velocityY = target.CenterY - y + random.NextInt32(-40, 41);
        if (!TryNormalize(ref velocityX, ref velocityY, 15f))
            return;
        x += velocityX * 3f;
        y += velocityY * 3f;
        var intent = new NpcAiProjectileIntent(
            VanillaProjectileIds.GroundFighter243Bolt,
            x - 2f,
            y - 2f,
            velocityX,
            velocityY,
            Damage: 32,
            KnockBack: 0f);
        mutations.TrySpawnProjectile(in source, in intent, out _);
    }

    private static void SpawnType251Bolt(
        in NpcSnapshot source,
        in VanillaNpcTargetCandidate target,
        in VanillaNpcHitboxSize hitbox,
        IVanillaNpcRandom random,
        INpcAiCommittedNpcMutationSink mutations)
    {
        float x = source.PositionX + hitbox.Width * .5f + 6f * source.Simulation.DirectionX;
        float y = source.PositionY + 12f;
        float velocityX = target.CenterX - x + random.NextInt32(-40, 41);
        float velocityY = target.CenterY - y + random.NextInt32(-30, 0);
        if (!TryNormalize(ref velocityX, ref velocityY, 15f))
            return;
        x += velocityX * 3f;
        y += velocityY * 3f;
        var intent = new NpcAiProjectileIntent(
            VanillaProjectileIds.GroundFighter251Bolt,
            x - 2f,
            y - 2f,
            velocityX,
            velocityY,
            Damage: 30,
            KnockBack: 0f);
        mutations.TrySpawnProjectile(in source, in intent, out _);
    }

    private static bool TryNormalize(ref float x, ref float y, float speed)
    {
        float length = MathF.Sqrt(x * x + y * y);
        if (!(length > 0f) || !float.IsFinite(length))
            return false;
        x = x / length * speed;
        y = y / length * speed;
        return true;
    }
}
