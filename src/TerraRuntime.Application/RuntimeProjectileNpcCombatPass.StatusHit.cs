using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeProjectileNpcCombatPass
{
    private bool TryPreparedStatusHit(ProjectileSnapshot projectile, NpcSnapshot target,
        VanillaPlayerCombatSnapshot ownerCombat, RuntimePlayerProjectileUseCapture? owner,
        RuntimeNpcBuffAdditionPlan1458? capturedStatus, int ownerRow, long tick, bool sharedImmunity, bool localImmunity,
        out ProjectileSnapshot current, out bool ended, out NpcSnapshot? acceptedTarget)
    {
        current = projectile;
        ended = false;
        acceptedTarget = null;
        if (!usesSourceRandom || status is null || owner is null || owner.Buffs is null ||
            capturedStatus is null || !capturedStatus.IsCurrent ||
            owner.Player.Luck != 0 || !players.IsCurrentProjectileUse(owner) ||
            target.TypeIdentity.Value is not (3 or 1 or 59) ||
            target.Simulation.LifeRegenCounter is null || target.Simulation.Immortal != false ||
            target.TypeIdentity.Value == 3 && (target.Simulation.ShimmerTransparency != 0f ||
                target.Simulation.LiquidContact == NpcLiquidContactKind.Shimmer) ||
            target.TypeIdentity.Value == 1 && target.Ai.Ai1 == 1345f ||
            !projectiles.TryPrepareNpcHit(in projectile, out var projectilePlan) || projectilePlan is null)
            return false;
        if (!HasSupportedNpcHitBuffs(owner.Buffs)) return false;
        var before = sourceRandom.Clone();
        var after = before.Clone();
        int crit = after.Next(1, 101);
        int variation = after.Next(-15, 16);
        if (!VanillaCombatFacts.TryResolvePveHit(projectile.Type, projectile.Damage, in ownerCombat,
                crit, variation, out var hit)) return false;
        // Damage_PVE_Inner chooses a hit point even when SpawnHitVisuals has no branch for this type.
        _ = after.NextDouble();
        _ = after.NextDouble();
        ProjectileNpcStatusAddition1458? addition = null;
        // Damage_PVE_Inner chooses the hit point for these ordinary arrows and plain bullet,
        // while StatusNPC contributes no intrinsic buff or random offer for 1/4/5/14.
        // Their Update/Kill random offers belong to a separate lifecycle boundary.
        if (projectile.Type.Value is not (1 or 4 or 5 or 14) &&
            !VanillaProjectileNpcStatus1458.TrySelect(projectile.Type, after.Next, out addition)) return false;
        RuntimeNpcBuffAdditionPlan1458 buffPlan;
        if (addition is { } value)
        {
            if (!status.TryPlanAddition(in target, value.Type, value.Duration, out buffPlan)) return false;
        }
        else if (!status.CaptureAddition(in target, out buffPlan)) return false;
        int immunityIndex = ownerRow + target.Handle.Slot;
        var immunityGeneration = lastOwnerNpcHitGeneration[immunityIndex];
        long immunityTick = lastOwnerNpcHitTick[immunityIndex];
        var ownerGeneration = ownerGenerations[projectile.Spawner];
        var localCheckpoint = localNpcImmunity.Capture(projectile.Handle, target.Handle);
        bool Current() => players.IsCurrentProjectileUse(owner) && projectilePlan.IsCurrent && buffPlan.IsCurrent &&
            ownerGenerations[projectile.Spawner] == ownerGeneration &&
            lastOwnerNpcHitGeneration[immunityIndex] == immunityGeneration &&
            lastOwnerNpcHitTick[immunityIndex] == immunityTick && localNpcImmunity.IsCurrent(in localCheckpoint);
        ProjectileSnapshot adopted = projectile;
        bool despawned = false;
        void AdoptAndPublish(NpcSnapshot accepted)
        {
            if (!status.TryAdoptAddition(buffPlan, in accepted) ||
                !projectilePlan.TryAdoptUnpublished(out adopted, out despawned))
                throw new InvalidOperationException("An admitted NPC hit lost its retained status or projectile owner.");
            if (sharedImmunity) MarkOwnerNpcCooldown(ownerRow, target.Handle, tick);
            if (localImmunity) localNpcImmunity.MarkHit(projectile.Handle, target.Handle, tick);
            CommittedHits++;
            if (accepted.Simulation.Life == 0) Kills++;
            if (despawned) ConsumedProjectiles++;
            status.PublishAddition(buffPlan);
        }
        int direction = projectile.VelocityX > .01f ? 1 : projectile.VelocityX < -.01f ? -1 : 0;
        var result = combat.TryStrikePreparedProjectile(in projectile, in target, direction,
            hit.Damage, hit.ArmorPenetration, hit.Critical, before, after, Current, AdoptAndPublish);
        if (result == RuntimeProjectileNpcDamageResult.Rejected) return false;
        projectilePlan.TryPublish();
        if (status.IsAcceptedAdditionCurrent(buffPlan)) acceptedTarget = buffPlan.Accepted;
        current = adopted;
        ended = despawned || !projectiles.TryGet(adopted.Handle, out var retained) || retained != adopted;
        return true;
    }
    // Original UpdateBuffs writes for these represented inputs introduce no intrinsic
    // StatusNPC/SpawnHitVisuals flag. Their class crit is captured once; Archery/Wrath
    // damage and speed already belong to the retained phase/launch boundary.
    private static bool HasSupportedNpcHitBuffs(ReadOnlySpan<TerraRuntime.Contracts.Gameplay.BuffTypeId> buffs)
    {
        foreach (var buff in buffs)
            if (buff.Value is not (0 or 5 or 16 or 93 or 112 or 114 or 115 or 117 or 321)) return false;
        return true;
    }

}
