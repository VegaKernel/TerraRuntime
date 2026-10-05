using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct RuntimeTownNpcMeleeIntent1458(NpcSnapshot Target,
    int Damage, float KnockBack, int Direction, int Immunity);

internal sealed partial class RuntimeTownNpcCombat1458
{
    internal void BeginWorldTick() => AdvanceMeleeImmunity();

    internal RuntimeNpcStinkyVisualOffer1458 PlanStinkyVisualOffer()
        => RuntimeNpcBuffStatus1458.PlanVisualOffer(random);

    internal RuntimeNpcStinkyVisualOffer1458 PlanBuffVisualOffers(in RuntimeNpcBuffPlan1458 plan)
        => RuntimeNpcBuffStatus1458.PlanVisualOffers(in plan, random, goodWorld: false);

    internal void RetainAcceptedBuffLife(in RuntimeNpcBuffPlan1458 plan, in NpcSnapshot accepted)
    {
        if (plan.DotDamage > 0) contactReplication?.RetainAcceptedBuffLife(plan.Expected, plan.LifeAfter, in accepted);
    }


    internal bool TryPlanActiveAttack(in NpcSnapshot source, in RuntimeTownNpcDanger1458 danger,
        ReadOnlySpan<NpcSnapshot> peers, Span<RuntimeTownNpcMeleeIntent1458> meleeIntents,
        out NpcStateUpdate update, out NpcAiProjectileIntent? projectile, out int meleeCount,
        out bool force)
    {
        projectile = null;
        meleeCount = 0;
        force = false;
        update = default;
        bool projectileAttack = VanillaTownNpcProjectileAttackCatalog1458.TryGet(source.TypeIdentity, out var shot);
        bool meleeAttack = VanillaTownNpcMeleeAttackCatalog1458.TryGet(source.TypeIdentity, out var swing);
        if (projectileAttack && source.Ai.Ai0 == shot.AttackState)
        {
            bool hardMode = IsComplete(VanillaWorldProgressionId.Hardmode);
            int direction = source.Simulation.SpriteDirection is -1 or 1 ? source.Simulation.SpriteDirection : 1;
            NpcAiState local = source.Simulation.LocalAi;
            NpcSimulationState simulation = source.Simulation;
            if (source.Ai.Ai1 == shot.AttackTime)
            {
                local = local with { Ai3 = 0f };
                simulation = simulation with { FrameCounter = 0d };
            }
            int elapsed = checked((int)local.Ai3 + 1);
            local = local with { Ai3 = elapsed };
            NpcAiState ai = source.Ai with { Ai1 = source.Ai.Ai1 - 1f };
            if (VanillaTownNpcProjectileAttackCatalog1458.ShouldFire(source.TypeIdentity, hardMode, elapsed))
            {
                NpcSnapshot target = danger.ThreatDirection == direction ? danger.AttackTarget : default;
                if (!TryPlanSourceProjectile(in source, in shot, hardMode, in target, direction,
                        out NpcAiProjectileIntent planned)) return false;
                projectile = planned;
                if (hardMode && source.TypeIdentity == VanillaNpcIds.ArmsDealer && elapsed != 1 && target.IsActive &&
                    VanillaTownNpcDefinitionCatalogBridge.TryGetCenter(in source, out float sx, out float sy) &&
                    VanillaTownNpcDefinitionCatalogBridge.TryGetCenter(in target, out float tx, out float ty))
                {
                    float dx = tx - sx, dy = ty - sy;
                    float length = MathF.Sqrt(dx * dx + dy * dy);
                    float vertical = dy * (1f / length);
                    if (vertical is >= -.5f and <= .5f) ai = ai with { Ai2 = vertical };
                }
            }
            if (ai.Ai1 <= 0f)
            {
                int recoveryBase = shot.RecoveryBase(hardMode), recoveryRandom = shot.RecoveryRandom(hardMode);
                int idle = recoveryBase + random.Next(recoveryRandom);
                int cooldown = recoveryBase / 2 + random.Next(recoveryRandom);
                ai = ai with { Ai0 = local.Ai2 == 8f && danger.WithinRange ? 8f : 0f,
                    Ai1 = idle, Ai2 = 0f };
                local = local with { Ai1 = cooldown, Ai3 = cooldown };
                force = true;
            }
            update = SnapshotUpdate(in source, ai, simulation with { LocalAi = local },
                source.VelocityX * .8f, source.VelocityY);
            return true;
        }
        if (!meleeAttack || source.Ai.Ai0 != 15f) return false;
        int facing = source.Simulation.SpriteDirection is -1 or 1 ? source.Simulation.SpriteDirection : 1;
        NpcAiState meleeAi = source.Ai with { Ai1 = source.Ai.Ai1 - 1f };
        NpcAiState meleeLocal = source.Simulation.LocalAi;
        NpcSimulationState meleeSimulation = source.Simulation;
        if (source.Ai.Ai1 == swing.AttackTime)
        {
            meleeLocal = meleeLocal with { Ai3 = 0f };
            meleeSimulation = meleeSimulation with { FrameCounter = 0d };
        }
        if (meleeDamage is not null && TryGetSwingRectangle(in source, swing.AttackTime * 2,
                checked((int)meleeAi.Ai1), facing, swing.HitboxWidth, swing.HitboxHeight, out var rectangle))
        {
            int baseDamage = swing.BaseDamage;
            float knockBack = swing.KnockBack;
            if (source.TypeIdentity == VanillaNpcIds.TaxCollector &&
                townNpcs.TryGet(checked((short)source.Handle.Slot), out WorldTownNpc resident) &&
                string.Equals(resident.GivenName, "Andrew", StringComparison.Ordinal))
            {
                baseDamage *= 2;
                knockBack *= 2f;
            }
            foreach (NpcSnapshot peer in peers)
            {
                if (!TryPlanMeleeTarget(in source, in peer, in rectangle)) continue;
                if (meleeCount >= meleeIntents.Length) return false;
                meleeIntents[meleeCount++] = new(peer, GetAttackDamage(baseDamage), knockBack, facing,
                    Math.Max(1, checked((int)meleeAi.Ai1 + 2)));
            }
        }
        if (meleeAi.Ai1 <= 0f)
        {
            bool repeat = false;
            if (danger.WithinRange && VanillaTownNpcDefinitionCatalogBridge.TryGetCenter(in source, out float sx, out float sy) &&
                (meleeLocal.Ai2 == 8f || !VanillaWorldCanHit.HasLineOfSight(tiles, sx, sy, 1, 1,
                    sx - danger.ThreatDirection * 32f, sy, 1, 1)))
            {
                NpcStateUpdate sourceState = SnapshotUpdate(in source, source.Ai, source.Simulation,
                    source.VelocityX, source.VelocityY);
                NpcSnapshot target = danger.AttackTarget;
                if (!CanHitAttackTarget(in sourceState, in target, false))
                    target = danger.ThreatDirection == 1 ? danger.LeftAttack : danger.RightAttack;
                if (CanHitAttackTarget(in sourceState, in target, false))
                {
                    repeat = true;
                    meleeAi = meleeAi with { Ai0 = 15f, Ai1 = swing.AttackTime, Ai2 = 0f };
                    meleeLocal = meleeLocal with { Ai3 = 0f };
                    meleeSimulation = meleeSimulation with { DirectionX = source.PositionX < target.PositionX ? 1 : -1 };
                }
            }
            if (!repeat)
            {
                int idle = swing.RecoveryBase + random.Next(swing.RecoveryRandom);
                int cooldown = swing.RecoveryBase / 2 + random.Next(swing.RecoveryRandom);
                meleeAi = meleeAi with { Ai0 = meleeLocal.Ai2 == 8f && danger.WithinRange ? 8f : 0f,
                    Ai1 = idle, Ai2 = 0f };
                meleeLocal = meleeLocal with { Ai1 = cooldown, Ai3 = cooldown };
            }
            force = true;
        }
        update = SnapshotUpdate(in source, meleeAi, meleeSimulation with { LocalAi = meleeLocal },
            source.VelocityX * .8f, source.VelocityY);
        return true;
    }

