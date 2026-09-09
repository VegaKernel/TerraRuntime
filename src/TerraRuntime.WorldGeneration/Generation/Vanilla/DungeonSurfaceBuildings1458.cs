using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal readonly record struct DungeonBuildingPlatform1458(DungeonPoint1458 Position, int HeightFluff,
    bool ForcePlacement, double PotsChance, double BooksChance, double PotionsChance, bool NoWaterbolt);

/// <summary>Ordinary Dome/Tower building geometry and ordered feature candidates, never live world mutation.</summary>
internal sealed class DungeonSurfaceBuildings1458(WorldTileStore tiles, ushort brick, ushort cracked, ushort wall,
    double worldSurface, IWorldGenerationVanillaRandom random, CancellationToken cancellationToken)
{
    private readonly DungeonPillars1458 pillars = new(tiles, brick, random);
    private readonly List<DungeonBuildingPlatform1458> platforms = [];
    private int center, floor;
    private DungeonBounds1458? generationBounds;

    public (DungeonEntranceResult1458 Entrance, IReadOnlyList<DungeonBuildingPlatform1458> Platforms) GenerateDome(
        DungeonPoint1458 origin, int seed, bool leftDungeon)
    {
        Admit(origin, minimumY: 96);
        center = origin.X; floor = origin.Y - 30;
        var local = new DungeonUnifiedRandom1458(seed);
        bool trees = local.Next(4) != 0;
        int windowKind = local.Next(3);
        Prepare(origin);
        Circle(center + (leftDungeon ? 39 : -39), floor - 20, 20);
        Foundation(tower: false);
        Slime(center, floor, 40, 1, 1, (x, y) =>
        {
            if (y >= floor + 1 || At(x, y).Wall == wall) return;
            generationBounds = generationBounds is { } area
                ? new(Math.Min(area.Left, x), Math.Min(area.Top, y), Math.Max(area.Right, x), Math.Max(area.Bottom, y))
                : new(x, y, x, y);
            At(x, y) = default; At(x, y).Type = brick; At(x, y).Flags = WorldTileFlags.Active;
        });
        Slime(center, floor, 38, 1, 1, (x, y) => { if (y < floor + 2) At(x, y).Wall = wall; });
        Slime(center, floor - 4, 40, (double).9f, (double)1.1f, (x, y) =>
        {
            if (y >= floor - 1 || At(x, y).IsActive && At(x, y).Type == brick || At(x, y).Wall == wall) return;
            At(x, y) = default; At(x, y).Flags = WorldTileFlags.Active;
        });
        Slime(center, floor - 4, 40, (double).9f, (double)1.1f, (x, y) =>
        {
            if (y < floor - 1 && At(x, y).IsActive && At(x, y).Type == 0 && TouchesAir(x, y, onlyInactive: true)) At(x, y).Type = 2;
        });
        Slime(center, floor, 35, 1, 1, (x, y) => { if (y < floor + 1) DungeonGenerationTiles1458.ClearTile(ref At(x, y)); });
        Door(leftDungeon);
        MainWindow(windowKind, floor - 16, tower: false);
        byte paint = windowKind == 1 ? (byte)26 : (byte)0;
        // Basic side windows keep the style glass for Skeletron; its override applies only to the mosaic.
        Window(center - 29, floor - 8, 5, 10, windowKind == 2 ? (ushort)241 : null, paint);
        Window(center + 29, floor - 8, 5, 10, windowKind == 2 ? (ushort)91 : null, paint);
        Window(center - 20, floor - 11, 5, 11, windowKind == 2 ? (ushort)90 : null, paint);
        Window(center + 20, floor - 11, 5, 11, windowKind == 2 ? (ushort)88 : null, paint);
        pillars.Place(center - 14, floor, 3, 0, true, true, true);
        pillars.Place(center + 14, floor, 3, 0, true, true, true);
        AddPlatform(center - 20, floor - 25, true); AddPlatform(center + 20, floor - 25, true);
        AddPlatform(center - 20, floor - 20, true, false); AddPlatform(center + 20, floor - 20, true, false);
        pillars.Place(center - 38, floor - 10, 5, 16); pillars.Place(center + 38, floor - 10, 5, 16);
        pillars.Place(center - 27, floor - 28, 4, 14); pillars.Place(center + 27, floor - 28, 4, 14);
        pillars.Place(center - 14, floor - 37, 3, 13); pillars.Place(center + 14, floor - 37, 3, 13);
        if (trees)
            foreach ((int offset, int rise) in new[] { (-38, 25), (-27, 41), (-14, 49), (38, 25), (27, 41), (14, 49) })
                TreeOnPillar(local, center + offset, floor - rise);
        Stairs(leftDungeon ? 1 : -1);
        return (Result(origin, seed, floor - 40), platforms.ToArray());
    }

    private void Slime(int x, int y, int radius, double sx, double sy, Action<int, int> paint)
    {
        for (int row = y - (int)(radius * sy); row <= y + (int)(radius * sy * .5) - 1; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double vertical = row <= y ? (row - y) / sy : (row - y) * (2d / sy);
            int half = (int)Math.Min(radius * sx, sx * Math.Sqrt((radius + 1) * (radius + 1) - vertical * vertical));
            for (int column = x - half; column <= x + half; column++) paint(column, row);
        }
    }
    private bool TouchesAir(int x, int y, bool onlyInactive)
    {
        for (int column = x - 1; column <= x + 1; column++)
        for (int row = y - 1; row <= y + 1; row++)
        {
            WorldTile cell = At(column, row);
            if (!cell.IsActive || !onlyInactive && (!DungeonGenerationTiles1458.IsSolidType(cell.TileType) || VanillaTileCollisionCatalog.IsSolidTop(cell.TileType))) return true;
        }
        return false;
    }
    private void TreeOnPillar(DungeonUnifiedRandom1458 local, int x, int y)
    {
        if (At(x, y - 1).IsActive) return;
        for (int column = -2; column <= 2; column++)
        for (int depth = 0; depth <= 3; depth++)
        {
            ref WorldTile cell = ref At(x + column, y + depth);
            if (cell.Wall != wall) cell.Wall = 0;
            if ((depth != 1 || local.Next(2) != 0) && (depth != 2 || local.Next(3) == 0) && (depth != 3 || local.Next(4) == 0))
                cell.Type = TouchesAir(x + column, y + depth, onlyInactive: false) ? (ushort)2 : (ushort)0;
        }
        TreeGrower1458.TryGrow(tiles, x, y, random, ignoreWalls: true);
    }

    public (DungeonEntranceResult1458 Entrance, IReadOnlyList<DungeonBuildingPlatform1458> Platforms) GenerateTower(
        DungeonPoint1458 origin, int seed, bool leftDungeon)
    {
        Admit(origin, minimumY: 161);
        center = origin.X; floor = origin.Y - 30;
        var local = new DungeonUnifiedRandom1458(seed);
        int windowKind = local.Next(3);
        Prepare(origin);
        Circle(center + (leftDungeon ? 34 : -34), floor - 15, 15);
        Foundation(tower: true);
        TowerShell();
        foreach (int offset in new[] { -28, 28, -18, 18 }) pillars.Place(center + offset, floor, 3, 0, true, true, true);
        foreach ((int offset, int rise) in new[] { (-44, 30), (-34, 50), (-24, 90), (44, 30), (34, 50), (24, 90) })
        {
            pillars.BottomWedge(center + offset - (offset > 0 ? 1 : 0), floor - rise, 5, offset < 0);
            OuterPillar(center + offset, floor - rise);
        }
        foreach (int side in new[] { -1, 1 })
        {
            pillars.Place(center + side * 35, floor - 31, 5, 2);
            Fence(center + (side < 0 ? -42 : 29), center + (side < 0 ? -29 : 42), floor - 31);
            pillars.Place(center + side * 25, floor - 51, 5, 2);
            Fence(center + (side < 0 ? -32 : 19), center + (side < 0 ? -19 : 32), floor - 51);
            pillars.Place(center + side * 15, floor - 91, 5, 2);
            pillars.Place(center + side * 7, floor - 91, 5, 2);
        }
        Fence(center - 22, center + 22, floor - 91);
        pillars.BottomWedge(center - 15, floor - 85, 3, false);
        pillars.BottomWedge(center + 14, floor - 85, 3, true);
        foreach (int side in new[] { -1, 1 })
        {
            AddPlatform(center + side * 32, floor - 15, true);
            AddPlatform(center + side * 32, floor - 9, true);
            AddPlatform(center + side * 22, floor - 35, true);
            AddPlatform(center + side * 22, floor - 29, true);
        }
        AddPlatform(center, floor - 48, true);
        MainWindow(windowKind, floor - 70, tower: true);
        Window(center - 8, floor - 16, 9, 24); Window(center + 8, floor - 16, 9, 24);
        Window(center - 10, floor - 37, 7, 11); Window(center + 10, floor - 37, 7, 11);
        Window(center, floor - 39, 7, 13);
        Door(leftDungeon); Door(!leftDungeon);
        Stairs(1); Stairs(-1);
        return (Result(origin, seed, floor - 90), platforms.ToArray());
    }

    private void Admit(DungeonPoint1458 origin, int minimumY)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((brick, cracked, wall) is not ((41, 481, 7) or (43, 482, 8) or (44, 483, 9)) ||
            origin.X < 70 || origin.X >= tiles.Dimensions.WidthTiles - 70 || origin.Y < minimumY || origin.Y >= tiles.Dimensions.HeightTiles - 70)
            throw new InvalidOperationException("Unsupported ordinary dungeon building palette or bounds.");
        platforms.Clear();
    }
    private void Prepare(DungeonPoint1458 origin)
    {
        for (int x = origin.X - 60; x < origin.X + 60; x++)
        for (int y = origin.Y - 60; y < origin.Y + 60; y++)
        {
            ref WorldTile cell = ref At(x, y); cell.LiquidAmount = 0; cell.Shape = 0;
            // Tile.lava(false) clears the lava kind bit, preserving the other liquid-kind bit.
            cell.LiquidKind = (WorldLiquidKind)((int)cell.LiquidKind & ~1);
        }
    }
    private void Circle(int x, int y, int radius)
    {
        for (int row = -radius; row <= radius; row++)
        {
            int span = Math.Min(radius, (int)Math.Sqrt((radius + 1) * (radius + 1) - row * row));
            for (int column = -span; column <= span; column++) At(x + column, y + row) = default;
        }
    }
    private void Foundation(bool tower)
    {
        int opening = tower ? 35 : 30, openingEnd = tower ? 31 : 25, chamber = tower ? 35 : 30;
        for (int column = -40; column <= 40; column++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int depth = -5; depth < 100; depth++)
            {
                ref WorldTile cell = ref At(center + column, floor + depth);
                bool dungeonWall = DungeonGenerationTiles1458.IsDungeonWall(cell.Wall);
                bool foreign = cell.IsActive && cell.Type != brick && cell.Type != cracked;
                if (depth < 0) cell = default;
                else if (depth < 5 && Math.Abs(column) >= openingEnd && Math.Abs(column) <= opening)
                {
                    cell = default; if (!dungeonWall) cell.Wall = wall;
                }
                else if (depth is >= 5 and < 10 && Math.Abs(column) <= chamber) { cell = default; cell.Wall = wall; }
                else if (!dungeonWall || depth >= 10 && foreign)
                {
                    cell.LiquidAmount = 0; cell.Flags |= WorldTileFlags.Active; cell.Type = brick;
                    if (Math.Abs(column) != 40) cell.Wall = wall;
                }
                else if (depth >= 10 && cell.Wall != wall)
                {
                    cell.LiquidAmount = 0; if (Math.Abs(column) != 40) cell.Wall = wall;
                }
                if (depth == 1 && (column == -opening || column == (tower ? 30 : 25))) AddPlatform(center + column, floor + depth);
                if (depth == 10 && column == 0) AddPlatform(center, floor + depth);
            }
        }
        int countdown = -1, width = 6;
        for (int depth = 10; depth < 50; depth++)
        {
            if (countdown == -1 && !At(center, floor + depth).IsActive) countdown = 15;
            if (countdown > 0) { if (--countdown == 0) break; if (countdown <= 5) width--; }
            for (int column = -width; column <= width; column++) { At(center + column, floor + depth) = default; At(center + column, floor + depth).Wall = wall; }
        }
    }
    private void TowerShell()
    {
        for (int column = -40; column <= 40; column++)
        for (int rise = 0; rise <= 90; rise++)
        {
            int exterior, interior, roof, nextInterior;
            if (rise <= 30) { exterior = 40; interior = 35; roof = 30; nextInterior = 25; }
            else if (rise <= 50 && Math.Abs(column) <= 30) { exterior = 30; interior = 25; roof = 50; nextInterior = 15; }
            else if (rise >= 45 && Math.Abs(column) <= 20) { exterior = 20; interior = 15; roof = 90; nextInterior = -1; }
            else continue;
            ref WorldTile cell = ref At(center + column, floor - rise);
            if (Math.Abs(column) < exterior) { cell = default; cell.Wall = wall; }
            if (Math.Abs(column) > interior || rise >= roof - 5 && Math.Abs(column) > nextInterior)
                DungeonGenerationTiles1458.SetBrick(ref cell, brick, false);
        }
    }
    private void AddPlatform(int x, int y, bool shelf = false, bool noWaterbolt = true) =>
        platforms.Add(new(new(x, y), 0, true, (double).33f, shelf ? .75 : 0, shelf ? (double).1f : 0, shelf && noWaterbolt));
    private void Window(int x, int y, int width, int height, ushort? glass = null, byte paint = 0) =>
        DungeonWindows1458.Basic(tiles, random, brick, x, y, width, height, glass, paint);
    private void MainWindow(int kind, int y, bool tower)
    {
        if (kind == 0)
        {
            Window(center - (tower ? 9 : 8), y + (tower ? 4 : 0), 5, 24);
            Window(center + (tower ? 9 : 8), y + (tower ? 4 : 0), 5, 24);
            Window(center, y + (tower ? 3 : -1), 5, 28);
        }
        else new DungeonMosaicWindows1458(tiles, kind == 1 ? (ushort)89 : (ushort)91,
            DungeonWindows1458.Palette(brick).Edge, kind == 1 ? (byte)26 : (byte)0)
            .Place(center, y + (kind == 1 ? (tower ? -1 : -3) : (tower ? 5 : -1)), kind);
    }
    private void Stairs(int direction)
    {
        int potentialTop = DungeonGraphGenerator1458.ResolvePotentialBounds(tiles.Dimensions, worldSurface, -direction).Top;
        DungeonEntranceStairs1458.Generate(tiles, center + direction * 40, floor, direction, 100,
            potentialTop, brick, wall, cancellationToken);
    }

    private void Door(bool right)
    {
        int start = right ? 34 : -42, end = right ? 42 : -34, outer = center + (right ? 39 : -39);
        for (int x = center + start; x <= center + end; x++)
        for (int y = floor - 3; y <= floor + 1; y++)
        {
            ref WorldTile cell = ref At(x, y);
            if (right ? x >= outer : x <= outer) cell.Wall = 0;
            if (y >= floor - 2 && y <= floor) DungeonGenerationTiles1458.ClearTile(ref cell);
        }
        DungeonGenerationTiles1458.PlaceEntranceDoor(tiles, random, outer, floor);
        DungeonGenerationTiles1458.PlaceEntranceDoor(tiles, random, center + (right ? 36 : -36), floor);
    }
    private void Fence(int left, int right, int y)
    {
        if (y <= 10 || left < 10 || right > tiles.Dimensions.WidthTiles - 10) return;
        for (int x = left; x <= right; x++) GenerationWallPlacement1458.Place(tiles, x, y, 245, random);
    }
    private void OuterPillar(int x, int y)
    {
        pillars.Place(x, y - 1, 7, 3); pillars.Place(x, y - 4, 5, 7);
        if (y - 11 >= 10) Campfire(x, y - 11);
        for (int column = -2; column <= 2; column++) GenerationWallPlacement1458.Place(tiles, x + column, y - 11, 245, random);
        if (y - 12 >= 10) { GenerationWallPlacement1458.Place(tiles, x - 2, y - 12, 245, random); GenerationWallPlacement1458.Place(tiles, x + 2, y - 12, 245, random); }
        if (y - 10 >= 10) { GenerationWallPlacement1458.Place(tiles, x - 2, y - 10, 245, random); GenerationWallPlacement1458.Place(tiles, x + 2, y - 10, 245, random); }
    }
    private void Campfire(int x, int y)
    {
        ref WorldTile anchor = ref At(x, y);
        if (!anchor.IsActive) HellFortGenerator1458.ClearPlacementAnchor(ref anchor);
        else
        {
            // PlaceTile(215) resets the anchor's half-brick and frames even when Place3x2 fails.
            // Entrance supports are ordinary non-frame-important terrain, not furniture.
            if (!VanillaTileDefinitionCatalog.TryGet(anchor.TileType, out VanillaTileDefinition definition) || definition.IsFrameImportant)
                throw new InvalidOperationException("Unsupported campfire placement intersection.");
            if (anchor.Shape == 1) anchor.Shape = 0;
            anchor.FrameX = anchor.FrameY = 0;
        }
        bool valid = true;
        for (int column = -1; column <= 1; column++)
        {
            for (int row = -1; row <= 0; row++) valid &= !At(x + column, y + row).IsActive && At(x + column, y + row).LiquidAmount == 0;
            valid &= DungeonGenerationTiles1458.SolidTile(At(x + column, y + 1));
        }
        if (valid)
            for (int column = -1; column <= 1; column++)
            for (int row = -1; row <= 0; row++)
            {
                ref WorldTile cell = ref At(x + column, y + row); cell.Type = 215; cell.Flags |= WorldTileFlags.Active;
                cell.FrameX = (short)((column + 1) * 18); cell.FrameY = (short)((row + 1) * 18);
            }
        for (int column = -1; column <= 1; column++)
        for (int row = -1; row <= 1; row++)
        {
            ref WorldTile cell = ref At(x + column, y + row);
            if (!cell.IsActive) { cell.Shape = cell.TileColor = 0; cell.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock); }
        }
    }
    private DungeonEntranceResult1458 Result(DungeonPoint1458 origin, int seed, int top) =>
        new(new(DungeonComponentKind1458.Entrance, origin, new(center, floor), new(center - 40, top, center + 40, Math.Max(origin.Y, floor + 9)), seed)
            { GenerationBounds = generationBounds }, new(center, floor), null);
    private ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
}
