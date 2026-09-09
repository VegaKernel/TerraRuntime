using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary EarlyDualDungeonFeatures pit traps, before spikes and decoration (1.4.5.8).</summary>
internal sealed class DungeonPitTraps1458(WorldTileStore tiles, IWorldGenerationVanillaRandom random,
    CancellationToken cancellationToken)
{
    internal readonly record struct Settings(int Width, int Height, int EdgeWidth, int EdgeHeight, bool Flooded);
    // Bounds retain source numeric edges. Rectangle.Contains excludes Right/Bottom, unlike paint loops.
    internal readonly record struct Pit(DungeonBounds1458 Bounds, bool Flooded);
    private int Width => tiles.Dimensions.WidthTiles;
    private int Height => tiles.Dimensions.HeightTiles;

    public IReadOnlyList<Pit> Place(DungeonBounds1458 bounds, ushort brick, ushort wall, ushort cracked,
        double worldSurface, int dungeonY)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePalette(brick, wall, cracked);
        var pits = new List<Pit>();
        bool firstFlooded = true;
        for (int attempt = 0, target = (int)(Width * 2.0); attempt < target; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(bounds.Left, bounds.Right);
            int top = Math.Max(bounds.Top, dungeonY + 25);
            if (top < worldSurface) top = (int)worldSurface;
            int y = random.Next(top, bounds.Bottom);
            bool flooded = firstFlooded || random.Next(8) == 0;
            int edgeHeight = random.Next(6, 10);
            var settings = new Settings(random.Next(8, 19), random.Next(19, 46), random.Next(6, 10), edgeHeight, flooded);
            if (TryPlace(x, y, settings, brick, wall, cracked) is { } pit)
            {
                pits.Add(pit);
                if (flooded) firstFlooded = false;
                attempt += 1500;
            }
            else attempt++; // Source additionally increments the for-loop counter on failure.
        }
        return pits;
    }

    public Pit? TryPlace(int x, int y, Settings settings, ushort brick, ushort wall, ushort cracked)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePalette(brick, wall, cracked);
        int halfWidth = settings.Width, depth = settings.Height;
        if (halfWidth < 1 || depth < 1) return null;
        int outerWidth = checked(halfWidth + settings.EdgeWidth), outerDepth = checked(depth + settings.EdgeHeight);
        int margin = Math.Max(outerWidth, outerDepth);
        if (margin < 1 || x < margin || y < margin || x >= Width - margin || y >= Height - margin) return null;
        if (!DungeonGenerationTiles1458.IsDungeonWall(At(x, y).Wall) || At(x, y).IsActive) return null;
        int floor = y;
        for (; floor < Height; floor++)
        {
            if (floor > Height - 200) return null;
            WorldTile tile = At(x, floor);
            if (tile.Type is >= 481 and <= 483 || !HellFortGenerator1458.Solid(tile, noDoors: false)) continue;
            if (tile.Type == 48) return null;
            break;
        }
        if (!DungeonGenerationTiles1458.IsDungeonWall(At(x - halfWidth, floor).Wall) ||
            !DungeonGenerationTiles1458.IsDungeonWall(At(x + halfWidth, floor).Wall)) return null;
        int lowerFloor = floor;
        for (int row = floor; row < floor + 30; row++)
        {
            bool dungeon = false;
            for (int column = x - halfWidth; column <= x + halfWidth; column++)
                if (At(column, row).IsActive && DungeonGenerationTiles1458.IsDungeonTile(At(column, row).Type))
                { dungeon = true; break; }
            if (!dungeon) { lowerFloor = row; break; }
        }
        if (lowerFloor + outerDepth >= Height - 200) return null;
        int left = x - halfWidth, right = x + halfWidth, bottom = lowerFloor + depth;
        for (int column = left; column <= right; column++)
        for (int row = lowerFloor; row <= bottom; row++)
        {
            WorldTile tile = At(column, row);
            if (tile.IsActive && (DungeonGenerationTiles1458.IsDungeonTile(tile.Type) || tile.Type is >= 481 and <= 483)) return null;
        }
        for (int column = left; column <= right; column++)
        for (int row = floor; row <= bottom; row++)
        {
            ref WorldTile tile = ref At(column, row);
            if (!tile.IsActive || !DungeonGenerationTiles1458.IsDungeonTile(tile.Type)) continue;
            DungeonGenerationTiles1458.SetBrick(ref tile, cracked, reset: true);
            DungeonGenerationTiles1458.SetWall(ref tile, wall, reset: false);
        }
        int outerLeft = x - outerWidth, outerRight = x + outerWidth, outerBottom = lowerFloor + outerDepth;
        for (int column = outerLeft; column <= outerRight; column++)
        for (int row = floor; row <= outerBottom; row++)
        {
            ref WorldTile tile = ref At(column, row);
            tile.LiquidKind = WorldLiquidKind.Water; tile.LiquidAmount = 0;
            if (DungeonGenerationTiles1458.IsDungeonWall(tile.Wall)) continue;
            bool interior = column > outerLeft && column < outerRight && row < outerBottom;
            DungeonGenerationTiles1458.SetBrick(ref tile, brick, reset: interior);
            if (interior) DungeonGenerationTiles1458.SetWall(ref tile, wall, reset: false);
        }
        for (int column = left; column <= right; column++)
        for (int row = floor; row <= bottom; row++)
        {
            ref WorldTile tile = ref At(column, row);
            if (tile.Type == cracked) continue; // Raw dormant type also matters in the original.
            tile.LiquidKind = WorldLiquidKind.Water; tile.LiquidAmount = settings.Flooded ? byte.MaxValue : (byte)0;
            bool spike = (column == left || column == left + 1 && row % 2 == 0) && At(column - 1, row).IsActive ||
                (column == right || column == right - 1 && row % 2 == 0) && At(column + 1, row).IsActive ||
                (row == bottom || row == bottom - 1 && column % 2 == 0) && At(column, row + 1).IsActive;
            if (spike) DungeonGenerationTiles1458.SetBrick(ref tile, 48, reset: false);
            else tile.Flags &= ~WorldTileFlags.Active;
        }
        return new(new(Math.Clamp(outerLeft, 10, Width - 10), Math.Clamp(floor, 10, Height - 10),
            Math.Clamp(outerRight, 10, Width - 10), Math.Clamp(outerBottom, 10, Height - 10)), settings.Flooded);
    }

    private static void ValidatePalette(ushort brick, ushort wall, ushort cracked)
    {
        if ((brick, wall, cracked) is not ((41, 7, 481) or (43, 8, 482) or (44, 9, 483)))
            throw new InvalidOperationException("Only verified ordinary Dungeon pit styles are admitted.");
    }
    public static bool Contains(IReadOnlyList<Pit>? pits, int x, int y)
    {
        if (pits is null) return false;
        foreach (Pit pit in pits)
            if (x >= pit.Bounds.Left && x < pit.Bounds.Right && y >= pit.Bounds.Top && y < pit.Bounds.Bottom) return true;
        return false;
    }
    private ref WorldTile At(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) throw new InvalidOperationException("Dungeon pit escaped the workspace.");
        return ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
    }
}