    private bool TryPlanMeleeTarget(in NpcSnapshot source, in NpcSnapshot target,
        in VanillaTownNpcSwingRectangle1458 rectangle)
    {
        if (!target.IsActive || target.Handle == source.Handle || target.Simulation.DontTakeDamage ||
            target.Simulation.Friendly != false ||
            !VanillaNpcDefinitionCatalog.TryGet(target.TypeIdentity, target.NetIdentity, out var definition) ||
            (target.Simulation.DamageOverride ?? definition.Damage) <= 0 ||
            !definition.TryResolveHitbox(target.Simulation, out var targetBody) ||
            !VanillaTownNpcDefinitionCatalogBridge.TryGetHitbox(in source, out var sourceBody) ||
            !rectangle.Intersects((int)target.PositionX, (int)target.PositionY, targetBody.Width, targetBody.Height)) return false;
        int slot = target.Handle.Slot;
        if (meleeImmuneGenerations[slot] == target.Handle.Generation.Value && meleeImmuneTicks[slot] > 0) return false;
        return target.Simulation.NoTileCollide || VanillaWorldCanHit.HasLineOfSight(tiles,
            source.PositionX, source.PositionY, sourceBody.Width, sourceBody.Height,
            target.PositionX, target.PositionY, targetBody.Width, targetBody.Height);
    }

