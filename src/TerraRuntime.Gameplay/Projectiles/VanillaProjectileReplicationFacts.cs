using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

/// <summary>
/// Version-pinned packet-27 behavior for server-owned projectiles. TerrariaServer 1.4.5.8 sends a projectile
/// creation packet independently, while <c>Projectile.Update</c> sends a later packet only when its source code
/// requests <c>netUpdate</c>.
/// </summary>
public static class VanillaProjectileReplicationFacts
{
    /// <summary>
    /// Returns false only for source-verified ordinary-flight commits that must remain local to the server until
    /// a separately admitted urgent intent occurs. Spawns, despawns, and client-originated packet relays are not
    /// governed by this fact.
    /// </summary>
    public static bool PublishesOrdinaryServerUpdate(ProjectileTypeId type) =>
        type != VanillaProjectileIds.SkeletronPrimeBomb &&
        type != VanillaProjectileIds.RetinazerDeathLaser &&
        // Projectile.AI styles 14, 38 and 23 do not request an ordinary update for these traps.
        // HandleMovement's spiky-ball bounce also has no netUpdate offer. Flame Kill still sends 29.
        type.Value is not (185 or 187 or 188 or VanillaWiringTrap1458.GeyserProjectileType);
}
