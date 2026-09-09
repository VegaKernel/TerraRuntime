using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary CloudIsland/CloudLake geometry on the unpublished generation candidate.</summary>
internal sealed class SkyIsland1458(WorldTileStore tiles, IWorldGenerationVanillaRandom random)
{
    private const ushort Cloud = 189, Rain = 196, Dirt = 0, Wall = 73;
    private int left, right, top, bottom;

    public bool TryFindAnchor(double worldSurface, double worldSurfaceLow, IReadOnlyList<int> used, out int x, out int y)
    {
        int width = tiles.Dimensions.WidthTiles;
        for (int attempts = width - 1; attempts > 0; attempts--)
        {
            int draws = 0;
            do
            {
                if (++draws > 100000) throw new InvalidOperationException("Sky anchor RNG failed to leave spawn exclusion.");
                x = random.Next((int)(width * .1), (int)(width * .9));
            } while (x > width / 2 - 150 && x < width / 2 + 150);
            bool tooClose = false;
            foreach (int previous in used)
                if (x > previous - 180 && x < previous + 180) { tooClose = true; break; }
            if (tooClose) continue;
            for (int ground = 200; ground < worldSurface; ground++)
                if (At(x, ground).IsActive)
                {
                    y = Math.Min(random.Next(90, ground - 100), (int)worldSurfaceLow - 50);
                    return true;
                }
        }
        x = y = 0;
        return false; // Source skips exhausted requests; no fabricated evenly spaced anchor or guessed ground.
    }

    public void Generate(int x, int y, bool lake)
    {
        // All source neighborhoods stay inside this envelope. Reject unsupported edge contexts before mutation.
        if (x < 160 || x >= tiles.Dimensions.WidthTiles - 160 || y < 70 || y >= tiles.Dimensions.HeightTiles - 100)
            throw new InvalidOperationException("Sky island anchor outside the admitted ordinary envelope.");
        left = right = x;
        top = bottom = y;
        Trail(x, y, core: false, lake);
        for (int bx = left + random.Next(5); bx < right;)
        {
            int by = bottom;
            while (!At(bx, by).IsActive && by > 15) by--;
            by += random.Next(-3, 4);
            int radius = random.Next(4, 8);
            ushort type = random.Next(4) == 0 ? Rain : Cloud;
            Blob(bx, by, radius, type, top, jitterMinimum: 0);
            bx += random.Next(radius, (int)(radius * 1.5));
        }
        Trail(x, top, core: true, lake);
        if (!lake) RestoreCloudUnderside();
        InteriorWalls(frameWalls: !lake);
        SurfaceWater();
        int pockets = lake ? random.Next(1, 4) : random.Next(4);
        for (int index = 0; index <= pockets; index++)
        {
            int bx = random.Next(left - 5, right + 5);
            int by = top - random.Next(20, 40);
            int radius = random.Next(4, 8);
            ushort type = (lake ? random.Next(4) != 0 : random.Next(2) == 0) ? Rain : Cloud;
            Blob(bx, by, radius, type, int.MinValue, jitterMinimum: -1);
            for (int tx = bx - radius + 2; tx <= bx + radius - 2; tx++)
            {
                int ty = by - radius;
                while (!At(tx, ty).IsActive && ty < tiles.Dimensions.HeightTiles - 2) ty++;
                if (WaterStays(tx, ty)) Fill(tx, ty);
            }
        }
    }

    private void Trail(int originX, int originY, bool core, bool lake)
    {
        double width = core ? random.Next(80, 95) : random.Next(100, 150);
        int steps = core ? random.Next(10, 15) : random.Next(20, 30);
        double x = originX, y = originY, vx;
        int draws = 0;
        do
        {
            if (++draws > 100000) throw new InvalidOperationException("Sky drift RNG failed to converge.");
            vx = random.Next(-20, 21) * .2;
        } while (vx > -2 && vx < 2);
        double vy = random.Next(-20, -10) * .02;
        while (width > 0 && steps-- > 0)
        {
            width -= random.Next(4);
            int minX = Math.Max(0, (int)(x - width * .5)), maxX = Math.Min(tiles.Dimensions.WidthTiles, (int)(x + width * .5));
            int minY = Math.Max(0, core ? top - 1 : (int)(y - width * .5));
            int maxY = Math.Min(tiles.Dimensions.HeightTiles, (int)(y + width * .5));
            double strength = width * random.Next(80, 120) * .01;
            double surface = y + 1;
            for (int tx = minX; tx < maxX; tx++)
            {
                if (random.Next(2) == 0) surface += random.Next(-1, 2);
                surface = Math.Clamp(surface, y, y + 2);
                for (int ty = minY; ty < maxY; ty++)
                {
                    if (ty <= surface - (core && lake ? 2 : 0)) continue;
                    double dx = Math.Abs(tx - x), dy = Math.Abs(ty - y) * 3;
                    if (Math.Sqrt(dx * dx + dy * dy) >= strength * .4) continue;
                    ref WorldTile tile = ref At(tx, ty);
                    if (!core)
                    {
                        tile.Flags |= WorldTileFlags.Active;
                        tile.Type = Cloud;
                        left = Math.Min(left, tx); right = Math.Max(right, tx);
                        top = Math.Min(top, ty); bottom = Math.Max(bottom, ty);
                    }
                    else if (tile.Type == Cloud)
                    {
                        if (!lake) tile.Type = Dirt;
                        else
                        {
                            tile.Flags &= ~WorldTileFlags.Active;
                            for (int wx = tx - 1; wx <= tx + 1; wx++)
                            {
                                At(wx, ty).Wall = 0;
                                At(wx, ty - 1).Wall = 0;
                            }
                            if (ty > surface + 1)
                            {
                                if (WaterStays(tx, ty)) tile.LiquidAmount = byte.MaxValue;
                                tile.LiquidKind = WorldLiquidKind.Water;
                            }
                        }
                    }
                }
            }
            x += vx; y += vy;
            vx = Math.Clamp(vx + random.Next(-20, 21) * .05, -1, 1);
            if (vy > .2) vy = -.2;
            if (vy < (core && lake ? 0 : -.2)) vy = core && lake ? 0 : -.2;
        }
    }

