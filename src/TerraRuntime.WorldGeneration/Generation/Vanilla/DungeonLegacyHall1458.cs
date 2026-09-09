using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Direct-generated ordinary LegacyDungeonHall, with its component-local random stream.</summary>
internal sealed class DungeonLegacyHall1458(WorldTileStore tiles, ushort brick, ushort crackedBrick, ushort wall,
    double rockLayer, int underworldTop, CancellationToken cancellationToken)
{
    private enum VerticalBias { Initial, Edge, Shallow }

    public (DungeonComponent1458 Component, DungeonPoint1458 Cursor, DungeonPoint1458 Direction)
        Generate(DungeonPoint1458 origin, DungeonPoint1458 previousDirection, int seed)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((brick, crackedBrick, wall) is not ((41, 481, 7) or (43, 482, 8) or (44, 483, 9)))
            throw new InvalidOperationException("Unsupported ordinary dungeon hall palette.");
        var random = new DungeonUnifiedRandom1458(seed);
        int strength = 4 + random.Next(2), steps = 35 + random.Next(45);
        bool cracked = random.NextDouble() <= .166;
        if (random.Next(5) == 0) { strength *= 2; steps /= 2; }
        int initialStrength = strength, lowerLimit = underworldTop - 100;
        double vx = 0, vy = 0;
        bool zigzag = false;
        DungeonPoint1458 direction = default;
        for (int retry = 0; ; retry++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (retry >= 100_000) throw new InvalidOperationException("Dungeon hall direction retry exhausted.");
            bool horizontal;
            int sign;
            if (retry == 0)
            {
                bool up = CanExtend(0, -1, out _), down = CanExtend(0, 1, out bool bottomBlocked);
                bool left = CanExtend(-1, 0, out _), right = CanExtend(1, 0, out _);
                if (!up && !down && !left && !right)
                {
                    sign = random.Next(2) != 0 ? 1 : -1;
                    horizontal = random.Next(2) == 0;
                    if (sign == 1 && !horizontal && bottomBlocked)
                    {
                        sign = random.Next(2) == 0 ? 1 : -1;
                        horizontal = true;
                    }
                }
                else
                {
                    int selected = 0;
                    for (int attempt = 99; attempt > 0; attempt--)
                    {
                        selected = random.Next(4);
                        if (selected == 1 && bottomBlocked) selected = random.Next(2) == 0 ? 2 : 3;
                        if (selected switch { 0 => up, 1 => down, 2 => left, _ => right }) break;
                        if (attempt == 1) selected = 0;
                    }
                    horizontal = selected >= 2;
                    sign = selected is 0 or 2 ? -1 : 1;
                }
            }
            else
            {
                sign = random.Next(2) != 0 ? 1 : -1;
                horizontal = random.Next(2) == 0;
                if (sign == 1 && origin.Y + steps >= lowerLimit)
                {
                    sign = random.Next(2) != 0 ? 1 : -1;
                    horizontal = true;
                }
            }
            if (horizontal) Horizontal(sign);
            else Vertical(sign, VerticalBias.Initial);
            if (previousDirection != new DungeonPoint1458(-direction.X, -direction.Y)) break;
        }

        // Source applies these in this priority order AFTER rejecting a reversed previous hall.
        if (origin.X > tiles.Dimensions.WidthTiles - 200) Horizontal(-1);
        else if (origin.X < 200) Horizontal(1);
        else if (origin.Y >= lowerLimit) Vertical(-1, VerticalBias.Edge);
        else if (origin.Y < 200) Vertical(1, VerticalBias.Edge);
        else if (origin.Y < rockLayer + 100) Vertical(1, VerticalBias.Shallow);
        else if (origin.X < tiles.Dimensions.WidthTiles / 2 && origin.X > tiles.Dimensions.WidthTiles * .25f) Horizontal(-1);
        else if (origin.X > tiles.Dimensions.WidthTiles / 2 && origin.X < tiles.Dimensions.WidthTiles * .75f) Horizontal(1);

        if (Math.Abs(vx) > Math.Abs(vy) && random.Next(3) != 0)
            strength = (int)((float)initialStrength * (random.Next(110, 150) * .01f));

        double x = origin.X, y = origin.Y;
        int leftBound = origin.X, top = origin.Y, rightBound = origin.X, bottom = origin.Y, turnTicks = 0;
        while (steps > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            turnTicks++;
            if ((direction.X > 0 && x > tiles.Dimensions.WidthTiles - 100) ||
                (direction.X < 0 && x < 100) || (direction.Y > 0 && y >= lowerLimit) ||
                (direction.Y < 0 && (y < 100 || y < rockLayer + 50))) steps = 0;
            steps--; // Even a stopped hall paints one final brush before moving its cursor.
            int x0 = ClampX((int)(x - strength - 4d - random.Next(6)));
            int x1 = ClampX((int)(x + strength + 4d + random.Next(6)));
            int y0 = ClampY((int)(y - strength - 4d - random.Next(6)));
            int y1 = ClampY((int)(y + strength + 4d + random.Next(6)));
            leftBound = Math.Min(leftBound, x0); top = Math.Min(top, y0);
            rightBound = Math.Max(rightBound, x1); bottom = Math.Max(bottom, y1);
            for (int tx = x0; tx < x1; tx++)
            for (int ty = y0; ty < y1; ty++)
            {
                ref WorldTile tile = ref At(tx, ty);
                tile.LiquidAmount = 0;
                if (ty <= underworldTop + 7 && DungeonGenerationTiles1458.CanPlaceHallBrick(in tile, crackedBrick))
                    DungeonGenerationTiles1458.SetBrick(ref tile, brick, tx > x0 && tx < x1 - 1 && ty > y0 && ty < y1 - 1);
            }
            // Direct GenerateRoom legacy rooms have empty protection shapes (CalculateRoom was not called).
            for (int tx = x0 + 1; tx < x1 - 1; tx++)
            for (int ty = y0 + 1; ty < y1 - 1; ty++)
                if (ty < underworldTop + 7) DungeonGenerationTiles1458.SetWall(ref At(tx, ty), wall, false);

            int widening = 0;
            if (vy == 0 && random.Next(strength + 1) == 0) widening = random.Next(1, 3);
            else if (vx == 0 && random.Next(strength - 1) == 0) widening = random.Next(1, 3);
            else if (random.Next(strength * 3) == 0) widening = random.Next(1, 3);
            double radius = strength * .5;
            int ix0 = ClampX((int)(x - radius - widening)), ix1 = ClampX((int)(x + radius + widening));
            int iy0 = ClampY((int)(y - radius - widening)), iy1 = ClampY((int)(y + radius + widening));
            for (int tx = ix0; tx < ix1; tx++)
            for (int ty = iy0; ty < iy1; ty++)
            {
                ref WorldTile tile = ref At(tx, ty);
                if (!cracked) DungeonGenerationTiles1458.ClearTile(ref tile);
                else if ((tile.IsActive || !DungeonGenerationTiles1458.IsDungeonWall(tile.Wall)) && ty < underworldTop)
                {
                    DungeonGenerationTiles1458.ClearTile(ref tile);
                    DungeonGenerationTiles1458.SetBrick(ref tile, crackedBrick, false);
                }
                if (ty < underworldTop) DungeonGenerationTiles1458.SetWall(ref tile, wall, false);
            }
            x += vx; y += vy;
            if (zigzag && turnTicks > random.Next(10, 20)) { turnTicks = 0; vx = -vx; }
        }
        DungeonPoint1458 end = new((int)x, (int)y);
        return (new(DungeonComponentKind1458.Hall, origin, end, new(leftBound, top, rightBound, bottom), seed)
            { HallDirection = direction, GenerationBounds = new(leftBound, top, rightBound, bottom) }, end, direction);

        bool CanExtend(int dx, int dy, out bool bottomBlocked)
        {
            bottomBlocked = false;
            bool leftDungeon = false;
            for (int distance = 0; distance < steps; distance++)
            {
                int tx = origin.X + dx * distance, ty = origin.Y + dy * distance;
                if (tx < 50 || ty < 50 || tx >= tiles.Dimensions.WidthTiles - 50 || ty >= tiles.Dimensions.HeightTiles - 50) return false;
                if (dy > 0 && ty >= lowerLimit) { bottomBlocked = true; return false; }
                if (DungeonGenerationTiles1458.IsDungeonWall(At(tx, ty).Wall))
                {
                    if (leftDungeon) return false;
                }
                else leftDungeon = true;
            }
            return true;
        }
        void Horizontal(int sign)
        {
            direction = new(sign, 0); vx = sign; vy = 0;
            if (random.Next(3) == 0) vy = random.Next(2) == 0 ? -(double).2f : (double).2f;
        }
        void Vertical(int sign, VerticalBias bias)
        {
            strength++;
            direction = new(0, sign); vx = 0; vy = sign;
            if (bias != VerticalBias.Edge && random.NextDouble() <= .66)
            {
                zigzag = true;
                vx = (random.Next(2) == 0 ? random.Next(10, 20) : -random.Next(10, 20)) * .1;
            }
            else if (random.Next(2) == 0)
            {
                bool positive = random.Next(2) == 0;
                if (bias == VerticalBias.Edge)
                    vx = (positive ? random.Next(20, 50) : -random.Next(20, 50)) * .01f;
                else if (bias == VerticalBias.Shallow)
                    vx = random.Next(20, 50) * .01; // Both source branches are positive; retain their sign draw above.
                else vx = (positive ? random.Next(20, 40) : -random.Next(20, 40)) * .01;
            }
            else if (bias == VerticalBias.Initial) steps /= 2;
        }
    }

    private int ClampX(int value) => Math.Clamp(value, 0, tiles.Dimensions.WidthTiles - 1);
    private int ClampY(int value) => Math.Clamp(value, 0, tiles.Dimensions.HeightTiles - 1);
    private ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
}
