using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeProjectilePlayerCombatPass
{
    private bool IsGroundCombatShotCurrent(in ProjectileSnapshot shot, in ProjectileLifecycleState life,
        NpcHandle source, NpcSnapshot? capturedSource) =>
        projectiles.TryGet(shot.Handle, out var current) && current == shot &&
        projectiles.TryGetLifecycle(shot.Handle, out var currentLife) && currentLife == life &&
        projectiles.TryGetServerNpcSource(shot.Handle, out var currentSource) && currentSource == source &&
        (capturedSource is not { } expected || npcs.TryGet(source, out var live) && live == expected);

    private void RetireGroundCombatPlayerHit(in ProjectileSnapshot shot, in ProjectileLifecycleState life,
        NpcHandle source)
    {
        if (!VanillaGroundCombatPlayerHit1458.ConsumesPlayerPenetration(shot.Type) ||
            terminationEffects is null || !projectiles.TryGet(shot.Handle, out var current) || current != shot ||
            !projectiles.TryGetLifecycle(shot.Handle, out var currentLife) || currentLife != life ||
            !projectiles.TryGetServerNpcSource(shot.Handle, out var currentSource) || currentSource != source)
            return;
        bool trusted = projectiles.IsCombatTrusted(shot.Handle);
        if (!projectiles.TryConsumeCombatHitPenetration(shot.Handle, out bool removed, out var final) || !removed) return;
        ConsumedProjectiles++;
        var termination = new ProjectileTerminationCommit(shot, final,
            ProjectileSimulationTerminationReason.BehaviorKill, trusted, default, source)
        { NpcProjectile = life.NpcProjectile };
        terminationEffects.ProjectileTerminated(in termination);
    }
}
