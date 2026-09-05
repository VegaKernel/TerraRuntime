using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Projectiles;

/// <summary>
/// Converts a target selected by a future projectile/NPC collision pass into a generation-safe combat intent.
/// It deliberately does not perform collision queries, damage variation, immunity, penetration or mutation.
/// Server-owned projectile provenance remains fail-closed until an NPC/source handle is stored with the projectile.
/// </summary>
public static class ProjectileNpcHitIntentBuilder
{
    public static bool TryCreateNpcHit(
        in ProjectileSnapshot projectile,
        NpcHandle target,
        int hitDirection,
        IRuntimePlayerSlotSnapshotLookup players,
        out ProjectileNpcHitIntent intent) =>
        TryCreateNpcHit(
            in projectile,
            target,
            hitDirection,
            projectile.Damage,
            armorPenetration: 0,
            critical: false,
            players,
            out intent);

    public static bool TryCreateNpcHit(
        in ProjectileSnapshot projectile,
        NpcHandle target,
        int hitDirection,
        int authoritativeDamage,
        int armorPenetration,
        bool critical,
        IRuntimePlayerSlotSnapshotLookup players,
        out ProjectileNpcHitIntent intent)
    {
        ArgumentNullException.ThrowIfNull(players);

        if (!projectile.IsActive ||
            !target.IsAssigned ||
            authoritativeDamage <= 0 ||
            armorPenetration < 0 ||
            !float.IsFinite(projectile.KnockBack) ||
            projectile.KnockBack < 0f ||
            hitDirection is < -1 or > 1 ||
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
            authoritativeDamage,
            projectile.KnockBack,
            hitDirection,
            armorPenetration,
            critical);
        return intent.IsValid;
    }
}
