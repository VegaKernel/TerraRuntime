using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary Corruption chasm geometry, pinned to WorldGen 1.4.5.8.
/// Mutates only an unpublished candidate; uses the existing ore runner and altar placement.</summary>
internal sealed class CorruptionCaves1458(WorldTileStore tiles, IWorldGenerationVanillaRandom random,
    double worldSurface, double rockLayer, VanillaLiquidLines1458 liquidLines, CancellationToken cancellationToken)
{
    private const ushort Stone = 25, Wall = 3;

    public void GenerateRegion(int left, int right, int center, double surfaceLow)
    {
        var surface = new EvilBiomeSurface1458(tiles, random, surfaceLow, worldSurface, cancellationToken);
        int cooldown = 0;
        for (int x = left; x < right; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cooldown > 0) cooldown--;
            if (x == center || cooldown == 0)
            {
                for (int y = (int)surfaceLow; y < worldSurface - 1; y++)
                {
                    WorldTile cell = At(x, y);
                    if (!cell.IsActive && cell.Wall == 0) continue;
                    if (x == center) { cooldown = 20; Run(x, y, random.Next(150) + 150, true); }
                    else if (random.Next(35) == 0 && cooldown == 0)
                    { cooldown = 30; Run(x, y, random.Next(50) + 50, true); }
                    break;
                }
            }
            surface.ConvertJungleColumn(x, left, right, false);
        }
        surface.ConvertRegion(left, right, false);
        // Source visits each active orb cell, not merely each 2x2 anchor. No CanEvilReplace gate here.
        for (int x = left; x < right; x++)
        for (int y = 0; y < tiles.Dimensions.HeightTiles - 50; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!At(x, y).IsActive || At(x, y).Type != 31) continue;
            for (int tx = x - 13; tx < x + 13; tx++)
            {
                if (tx <= 10 || tx >= tiles.Dimensions.WidthTiles - 10) continue;
                for (int ty = y - 13; ty < y + 13; ty++)
                {
                    ref WorldTile cell = ref At(tx, ty);
                    int dx = Math.Abs(tx - x), dy = Math.Abs(ty - y);
                    if (dx + dy < 9 + random.Next(11) && random.Next(3) != 0 && cell.Type != 31)
                    {
                        cell.Flags |= WorldTileFlags.Active; cell.Type = Stone;
                        if (dx <= 1 && dy <= 1) cell.Flags &= ~WorldTileFlags.Active;
                    }
                    if (cell.Type != 31 && dx <= 2 + random.Next(3) && dy <= 2 + random.Next(3))
                        cell.Flags &= ~WorldTileFlags.Active;
                }
            }
        }
    }

    public void Run(int originX, int originY, int steps, bool makeOrb)
    {
        Check(0);
        double x = originX, y = originY, remaining = steps;
        double vx = random.Next(-10, 11) * 0.1, vy = random.Next(11) * 0.2 + 0.5;
        double radius = random.Next(5) + 7;
        bool branched = false, orbPlaced = !makeOrb;
        for (int iteration = 0; radius > 0; iteration++)
        {
            Check(iteration);
            radius = AdvanceRadius(radius, remaining, y > worldSurface + 45);
            if (y > rockLayer && remaining > 0) remaining = 0;
            remaining--;
            if (!branched && y > worldSurface + 20)
            {
                branched = true;
                Sideways((int)x, (int)y, -1, random.Next(20, 40));
                Sideways((int)x, (int)y, 1, random.Next(20, 40));
            }
            if (remaining > 5)
            {
                var bounds = Bounds(x, y, radius * 0.5, 0);
                for (int tx = bounds.Left; tx < bounds.Right; tx++)
                for (int ty = bounds.Top; ty < bounds.Bottom; ty++)
                    // Main chasm samples the brush BEFORE checking protected material, unlike sideways.
                    if (Inside(tx, ty, x, y, radius * 0.5)) EvilBiomeTiles1458.Excavate(ref At(tx, ty));
            }
            if (remaining <= 2 && y < worldSurface + 45) remaining = 2;
            if (remaining <= 0)
            {
                if (!orbPlaced) { orbPlaced = true; EvilBiomeTiles1458.PlaceOrb(tiles, (int)x, (int)y, false); }
                // Source's altar flag remains false: this search repeats on subsequent terminal iterations.
                else PlaceTerminalAltar((int)x, (int)y);
            }
            x += vx; y += vy;
            vx = Math.Clamp(vx + random.Next(-10, 11) * 0.01, -0.3, 0.3);
            var shell = Bounds(x, y, radius * 1.1, 1);
            for (int pass = 0; pass < 2; pass++)
            for (int tx = shell.Left; tx < shell.Right; tx++)
            for (int ty = shell.Top; ty < shell.Bottom; ty++)
            {
                ref WorldTile cell = ref At(tx, ty);
                if (!EvilBiomeTiles1458.CanReplace(cell) || !Inside(tx, ty, x, y, radius * 1.1)) continue;
                if (pass == 0 && cell.Type != Stone && ty > originY + random.Next(3, 20)) cell.Flags |= WorldTileFlags.Active;
                if (steps <= 5) cell.Flags |= WorldTileFlags.Active;
                if (cell.Type != 31) cell.Type = Stone;
                if (pass == 1 && ty > originY + random.Next(3, 20)) cell.Wall = Wall;
            }
        }
    }

    public void Sideways(int originX, int originY, int direction, int steps)
    {
        Check(0);
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        double x = originX, y = originY, remaining = steps;
        double vx = random.Next(10, 21) * 0.1 * direction, vy = random.Next(-10, 10) * 0.01;
        double radius = random.Next(5) + 7;
        for (int iteration = 0; radius > 0; iteration++)
        {
            Check(iteration);
            radius = AdvanceRadius(radius, remaining, true);
            if (y > rockLayer && remaining > 0) remaining = 0;
            remaining--;
            var cavity = Bounds(x, y, radius * 0.5, 0);
            for (int tx = cavity.Left; tx < cavity.Right; tx++)
            for (int ty = cavity.Top; ty < cavity.Bottom; ty++)
            {
                ref WorldTile cell = ref At(tx, ty);
                if (EvilBiomeTiles1458.CanReplace(cell) && Inside(tx, ty, x, y, radius * 0.5))
                    EvilBiomeTiles1458.Excavate(ref cell);
            }
            x += vx; y += vy;
            vy += random.Next(-10, 10) * 0.1;
            if (y < originY - 20) vy += random.Next(20) * 0.01;
            if (y > originY + 20) vy -= random.Next(20) * 0.01;
            vy = Math.Clamp(vy, -0.5, 0.5);
            vx = Math.Clamp(vx + random.Next(-10, 11) * 0.01, direction == -1 ? -2 : 0.5, direction == -1 ? -0.5 : 2);
            var shell = Bounds(x, y, radius * 1.1, 1);
            for (int pass = 0; pass < 2; pass++)
            for (int tx = shell.Left; tx < shell.Right; tx++)
            for (int ty = shell.Top; ty < shell.Bottom; ty++)
            {
                ref WorldTile cell = ref At(tx, ty);
                if (!EvilBiomeTiles1458.CanReplace(cell) || !Inside(tx, ty, x, y, radius * 1.1) || cell.Wall == Wall) continue;
                if (!cell.IsActive || cell.Type is not (31 or 22 or 204)) cell.Type = Stone;
                cell.Flags |= WorldTileFlags.Active;
                if (pass == 0) { if (cell.Wall == 2) cell.Wall = 0; }
                else PlaceWall(tx, ty);
            }
        }
        if (random.Next(3) != 0) return;
        int oreX = (int)x, oreY = (int)y;
        while (!At(oreX, oreY).IsActive) { cancellationToken.ThrowIfCancellationRequested(); oreY++; }
        new SmallTerrainRunner1458(tiles, random, worldSurface, liquidLines, cancellationToken)
            .Run(oreX, oreY, random.Next(2, 6), random.Next(3, 7), 22);
    }

    private void PlaceTerminalAltar(int x, int y)
    {
        for (int attempt = 0; attempt < 10_000; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int tx = Math.Clamp(random.Next(x - 25, x + 25), 5, tiles.Dimensions.WidthTiles - 5);
            int ty = Math.Clamp(random.Next(y - 50, y), 5, tiles.Dimensions.HeightTiles - 5);
            if (ty <= worldSurface) return;
            if (!EvilAltarPlacement1458.IsNearby(tiles, tx, ty)) EvilAltarPlacement1458.TryPlace(tiles, tx, ty, false);
            if (At(tx, ty).Type == 26) return;
        }
    }

    private void PlaceWall(int x, int y) => GenerationWallPlacement1458.Place(tiles, x, y, Wall, random);

    private double AdvanceRadius(double radius, double remaining, bool decay)
    {
        if (remaining <= 0) return decay ? radius - random.Next(4) : radius;
        radius = Math.Clamp(radius + random.Next(3) - random.Next(3), 7, 20);
        return remaining == 1 ? Math.Max(10, radius) : radius;
    }

    private bool Inside(int tx, int ty, double x, double y, double radius) =>
        Math.Abs(tx - x) + Math.Abs(ty - y) < radius * (1 + random.Next(-10, 11) * 0.015);

    private (int Left, int Right, int Top, int Bottom) Bounds(double x, double y, double radius, int leftMargin) =>
        (Math.Max(leftMargin, (int)(x - radius)), Math.Min(tiles.Dimensions.WidthTiles - 1, (int)(x + radius)),
         Math.Max(0, (int)(y - radius)), Math.Min(tiles.Dimensions.HeightTiles, (int)(y + radius)));

    private void Check(int iteration)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (iteration >= 100_000) throw new InvalidOperationException("Corruption chasm exceeded its safety iteration budget.");
    }

    private ref WorldTile At(int x, int y)
    {
        if ((uint)x >= (uint)tiles.Dimensions.WidthTiles || (uint)y >= (uint)tiles.Dimensions.HeightTiles)
            throw new InvalidOperationException("Corruption chasm reached outside the candidate terrain.");
        return ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
    }
}
