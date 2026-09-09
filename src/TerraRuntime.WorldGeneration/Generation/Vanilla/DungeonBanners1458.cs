using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>DungeonGlobalBanners ordinary sampling, solid-ceiling search and separation rules.</summary>
internal sealed class DungeonBanners1458(WorldTileStore tiles, IWorldGenerationVanillaRandom random, CancellationToken cancellationToken,
    IReadOnlyList<DungeonPitTraps1458.Pit>? pits = null)
{
    public int Place(DungeonBounds1458 bounds, IReadOnlyList<int> wallVariants)
    {
        int width = tiles.Dimensions.WidthTiles, height = tiles.Dimensions.HeightTiles;
        if (bounds.Left < 10 || bounds.Top < 10 || bounds.Right > width - 10 || bounds.Bottom > height - 10 ||
            bounds.Left >= bounds.Right || bounds.Top >= bounds.Bottom || wallVariants.Count != 3)
            throw new InvalidOperationException("Invalid dungeon banner sampling bounds.");
        var placement = new DungeonObjectPlacement1458(tiles, random);
        int placed = 0, attempts = (int)(200f * ((float)width / 4200f));
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(bounds.Left, bounds.Right), y = random.Next(bounds.Top, bounds.Bottom), remaining = 1000;
            while (!DungeonGenerationTiles1458.IsDungeonWall(At(x, y).Wall) || At(x, y).IsActive)
            {
                if (--remaining <= 0) break;
                x = random.Next(bounds.Left, bounds.Right); y = random.Next(bounds.Top, bounds.Bottom);
            }
            remaining = 1000;
            while (!DungeonGenerationTiles1458.SolidTile(At(x, y)) && y > 10)
            {
                if (--remaining <= 0) break;
                y--;
            }
            y++;
            if (At(x, y).Wall == 350 || DungeonPitTraps1458.Contains(pits, x, y) ||
                !DungeonGenerationTiles1458.IsDungeonWall(At(x, y).Wall) || At(x, y - 1).Type == 48 ||
                At(x, y).IsActive || At(x, y + 1).IsActive || At(x, y + 2).IsActive || At(x, y + 3).IsActive) continue;
            bool clear = true;
            for (int tx = x - 1; tx <= x + 1; tx++)
            for (int ty = y; ty <= y + 3; ty++)
                if (At(tx, ty) is { IsActive: true, Type: 10 or 11 or 91 }) clear = false;
            if (!clear) continue;
            int variant = At(x, y).Wall == wallVariants[2] ? 2 : At(x, y).Wall == wallVariants[1] ? 1 : 0;
            if (placement.Banner(x, y, 10 + variant * 2 + random.Next(2))) placed++;
        }
        return placed;
    }

    private ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
}
