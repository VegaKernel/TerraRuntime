using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>DungeonGlobalWallVariants seed selection and connected-region spreading for ordinary dungeons.</summary>
internal sealed class DungeonWallVariants1458(WorldTileStore tiles, IWorldGenerationVanillaRandom random, CancellationToken cancellationToken)
{
    private readonly Queue<int> frontier = new();

    public void Apply(DungeonBounds1458 bounds, ushort baseWall, IReadOnlyList<int> variants, double worldSurface)
    {
        int slab = baseWall switch { 7 => 94, 8 => 98, 9 => 96, _ => -1 };
        if (slab < 0 || variants.Count != 3 || variants[0] != baseWall || variants[1] != slab || variants[2] != slab + 1)
            throw new InvalidOperationException("Unsupported ordinary dungeon wall palette.");
        for (int round = 0; round < 5; round++)
        foreach (int variant in variants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int radius = random.Next(40, 240), x = random.Next(bounds.Left, bounds.Right), y = random.Next(bounds.Top, bounds.Bottom);
            double threshold = radius * .4f;
            for (int tx = Math.Max(2, x - radius); tx < Math.Min(tiles.Dimensions.WidthTiles - 2, x + radius); tx++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int ty = Math.Max(2, y - radius); ty < Math.Min(tiles.Dimensions.HeightTiles - 2, y + radius); ty++)
                {
                    int dx = tx - x, dy = ty - y;
                    if (ty > worldSurface && Math.Sqrt(dx * dx + dy * dy) < threshold && DungeonGenerationTiles1458.IsDungeonWall(At(tx, ty).Wall))
                        Spread(tx, ty, baseWall, checked((ushort)variant));
                }
            }
        }
    }

    internal void Spread(int x, int y, ushort baseWall, ushort targetWall)
    {
        cancellationToken.ThrowIfCancellationRequested();
        frontier.Clear();
        Visit(x, y);
        // A changed wall is the visitation mark. Rejected cells have no outgoing edges, and each accepted cell
        // is enqueued at most once. No source List.RemoveAt(0) waves or world-sized scratch/recursive flood fill.
        // All admission checks here depend only on immutable tile state and that cell's own wall, not visit order.
        while (frontier.TryDequeue(out int index))
        {
            cancellationToken.ThrowIfCancellationRequested();
            int tx = index / tiles.Dimensions.HeightTiles, ty = index % tiles.Dimensions.HeightTiles;
            Visit(tx - 1, ty); Visit(tx + 1, ty); Visit(tx, ty - 1); Visit(tx, ty + 1);
        }

        void Visit(int tx, int ty)
        {
            // Ordinary entrance and room styles allow wall variants; the source global gate still requires margin5.
            if (tx < 5 || ty < 5 || tx >= tiles.Dimensions.WidthTiles - 5 || ty >= tiles.Dimensions.HeightTiles - 5) return;
            ref WorldTile tile = ref At(tx, ty);
            if (tile.Wall != baseWall || tile.Wall == targetWall || tile.Wall is 0 or 244 or 62 or 350) return;
            if (!VanillaTileDefinitionCatalog.TryGet(tile.TileType, out var definition))
                throw new InvalidOperationException("Unknown tile in dungeon wall propagation.");
            tile.Wall = targetWall;
            // MakeDungeon disables cracked-brick solidity before all feature passes. A crack must
            // propagate the wall fill just like air, even though its live-world catalog is solid.
            bool solid = DungeonGenerationTiles1458.SolidTile(tile);
            if (!solid) frontier.Enqueue(tiles.GetUncheckedIndex(tx, ty));
        }
    }

    private ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
}
