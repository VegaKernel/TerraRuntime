using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary WorldGen Hellforges pass and its Place3x2(77) subset, TerrariaServer 1.4.5.8.</summary>
internal static class HellforgePlacement1458
{
    internal const ushort Hellforge = 77;

    public static int Generate(WorldTileStore store, IWorldGenerationVanillaRandom random, CancellationToken cancellationToken)
    {
        int width = store.Dimensions.WidthTiles, height = store.Dimensions.HeightTiles, placed = 0;
        for (int forge = 0; forge < width / 200; forge++)
        {
            int placementFailures = 0;
            for (int sample = 0; ; sample++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Vanilla doesn't count samples outside house walls. Bound that otherwise infinite search without
                // placing on an arbitrary ash floor or accepting an incomplete candidate as a replacement.
                if (sample == 1_000_000)
                    throw new InvalidOperationException("Hellforge wall search exhausted its safety budget.");
                int x = random.Next(1, width), y = random.Next(height - 250, height - 30);
                if (At(store, x, y).Wall is not (13 or 14)) continue;
                while (!At(store, x, y).IsActive && y < height - 20) y++;
                y--;
                if (TryPlaceTile(store, x, y)) { placed++; break; }
                if (++placementFailures >= 10_000) break;
            }
        }
        return placed;
    }

    internal static bool TryPlaceTile(WorldTileStore store, int centerX, int bottomY)
    {
        if ((uint)centerX >= (uint)store.Dimensions.WidthTiles || (uint)bottomY >= (uint)store.Dimensions.HeightTiles)
            return false;
        ref WorldTile anchor = ref At(store, centerX, bottomY);
        if (!anchor.IsActive) HellFortGenerator1458.ClearPlacementAnchor(ref anchor);
        return TryPlace(store, centerX, bottomY);
    }

    internal static bool TryPlace(WorldTileStore store, int centerX, int bottomY)
    {
        if (centerX < 5 || centerX > store.Dimensions.WidthTiles - 5 || bottomY < 5 || bottomY > store.Dimensions.HeightTiles - 5)
            return false;
        for (int x = centerX - 1; x <= centerX + 1; x++)
        {
            // Place3x2 uses SolidTile2: full solid platforms count too, unlike SolidTile/no-solid-top.
            WorldTile support = At(store, x, bottomY + 1);
            if (!support.IsActive || support.IsActuated || support.Shape != 0 ||
                !VanillaTileCollisionCatalog.IsSolid(support.TileType))
                return false;
            for (int y = bottomY - 1; y <= bottomY; y++)
                if (At(store, x, y).IsActive) return false;
        }
        for (int dx = 0; dx < 3; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            ref WorldTile tile = ref At(store, centerX - 1 + dx, bottomY - 1 + dy);
            tile.Flags |= WorldTileFlags.Active;
            tile.Type = Hellforge;
            tile.FrameX = (short)(dx * 18); tile.FrameY = (short)(dy * 18);
            // Hellforge placement is lava-safe and does not erase liquid, wall, paint or wires.
        }
        return true;
    }

    private static ref WorldTile At(WorldTileStore store, int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
