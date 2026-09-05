using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// TerrariaServer 1.4.5.8 pick-power policy for authoritative mining. Tile identities are classified once by
/// <see cref="VanillaTileDefinitionCatalog"/>; this policy applies only the position-sensitive world rules and never
/// reconstructs tile semantics from raw numeric IDs.
/// </summary>
internal static class VanillaTileMiningRequirements1458
{
    private const int VanillaUnderworldLayerOffsetTiles = 200;

    public static bool CanMine(
        WorldTileStore tiles,
        int tileX,
        int tileY,
        TileTypeId tileType,
        short pickPower)
    {
        ArgumentNullException.ThrowIfNull(tiles);

        if (!VanillaTileDefinitionCatalog.TryGet(tileType, out VanillaTileDefinition definition) ||
            !definition.IsBreakableByPick)
        {
            return false;
        }

        double worldSurface = tiles.WorldSurfaceTiles ?? tiles.Dimensions.HeightTiles * 0.3d;
        int required = definition.MiningProfile switch
        {
            VanillaTileMiningProfile.Standard => 0,
            VanillaTileMiningProfile.EvilStone => 65,
            VanillaTileMiningProfile.Meteorite => 50,
            VanillaTileMiningProfile.Obsidian => 55,
            VanillaTileMiningProfile.Hellstone => 65,
            VanillaTileMiningProfile.DemoniteCrimtaneDepthSensitive => tileY > worldSurface ? 55 : 0,
            VanillaTileMiningProfile.HellforgeDepthSensitive =>
                tileY >= tiles.Dimensions.HeightTiles - VanillaUnderworldLayerOffsetTiles ? 65 : 0,
            VanillaTileMiningProfile.DungeonBrick =>
                IsProtectedDungeonDepth(tiles, tileX, tileY, worldSurface) ? 100 : 0,
            VanillaTileMiningProfile.CobaltTier => 100,
            VanillaTileMiningProfile.MythrilTier => 110,
            VanillaTileMiningProfile.AdamantiteTier => 150,
            VanillaTileMiningProfile.Chlorophyte => 200,
            VanillaTileMiningProfile.LihzahrdTemple => 210,
            VanillaTileMiningProfile.Unbreakable => int.MaxValue,
            _ => int.MaxValue
        };

        return pickPower >= required;
    }

    private static bool IsProtectedDungeonDepth(
        WorldTileStore tiles,
        int tileX,
        int tileY,
        double worldSurface) =>
        tileY > worldSurface &&
        (tileX < tiles.Dimensions.WidthTiles * 0.35d || tileX > tiles.Dimensions.WidthTiles * 0.65d);
}
