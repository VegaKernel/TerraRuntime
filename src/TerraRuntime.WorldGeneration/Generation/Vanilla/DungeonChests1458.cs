using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary DungeonGlobalBiomeChests/BasicChests and their AddBuriedChest placement slice.</summary>
internal sealed class DungeonChests1458(Workspace workspace, IWorldGenerationVanillaRandom random,
    VanillaWorldGenerationBootstrapState1458 bootstrap, double worldSurface, double rockLayer, CancellationToken cancellationToken,
    IReadOnlyList<DungeonPitTraps1458.Pit>? pits = null)
{
    private readonly DungeonChestLoot1458 loot = new(random, bootstrap);
    private static ReadOnlySpan<int> BasicLoot => [155, 156, 157, 163, 113, 3317, 327, 164];
    private int lootIndex;
    private int Width => workspace.WidthTiles;
    private int Height => workspace.HeightTiles;

    internal int PlaceBiome(DungeonBounds1458 bounds, DungeonBounds1458 entrance)
    {
        int placed = 0;
        for (int biome = 0; biome < 5; biome++)
        for (int attempt = 1; attempt < 1000; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(bounds.Left, bounds.Right), y = random.Next((int)worldSurface, bounds.Bottom);
            if (x >= entrance.Left && x <= entrance.Right && y >= entrance.Top && y <= entrance.Bottom ||
                !DungeonGenerationTiles1458.IsDungeonWall(At(x, y).Wall) || At(x, y).IsActive) continue;
            (int item, int style, int type) = biome switch
            {
                0 => (1156, 23, 21),
                1 => bootstrap.EffectiveCrimson ? (1569, 25, 21) : (1571, 24, 21),
                2 => (1260, 26, 21), 3 => (1572, 27, 21), _ => (4607, 13, 467)
            };
            if (TryPlace(x, y, (ushort)type, style, item)) { placed++; break; }
        }
        return placed;
    }

    internal int PlaceBasic(IReadOnlyList<DungeonComponent1458> components)
    {
        int placed = 0;
        foreach (DungeonComponent1458 room in components)
        {
            if (room.Kind is not (DungeonComponentKind1458.Room or DungeonComponentKind1458.StartingRoom)) continue;
            int strength = room.RoomStrength ?? throw new InvalidOperationException("Dungeon room lacks its source chest-sampling strength.");
            int radius = (int)(strength * .4f);
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int x = (int)(room.End.X - radius + radius * 2 * random.NextDouble());
                int y = (int)(room.End.Y - radius + radius * 2 * random.NextDouble());
                if (!AllowsBasicChestArea(x, y)) continue;
                if (lootIndex >= 8) lootIndex = 0;
                int item = BasicLoot[lootIndex], style = lootIndex == 6 ? 0 : 2;
                if (y < worldSurface + 50d) { item = 327; style = 0; }
                if (!TryPlace(x, y, 21, style, item)) continue;
                lootIndex++; placed++; break;
            }
        }
        return placed;
    }

    private bool AllowsBasicChestArea(int x, int y)
    {
        // Ordinary room/style/entrance gates accept basic chests. DungeonData still tests the inclusive
        // clamped fluff rectangle for wall350 before advancing the cyclic primary item selection.
        int left = Math.Clamp(x - 1, 10, Width - 10), right = Math.Clamp(x + 1, 10, Width - 10);
        int top = Math.Clamp(y - 1, 10, Height - 10), bottom = Math.Clamp(y + 1, 10, Height - 10);
        if (right <= left) right = Math.Clamp(left + 1, 10, Width - 10);
        if (bottom <= top) bottom = Math.Clamp(top + 1, 10, Height - 10);
        for (int tx = left; tx <= right; tx++)
        for (int ty = top; ty <= bottom; ty++)
            if (At(tx, ty).Wall == 350 || DungeonPitTraps1458.Contains(pits, tx, ty)) return false;
        return true;
    }

    internal bool TryPlace(int x, int y, ushort type, int style, int item)
    {
        if (x < 3 || x >= Width - 3 || y < 3 || y >= Height - 10)
            throw new InvalidOperationException("Dungeon chest seed escaped the admitted world margin.");
        for (int floor = y; floor < Height - 10; floor++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorldTile cell = At(x, floor);
            if (cell.LiquidAmount > 0 && cell.LiquidKind == WorldLiquidKind.Shimmer || cell.IsActive && cell.Type == 231) return false;
            for (int tx = x - 2; tx <= x + 2; tx++)
            for (int ty = floor - 2; ty <= floor + 2; ty++)
                if (tx >= 100 && tx < Width - 100 && ty >= 100 && ty < Height - 100 && At(tx, ty).IsActive &&
                    (GenerationObjectSupport1458.IsBoulder(At(tx, ty).Type) || At(tx, ty).Type is 26 or 237)) return false;
            if (!DungeonGenerationTiles1458.SolidTile(cell)) continue;
            int left = x - 1, top = floor - 2;
            if (!CanPlaceObject(left, top)) return false;
            for (int column = 0; column < 2; column++)
            for (int row = 0; row < 2; row++)
            {
                ref WorldTile target = ref At(left + column, top + row);
                target.Flags |= WorldTileFlags.Active; target.Type = type;
                target.FrameX = (short)(style * 36 + column * 18); target.FrameY = (short)(row * 18);
            }
            var items = loot.Build(item, type, style, floor, worldSurface, rockLayer, Height,
                DungeonGenerationTiles1458.IsDungeonWall(cell.Wall));
            if (!workspace.TryAddChest(left, top, string.Empty, items))
                throw new InvalidOperationException("Dungeon chest passed placement but failed side-table registration.");
            return true;
        }
        return false;
    }

    private bool CanPlaceObject(int left, int top)
    {
        if (left < 5 || left + 2 > Width - 5 || top < 5 || top + 2 > Height - 5) return false;
        for (int column = 0; column < 2; column++)
        {
            WorldTile floor = At(left + column, top + 2);
            if (GenerationObjectSupport1458.IsBoulder(floor.Type) || !GenerationObjectSupport1458.SupportsChest(floor, crackedBricksSolid: false)) return false;
            for (int row = 0; row < 2; row++)
            {
                WorldTile cell = At(left + column, top + row);
                if (cell.LiquidAmount > 0 && cell.LiquidKind == WorldLiquidKind.Lava) return false;
                if (!cell.IsActive) continue;
                if ((VanillaProjectileTileCutFacts.IsCuttable(cell.TileType) && cell.Type is not (484 or 654)) ||
                    GenerationObjectSupport1458.BreakableWhenPlacing(cell.Type))
                    throw new InvalidOperationException($"Unverified break-on-placement object {cell.Type} at {left + column},{top + row} in Dungeon chest footprint.");
                return false;
            }
        }
        return workspace.CanRegisterGeneratedChest(left, top);
    }

    private ref WorldTile At(int x, int y) => ref workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(x, y)];
}
