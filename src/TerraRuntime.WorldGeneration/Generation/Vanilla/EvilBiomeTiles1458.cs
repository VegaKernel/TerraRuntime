using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Generation-only CanEvilReplace and chasm excavation; never a live tile authority.</summary>
internal static class EvilBiomeTiles1458
{
    internal static void PlaceOrb(WorldTileStore store, int x, int y, bool crimson)
    {
        // WorldGen.AddShadowOrb: a 2x2 object, preserving all fields except identity/activity/frames.
        if (x < 10 || x > store.Dimensions.WidthTiles - 10 || y < 10 || y > store.Dimensions.HeightTiles - 10) return;
        for (int tx = x - 1; tx <= x; tx++)
        for (int ty = y - 1; ty <= y; ty++)
            if (store.Get(tx, ty).IsActive && store.Get(tx, ty).Type == 31) return;
        for (int tx = x - 1; tx <= x; tx++)
        for (int ty = y - 1; ty <= y; ty++)
        {
            ref WorldTile tile = ref store.Tiles[store.GetUncheckedIndex(tx, ty)];
            tile.Flags |= WorldTileFlags.Active; tile.Type = 31;
            tile.FrameX = (short)((crimson ? 36 : 0) + (tx - x + 1) * 18);
            tile.FrameY = (short)((ty - y + 1) * 18);
        }
    }

    public static bool CanReplace(in WorldTile tile)
    {
        // WorldGen.CanEvilReplace, Main.tileDungeon and TileID.Sets.CrackedBricks, 1.4.5.8.
        // Inactive stored material is ignored by the source, but walls remain protective.
        if (tile.IsActive && (tile.Type >= VanillaTileIds.Count ||
            tile.Type is 41 or 43 or 44 or 677 or 678 or 679 or 481 or 482 or 483))
            return false;
        return VanillaWallDefinitionCatalog.TryGet(new WallTypeId(tile.Wall), out var wall) &&
            !wall.IsDungeonWall;
    }

    public static void Excavate(ref WorldTile tile, bool crimson = false)
    {
        // ChasmRunner/ChasmRunnerSideways preserve ore/orb identities even when inactive;
        // CrimStart does not have that additional exclusion. Geometry/walls belong to the caller.
        // active(false) does not erase frame, slope, paint, liquid or retained material bytes.
        if (CanReplace(tile) && (crimson || tile.Type is not (31 or 22 or 204)))
            tile.Flags &= ~WorldTileFlags.Active;
    }
}
