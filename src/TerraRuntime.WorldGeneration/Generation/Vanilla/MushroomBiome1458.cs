using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary GlowingMushroomPatches selection, grass and topology cleanup, pinned to 1.4.5.8.</summary>
internal static class MushroomBiome1458
{
    public static void Apply(Workspace workspace, IWorldGenerationVanillaRandom random,
        double worldSurface, double rockLayer, CancellationToken cancellation)
    {
        WorldTileStore store = workspace.TileStore;
        int width = workspace.WidthTiles, height = workspace.HeightTiles;
        VanillaUndergroundDesertRegion1458 desert = workspace.VanillaUndergroundDesertRegion ??
            throw new InvalidOperationException("Mushroom selection requires the preceding Full Desert region.");
        VanillaLiquidLines1458 lines = workspace.VanillaLiquidLines ??
            throw new InvalidOperationException("Mushroom roots require retained vanilla liquid lines.");
        var patch = new MushroomPatch1458(store, random, worldSurface, lines,
            new(desert.X, desert.Y, desert.Width, desert.Height), cancellation);
        var centers = new List<WorldGenerationPoint>(50);
        double count = Math.Min(width / 700d, 50);
        for (int index = 0; index < count; index++)
        {
            for (int attempt = 0; attempt <= width / 2; attempt++)
            {
                cancellation.ThrowIfCancellationRequested();
                int x = random.Next((int)(width * .2), (int)(width * .8));
                if (attempt > width / 4) x = random.Next((int)(width * .025), (int)(width * .975));
                int y = random.Next((int)rockLayer + 50, height - 300);
                bool blocked = false;
                for (int tx = x - 100; tx < x + 100; tx += 3)
                for (int ty = y - 100; ty < y + 100; ty += 3)
                {
                    if (tx < 0 || ty < 0 || tx >= width || ty >= height) { blocked = true; continue; }
                    WorldTile tile = At(store, tx, ty);
                    if ((tile.IsActive && tile.Type is 147 or 161 or 162 or 60 or 368 or 367) ||
                        (tx >= desert.X && tx < desert.Right && ty >= desert.Y && ty < desert.Bottom))
                    { blocked = true; break; }
                }
                if (!blocked)
                    foreach (WorldGenerationPoint center in centers)
                    {
                        double dx = center.X - x, dy = center.Y - y;
                        if (Math.Sqrt(dx * dx + dy * dy) < 500) { blocked = true; break; }
                    }
                if (blocked) continue;
                patch.Place(x, y);
                for (int satellite = 0; satellite < 5; satellite++)
                    patch.Place(x + random.Next(-40, 41), y + random.Next(-40, 41));
                centers.Add(new(x, y));
                break;
            }
        }
        workspace.SetVanillaMushroomCenters(centers.ToArray());

        // repeat:false means this source scan is nonrecursive. It also visits mud
        // outside the newly placed patches, so a patch-local grass pass is incorrect.
        for (int x = 50; x < width - 50; x++)
        {
            if ((x & 31) == 0) cancellation.ThrowIfCancellationRequested();
            for (int y = Math.Max(50, (int)worldSurface); y < height - 50; y++)
            {
                ref WorldTile tile = ref At(store, x, y);
                if (!tile.IsActive || tile.Type != 59) continue;
                bool enclosed = true;
                for (int tx = x - 1; tx <= x + 1; tx++)
                for (int ty = y - 1; ty <= y + 1; ty++)
                {
                    WorldTile neighbour = At(store, tx, ty);
                    if (neighbour.Type >= VanillaTileIds.Count) throw new InvalidOperationException("Unknown mushroom exposure tile.");
                    if (!neighbour.IsActive || neighbour.TileType == VanillaTileIds.RollingCactus ||
                        !VanillaTileCollisionCatalog.IsSolid(neighbour.TileType)) enclosed = false;
                    if (neighbour.LiquidKind == WorldLiquidKind.Lava && neighbour.LiquidAmount > 0) { enclosed = true; break; }
                }
                if (enclosed) continue;
                RequireNaturalNeighbours(store, x, y);
                tile.Type = 70;
                FrameInactive(store, x, y);
                tile.TileColor = 0;
                tile.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
            }
        }

        for (int x = 0; x < width; x++)
        {
            if ((x & 31) == 0) cancellation.ThrowIfCancellationRequested();
            for (int y = (int)worldSurface; y < height; y++)
            {
                WorldTile center = At(store, x, y);
                if (!center.IsActive || center.Type != 70) continue;
                if (x < 2 || y < 2 || x >= width - 2 || y >= height - 2)
                    throw new InvalidOperationException("Mushroom cleanup escaped its admitted world bounds.");
                for (int tx = x - 1; tx <= x + 1; tx++)
                for (int ty = y - 1; ty <= y + 1; ty++)
                {
                    if (At(store, tx, ty).IsActive)
                    {
                        if ((!At(store, tx - 1, ty).IsActive && !At(store, tx + 1, ty).IsActive) ||
                            (!At(store, tx, ty - 1).IsActive && !At(store, tx, ty + 1).IsActive)) Kill(store, random, tx, ty);
                    }
                    else if (At(store, tx - 1, ty).IsActive && At(store, tx + 1, ty).IsActive)
                    {
                        PlaceMud(store, tx, ty);
                        // Source uses the center's y here, not the inner loop's ty.
                        if (At(store, tx - 1, y).Type == 70) At(store, tx - 1, y).Type = 59;
                        if (At(store, tx + 1, y).Type == 70) At(store, tx + 1, y).Type = 59;
                    }
                    else if (At(store, tx, ty - 1).IsActive && At(store, tx, ty + 1).IsActive)
                    {
                        PlaceMud(store, tx, ty);
                        if (At(store, tx, y - 1).Type == 70) At(store, tx, y - 1).Type = 59;
                        if (At(store, tx, y + 1).Type == 70) At(store, tx, y + 1).Type = 59;
                    }
                }
                if (random.Next(4) != 0) continue;
                int sx = x + random.Next(-20, 21), sy = y + random.Next(-20, 21);
                if (sx >= 0 && sy >= 0 && sx < width && sy < height && At(store, sx, sy).Type == 59)
                    At(store, sx, sy).Type = 70;
            }
        }
    }

