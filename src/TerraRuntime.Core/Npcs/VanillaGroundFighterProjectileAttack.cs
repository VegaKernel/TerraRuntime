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
    private static readonly NpcTypeId SkeletonSniper = new(291);
    private static readonly NpcTypeId TacticalSkeleton = new(292);
    private static readonly NpcTypeId SkeletonCommando = new(293);
    private static readonly NpcTypeId Paladin = new(290);
    private static readonly NpcTypeId SkeletonArcher = new(110);
    private static readonly NpcTypeId GoblinArcher = new(111);
    private static readonly NpcTypeId IcyMerman = new(206);

    public static bool IsSupported(NpcTypeId type) =>
        type == Type243 || type == Type251 || type == Type350 || type == SkeletonSniper || type == TacticalSkeleton || type == SkeletonCommando || type == Paladin || type == SkeletonArcher || type == GoblinArcher || type == IcyMerman || IsSalamander(type);

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
        if (IsSalamander(before.TypeIdentity))
            return CompleteSalamander(in before, in committed, in definition, in hitbox, context, random, environment, mutations);
        if (before.TypeIdentity == SkeletonSniper)
            return CompleteDungeonSkeletonShooter(in before, in committed, in definition, in hitbox, context, random, environment,
                mutations, 200f, 100f, 11f, .2f, noVerticalLead: true, sourceYOffset: 0f, VanillaProjectileIds.SkeletonSniperBullet, 100);
        if (before.TypeIdentity == TacticalSkeleton)
            return CompleteTacticalSkeleton(in before, in committed, in definition, in hitbox, context, random, environment, mutations);
        if (before.TypeIdentity == SkeletonCommando)
            return CompleteDungeonSkeletonShooter(in before, in committed, in definition, in hitbox, context, random, environment,
                mutations, 90f, 45f, 4f, 1f, noVerticalLead: false, sourceYOffset: 0f, VanillaProjectileIds.SkeletonCommandoRocket, 60);
        if (before.TypeIdentity == Paladin)
            return CompleteDungeonSkeletonShooter(in before, in committed, in definition, in hitbox, context, random, environment,
                mutations, 30f, 15f, 9f, 1f, noVerticalLead: false, sourceYOffset: -10f, VanillaProjectileIds.PaladinHammer, 60);
        if (before.TypeIdentity == SkeletonArcher)
            return CompleteDungeonSkeletonShooter(in before, in committed, in definition, in hitbox, context, random, environment,
                mutations, 70f, 35f, 11f, 1f, noVerticalLead: false, sourceYOffset: 0f, VanillaProjectileIds.GroundFighter350Bolt, 35);
        if (before.TypeIdentity == GoblinArcher)
            return CompleteDungeonSkeletonShooter(in before, in committed, in definition, in hitbox, context, random, environment,
                mutations, 180f, 90f, 9f, 1f, noVerticalLead: false, sourceYOffset: 0f, VanillaProjectileIds.GoblinArcherArrow, 11);
        if (before.TypeIdentity == IcyMerman)
            return CompleteDungeonSkeletonShooter(in before, in committed, in definition, in hitbox, context, random, environment,
                mutations, 50f, 25f, 7f, 1f, noVerticalLead: false, sourceYOffset: -10f, VanillaProjectileIds.IcewaterSpit, 37);

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

    private static NpcSnapshot CompleteDungeonSkeletonShooter(
        in NpcSnapshot before, in NpcSnapshot committed, in VanillaNpcDefinition definition, in VanillaNpcHitboxSize hitbox,
        VanillaNpcBehaviorContext context, IVanillaNpcRandom random, IVanillaNpcProjectileEnvironment? environment,
        INpcAiCommittedNpcMutationSink mutations, float windup, float fireAt, float projectileSpeed, float firingJitter,
        bool noVerticalLead, float sourceYOffset, ProjectileTypeId projectileType, short damage)
    {
        float timer = committed.Ai.Ai1;
        float mode = committed.Ai.Ai2;
        float velocityX = committed.VelocityX;
        ushort targetSlot = committed.Target;
        int directionX = committed.Simulation.DirectionX;
        int directionY = committed.Simulation.DirectionY;
        int spriteDirection = committed.Simulation.SpriteDirection;
        if (timer > 0f)
            timer--;
        if (before.Simulation.JustHit)
        {
            timer = 30f;
            mode = 0f;
        }
        if (committed.Simulation.Confused)
            mode = 0f;

        bool hasShot = false;
        float shotX = 0f;
        float shotY = 0f;
        float shotVelocityX = 0f;
        float shotVelocityY = 0f;
        if (mode > 0f)
        {
            if (TrySelectClosestTarget(in committed, in definition, context, out VanillaNpcTargetCandidate target,
                out VanillaBlueSlimeTargetRefresh refresh))
            {
                targetSlot = refresh.Target;
                directionX = refresh.DirectionX;
                directionY = refresh.DirectionY;
                if (timer == fireAt)
                {
                    float sourceX = committed.PositionX + hitbox.Width * .5f;
                    float sourceY = committed.PositionY + hitbox.Height * .5f + sourceYOffset;
                    float targetX = target.CenterX - sourceX;
                    shotVelocityX = targetX + random.NextInt32(-40, 41) * firingJitter;
                    shotVelocityY = target.CenterY - sourceY - (noVerticalLead ? 0f : MathF.Abs(targetX) * .1f) +
                        random.NextInt32(-40, 41) * firingJitter;
                    hasShot = TryNormalize(ref shotVelocityX, ref shotVelocityY, projectileSpeed);
                    if (hasShot)
                    {
                        shotX = sourceX + shotVelocityX;
                        shotY = sourceY + shotVelocityY;
                        mode = AimCategory(shotVelocityX, shotVelocityY);
                    }
                }
            }
            if (committed.VelocityY != 0f || timer <= 0f)
            {
                mode = 0f;
                timer = 0f;
            }
            else
            {
                velocityX *= .9f;
                spriteDirection = directionX;
            }
        }
        else if (committed.VelocityY == 0f && timer <= 0f &&
                 TryGetTarget(in committed, context, out VanillaNpcTargetCandidate target) && !target.Dead && environment is not null &&
                 environment.CanHit(committed.PositionX, committed.PositionY, hitbox.Width, hitbox.Height,
                     target.CenterX - target.Width * .5f, target.CenterY - target.Height * .5f, (int)target.Width, (int)target.Height))
        {
            float sourceX = committed.PositionX + hitbox.Width * .5f;
            float sourceY = committed.PositionY + hitbox.Height * .5f;
            float aimX = target.CenterX - sourceX + random.NextInt32(-40, 41);
            float aimY = target.CenterY - sourceY - MathF.Abs(target.CenterX - sourceX) * .1f + random.NextInt32(-40, 41);
            if (MathF.Sqrt(aimX * aimX + aimY * aimY) < 700f)
            {
                velocityX *= .5f;
                mode = AimCategory(aimX, aimY);
                timer = windup;
            }
        }

        NpcAiState ai = committed.Ai with { Ai1 = timer, Ai2 = mode };
        NpcSimulationState simulation = committed.Simulation with { DirectionX = directionX, DirectionY = directionY, SpriteDirection = spriteDirection };
        if (ai == committed.Ai && simulation == committed.Simulation && velocityX == committed.VelocityX && targetSlot == committed.Target)
            return committed;
        var update = new NpcStateUpdate(committed.Type, committed.NetId, committed.PositionX, committed.PositionY,
            velocityX, committed.VelocityY, targetSlot, ai, simulation);
        if (!mutations.TryUpdateState(in committed, in update, out NpcSnapshot completed))
            return committed;
        if (hasShot)
        {
            var intent = new NpcAiProjectileIntent(
                projectileType, shotX, shotY, shotVelocityX, shotVelocityY, damage, 0f);
            mutations.TrySpawnProjectile(in completed, in intent, out _);
        }
        return completed;
    }

    private static NpcSnapshot CompleteTacticalSkeleton(
        in NpcSnapshot before, in NpcSnapshot committed, in VanillaNpcDefinition definition, in VanillaNpcHitboxSize hitbox,
        VanillaNpcBehaviorContext context, IVanillaNpcRandom random, IVanillaNpcProjectileEnvironment? environment,
        INpcAiCommittedNpcMutationSink mutations)
    {
        float timer = committed.Ai.Ai1;
        float mode = committed.Ai.Ai2;
        float velocityX = committed.VelocityX;
        ushort targetSlot = committed.Target;
        int directionX = committed.Simulation.DirectionX;
        int directionY = committed.Simulation.DirectionY;
        int spriteDirection = committed.Simulation.SpriteDirection;
        if (timer > 0f)
            timer--;
        if (before.Simulation.JustHit)
        {
            timer = 30f;
            mode = 0f;
        }
        if (committed.Simulation.Confused)
            mode = 0f;

        Span<NpcAiProjectileIntent> shots = stackalloc NpcAiProjectileIntent[4];
        int shotCount = 0;
        if (mode > 0f)
        {
            if (TrySelectClosestTarget(in committed, in definition, context, out VanillaNpcTargetCandidate target,
                out VanillaBlueSlimeTargetRefresh refresh))
            {
                targetSlot = refresh.Target;
                directionX = refresh.DirectionX;
                directionY = refresh.DirectionY;
                if (timer == 60f)
                {
                    float sourceX = committed.PositionX + hitbox.Width * .5f;
                    float sourceY = committed.PositionY + hitbox.Height * .5f;
                    float aimX = target.CenterX - sourceX;
                    float aimY = target.CenterY - sourceY;
                    if (TryNormalize(ref aimX, ref aimY, 11f))
                    {
                        sourceX += aimX;
                        sourceY += aimY;
                        for (int i = 0; i < shots.Length; i++)
                        {
                            float bulletX = target.CenterX - sourceX;
                            float bulletY = target.CenterY - sourceY;
                            float length = MathF.Sqrt(bulletX * bulletX + bulletY * bulletY);
                            if (!(length > 0f) || !float.IsFinite(length))
                                continue;
                            float scale = 12f / length;
                            bulletX = (bulletX + random.NextInt32(-40, 41)) * scale;
                            bulletY = (bulletY + random.NextInt32(-40, 41)) * scale;
                            shots[shotCount++] = new NpcAiProjectileIntent(
                                VanillaProjectileIds.TacticalSkeletonBullet, sourceX, sourceY, bulletX, bulletY, 50, 0f);
                            mode = AimCategory(bulletX, bulletY);
                        }
                    }
                }
            }
            if (committed.VelocityY != 0f || timer <= 0f)
            {
                mode = 0f;
                timer = 0f;
            }
            else
            {
                velocityX *= .9f;
                spriteDirection = directionX;
            }
        }
        else if (committed.VelocityY == 0f && timer <= 0f &&
                 TryGetTarget(in committed, context, out VanillaNpcTargetCandidate target) && !target.Dead && environment is not null &&
                 environment.CanHit(committed.PositionX, committed.PositionY, hitbox.Width, hitbox.Height,
                     target.CenterX - target.Width * .5f, target.CenterY - target.Height * .5f, (int)target.Width, (int)target.Height))
        {
            float sourceX = committed.PositionX + hitbox.Width * .5f;
            float sourceY = committed.PositionY + hitbox.Height * .5f;
            float aimX = target.CenterX - sourceX + random.NextInt32(-40, 41);
            float aimY = target.CenterY - sourceY - MathF.Abs(target.CenterX - sourceX) * .1f + random.NextInt32(-40, 41);
            if (MathF.Sqrt(aimX * aimX + aimY * aimY) < 700f)
            {
                velocityX *= .5f;
                mode = AimCategory(aimX, aimY);
                timer = 120f;
            }
        }

        NpcAiState ai = committed.Ai with { Ai1 = timer, Ai2 = mode };
        NpcSimulationState simulation = committed.Simulation with { DirectionX = directionX, DirectionY = directionY, SpriteDirection = spriteDirection };
        if (ai == committed.Ai && simulation == committed.Simulation && velocityX == committed.VelocityX && targetSlot == committed.Target)
            return committed;
        var update = new NpcStateUpdate(committed.Type, committed.NetId, committed.PositionX, committed.PositionY,
            velocityX, committed.VelocityY, targetSlot, ai, simulation);
        if (!mutations.TryUpdateState(in committed, in update, out NpcSnapshot completed))
            return committed;
        for (int i = 0; i < shotCount; i++)
            mutations.TrySpawnProjectile(in completed, in shots[i], out _);
        return completed;
    }

    private static NpcSnapshot CompleteSalamander(
        in NpcSnapshot before,
        in NpcSnapshot committed,
        in VanillaNpcDefinition definition,
        in VanillaNpcHitboxSize hitbox,
        VanillaNpcBehaviorContext context,
        IVanillaNpcRandom random,
        IVanillaNpcProjectileEnvironment? environment,
        INpcAiCommittedNpcMutationSink mutations)
    {
        // NPC.AI_003_Fighters (498..506): ai[1] is a 70-tick wind-up and ai[2] is the shot-facing category.
        float timer = committed.Ai.Ai1;
        float mode = committed.Ai.Ai2;
        float velocityX = committed.VelocityX;
        ushort targetSlot = committed.Target;
        int directionX = committed.Simulation.DirectionX;
        int directionY = committed.Simulation.DirectionY;
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
            // Unlike generic AI_003 pursuit, the source reacquires a target for every armed Salamander tick.
            if (TrySelectClosestTarget(in committed, in definition, context, out VanillaNpcTargetCandidate firingTarget,
                out VanillaBlueSlimeTargetRefresh refresh))
            {
                targetSlot = refresh.Target;
                directionX = refresh.DirectionX;
                directionY = refresh.DirectionY;
                if (timer == 35f)
                {
                    float sourceCenterX = committed.PositionX + hitbox.Width * .5f;
                    float sourceCenterY = committed.PositionY + hitbox.Height * .5f - 8f;
                    float targetX = firingTarget.CenterX - sourceCenterX;
                    float verticalLead = MathF.Abs(targetX) * random.NextInt32(1, 11) * .0025f;
                    float aimX = targetX + random.NextInt32(-40, 41);
                    float aimY = firingTarget.CenterY - sourceCenterY - verticalLead + random.NextInt32(-40, 41);
                    shotVelocityX = aimX;
                    shotVelocityY = aimY;
                    hasShot = TryNormalize(ref shotVelocityX, ref shotVelocityY, 7f);
                    if (hasShot)
                    {
                        shotX = sourceCenterX;
                        shotY = sourceCenterY;
                        mode = AimCategory(shotVelocityX, shotVelocityY);
                    }
                }
            }

            if (committed.VelocityY != 0f || timer <= 0f)
            {
                mode = 0f;
                timer = 0f;
            }
            else
            {
                velocityX *= .9f;
                spriteDirection = directionX;
            }
        }
        else if (committed.VelocityY == 0f && timer <= 0f &&
                 TryGetTarget(in committed, context, out VanillaNpcTargetCandidate preparationTarget) &&
                 !preparationTarget.Dead && environment is not null)
        {
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
                if (MathF.Sqrt(aimX * aimX + aimY * aimY) < 190f)
                {
                    velocityX *= .5f;
                    mode = AimCategory(aimX, aimY);
                    timer = 70f;
                }
            }
        }

        NpcAiState ai = committed.Ai with { Ai1 = timer, Ai2 = mode };
        NpcSimulationState simulation = committed.Simulation with
        {
            DirectionX = directionX,
            DirectionY = directionY,
            SpriteDirection = spriteDirection
        };
        if (ai == committed.Ai && simulation == committed.Simulation && velocityX == committed.VelocityX && targetSlot == committed.Target)
            return committed;

        var update = new NpcStateUpdate(
            committed.Type,
            committed.NetId,
            committed.PositionX,
            committed.PositionY,
            velocityX,
            committed.VelocityY,
            targetSlot,
            ai,
            simulation);
        if (!mutations.TryUpdateState(in committed, in update, out NpcSnapshot completed))
            return committed;

        if (hasShot)
        {
            var intent = new NpcAiProjectileIntent(
                VanillaProjectileIds.SalamanderBolt,
                shotX,
                shotY,
                shotVelocityX,
                shotVelocityY,
                Damage: 14,
                KnockBack: 0f);
            mutations.TrySpawnProjectile(in completed, in intent, out _);
        }

        return completed;
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

    private static bool TrySelectClosestTarget(
        in NpcSnapshot npc,
        in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context,
        out VanillaNpcTargetCandidate target,
        out VanillaBlueSlimeTargetRefresh refresh)
    {
        target = default;
        refresh = default;
        return context.TrySelectClosestTarget(in npc, in definition, out refresh) &&
            refresh.Target < byte.MaxValue &&
            context.TryFindCandidate(checked((byte)refresh.Target), out target) &&
            target.Active &&
            !target.Dead;
    }

    private static bool IsSalamander(NpcTypeId type) => type.Value is >= 498 and <= 506;

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
