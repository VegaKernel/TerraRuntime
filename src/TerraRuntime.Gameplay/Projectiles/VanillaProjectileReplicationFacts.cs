using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

/// <summary>
/// Version-pinned packet-27 behavior for server-owned projectiles. TerrariaServer 1.4.5.8 sends a projectile
/// creation packet independently, while <c>Projectile.Update</c> sends a later packet only when its source code
/// requests <c>netUpdate</c>. Prime bomb 102 and Prime laser 100 do not request it during their ordinary flight.
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
        type != VanillaProjectileIds.RetinazerDeathLaser;
}
