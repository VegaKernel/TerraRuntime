using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary, pre-structure Mud Caves To Grass on the unpublished generation tile store.</summary>
internal static class JungleMudSurface1458
{
    private const int ComponentLimit = 20;

    public static void Apply(WorldTileStore store, CancellationToken cancellationToken)
    {
        int width = store.Dimensions.WidthTiles, height = store.Dimensions.HeightTiles;
        // Only the natural solid types produced by the verified ordinary prefix are
        // admitted. Unknown solidity/removal/tree semantics must not be guessed.
        for (int x = 0; x < width; x++)
        {
            if ((x & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            for (int y = 0; y < height; y++)
            {
                ref WorldTile tile = ref At(store, x, y);
                if (tile.IsActive && !IsNaturalSolid(tile.Type))
                    throw new InvalidOperationException("Mud cave grass encountered unsupported terrain semantics.");
            }
        }

        // SpreadGrass recursion only changes solid Mud59 to solid Grass60 here:
        // it cannot change another cell's exposure or liquid gate. The source's
        // enclosing x-major scan visits every cell, so this has the same final
        // mutations without recursive depth-dependent work. Neither slice uses RNG.
        for (int x = 10; x < width - 10; x++)
        {
            if ((x & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            for (int y = 10; y < height - 10; y++)
            {
                ref WorldTile tile = ref At(store, x, y);
                if (!tile.IsActive || tile.Type != 59 || IsEnclosed(store, x, y)) continue;
                tile.Type = 60;
                tile.TileColor = 0;
                tile.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
                for (int tx = x - 1; tx <= x + 1; tx++)
                for (int ty = y - 1; ty <= y + 1; ty++)
                {
                    ref WorldTile neighbour = ref At(store, tx, ty);
                    if (neighbour.IsActive) continue;
                    neighbour.Shape = 0;
                    neighbour.TileColor = 0;
                    neighbour.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
                }
            }
        }

        var component = new (int X, int Y)[ComponentLimit];
        for (int x = 10; x < width - 10; x++)
        {
            if ((x & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            int consecutive = 0, startY = 0;
            for (int y = 10; y < height - 10; y++)
            {
                if (At(store, x, y).IsActive)
                {
                    if (consecutive == 0) startY = y;
                    consecutive++;
                    continue;
                }
                if (consecutive is > 0 and < ComponentLimit)
                {
                    int count = 0;
                    CountComponent(store, x, startY, component, ref count);
                    if (count < ComponentLimit)
                        for (int i = 0; i < count; i++)
                            At(store, component[i].X, component[i].Y).Flags &= ~WorldTileFlags.Active;
                }
                consecutive = 0;
            }
        }
    }

    private static bool IsNaturalSolid(ushort type) =>
        type is 0 or 1 or 2 or 40 or 53 or 59 or 60 or >= 63 and <= 68 or 147 or 161;

    private static bool IsEnclosed(WorldTileStore store, int x, int y)
    {
        bool enclosed = true;
        for (int tx = x - 1; tx <= x + 1; tx++)
        for (int ty = y - 1; ty <= y + 1; ty++)
        {
            ref WorldTile neighbour = ref At(store, tx, ty);
            if (!neighbour.IsActive) enclosed = false;
            // Source breaks the inner y loop only. Later columns may change this flag.
            if (neighbour.LiquidKind == WorldLiquidKind.Lava && neighbour.LiquidAmount > 0)
            {
                enclosed = true;
                break;
            }
        }
        return enclosed;
    }

    private static void CountComponent(WorldTileStore store, int x, int y, (int X, int Y)[] component, ref int count)
    {
        if (count >= ComponentLimit || x < 5 || x > store.Dimensions.WidthTiles - 5 ||
            y < 5 || y > store.Dimensions.HeightTiles - 5 || !At(store, x, y).IsActive)
            return;
        for (int i = 0; i < count; i++)
            if (component[i] == (x, y)) return;
        component[count++] = (x, y);
        CountComponent(store, x - 1, y, component, ref count);
        CountComponent(store, x + 1, y, component, ref count);
        CountComponent(store, x, y - 1, component, ref count);
        CountComponent(store, x, y + 1, component, ref count);
    }

    private static ref WorldTile At(WorldTileStore store, int x, int y) =>
        ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
