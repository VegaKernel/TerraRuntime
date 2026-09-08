using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary generation-time PlantCactus/GrowCactus, TerrariaServer 1.4.5.8.
/// Placement density remains caller-owned. Runtime growth, fruit and special seeds are not admitted here.
/// TileFrame skips cosmetic CactusFrame while generatingWorld; shape, not precomputed sprite frames, is the output.</summary>
internal static class CactusGrower1458
{
    private const ushort Cactus = 80;

    public static bool Plant(WorldTileStore store, int x, int floor, IWorldGenerationVanillaRandom random)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(random);
        if (!Inside(store, x, floor, 3) || floor < 13)
            return false;
        bool changed = TryGrow(store, x, floor, random);
        // PlantCactus always performs all 150 candidate attempts, even if its first root attempt failed.
        for (int attempt = 0; attempt < 150; attempt++)
        {
            int candidateX = random.Next(x - 1, x + 2);
            int candidateY = random.Next(floor - 10, floor + 2);
            changed |= TryGrow(store, candidateX, candidateY, random);
        }
        return changed;
    }

    internal static bool TryGrow(WorldTileStore store, int x, int y, IWorldGenerationVanillaRandom random)
    {
        // The source indexes local neighbours directly. Keep malformed/border sites fail-closed.
        if (!Inside(store, x, y, 3) || y < 12)
            return false;
        WorldTile source = store.Get(x, y);
        if (!source.IsActive || source.IsActuated || source.Shape == 1 ||
            (source.Type != Cactus && !IsSand(source.Type)) || store.Get(x, y - 1).LiquidAmount != 0)
            return false;

        int liquid = 0;
        for (int column = Math.Max(0, x - 50); column < Math.Min(store.Dimensions.WidthTiles, x + 50); column++)
        for (int row = Math.Max(0, y - 25); row < Math.Min(store.Dimensions.HeightTiles, y + 25); row++)
            liquid += store.Get(column, row).LiquidAmount;
        if (liquid / 255 > 25)
            return false;

        if (IsSand(source.Type))
            return TryRoot(store, x, y, source, random);
        if (!TryFindRoot(store, x, y, out int rootX, out int rootBottom))
            return false;

        int rise = rootBottom - y;
        int branch = x - rootX;
        int top = y - (11 - rise);
        if (top < 0 || rootX < 2 || rootX >= store.Dimensions.WidthTiles - 2)
            return false;
        int population = 0;
        for (int column = rootX - 2; column <= rootX + 2; column++)
        for (int row = top; row <= rootBottom; row++)
            if (IsCactus(store, column, row)) population++;
        if (population >= random.Next(11, 13))
            return false;

        if (branch == 0)
        {
            if (rise == 0)
                return Place(store, x, y - 1, source);
            bool left = IsCactus(store, x, y - 1) && ClearBranch(store, x, y, -1);
            bool right = IsCactus(store, x, y - 1) && ClearBranch(store, x, y, 1);
            int choice = random.Next(3);
            if (choice == 0 && left) return Place(store, x - 1, y, source);
            if (choice == 1 && right) return Place(store, x + 1, y, source);
            // The height roll is consumed even when the upper cell is already occupied.
            if (rise < random.Next(2, 8) && !IsCactus(store, x - 1, y - 1) && !IsCactus(store, x + 1, y - 1))
                return Place(store, x, y - 1, source);
            return false;
        }

        if (branch is not (-1 or 1))
            return false;
        return !Active(store, x, y - 1) && !Active(store, x, y - 2) &&
            !Active(store, x + branch, y - 1) && IsCactus(store, x - branch, y - 1) &&
            Place(store, x, y - 1, source);
    }

    private static bool TryRoot(WorldTileStore store, int x, int y, WorldTile source, IWorldGenerationVanillaRandom random)
    {
        if (Active(store, x, y - 1) || Active(store, x - 1, y - 1) || Active(store, x + 1, y - 1))
            return false;
        int cactusCount = 0, sandCount = 0;
        for (int column = x - 6; column <= x + 6 && sandCount <= 10; column++)
        for (int row = y - 3; row <= y + 1; row++)
        {
            if (!Inside(store, column, row, 5)) continue;
            WorldTile tile = store.Get(column, row);
            if (!tile.IsActive) continue;
            if (tile.Type == Cactus && ++cactusCount >= 4) return false;
            if (IsSand(tile.Type) && ++sandCount > 10) break;
        }
        if (sandCount <= 10) return false;
        if (random.Next(2) == 0)
        {
            ref WorldTile ground = ref store.Tiles[store.GetUncheckedIndex(x, y)];
            ground.Shape = 0;
        }
        // SquareTileFrame -> CheckCactus immediately removes growth on a slope left unflattened by the roll.
        if (store.Get(x, y).Shape != 0) return false;
        return Place(store, x, y - 1, source);
    }

    private static bool TryFindRoot(WorldTileStore store, int x, int y, out int rootX, out int bottom)
    {
        rootX = x;
        int row = y;
        while (IsCactus(store, rootX, row))
        {
            row++;
            if (!Inside(store, rootX, row, 1)) { bottom = 0; return false; }
            if (IsCactus(store, rootX, row)) continue;
            if (rootX >= x && IsCactus(store, rootX - 1, row) && IsCactus(store, rootX - 1, row - 1)) rootX--;
            if (rootX <= x && IsCactus(store, rootX + 1, row) && IsCactus(store, rootX + 1, row - 1)) rootX++;
        }
        bottom = row - 1;
        WorldTile support = store.Get(rootX, row);
        return support.IsActive && !support.IsActuated && support.Shape == 0 && IsSand(support.Type);
    }

    private static bool ClearBranch(WorldTileStore store, int x, int y, int direction) =>
        !Active(store, x + direction, y) && !Active(store, x + direction * 2, y + 1) &&
        !Active(store, x + direction, y - 1) && !Active(store, x + direction, y + 1) &&
        !Active(store, x + direction * 2, y);

    private static bool Place(WorldTileStore store, int x, int y, WorldTile source)
    {
        if (!Inside(store, x, y, 1) || Active(store, x, y)) return false;
        ref WorldTile tile = ref store.Tiles[store.GetUncheckedIndex(x, y)];
        const WorldTileFlags coatings = WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock;
        tile.Type = Cactus;
        tile.Flags = (tile.Flags & ~coatings) | WorldTileFlags.Active | (source.Flags & coatings);
        tile.TileColor = source.TileColor;
        return true;
    }

    private static bool Inside(WorldTileStore store, int x, int y, int margin) =>
        x >= margin && y >= margin && x < store.Dimensions.WidthTiles - margin && y < store.Dimensions.HeightTiles - margin;
    private static bool Active(WorldTileStore store, int x, int y) => store.Get(x, y).IsActive;
    private static bool IsCactus(WorldTileStore store, int x, int y) => store.Get(x, y) is { IsActive: true, Type: Cactus };
    private static bool IsSand(ushort type) => type is 53 or 112 or 116 or 234;
}
