using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Core.Npcs;

/// <summary>
/// Accepted-state AI_003 projectile tails for the two lost-health ground fighters. Terraria rolls their firing
/// threshold and allocates a shot after common fighter motion. Keeping that tail in the deferred commit phase
/// prevents a stale NPC revision from advancing the shared random stream or allocating a projectile.
/// </summary>
internal static class VanillaGroundFighterProjectileAttack
{
    private static readonly NpcTypeId Type243 = new(243);
    private static readonly NpcTypeId Type251 = new(251);

    public static bool IsSupported(NpcTypeId type) => type == Type243 || type == Type251;

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
