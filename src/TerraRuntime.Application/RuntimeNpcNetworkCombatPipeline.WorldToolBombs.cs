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
        bool landMine = projectile.Type.Value == 164;
        if (projectile.Type.Value is not (28 or 164) || projectile.Spawner != byte.MaxValue ||
            projectile.Damage != (landMine ? VanillaLandMine1458.Damage : 100) ||
            projectile.OriginalDamage != 0 || projectile.KnockBack != (landMine ? VanillaLandMine1458.KnockBack : 8f) ||
            !store.TryGetLifecycle(projectile.Handle, out var life) || life.NpcProjectile == landMine ||
            !life.WorldToolBombKillInProgress || life.TimeLeft != 0)
            return RuntimeProjectileNpcDamageResult.Rejected;
        return TryStrikePreparedEnvironmentProjectile(store, in projectile, in target, hitDirection,
            damageValue, before, after, isCurrent, adoptAndPublish, deferOrdinaryUpdate: landMine);
    }
}
