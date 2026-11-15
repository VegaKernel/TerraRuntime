using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    internal RuntimeProjectileNpcDamageResult TryStrikePreparedWiringDart(
        RuntimeProjectileStore store, in ProjectileSnapshot projectile, in NpcSnapshot target,
        int hitDirection, int damageValue, VanillaUnifiedRandom1458 before, VanillaUnifiedRandom1458 after,
        Func<bool> isCurrent, Action<NpcSnapshot> adoptAndPublish)
    {
        if (projectile.Type.Value is not (98 or 184 or 980) || projectile.OriginalDamage != 0 || projectile.KnockBack != 2f ||
            projectile.Damage != (projectile.Type.Value == 98 ? 20 : projectile.Type.Value == 184 ? 40 : 30) ||
            !store.TryGetLifecycle(projectile.Handle, out var life) || life.NpcProjectile || life.TimeLeft <= 0)
            return RuntimeProjectileNpcDamageResult.Rejected;
        return TryStrikePreparedEnvironmentProjectile(store, in projectile, in target, hitDirection,
            damageValue, before, after, isCurrent, adoptAndPublish);
    }

    private RuntimeProjectileNpcDamageResult TryStrikePreparedEnvironmentProjectile(
        RuntimeProjectileStore store, in ProjectileSnapshot projectile, in NpcSnapshot target,
        int hitDirection, int damageValue, VanillaUnifiedRandom1458 before, VanillaUnifiedRandom1458 after,
        Func<bool> isCurrent, Action<NpcSnapshot> adoptAndPublish)
    {
        if (projectile.Spawner != byte.MaxValue || hitDirection is not (-1 or 1) || damageValue <= 0 ||
            target.TypeIdentity.Value is not (1 or 3 or 59) ||
            !store.IsCombatTrusted(projectile.Handle) || store.TryGetCombatTrustedOwner(projectile.Handle, out _) ||
            store.TryGetServerNpcSource(projectile.Handle, out _) ||
            !store.TryGet(projectile.Handle, out var current) || current != projectile ||
            pendingProjectileStrike is not null || pendingDeathPlan is not null || pendingDebuffPrelude is not null ||
            !random.SourceRandom.HasSameState(before) || !isCurrent() ||
            !npcs.TryGet(target.Handle, out var retained) || retained != target)
            return RuntimeProjectileNpcDamageResult.Rejected;
        var strike = new ProjectileStrikePlan1458(before, after, isCurrent, target.Handle, default);
        pendingProjectileStrike = strike;
        bool Current() => IsPreparedDeathCurrent() && isCurrent() && random.SourceRandom.HasSameState(before);
        void Publish(NpcSnapshot committed)
        {
            AdoptPreparedStrikeOwners(strike.AcceptedDeath);
            adoptAndPublish(committed);
        }
        var prelude = new NpcDamagePrelude1458(target, null, Current, Publish);
        try
        {
            var result = CommitNonPlayerDamage(target, DamageSource.FromEnvironmentProjectile(projectile.Handle),
                damageValue, projectile.KnockBack, hitDirection, prelude);
            return result switch
            {
                RuntimeTownNpcMeleeDamageResult1458.Committed => RuntimeProjectileNpcDamageResult.Committed,
                RuntimeTownNpcMeleeDamageResult1458.Killed => RuntimeProjectileNpcDamageResult.Killed,
                _ => RuntimeProjectileNpcDamageResult.Rejected
            };
        }
        finally
        {
            pendingProjectileStrike = null;
            preparedDeathPreludePublication = null;
            CancelPendingDeathPlan();
        }
    }
}
