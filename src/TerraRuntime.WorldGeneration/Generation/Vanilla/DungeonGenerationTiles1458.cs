using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary DungeonUtils cell operations; only unpublished generation components call these.</summary>
internal static class DungeonGenerationTiles1458
{
    // WorldGen.SetCrackedBrickSolidity(false) scopes MakeDungeon. Never mutate the live tile catalog.
    public static bool IsSolidType(TileTypeId type) => type.Value is not (481 or 482 or 483) && VanillaTileCollisionCatalog.IsSolid(type);
    public static bool SolidTile(in WorldTile tile) => tile.Type is not (481 or 482 or 483) && HellFortGenerator1458.Solid(tile, noDoors: false);
    public static void PlaceEntranceDoor(WorldTileStore tiles, IWorldGenerationVanillaRandom worldRandom, int x, int y, int style = 13)
    {
        // Entrance and global door builders clear this passage before placement. They share the inactive-anchor
        // PlaceTile(10) branch (ordinary styles13/16/17/18); no client placement or runtime door authority is involved.
        if (At(x, y).IsActive) throw new InvalidOperationException("Dungeon entrance door anchor was not cleared.");
        HellFortGenerator1458.ClearPlacementAnchor(ref At(x, y));
        int center;
        if (!At(x, y - 1).IsActive && !At(x, y - 2).IsActive && ActiveSolid(At(x, y - 3))) center = y - 1;
        else if (!At(x, y + 1).IsActive && !At(x, y + 2).IsActive && ActiveSolid(At(x, y + 3))) center = y + 1;
        else return;
        ref WorldTile ceiling = ref At(x, center - 2);
        if (!ceiling.IsActuated && ActiveSolid(ceiling) && DungeonGenerationTiles1458.SolidTile(At(x, center + 2)))
        {
            for (int row = 0; row < 3; row++)
            {
                ref WorldTile cell = ref At(x, center - 1 + row);
                cell.Flags |= WorldTileFlags.Active; cell.Type = 10;
                cell.FrameX = (short)(style / 36 * 54 + worldRandom.Next(3) * 18);
                cell.FrameY = (short)(style % 36 * 54 + row * 18);
            }
        }
        // TileFrame during generation clears only inactive block paint/coatings/slopes here. All active
        // neighbors are freshly built, full dungeon bricks or the complete door, never arbitrary objects.
        for (int tx = x - 1; tx <= x + 1; tx++)
        for (int ty = y - 1; ty <= y + 1; ty++)
        {
            ref WorldTile cell = ref At(tx, ty);
            if (cell.IsActive) continue;
            cell.Shape = 0; cell.TileColor = 0; cell.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        }
        ref WorldTile At(int tx, int ty) => ref tiles.Tiles[tiles.GetUncheckedIndex(tx, ty)];
    }
    private static bool ActiveSolid(in WorldTile tile) => tile.IsActive && DungeonGenerationTiles1458.IsSolidType(tile.TileType);

    public static bool CanPlaceHallBrick(in WorldTile tile, ushort crackedBrick) =>
        !IsDungeonWall(tile.Wall) || (tile.IsActive && !IsDungeonTile(tile.Type) && tile.Type != crackedBrick);

    public static bool IsDungeonWall(ushort wall)
    {
        if (!VanillaWallDefinitionCatalog.TryGet(new WallTypeId(wall), out VanillaWallDefinition definition))
            throw new InvalidOperationException("Unknown wall in dungeon generation.");
        return definition.IsDungeonWall;
    }

    public static bool IsDungeonTile(ushort type)
    {
        if (!VanillaTileDefinitionCatalog.TryGet(new TileTypeId(type), out VanillaTileDefinition definition))
            throw new InvalidOperationException("Unknown tile in dungeon generation.");
        // This pinned catalog profile is exactly Main.tileDungeon's six ordinary/ancient brick identities.
        return definition.MiningProfile == VanillaTileMiningProfile.DungeonBrick;
    }

    public static void SetBrick(ref WorldTile tile, ushort brick, bool reset)
    {
        if (reset) tile = default;
        tile.Flags |= WorldTileFlags.Active;
        tile.Shape = 0;
        tile.Type = brick;
    }

    public static void SetWall(ref WorldTile tile, ushort wall, bool reset)
    {
        if (reset) tile = default;
        tile.Wall = wall;
    }

    public static void ClearTile(ref WorldTile tile)
    {
        tile.Shape = 0;
        tile.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive);
    }
}
