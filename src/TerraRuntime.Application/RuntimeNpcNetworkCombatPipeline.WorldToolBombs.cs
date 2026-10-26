using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    // Reuse the retained strike/death prelude with a real environment-projectile
    // source. The ordinary player intent builder correctly refuses owner255;
    // no synthetic player/NPC handle or player interaction credit is created.
    internal RuntimeProjectileNpcDamageResult TryStrikePreparedWorldToolBomb(
        RuntimeProjectileStore store, in ProjectileSnapshot projectile, in NpcSnapshot target,
        int hitDirection, int damageValue, VanillaUnifiedRandom1458 before, VanillaUnifiedRandom1458 after,
        Func<bool> isCurrent, Action<NpcSnapshot> adoptAndPublish)
    {
        if (projectile.Type.Value != 28 || projectile.Spawner != byte.MaxValue || projectile.Damage != 100 ||
            projectile.OriginalDamage != 0 || projectile.KnockBack != 8f || hitDirection is not (-1 or 1) ||
            damageValue <= 0 || target.TypeIdentity.Value is not (1 or 3 or 59) ||
            !store.IsCombatTrusted(projectile.Handle) || store.TryGetCombatTrustedOwner(projectile.Handle, out _) ||
            store.TryGetServerNpcSource(projectile.Handle, out _) ||
            !store.TryGet(projectile.Handle, out var current) || current != projectile ||
            !store.TryGetLifecycle(projectile.Handle, out var life) || !life.NpcProjectile ||
            !life.WorldToolBombKillInProgress || life.TimeLeft != 0 ||
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
