using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

/// <summary>
/// Test-only fixture mutations. Production gameplay rules stay pure; authoritative storage commits go through the
/// same semantic mutation service used by the live world owner.
/// </summary>
internal static class WorldTileTestMutations
{
    public static bool TryPlaceDirtOnEmpty(WorldTileStore tiles, int x, int y)
    {
        if (!VanillaDirtRules1458.CanPlaceOnEmpty(tiles, x, y))
            return false;

        var mutations = new VanillaWorldTileMutationService(tiles);
        var request = new WorldTileMutationRequest(
            WorldTileMutationKind.PlaceTile,
            x,
            y,
            TileType: VanillaTileIds.Dirt);
        return mutations.Apply(in request).Applied;
    }

    public static bool TryKillIsolatedDirt(WorldTileStore tiles, int x, int y)
    {
        if (!VanillaDirtRules1458.CanKillIsolated(tiles, x, y))
            return false;

        var mutations = new VanillaWorldTileMutationService(tiles);
        var request = new WorldTileMutationRequest(WorldTileMutationKind.KillTile, x, y);
        return mutations.Apply(in request).Applied;
    }
}
