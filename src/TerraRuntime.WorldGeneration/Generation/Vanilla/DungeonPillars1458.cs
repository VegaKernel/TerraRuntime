using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary DungeonPillar and DungeonUtils bottom wedges on unpublished generation tiles.</summary>
internal sealed class DungeonPillars1458(WorldTileStore tiles, ushort brick, IWorldGenerationVanillaRandom random)
{
    private readonly StoneBiomeTiles1458 framing = new(tiles, random, crackedBricksSolid: false);

    public DungeonBounds1458 Place(int x, int y, int width, int height, bool actuated = false,
        bool crownTop = false, bool crownBottom = false)
    {
        if (brick is not (41 or 43 or 44) || width <= 0 || width > 7 || height < 0 || height > 100)
            throw new InvalidOperationException("Unsupported ordinary dungeon pillar.");
        int left = ClampX(x), right = ClampX(x + 1), top = ClampY(y), bottom = ClampY(y + 1);
        for (int column = 0; column < width; column++)
        {
            int px = x + column - width / 2;
            var strip = Strip(px, y, height, true, actuated, false, false);
            left = Math.Min(left, ClampX(px)); right = Math.Max(right, ClampX(px));
            top = Math.Min(top, ClampY(strip.Top)); bottom = Math.Max(bottom, ClampY(strip.Bottom));
            // Crowns are drawn immediately per column; their own extent is not added to the pillar Bounds.
            if (column != 0 && column != width - 1) continue;
            int edgeX = px + (column == 0 ? -1 : 1);
            if (crownTop) Strip(edgeX, strip.Top + 3, 0, true, actuated, false, true);
            if (crownBottom) Strip(edgeX, strip.Bottom - 3, 0, false, actuated, true, false);
        }
        return new(left, top, right, bottom);
    }

    private (int Top, int Bottom) Strip(int x, int y, int height, bool up, bool actuated, bool smoothTop, bool smoothBottom)
    {
        if (height == 0)
        {
            int length = 0, direction = up ? -1 : 1;
            while (length < 100 && InWorld(x, y + direction * length) && !At(x, y + direction * length).IsActive) length++;
            height = length;
            if (!up) y += length - 1;
        }
        int top = y, bottom = y;
        for (int step = 0; step < height; step++)
        {
            int offset = up ? -height + 1 + step : -step, row = y + offset;
            if (up ? row <= 10 : row >= tiles.Dimensions.HeightTiles - 10) break;
            ref WorldTile cell = ref At(x, row);
            DungeonGenerationTiles1458.ClearTile(ref cell);
            cell.Type = brick; cell.Flags |= WorldTileFlags.Active;
            if (offset == -height + 1 && smoothTop || offset == 0 && smoothBottom) framing.SmoothCell(x, row);
            if (actuated) cell.Flags |= WorldTileFlags.Inactive;
            top = Math.Min(top, row); bottom = Math.Max(bottom, row);
        }
        return (top, bottom);
    }

    public void BottomWedge(int x, int y, int width, bool left)
    {
        // Source crowningBottom adds two before the inclusive column loop. Its second loop smooths
        // the SAME center column; replacing that with each painted column changes the geometry.
        width += 2;
        for (int column = 0; column <= width; column++)
        {
            int px = x + column - width / 2, height = left ? column + 1 : width - column + 1;
            for (int row = y; row < y + height; row++)
            {
                if (!InWorld(px, row)) continue;
                ref WorldTile cell = ref At(px, row);
                DungeonGenerationTiles1458.ClearTile(ref cell); cell.Type = brick; cell.Flags |= WorldTileFlags.Active;
            }
        }
        for (int column = 0; column <= width; column++) framing.SmoothCell(x, y + (left ? column + 1 : width - column + 1));
    }

    private bool InWorld(int x, int y) => x >= 10 && y >= 10 && x < tiles.Dimensions.WidthTiles - 10 && y < tiles.Dimensions.HeightTiles - 10;
    private int ClampX(int x) => Math.Clamp(x, 10, tiles.Dimensions.WidthTiles - 10);
    private int ClampY(int y) => Math.Clamp(y, 10, tiles.Dimensions.HeightTiles - 10);
    private ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
}
