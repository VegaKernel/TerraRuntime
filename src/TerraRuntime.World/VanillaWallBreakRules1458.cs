using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 Player.CanPlayerSmashWall and WorldGen.KillWall progression gates.
/// The caller supplies progression facts because the wall layer must not infer boss state from geometry or wall ids.
/// </summary>
public static class VanillaWallBreakRules1458
{
    public static bool CanPlayerSmashWall(WorldTileStore tiles, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        WorldTile tile = tiles.Get(x, y);
        WallTypeId wall = tile.WallType;
        if (wall == VanillaWallIds.None || wall == VanillaWallIds.UnbreakableTemple)
            return false;
        if (!VanillaWallDefinitionCatalog.TryGet(wall, out VanillaWallDefinition definition))
            return false;
        if (definition.IsHousingWall)
            return true;

        for (int nx = x - 1; nx <= x + 1; nx++)
        {
            for (int ny = y - 1; ny <= y + 1; ny++)
            {
                WallTypeId neighbor = tiles.Get(nx, ny).WallType;
                if (neighbor == VanillaWallIds.None)
                    return true;
                if (VanillaWallDefinitionCatalog.TryGet(neighbor, out VanillaWallDefinition neighborDefinition) &&
                    neighborDefinition.IsHousingWall)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool IsProgressionUnlocked(
        WallTypeId wall,
        bool skeletronDowned,
        bool golemDowned)
    {
        if (!VanillaWallDefinitionCatalog.TryGet(wall, out VanillaWallDefinition definition))
            return false;
        if (definition.IsDungeonWall && !skeletronDowned)
            return false;
        return wall != VanillaWallIds.LihzahrdBrickUnsafe || golemDowned;
    }
}
