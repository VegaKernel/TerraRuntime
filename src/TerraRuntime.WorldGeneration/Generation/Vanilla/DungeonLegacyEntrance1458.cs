using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary surface LegacyEntrance building. Owns geometry, not graph layout or NPC creation.</summary>
internal sealed class DungeonLegacyEntrance1458(WorldTileStore tiles, ushort brick, ushort crackedBrick, ushort wall,
    double surface, int strengthX, int strengthY, int strengthX2, int strengthY2,
    IWorldGenerationVanillaRandom worldRandom, CancellationToken cancellationToken)
{
    // LegacyDungeonEntrance uses promoted single-precision ratios, except its outer wall clearing rectangle.
    private const double ExteriorRatio = .6000000238418579, VestibuleOffset = .550000011920929;
    private int left, top, right, bottom;
    private readonly record struct Rectangle(int Left, int Top, int Right, int Bottom);

    public DungeonEntranceResult1458 Generate(DungeonPoint1458 origin, int seed)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((brick, crackedBrick, wall) is not ((41, 481, 7) or (43, 482, 8) or (44, 483, 9)) ||
            strengthX is < 25 or > 29 || strengthY is < 20 or > 24 || strengthX2 is < 35 or > 49 || strengthY2 is < 10 or > 14)
            throw new InvalidOperationException("Unsupported ordinary dungeon entrance settings.");
        if (!double.IsFinite(surface) || surface < 10 || surface >= tiles.Dimensions.HeightTiles - 10 ||
            origin.X < 50 || origin.X >= tiles.Dimensions.WidthTiles - 50 || origin.Y < 50 || origin.Y >= tiles.Dimensions.HeightTiles - 50)
            throw new InvalidOperationException("Dungeon surface entrance is outside its admitted world bounds.");

        var random = new DungeonUnifiedRandom1458(seed);
        for (int x = Math.Max(0, origin.X - 60); x < Math.Min(tiles.Dimensions.WidthTiles, origin.X + 60); x++)
        for (int y = Math.Max(0, origin.Y - 60); y < Math.Min(tiles.Dimensions.HeightTiles, origin.Y + 60); y++)
        {
            ref WorldTile cell = ref At(x, y);
            cell.LiquidAmount = 0; UnderworldTerrain1458.ClearLavaFlag(ref cell); cell.Shape = 0;
        }
        double cx = origin.X, cy = origin.Y - strengthY / 2d;
        int direction = origin.X > tiles.Dimensions.WidthTiles / 2 ? -1 : 1;
        left = (int)cx; top = (int)cy; right = left + 1; bottom = top + 1;
        var hall = Outer(cx, cy, strengthX, strengthY, random, mainHall: true);
        Include(hall);
        for (int x = hall.Left; x < hall.Right; x++)
        for (int y = hall.Top; y < hall.Bottom; y++)
        {
            ref WorldTile cell = ref At(x, y); cell.LiquidAmount = 0;
            if (cell.Wall == wall) continue;
            cell.Wall = x > hall.Left + 1 && x < hall.Right - 2 && y > hall.Top + 1 && y < hall.Bottom - 2 ? wall : (ushort)0;
            Brick(ref cell);
        }
        Turrets(hall.Left, hall.Right, hall.Top, random);
        int merlonWidth = 2 + random.Next(4), merlonHeight = 1 + random.Next(2);
        var generationBounds = new DungeonBounds1458(hall.Left, Math.Max(0, hall.Top - merlonHeight), hall.Right, hall.Top);
        Battlements(hall.Left, hall.Right, hall.Top, merlonWidth, merlonHeight, trackBounds: true);
        Supports(hall.Left, hall.Right, hall.Top);
        var chamber = Rect(cx - strengthX * .5, cy - strengthY * .5, cx + strengthX * .5, cy + strengthY * .5);
        ClearChamber(chamber, wall);
        DungeonPoint1458? platform = FindPlatform((int)cx, chamber.Bottom);

        cx += strengthX * ExteriorRatio * direction;
        cy += strengthY * .5;
        cx += strengthX2 * VestibuleOffset * direction;
        cy -= strengthY2 * .5;
        var vestibule = Outer(cx, cy, strengthX2, strengthY2, random, mainHall: false);
        Include(vestibule);
        for (int x = vestibule.Left; x < vestibule.Right; x++)
        for (int y = vestibule.Top; y < vestibule.Bottom; y++)
        {
            ref WorldTile cell = ref At(x, y);
            if (cell.IsActive && cell.Type == brick) continue;
            cell.LiquidAmount = 0;
            if (direction < 0 ? x < cx - strengthX2 * .5 : x > cx + strengthX2 * .5 - 1) continue;
            cell.Wall = 0; Brick(ref cell);
        }
        Include(vestibule with { Bottom = (int)surface });
        Supports(vestibule.Left, vestibule.Right, vestibule.Bottom);
        int innerLeft = ClampX((int)(cx - strengthX2 * .5)), innerRight = ClampX((int)(cx + strengthX2 * .5));
        Turrets(innerLeft + (direction < 0 ? 1 : 0), innerRight, vestibule.Top, random);
        merlonHeight = 1 + random.Next(2); merlonWidth = 2 + random.Next(4);
        Battlements(innerLeft + 1, innerRight - 1 + (direction < 0 ? 1 : 0), vestibule.Top, merlonWidth, merlonHeight, trackBounds: false);

        // Source's wall-only clearing rectangle uses width for BOTH axes. Admitted origins keep it in range.
        var windows = new Rectangle(ClampX((int)(cx - strengthX2 * .6)), ClampX((int)(cy - strengthY2 * .6)),
            ClampX((int)(cx + strengthX2 * .6)), ClampX((int)(cy + strengthY2 * .6)));
        Include(windows);
        for (int x = windows.Left; x < windows.Right; x++)
        for (int y = windows.Top; y < windows.Bottom; y++) { At(x, y).LiquidAmount = 0; At(x, y).Wall = 0; }
        chamber = Rect(cx - strengthX2 * .5, cy - strengthY2 * .5, cx + strengthX2 * .5, cy + strengthY2 * .5);
        ClearChamber(chamber, 0);
        var oldMan = new DungeonPoint1458((int)cx, chamber.Bottom);
        int potentialTop = DungeonGraphGenerator1458.ResolvePotentialBounds(tiles.Dimensions, surface, direction > 0 ? -1 : 1).Top;
        DungeonEntranceStairs1458.Generate(tiles, direction > 0 ? chamber.Right : chamber.Left, chamber.Bottom,
            direction, potentialTop - chamber.Bottom - 5, potentialTop, brick, wall, cancellationToken);
        _ = random.Next(2); // Source draws an unused pillar height before pillar width.
        int pillarWidth = 2 + random.Next(4), drawn = 0;
        for (int x = ClampX((int)(cx - strengthX2 * .5) + 2); x < ClampX((int)(cx + strengthX2 * .5) - 2); x++)
        {
            for (int y = chamber.Top; y <= chamber.Bottom; y++) GenerationWallPlacement1458.Place(tiles, x, y, wall, worldRandom);
            if (++drawn >= pillarWidth) { x += pillarWidth * 2; drawn = 0; }
        }
        cx -= strengthX2 * ExteriorRatio * direction;
        cy += strengthY2 * .5 - 1.5;
        var passage = Rect(cx - 7.5, cy - 1.5, cx + 7.5, cy + 1.5);
        Include(passage);
        if (direction < 0) cx--;
        for (int x = passage.Left; x < passage.Right; x++)
        for (int y = passage.Top; y < passage.Bottom; y++)
        {
            ref WorldTile cell = ref At(x, y); cell.Flags &= ~WorldTileFlags.Active;
            if (direction > 0 ? x < cx : x > cx) cell.Wall = wall;
        }
        DungeonGenerationTiles1458.PlaceEntranceDoor(tiles, worldRandom, (int)cx, (int)(cy + 1));
        return new(new(DungeonComponentKind1458.Entrance, origin, oldMan, new(left, top, right - 1, bottom - 1), seed)
            { GenerationBounds = generationBounds }, oldMan, platform)
            { GenerationTopOverride = (int)(origin.Y - strengthY / 2d) };
    }

    private Rectangle Outer(double x, double y, int sx, int sy, DungeonUnifiedRandom1458 random, bool mainHall)
    {
        int padMin = mainHall ? 2 : 1, padMax = mainHall ? 5 : 3;
        int x0 = ClampX((int)(x - sx * ExteriorRatio - random.Next(padMin, padMax)));
        int x1 = ClampX((int)(x + sx * ExteriorRatio + random.Next(padMin, padMax)));
        int y0 = ClampY((int)(y - sy * ExteriorRatio - random.Next(padMin, padMax)));
        int y1 = ClampY((int)(y + sy * ExteriorRatio + random.Next(mainHall ? 8 : 6, 16)));
        return new(x0, y0, x1, y1);
    }

    private void Turrets(int x0, int x1, int roof, DungeonUnifiedRandom1458 random)
    {
        var a = new Rectangle(x0, 0, ClampX(x0 + 5 + random.Next(4)), roof);
        a = a with { Top = ClampY(roof - 3 - random.Next(3)) };
        Turret(a);
        var b = new Rectangle(ClampX(x1 - 5 - random.Next(4)), 0, x1, roof);
        b = b with { Top = ClampY(roof - 3 - random.Next(3)) };
        Turret(b);
    }
    private void Turret(Rectangle rect)
    {
        Include(rect);
        for (int x = rect.Left; x < rect.Right; x++)
        for (int y = rect.Top; y < rect.Bottom; y++) ProtectedBrick(ref At(x, y));
    }
    private void Battlements(int x0, int x1, int roof, int width, int height, bool trackBounds)
    {
        int drawn = 0;
        for (int x = x0; x < x1; x++)
        {
            for (int y = ClampY(roof - height); y < roof; y++)
            {
                if (trackBounds) Include(new(x, y, x, y));
                ProtectedBrick(ref At(x, y));
            }
            if (++drawn >= width) { x += width; drawn = 0; }
        }
    }
    private void Supports(int x0, int x1, int startY)
    {
        // Ordinary potential dungeon bounds start at worldSurface+10; the (x,y-5) test cannot intersect
        // these above-surface supports. Dual/buried/other special-seed support cutoffs are not admitted here.
        for (int x = x0; x < x1; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = startY; y < surface; y++)
            {
                ref WorldTile cell = ref At(x, y); cell.LiquidAmount = 0;
                bool inside = x > x0 && x < x1 - 1;
                if ((cell.IsActive && cell.Type != brick && cell.Type != crackedBrick) || !DungeonGenerationTiles1458.IsDungeonWall(cell.Wall))
                {
                    Brick(ref cell); if (inside) cell.Wall = wall;
                }
                else if (cell.Wall != wall && inside) cell.Wall = wall;
            }
        }
    }
    private void ClearChamber(Rectangle rect, ushort background)
    {
        Include(rect);
        for (int x = rect.Left; x < rect.Right; x++)
        for (int y = rect.Top; y < rect.Bottom; y++)
        {
            ref WorldTile cell = ref At(x, y); cell.LiquidAmount = 0;
            cell.Flags &= ~WorldTileFlags.Active; cell.Wall = background;
        }
    }
    private DungeonPoint1458? FindPlatform(int cx, int y)
    {
        for (int offset = 0; offset < 20; offset++)
        {
            int x = cx - offset; if (x <= 0) break;
            if (!At(x, y).IsActive && DungeonGenerationTiles1458.IsDungeonWall(At(x, y).Wall)) return new(x, y);
            x = cx + offset; if (x >= tiles.Dimensions.WidthTiles) break;
            if (!At(x, y).IsActive && DungeonGenerationTiles1458.IsDungeonWall(At(x, y).Wall)) return new(x, y);
        }
        return null;
    }
    private void ProtectedBrick(ref WorldTile tile) { tile.LiquidAmount = 0; if (tile.Wall != wall) Brick(ref tile); }
    private void Brick(ref WorldTile tile) { tile.Flags |= WorldTileFlags.Active; tile.Type = brick; tile.Shape = 0; }
    private Rectangle Rect(double x0, double y0, double x1, double y1) => new(ClampX((int)x0), ClampY((int)y0), ClampX((int)x1), ClampY((int)y1));
    private int ClampX(int x) => Math.Clamp(x, 0, tiles.Dimensions.WidthTiles - 1);
    private int ClampY(int y) => Math.Clamp(y, 0, tiles.Dimensions.HeightTiles - 1);
    private void Include(Rectangle rect)
    {
        left = Math.Clamp(Math.Min(left, rect.Left), 10, tiles.Dimensions.WidthTiles - 10);
        right = Math.Clamp(Math.Max(right, rect.Right), 10, tiles.Dimensions.WidthTiles - 10);
        top = Math.Clamp(Math.Min(top, rect.Top), 10, tiles.Dimensions.HeightTiles - 10);
        bottom = Math.Clamp(Math.Max(bottom, rect.Bottom), 10, tiles.Dimensions.HeightTiles - 10);
    }
    private ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
}
