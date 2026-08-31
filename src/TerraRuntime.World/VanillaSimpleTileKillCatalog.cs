using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Version-pinned TerrariaServer 1.4.5.8 subset whose generic single-cell <c>KillTile</c> storage transition is
/// completely modelled by <see cref="VanillaWorldTileMutationService"/>. This is a mutation-capability catalog, not
/// a pick-power table: held-tool authority remains owned by the gameplay caller.
///
/// The pinned <c>Player.GetPickaxeDamage</c>, <c>Player.DoesPickTargetTransformOnKill</c> and
/// <c>WorldGen.CanKillTile</c> source contracts prove that many apparently ordinary tiles have hard pick-power,
/// transform, frame, support, container or world-position semantics. They must therefore fail closed here until
/// those semantics are implemented instead of being approximated as raw cell clearing.
/// </summary>
public static class VanillaSimpleTileKillCatalog
{
    /// <summary>
    /// Current end-to-end simple-removal slice. These identities also have source-backed production drop mappings.
    /// </summary>
    public static bool IsSupported(TileTypeId tileType) =>
        tileType == VanillaTileIds.Dirt ||
        tileType == VanillaTileIds.Stone ||
        tileType == VanillaTileIds.Sand;
}
