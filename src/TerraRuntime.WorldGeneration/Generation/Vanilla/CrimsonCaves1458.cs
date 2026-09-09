using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary CrimStart/CrimVein/CrimEnt and deferred CrimPlaceHearts, pinned to 1.4.5.8.
/// Owns only unpublished cave geometry and the pass-local, ordered heart endpoints.</summary>
internal sealed class CrimsonCaves1458(WorldTileStore tiles, IWorldGenerationVanillaRandom random,
    double worldSurface, CancellationToken cancellationToken)
{
    private const ushort Stone = 203, Wall = 83;
    private const int SafetyIterations = 100_000; // Abort malformed geometry; never choose fallback coordinates.
    private readonly List<(int X, int Y)> hearts = new(100);
    internal IReadOnlyList<(int X, int Y)> HeartPositions => hearts;

    public void Start(int x, int y)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!double.IsFinite(worldSurface) || worldSurface < 0 || worldSurface >= tiles.Dimensions.HeightTiles)
            throw new InvalidOperationException("Crimson requires valid ordinary surface metadata.");
        y = Math.Min(y, (int)worldSurface);
        while (!Solid(x, y)) { cancellationToken.ThrowIfCancellationRequested(); y++; }
        int surfaceY = y;
        double px = x, py = y;
        double vx = random.Next(-20, 21) * 0.1, vy = random.Next(20, 201) * 0.01;
        int direction = vx < 0 ? -1 : 1, steering = 0;
        double width = random.Next(15, 26);
        for (int step = 0; ; step++)
        {
            CheckIteration(step);
            width = Math.Clamp(width + random.Next(-50, 51) * 0.01, 15, 25);
            for (int tx = (int)(px - width / 2); tx < px + width / 2; tx++)
            for (int ty = (int)(py - width / 2); ty < py + width / 2; ty++)
            {
                ref WorldTile tile = ref At(tx, ty);
                if (!EvilBiomeTiles1458.CanReplace(tile)) continue;
                double distance = Math.Abs(tx - px) + Math.Abs(ty - py);
                if (ty > surfaceY) Paint(ref tile, distance, width, 0.3, 0.8, 0.6);
                else if (distance < width * 0.3 && tile.IsActive) Hollow(ref tile);
            }
            if (px > x + 50) steering = -100;
            if (px < x - 50) steering = 100;
            vx += steering < 0 ? -random.Next(20, 51) * 0.01 :
                steering > 0 ? random.Next(20, 51) * 0.01 : random.Next(-50, 51) * 0.01;
            vy = Math.Clamp(vy + random.Next(-50, 51) * 0.01, 0.25, 2);
            vx = Math.Clamp(vx, -2, 2);
            px += vx; py += vy;
            if (py > worldSurface + 100) break;
        }

        width = random.Next(40, 55);
        for (int patch = 0; patch < 50; patch++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int cx = (int)px + random.Next(-20, 21), cy = (int)py + random.Next(-20, 21);
            for (int tx = (int)(cx - width / 2); tx < cx + width / 2; tx++)
            for (int ty = (int)(cy - width / 2); ty < cy + width / 2; ty++)
            {
                ref WorldTile tile = ref At(tx, ty);
                if (!EvilBiomeTiles1458.CanReplace(tile)) continue;
                double dx = Math.Abs(tx - cx) * (1 + random.Next(-20, 21) * 0.01);
                double dy = Math.Abs(ty - cy) * (1 + random.Next(-20, 21) * 0.01);
                Paint(ref tile, Math.Sqrt(dx * dx + dy * dy), width, 0.25, 0.4, 0.35);
            }
        }

        int branches = random.Next(5, 9);
        var directions = new (double X, double Y)[branches];
        for (int branch = 0; branch < branches; branch++)
        {
            // The source consumes an initial pair before its retry loop. Its separation test
            // compares the trunk velocity (not the candidate vein velocity); preserve that ordering.
            double bx = random.Next(-20, 21) * 0.15, by = random.Next(0, 21) * 0.15;
            int retries = 0;
            for (int attempt = 0; ; attempt++)
            {
                CheckIteration(attempt);
                bx = random.Next(-20, 21) * 0.15; by = random.Next(0, 21) * 0.15;
                for (int redraw = 0; Math.Abs(bx) + Math.Abs(by) < 1.5; redraw++)
                {
                    CheckIteration(redraw);
                    bx = random.Next(-20, 21) * 0.15; by = random.Next(0, 21) * 0.15;
                }
                bool overlap = false;
                for (int previous = 0; previous < branch; previous++)
                    if (vx > directions[previous].X - 0.75 && vx < directions[previous].X + 0.75 &&
                        vy > directions[previous].Y - 0.75 && vy < directions[previous].Y + 0.75)
                    { overlap = true; retries++; break; }
                if (!overlap || retries > 10_000) break;
            }
            directions[branch] = (bx, by);
            Vein((int)px, (int)py, bx, by);
        }

        px = x; py = surfaceY;
        width = random.Next(25, 35);
        double lift = random.Next(0, 6);
        int left = tiles.Dimensions.WidthTiles, right = 0;
        for (int patch = 0; patch < 50; patch++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lift > 0) { double amount = random.Next(10, 30) * 0.01; lift -= amount; py -= amount; }
            int cx = (int)px + random.Next(-2, 3), cy = (int)py + random.Next(-2, 3);
            for (int tx = (int)(cx - width / 2); tx < cx + width / 2; tx++)
            for (int ty = (int)(cy - width / 2); ty < cy + width / 2; ty++)
            {
                ref WorldTile tile = ref At(tx, ty);
                if (!EvilBiomeTiles1458.CanReplace(tile)) continue;
                double dx = Math.Abs(tx - cx) * (1 + random.Next(-20, 21) * 0.005);
                double dy = Math.Abs(ty - cy) * (1 + random.Next(-20, 21) * 0.005);
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance < width * 0.2 * (random.Next(90, 111) * 0.01)) Hollow(ref tile);
                else if (distance < width * 0.45)
                {
                    left = Math.Min(left, tx); right = Math.Max(right, tx);
                    if (tile.Wall != Wall)
                    {
                        tile.Flags |= WorldTileFlags.Active; tile.Type = Stone;
                        if (distance < width * 0.35) tile.Wall = Wall;
                    }
                }
            }
        }
        for (int tx = left; tx <= right; tx++)
        {
            int ty = surfaceY;
            while ((At(tx, ty).IsActive && At(tx, ty).Type == Stone) || At(tx, ty).Wall == Wall) ty++;
            int depth = random.Next(15, 20);
            while (!At(tx, ty).IsActive && depth > 0 && At(tx, ty).Wall != Wall)
            {
                // Source otherwise cannot advance this loop. Fail closed on the protected obstruction.
                if (!EvilBiomeTiles1458.CanReplace(At(tx, ty)))
                    throw new InvalidOperationException("Crimson foundation intersects protected terrain.");
                At(tx, ty).Type = Stone; At(tx, ty).Flags |= WorldTileFlags.Active;
                ty++; depth--;
            }
        }
        Entrance(px, py, direction);
    }

    private void Vein(double x, double y, double vx, double vy)
    {
        double width = random.Next(15, 26), startX = x, startY = y, initialX = vx, initialY = vy;
        int length = random.Next(100, 150) - (vy < 0 ? 25 : 0);
        for (int step = 0; ; step++)
        {
            CheckIteration(step);
            width = Math.Clamp(width + random.Next(-50, 51) * 0.02, 15, 25);
            for (int tx = (int)(x - width / 2); tx < x + width / 2; tx++)
            for (int ty = (int)(y - width / 2); ty < y + width / 2; ty++)
            {
                ref WorldTile tile = ref At(tx, ty);
                if (!EvilBiomeTiles1458.CanReplace(tile)) continue;
                double dx = Math.Abs(tx - x), dy = Math.Abs(ty - y);
                Paint(ref tile, Math.Sqrt(dx * dx + dy * dy), width, 0.2, 0.5, 0.4);
            }
            vx = Math.Clamp(vx + random.Next(-50, 51) * 0.05, initialX - 0.75, initialX + 0.75);
            vy = Math.Clamp(vy + random.Next(-50, 51) * 0.05, initialY - 0.75, initialY + 0.75);
            x += vx; y += vy;
            if (Math.Abs(x - startX) + Math.Abs(y - startY) > length) break;
        }
        if (hearts.Count == 100) throw new InvalidOperationException("Crimson heart endpoint capacity exceeded.");
        hearts.Add(((int)x, (int)y));
    }

    private void Entrance(double x, double y, int direction)
    {
        double width = random.Next(6, 11), vx = -2 * direction, vy = random.Next(-20, 0) * 0.01;
        int emptySteps = 0;
        for (int step = 0; ; step++)
        {
            CheckIteration(step);
            emptySteps++;
            width = Math.Clamp(width + random.Next(-10, 11) * 0.02, 6, 10);
            for (int tx = (int)(x - width / 2); tx < x + width / 2; tx++)
            for (int ty = (int)(y - width / 2); ty < y + width / 2; ty++)
            {
                ref WorldTile tile = ref At(tx, ty);
                if (!EvilBiomeTiles1458.CanReplace(tile)) continue;
                double dx = Math.Abs(tx - x), dy = Math.Abs(ty - y);
                if (Math.Sqrt(dx * dx + dy * dy) < width * 0.5 && tile.IsActive && tile.Type == Stone)
                { tile.Flags &= ~WorldTileFlags.Active; emptySteps = 0; }
            }
            x += vx; y += vy;
            if (emptySteps >= 20) break;
        }
    }

    public void PlaceHearts()
    {
        // Three source phases across ALL endpoints, not three phases per endpoint.
        foreach (var heart in hearts) HeartChamber(heart, random.Next(16, 21), hollow: false);
        foreach (var heart in hearts) HeartChamber(heart, random.Next(10, 14), hollow: true);
        foreach (var (x, y) in hearts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EvilBiomeTiles1458.PlaceOrb(tiles, x, y, true);
        }
    }

    private void HeartChamber((int X, int Y) heart, int width, bool hollow)
    {
        cancellationToken.ThrowIfCancellationRequested();
        for (int x = heart.X - width / 2; x < heart.X + width / 2; x++)
        for (int y = heart.Y - width / 2; y < heart.Y + width / 2; y++)
        {
            double dx = Math.Abs(x - heart.X), dy = Math.Abs(y - heart.Y);
            if (Math.Sqrt(dx * dx + dy * dy) >= width * (hollow ? 0.3 : 0.4)) continue;
            ref WorldTile tile = ref At(x, y);
            // Unlike the cave brushes, CrimPlaceHearts does not call CanEvilReplace.
            if (hollow) Hollow(ref tile);
            else { tile.Flags |= WorldTileFlags.Active; tile.Type = Stone; tile.Wall = Wall; }
        }
    }

    private static void Paint(ref WorldTile tile, double distance, double width, double cavity, double shell, double lining)
    {
        if (distance < width * cavity) Hollow(ref tile);
        else if (distance < width * shell && tile.Wall != Wall)
        {
            tile.Flags |= WorldTileFlags.Active; tile.Type = Stone;
            if (distance < width * lining) tile.Wall = Wall;
        }
    }
    private static void Hollow(ref WorldTile tile) { tile.Flags &= ~WorldTileFlags.Active; tile.Wall = Wall; }
    private bool Solid(int x, int y)
    {
        WorldTile tile = At(x, y);
        return tile.IsActive && !tile.IsActuated && tile.Shape == 0 && tile.Type != 484 &&
            VanillaTileCollisionCatalog.IsSolid(tile.TileType) && !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
    }
    private ref WorldTile At(int x, int y)
    {
        if ((uint)x >= (uint)tiles.Dimensions.WidthTiles || (uint)y >= (uint)tiles.Dimensions.HeightTiles)
            throw new InvalidOperationException("Crimson geometry left the generation tile bounds.");
        return ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
    }
    private void CheckIteration(int iteration)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (iteration >= SafetyIterations) throw new InvalidOperationException("Crimson geometry exhausted its bounded search.");
    }
}
