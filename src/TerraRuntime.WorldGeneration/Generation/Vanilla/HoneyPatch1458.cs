using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed class HoneyPatch1458(WorldTileStore store, IWorldGenerationVanillaRandom random,
    Func<int, int, int, int, bool> canPlace, Action<WorldTileRegion> protect, Workspace? workspace = null)
{
    private readonly GenerationTileFraming1458 framing = new(store, random, workspace, useWorldGenerationSolidity: true);

    internal bool TryPlace(int x, int y)
    {
        if (Solid(x, y) && At(x, y).SourceSlope == 0 && !At(x, y).IsHalfBrick) return false;
        int floor = -1;
        for (int k = 0; k < 80 && y + k < store.Dimensions.HeightTiles; k++)
            if (x >= 10 && x < store.Dimensions.WidthTiles - 10 && y + k >= 10 &&
                y + k < store.Dimensions.HeightTiles - 10 && At(x, y + k).IsActive && SolidType(At(x, y + k).Type))
            { floor = y + k + 2; break; }
        if (floor < 0) return false;
        int solids = 0, ground = 0, grass = 0;
        Circle(x, floor, 15, 15, (a, b) =>
        {
            if (!Solid(a, b)) return;
            solids++;
            if (At(a, b).Type is 59 or 60) ground++;
            if (At(a, b).Type == 60) grass++;
        });
        if ((double)ground / solids < .75 || grass < 2) return false;
        int inner = 0;
        Circle(x, floor, 8, 8, (a, b) => { if (Solid(a, b)) inner++; });
        if (inner < 20 || !canPlace(x - 8, floor - 8, 16, 16) || TooClose(x, floor)) return false;

        Circle(x, floor, 8, 8, (a, b) =>
        {
            double distance = Math.Sqrt((double)(a - x) * (a - x) + (double)(b - floor) * (b - floor));
            if (random.NextDouble() > Math.Clamp(distance / 10d, 0d, 1d) && Solid(a, b)) Set(a, b, 229);
        });
        var points = new List<(int X, int Y)>();
        var seen = new HashSet<(int X, int Y)>();
        void Hollow(int a, int b)
        {
            if (!Solid(a, b)) return;
            ref WorldTile tile = ref At(a, b);
            tile.ClearSourceShape();
            tile.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive);
            FrameNeighbours(a, b, false);
            int dx = a - x, dy = b - floor;
            if (dx < -6 || dx > 6 || dy < 0 || dy > 3) return;
            if (seen.Add((dx, dy))) points.Add((dx, dy));
            tile.LiquidKind = WorldLiquidKind.Honey;
            tile.LiquidAmount = 255;
        }
        Circle(x, floor, 4, 3, (a, b) =>
        {
            random.NextDouble();
            if (random.NextDouble() >= .3) { Hollow(a, b); return; }
            int left = random.Next(-1, 1), right = random.Next(0, 2);
            int top = random.Next(-1, 1), bottom = random.Next(0, 2);
            for (int dx = left; dx <= right; dx++)
                for (int dy = top; dy <= bottom; dy++) Hollow(a + dx, b + dy);
        });
        (int X, int Y)[] directions = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];
        foreach (var point in points)
        {
            if (!directions.Any(d => !seen.Contains((point.X + d.X, point.Y + d.Y)))) continue;
            int a = x + point.X, b = floor + 1 + point.Y;
            if (!At(a, b).IsActive && point.X is >= -6 and <= 6 && point.Y is >= 1 and <= 3) Set(a, b, 59);
        }
        foreach (var point in points)
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int a = x + point.X + dx, b = floor + point.Y + dy;
                    if (b >= floor && !Solid(a, b) && !(At(a, b).LiquidAmount > 0 && At(a, b).LiquidKind == WorldLiquidKind.Honey))
                        Set(a, b, 229);
                }
        protect(new WorldTileRegion(x - 8, floor - 8, 16, 16));
        return true;
    }

    private void Set(int x, int y, ushort type)
    {
        WorldTileFlags keep = At(x, y).Flags & (WorldTileFlagMasks.Wires | WorldTileFlagMasks.Actuation);
        At(x, y) = new WorldTile { Type = type, Flags = keep | WorldTileFlags.Active };
        framing.TileFrame(x, y, resetFrame: true);
        FrameNeighbours(x, y, true);
    }

    private void FrameNeighbours(int x, int y, bool reset)
    {
        framing.TileFrame(x + 1, y, reset);
        framing.TileFrame(x - 1, y, reset);
        framing.TileFrame(x, y + 1, reset);
        framing.TileFrame(x, y - 1, reset);
    }

    private bool TooClose(int x, int y)
    {
        if (y >= store.Dimensions.HeightTiles - 230) return true;
        for (int a = x - 150; a < x + 150; a += 10)
            for (int b = y - 150; b < y + 150; b += 10)
                if (a > 0 && a < store.Dimensions.WidthTiles && b > 0 && b < store.Dimensions.HeightTiles &&
                    (At(a, b).IsActive && At(a, b).Type == 226 || At(a, b).Wall is 83 or 3 or 87)) return true;
        return false;
    }

    private static void Circle(int cx, int cy, int rx, int ry, Action<int, int> visit)
    {
        int limit = (rx + 1) * (rx + 1);
        for (int y = cy - ry; y <= cy + ry; y++)
        {
            double offset = (double)rx / ry * (y - cy);
            int span = Math.Min(rx, (int)Math.Sqrt(limit - offset * offset));
            for (int x = cx - span; x <= cx + span; x++) visit(x, y);
        }
    }

    private bool SolidType(ushort type) => workspace?.HasVanillaGenerationSolidity == true
        ? workspace.IsVanillaGenerationSolid(new TileTypeId(type))
        : type is not (192 or 481 or 482 or 483 or 484 or 659) &&
          VanillaTileCollisionCatalog.IsSolid(new TileTypeId(type));
    private bool Solid(int x, int y) => At(x, y).IsActive && !At(x, y).IsActuated && SolidType(At(x, y).Type) &&
        !VanillaTileCollisionCatalog.IsSolidTop(At(x, y).TileType);
    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
