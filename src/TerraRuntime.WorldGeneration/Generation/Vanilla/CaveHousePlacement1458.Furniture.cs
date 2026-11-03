using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal static partial class CaveHousePlacement1458
{
    internal static void FillRooms(Workspace workspace, IWorldGenerationVanillaRandom random,
        IReadOnlyList<CaveHouseRoom1458> rooms, CaveHouseType1458 type)
    {
        (int table, int workbench, int piano, int bookcase) = type switch
        {
            CaveHouseType1458.Wood => (0, 0, 0, 0),
            CaveHouseType1458.Ice => (28, 23, 23, 25),
            CaveHouseType1458.Desert => (7, 39, 38, 39),
            CaveHouseType1458.Jungle => (2, 2, 2, 12),
            CaveHouseType1458.Mushroom => (27, 7, 22, 24),
            CaveHouseType1458.Granite => (33, 29, 28, 30),
            CaveHouseType1458.Marble => (34, 30, 29, 31),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        (ushort Type, int Style)[] furniture =
        [((ushort)(type == CaveHouseType1458.Desert ? 469 : 14), table), (16, 0), (18, workbench),
            (86, 0), (87, piano), (94, 0), (101, bookcase)];
        var grid = new Grid(workspace.TileStore, workspace.HasVanillaGenerationSolidity ? workspace.IsVanillaGenerationSolid : null);
        foreach (CaveHouseRoom1458 room in rooms)
        {
            int fixtures = room.Width / 8, spacing = room.Width / (fixtures + 1);
            int first = random.Next(2);
            for (int fixture = 0; fixture < fixtures; fixture++)
            {
                int x = room.X + (fixture + 1) * spacing;
                // The source adds the column index to the parity. It does not
                // alternate beyond the first two decorative positions.
                if (fixture + first % 2 == 0)
                {
                    int y = room.Y + Math.Min(room.Height / 2, room.Height - 5);
                    (ushort picture, int style) = FurnitureSelectPicture(random, type == CaveHouseType1458.Desert);
                    PlaceHouseObject(workspace, random, x, y, picture, style);
                }
                else if (fixture + first % 2 == 1)
                {
                    int y = room.Y + 1;
                    PlaceHouseObject(workspace, random, x, y, 34, random.Next(6));
                    // Even a refused chandelier attempt shifts all nine cells.
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = 0; dy < 3; dy++)
                            grid.At(x + dx, y + dy).FrameX += 54;
                }
            }
            int attempts = room.Width / 8 + 3;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                int x = random.Next(room.Width - 3) + room.X + 1, y = room.Bottom - 2;
                switch (random.Next(4))
                {
                    case 0:
                        FurniturePlaceSmallPile(grid, x, y, random.Next(31, 34));
                        break;
                    case 1:
                        PlaceHouseObject(workspace, random, x, y, 186, random.Next(22, 26));
                        break;
                    case 2:
                        int statue = random.Next(2, 44 + FurnitureExtraStatueStyles.Length);
                        int style = statue < 43 ? statue : statue == 43 ? 50 : FurnitureExtraStatueStyles[statue - 44];
                        PlaceHouseObject(workspace, random, x, y, statue == 34 ? (ushort)349 : (ushort)105,
                            statue == 34 ? 0 : style);
                        if (statue is 4 or 7 or 10 or 18)
                            FurniturePlaceStatueTrap(workspace, random, grid, x, y);
                        break;
                    default:
                        (ushort objectType, int objectStyle) = furniture[random.Next(furniture.Length)];
                        PlaceHouseObject(workspace, random, x, y, objectType, objectStyle);
                        break;
                }
            }
        }
    }

    private static ReadOnlySpan<int> FurnitureExtraStatueStyles =>
        [63, 64, 65, 66, 68, 69, 70, 71, 72, 73, 75, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 77, 78, 67, 74, 37, 2];

    private static (ushort Type, int Style) FurnitureSelectPicture(IWorldGenerationVanillaRandom random, bool desert)
    {
        int family = random.Next(4);
        if (desert)
            return family <= 1 ? ((ushort)240, 63 + random.Next(6)) :
                family == 2 ? ((ushort)245, 7 + random.Next(2)) : ((ushort)242, 37 + random.Next(6));
        if (family == 3 && random.Next(2) != 0)
            family = random.Next(3);
        if (family <= 1)
        {
            ReadOnlySpan<int> styles = [26, 28, 20, 21, 22, 24, 25, 33, 34, 35, 76, 77, 78, 79, 82, 96, 98, 100];
            return (240, styles[random.Next(styles.Length)]);
        }
        if (family == 2)
        {
            int choice = random.Next(9);
            if (choice == 0 && random.Next(3) != 0)
                choice = random.Next(9);
            return (245, choice switch
            {
                0 => 0,
                1 => 3,
                2 => 5,
                3 => 6,
                4 => 14,
                5 => 15 + random.Next(4),
                6 => 19,
                7 => 20,
                _ => 21
            });
        }
        ReadOnlySpan<int> wideStyles = [1, 30, 31, 32, 33, 34, 35, 36];
        return (246, wideStyles[random.Next(wideStyles.Length)]);
    }

    internal static bool PlaceHouseObject(Workspace workspace, IWorldGenerationVanillaRandom random,
        int x, int y, ushort type, int style = 0, bool forced = false)
    {
        var grid = new Grid(workspace.TileStore, workspace.HasVanillaGenerationSolidity ? workspace.IsVanillaGenerationSolid : null);
        if (!grid.InWorld(x, y, 1, 1, 5) || grid.At(x, y) is { IsActive: true, Type: 488 })
            return false;
        ref WorldTile anchor = ref grid.At(x, y);
        if (!forced && anchor.IsActive && grid.IsSolidType(new TileTypeId(type)))
            return false;
        if (!anchor.IsActive)
        {
            HellFortGenerator1458.ClearPlacementAnchor(ref anchor);
            anchor.Flags &= ~WorldTileFlags.HalfBrickWithSlope;
        }
        else if (!VanillaWorldFrameImportance326.IsFrameImportant(anchor.Type))
        {
            anchor.TrySetSourceShape(false, anchor.SourceSlope);
            anchor.FrameX = 0;
            anchor.FrameY = 0;
        }
        if (type == 105 && style == 34)
        {
            type = 349;
            style = 0;
        }
        bool picture = type is 240 or 242 or 245 or 246;
        (int dx, int dy, int width, int height, int frameX, int frameY) = type switch
        {
            14 when style == 25 => (-1, 0, 3, 1, style * 54, 0),
            14 or 86 or 87 or 186 or 377 or 469 => (-1, -1, 3, 2, style * 54, 0),
            16 or 18 => (0, 0, 2, 1, style * 36, 0),
            94 => (-1, -1, 2, 2, 0, style * 36),
            101 => (-1, -3, 3, 4, style * 54, 0),
            105 => (0, -2, 2, 3, style % 55 * 36, style / 55 * 54),
            349 or 506 => (0, -2, 2, 3, style * 36, 0),
            219 => (-1, -2, 3, 3, style * 54, 0),
            135 => (0, 0, 1, 1, 0, style * 18),
            91 => (0, 0, 1, 3, style % 111 * 18, style / 111 * 54),
            34 => (-1, 0, 3, 3, style / 36 * 108, style * 54 - style / 36 * 1998),
            240 => (-1, -1, 3, 3, style % 36 * 54, style / 36 * 54),
            245 => (0, -1, 2, 3, style * 36, 0),
            246 => (-1, 0, 3, 2, 0, style * 36),
            242 => (-2, -2, 6, 4, style / 27 * 108, style % 27 * 72),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        int left = x + dx, top = y + dy;
        bool available = true;
        for (int column = left; column < left + width; column++)
        {
            for (int row = top; row < top + height; row++)
            {
                WorldTile tile = grid.At(column, row);
                available &= !tile.IsActive && (!picture || tile.Wall != 0);
            }
            if (!picture && type is not (34 or 91))
            {
                WorldTile floor = grid.At(column, top + height);
                available &= type == 94
                    ? floor.IsActive && !floor.IsActuated && (FurnitureSolidFloor(floor, false, workspace.HasVanillaGenerationSolidity ? workspace.IsVanillaGenerationSolid : null) || GenerationFurnitureTiles1458.IsTable(floor.Type))
                    : FurnitureSolidFloor(floor, true, workspace.HasVanillaGenerationSolidity ? workspace.IsVanillaGenerationSolid : null);
                if (type == 186 || type == 135)
                    available &= !floor.IsActive || floor.Type is not (138 or 664);
            }
        }
        if (type == 34)
            available &= grid.IsSolid(x, y - 1);
        if(type==91){WorldTile ceiling=grid.At(x,y-1);available &= ceiling.IsActive&&!ceiling.IsActuated&&grid.IsSolidType(ceiling.TileType)&&!VanillaTileCollisionCatalog.IsSolidTop(ceiling.TileType);}
        if (available)
            FurnitureWrite(grid, left, top, width, height, type, frameX, frameY);
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace, useWorldGenerationSolidity: workspace.HasVanillaGenerationSolidity);
        if (!picture && type != 94)
            framing.SquareTileFrame(x, y);
        if (grid.At(x, y).IsActive)
            framing.SquareTileFrame(x, y);
        return grid.At(x, y).IsActive;
    }

    private static bool FurnitureSolidFloor(in WorldTile floor, bool platformSlope, Func<TileTypeId, bool>? solidity = null) =>
        floor.IsActive && !floor.IsActuated && !floor.IsHalfBrick && (solidity?.Invoke(floor.TileType) ?? VanillaTileCollisionCatalog.IsSolid(floor.TileType)) &&
        (floor.SourceSlope == 0 || platformSlope && VanillaTileIds.IsPlatform(floor.TileType) && floor.SourceSlope is 1 or 2);

    private static void FurnitureWrite(Grid grid, int left, int top, int width, int height,
        ushort type, int frameX, int frameY)
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                ref WorldTile tile = ref grid.At(left + x, top + y);
                tile.Flags |= WorldTileFlags.Active;
                tile.Type = type;
                tile.FrameX = (short)(frameX + x * 18);
                tile.FrameY = (short)(frameY + y * 18);
            }
    }

    private static void FurniturePlaceSmallPile(Grid grid, int x, int y, int style)
    {
        if (grid.At(x, y) is { LiquidAmount: > 0, LiquidKind: WorldLiquidKind.Lava })
            return;
        for (int dx = 0; dx < 2; dx++)
        {
            WorldTile floor = grid.At(x + dx, y + 1);
            if (grid.At(x + dx, y).IsActive || !FurnitureSolidFloor(floor, true, grid.IsSolidType) || floor.Type is 138 or 664)
                return;
        }
        FurnitureWrite(grid, x, y, 2, 1, 185, style * 36, 18);
    }

    private static void FurniturePlaceStatueTrap(Workspace workspace, IWorldGenerationVanillaRandom random,
        Grid grid, int x, int y)
    {
        if (!grid.InWorld(x, y, 1, 1, 20))
            return;
        for (int dx = -10; dx <= 10; dx++)
            for (int dy = -10; dy <= 10; dy++)
            {
                WorldTile floor = grid.At(x + dx, y + dy + 1);
                if (grid.At(x + dx, y + dy).IsActive || floor.Type == 162 || !FurnitureSolidFloor(floor, false, workspace.HasVanillaGenerationSolidity ? workspace.IsVanillaGenerationSolid : null))
                    continue;
                PlaceHouseObject(workspace, random, x + dx, y + dy, 135);
                if (grid.At(x + dx, y + dy) is not { IsActive: true, Type: 135 })
                    continue;
                for (int wireX = Math.Min(x, x + dx); wireX <= Math.Max(x, x + dx); wireX++)
                    grid.At(wireX, y).Flags |= WorldTileFlags.WireRed;
                for (int wireY = Math.Min(y, y + dy); wireY <= Math.Max(y, y + dy); wireY++)
                    grid.At(x + dx, wireY).Flags |= WorldTileFlags.WireRed;
                return;
            }
    }

    internal static (ushort Type, int Style) ResolveStatue(int index) => index == 34
        ? ((ushort)349, 0)
        : ((ushort)105, index == 43 ? 50 : index < 44 ? index : FurnitureExtraStatueStyles[index - 44]);

    internal static void PlaceStatueTrap(Workspace workspace, IWorldGenerationVanillaRandom random, int x, int y) =>
        FurniturePlaceStatueTrap(workspace, random,
            new Grid(workspace.TileStore, workspace.HasVanillaGenerationSolidity ? workspace.IsVanillaGenerationSolid : null), x, y);
}