    private bool TryPlanSourceProjectile(in NpcSnapshot source,
        in VanillaTownNpcProjectileAttackProfile1458 profile, bool hardMode,
        in NpcSnapshot target, int direction, out NpcAiProjectileIntent intent)
    {
        intent = default;
        if (!VanillaTownNpcDefinitionCatalogBridge.TryGetCenter(in source, out float sx, out float sy)) return false;
        float vx = 0f;
        float vy = profile.Kind == VanillaTownNpcProjectileAttackKind1458.Lobbed ? -1f : 0f;
        if (target.IsActive && npcs.TryGet(target.Handle, out var live) && live.Revision == target.Revision &&
            VanillaTownNpcDefinitionCatalogBridge.TryGetCenter(in target, out float tx, out float ty))
        {
            float dx = tx - sx, dy = ty - sy;
            if (profile.Kind == VanillaTownNpcProjectileAttackKind1458.Lobbed)
                dy = ty + -profile.AimOffsetY * Math.Clamp(MathF.Sqrt(dx * dx + dy * dy) / profile.DangerDetectRange, 0f, 1f) - sy;
            else dy = ty - profile.AimOffsetY - sy;
            float length = MathF.Sqrt(dx * dx + dy * dy);
            float inverseLength = 1f / length;
            vx = dx * inverseLength;
            vy = dy * inverseLength;
        }
        if (!float.IsFinite(vx) || !float.IsFinite(vy) || Math.Sign(vx) != direction)
        {
            vx = direction;
            vy = profile.Kind == VanillaTownNpcProjectileAttackKind1458.Lobbed ? -1f : 0f;
        }
        // Source Utils.RandomVector2 is an actual two-component spread offer even when spread is zero.
        vx = vx * profile.ProjectileSpeed + random.NextFloat(-profile.Spread, profile.Spread);
        vy = vy * profile.ProjectileSpeed + random.NextFloat(-profile.Spread, profile.Spread);
        intent = new(profile.Projectile(hardMode), sx + direction * 16f, sy - 2f, vx, vy,
            GetAttackDamage(profile.BaseDamage(hardMode)), profile.KnockBack);
        return true;
    }

    internal void ApplyCommittedEffects(in NpcSnapshot committed, NpcAiProjectileIntent? projectile,
        ReadOnlySpan<RuntimeTownNpcMeleeIntent1458> meleeIntents)
    {
        if (!npcs.TryGet(committed.Handle, out var liveSource) || liveSource.Revision != committed.Revision) return;
        if (projectile is NpcAiProjectileIntent shot &&
            TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(shot.Type, out var definition))
        {
            // AI007 offers a center to Projectile.NewProjectile; the physical store owns top-left coordinates.
            NpcAiProjectileIntent physical = shot with { PositionX = shot.PositionX - definition.Width / 2,
                PositionY = shot.PositionY - definition.Height / 2 };
            RuntimeNpcProjectileIntentApplier.TryApply(projectiles, committed.Handle, in physical, out _);
        }
        if (meleeDamage is null) return;
        foreach (RuntimeTownNpcMeleeIntent1458 intent in meleeIntents)
        {
            if (!npcs.TryGet(intent.Target.Handle, out var liveTarget) || liveTarget.Revision != intent.Target.Revision) continue;
            if (meleeDamage.TryStrike(committed.Handle, intent.Target.Handle, intent.Damage,
                    intent.KnockBack, intent.Direction) != RuntimeTownNpcMeleeDamageResult1458.Rejected)
            {
                NpcSnapshot target = intent.Target;
                SetMeleeImmunity(in target, intent.Immunity);
            }
        }
    }
}
