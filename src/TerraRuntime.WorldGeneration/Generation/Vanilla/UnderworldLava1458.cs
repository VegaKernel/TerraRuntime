using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>The ordinary Underworld pass's two-row lava restoration, after carving and before Hellstone.</summary>
internal static class UnderworldLava1458
{
    public static void RestoreSurface(WorldTileStore store, CancellationToken cancellationToken)
    {
        int height = store.Dimensions.HeightTiles;
        if (height < 145)
            throw new ArgumentOutOfRangeException(nameof(store));
        for (int x = 0; x < store.Dimensions.WidthTiles; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int y = height - 145; y <= height - 144; y++)
            {
                ref WorldTile tile = ref store.Tiles[store.GetUncheckedIndex(x, y)];
                if (tile.IsActive) continue;
                tile.LiquidAmount = byte.MaxValue;
                tile.LiquidKind = WorldLiquidKind.Lava;
            }
        }
    }
}