    private void RestoreCloudUnderside()
    {
        int x = left + random.Next(5);
        while (x < right)
        {
            int y = bottom;
            while ((!At(x, y).IsActive || At(x, y).Type != Dirt) && x < right)
            {
                if (--y < top) { y = bottom; x += random.Next(1, 4); }
            }
            if (x >= right) continue;
            y += random.Next(0, 4);
            int radius = random.Next(2, 5);
            for (int tx = x - radius; tx <= x + radius; tx++)
                for (int ty = y - radius; ty <= y + radius; ty++)
                    if (ty > top && Math.Sqrt((tx - x) * (tx - x) + (ty - y) * (ty - y) * 4) < radius)
                        At(tx, ty).Type = Cloud; // Source changes type without activating air.
            x += random.Next(radius, (int)(radius * 1.5));
        }
    }

    private void InteriorWalls(bool frameWalls)
    {
        for (int x = left - 20; x <= right + 20; x++)
            for (int y = top - 20; y <= bottom + 20; y++)
            {
                bool enclosed = true;
                for (int nx = x - 1; nx <= x + 1; nx++)
                    for (int ny = y - 1; ny <= y + 1; ny++)
                        if (!At(nx, ny).IsActive || (At(nx, ny).Wall > 0 && At(nx, ny).Wall != Wall)) enclosed = false;
                if (enclosed)
                {
                    At(x, y).Wall = Wall;
                    // CloudIsland calls SquareWallFrame(reset:true); CloudLake deliberately does not.
                    if (frameWalls)
                        _ = random.Next(0, 3); // Only the center requests a new cosmetic variant.
                }
            }
    }

    private void SurfaceWater()
    {
        for (int x = left; x <= right; x++)
        {
            int y = top - 10;
            while (!At(x, y + 1).IsActive && y < tiles.Dimensions.HeightTiles - 2) y++;
            if (y >= bottom || At(x, y + 1).Type != Cloud) continue;
            if (random.Next(10) == 0)
            {
                int radius = random.Next(1, 3);
                for (int tx = x - radius; tx <= x + radius; tx++)
                {
                    if (At(tx, y).Type == Cloud && WaterStays(tx, y)) Fill(tx, y);
                    if (At(tx, y + 1).Type == Cloud && WaterStays(tx, y + 1)) Fill(tx, y + 1);
                    if (tx > x - radius && tx < x + 2 && At(tx, y + 2).Type == Cloud && WaterStays(tx, y + 2)) Fill(tx, y + 2);
                }
            }
            if (random.Next(5) == 0 && WaterStays(x, y)) At(x, y).LiquidAmount = byte.MaxValue;
            At(x, y).LiquidKind = WorldLiquidKind.Water;
        }
    }

    private void Blob(int x, int y, int radius, ushort type, int minY, int jitterMinimum)
    {
        for (int tx = x - radius; tx <= x + radius; tx++)
            for (int ty = y - radius; ty <= y + radius; ty++)
                if (ty > minY && Math.Sqrt((tx - x) * (tx - x) + (ty - y) * (ty - y) * 4) < radius + random.Next(jitterMinimum, 2))
                {
                    At(tx, ty).Flags |= WorldTileFlags.Active;
                    At(tx, ty).Type = type;
                }
    }

    private bool WaterStays(int x, int y) => HoldsWater(x, y + 1) && HoldsWater(x - 1, y) && HoldsWater(x + 1, y);
    private bool HoldsWater(int x, int y)
    {
        ref WorldTile tile = ref At(x, y);
        return tile.LiquidAmount == byte.MaxValue || (tile.IsActive && VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType));
    }
    private void Fill(int x, int y)
    {
        At(x, y).Flags &= ~WorldTileFlags.Active;
        At(x, y).LiquidAmount = byte.MaxValue;
        At(x, y).LiquidKind = WorldLiquidKind.Water;
    }
    private ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
}