    private static void RequireNaturalNeighbours(WorldTileStore store, int x, int y)
    {
        for (int tx = x - 1; tx <= x + 1; tx++)
        for (int ty = y - 1; ty <= y + 1; ty++)
        {
            WorldTile tile = At(store, tx, ty);
            if (tile.IsActive && tile.Type is not (0 or 1 or 2 or 40 or 53 or 59 or 60 or 70 or >= 63 and <= 68 or 147 or 161))
                throw new InvalidOperationException($"Unverified mushroom cleanup neighbour {tile.Type} at {tx},{ty}.");
        }
    }

    private static void Kill(WorldTileStore store, IWorldGenerationVanillaRandom random, int x, int y)
    {
        RequireNaturalNeighbours(store, x, y);
        ref WorldTile tile = ref At(store, x, y);
        if (tile.Type == 2) for (int i = 0; i < 10; i++) _ = random.Next(2);
        tile.Type = 0;
        tile.FrameX = tile.FrameY = -1;
        tile.Shape = tile.TileColor = 0;
        tile.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        FrameInactive(store, x, y);
    }

    private static void PlaceMud(WorldTileStore store, int x, int y)
    {
        RequireNaturalNeighbours(store, x, y);
        ref WorldTile tile = ref At(store, x, y);
        tile.Type = 59;
        tile.FrameX = tile.FrameY = 0;
        tile.Shape = tile.TileColor = 0;
        tile.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        tile.Flags |= WorldTileFlags.Active;
        FrameInactive(store, x, y);
    }

    private static void FrameInactive(WorldTileStore store, int x, int y)
    {
        for (int tx = x - 1; tx <= x + 1; tx++)
        for (int ty = y - 1; ty <= y + 1; ty++)
        {
            ref WorldTile tile = ref At(store, tx, ty);
            if (tile.IsActive) continue;
            tile.Shape = tile.TileColor = 0;
            tile.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        }
    }

    private static ref WorldTile At(WorldTileStore store, int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
