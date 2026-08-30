using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Converts a target selected by a future projectile/NPC collision pass into a generation-safe combat intent.
/// It deliberately does not perform collision queries, damage variation, immunity, penetration or mutation.
/// Server-owned projectile provenance remains fail-closed until an NPC/source handle is stored with the projectile.
/// </summary>
public static class RuntimeProjectileCombatIntentFactory
{
    public static bool TryCreateNpcHit(
        in ProjectileSnapshot projectile,
        NpcHandle target,
        IRuntimePlayerSlotSnapshotLookup players,
        out ProjectileNpcHitIntent intent)
    {
        ArgumentNullException.ThrowIfNull(players);

        if (!projectile.IsActive ||
            !target.IsAssigned ||
            projectile.Damage <= 0 ||
            !float.IsFinite(projectile.KnockBack) ||
            projectile.KnockBack < 0f ||
            !VanillaProjectileOwnership.IsPlayerOwned(projectile.Spawner))
        {
            intent = default;
            return false;
        }

        var ownerSlot = new PlayerSlotId(projectile.Spawner);
        if (!players.TryGetPlayer(ownerSlot, out PlayerStateSnapshot owner) ||
            !owner.Player.IsAssigned ||
            owner.Player.Slot != ownerSlot)
        {
            intent = default;
            return false;
        }

        intent = new ProjectileNpcHitIntent(
            target,
            DamageSource.FromPlayerProjectile(owner.Player, projectile.Handle),
            projectile.Damage,
            projectile.KnockBack);
        return intent.IsValid;
    }
}
