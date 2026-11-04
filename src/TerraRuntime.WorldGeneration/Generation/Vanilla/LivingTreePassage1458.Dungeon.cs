using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class LivingTreePassage1458
{
    /// <summary>The dungeon tree's bounded attachment shaft; ordinary living-tree rooms and loot remain separate.</summary>
    internal void MakeDungeonPassage(int groundY, int attachmentY, int trunkWidth, ref int left, ref int right, int platformStyle)
    {
        int originalLeft = left, originalRight = right, rows = 0;
        bool firstLanding = true;
        _ = random.Next(5, 16);
        for (int y = groundY - 5; y <= attachmentY; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            int middle = (left + right) / 2;
            int padding = y > groundY && trunkWidth <= 4 ? 2 : 1;
            for (int x = left - padding; x <= right + padding; x++)
            {
                if (x > middle - 2 && x <= middle + 1)
                {
                    if (y > groundY - 4)
                    {
                        if (DungeonTile(x, y).Type is not (19 or 15 or 304 or 21 or 10) &&
                            DungeonTile(x, y - 1).Type is not (15 or 304 or 21 or 10) && DungeonTile(x, y + 1).Type != 10)
                            ClearDungeonTileAndPaint(x, y);
                        if (!DungeonGenerationTiles1458.IsDungeonWall(DungeonTile(x, y).Wall)) DungeonTile(x, y).Wall = LivingWoodWall;
                        if (!DungeonGenerationTiles1458.IsDungeonWall(DungeonTile(x - 1, y).Wall) && (DungeonTile(x - 1, y).Wall > 0 || y >= worldSurface))
                            DungeonTile(x - 1, y).Wall = LivingWoodWall;
                        if (!DungeonGenerationTiles1458.IsDungeonWall(DungeonTile(x + 1, y).Wall) && (DungeonTile(x + 1, y).Wall > 0 || y >= worldSurface))
                            DungeonTile(x + 1, y).Wall = LivingWoodWall;
                        if (y == groundY)
                        {
                            ClearDungeonTileAndPaint(x, y + 1);
                            PlacePlatform(x, y + 1, 23);
                        }
                    }
                }
                else
                {
                    if (DungeonTile(x, y).Type is not (15 or 304 or 21 or 10) && DungeonTile(x - 1, y).Type != 10 && DungeonTile(x + 1, y).Type != 10)
                    {
                        if (!DungeonGenerationTiles1458.IsDungeonWall(DungeonTile(x, y).Wall))
                        {
                            ref WorldTile wood = ref DungeonTile(x, y);
                            wood.Type = LivingWood;
                            wood.Flags |= WorldTileFlags.Active;
                            wood.ClearSourceShape();
                        }
                        if (DungeonTile(x - 1, y).Type == Sandstone) DungeonTile(x - 1, y).Type = 0;
                        if (DungeonTile(x + 1, y).Type == Sandstone) DungeonTile(x + 1, y).Type = 0;
                    }
                    if (y <= groundY && y > groundY - 4 && x > left - padding && x <= right + padding - 1)
                        DungeonTile(x, y).Wall = LivingWoodWall;
                }
            }
            if (++rows < 6) continue;
            rows = 0;
            int shift = random.Next(3);
            if (shift == 0) shift = -1;
            if (firstLanding) shift = 2;
            if (shift == -1 && DungeonTile(left - 5, y).Wall == LivingWoodWall) shift = 1;
            else if (shift == 1 && DungeonTile(right + 5, y).Wall == LivingWoodWall) shift = -1;
            if (shift != 2)
            {
                left += shift;
                right += shift;
                continue;
            }
            firstLanding = false;
            bool dungeonBelow = DungeonGenerationTiles1458.IsDungeonWall(DungeonTile(left, y + 1).Wall) ||
                DungeonGenerationTiles1458.IsDungeonWall(DungeonTile(left + 1, y + 1).Wall) ||
                DungeonGenerationTiles1458.IsDungeonWall(DungeonTile(left + 2, y + 1).Wall);
            if (dungeonBelow && !(Contains(left - 1, y + 1) && IsSolid(left - 1, y + 1)) && !(Contains(right + 1, y + 1) && IsSolid(right + 1, y + 1))) continue;
            for (int x = left; x <= right; x++)
            {
                if (x <= middle - 2 || x > middle + 1) continue;
                ClearDungeonTileAndPaint(x, y + 1);
                PlacePlatform(x, y + 1, dungeonBelow ? platformStyle : 23);
            }
        }
        left = originalLeft;
        right = originalRight;
        for (int x = left; x <= right; x++)
        for (int y = groundY - 3; y <= groundY; y++)
        {
            ClearDungeonTileAndPaint(x, y);
            if (!DungeonGenerationTiles1458.IsDungeonWall(DungeonTile(x, y).Wall) && !DungeonGenerationTiles1458.IsDefaultDungeonGlass(DungeonTile(x, y).Wall))
                DungeonTile(x, y).Wall = LivingWoodWall;
        }
    }

    private ref WorldTile DungeonTile(int x, int y)
    {
        if (!Contains(x, y)) throw new IndexOutOfRangeException("Dungeon tree passage escaped the source tile bounds.");
        return ref At(x, y);
    }

    private void ClearDungeonTileAndPaint(int x, int y)
    {
        ref WorldTile cell = ref DungeonTile(x, y);
        cell.ClearSourceShape();
        cell.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        cell.TileColor = 0;
    }
}
