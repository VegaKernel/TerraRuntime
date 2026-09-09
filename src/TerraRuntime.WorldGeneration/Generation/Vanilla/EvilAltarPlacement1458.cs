using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary 1.4.5.8 evil altar searches and the generation-only Place3x2(26) operation.</summary>
internal sealed class EvilAltarPlacement1458(
    WorldTileStore store, IWorldGenerationVanillaRandom random,
    double worldSurface, double rockLayer, CancellationToken cancellationToken)
{
    private const ushort Altar = 26;
    // Safety policy for vanilla's otherwise unbounded rejected-coordinate loops. Never invent a position.
    private const int SearchBudget = 1_000_000;

    public int Generate(WorldGenerationPoint shimmer, bool crimson)
    {
        int placed = 0;
        int count = (int)((double)(store.Dimensions.WidthTiles * store.Dimensions.HeightTiles) * 3.3E-06);
        int low = (int)(worldSurface * 2 + rockLayer) / 3;
        int high = (int)(rockLayer + (store.Dimensions.HeightTiles - 350) * 2) / 3;
        for (int altar = 0; altar < count; altar++)
        for (int attempt = 0; attempt < 10_000; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x, y, samples = 0;
            do
            {
                do
                {
                    CheckSearch(ref samples);
                    x = random.Next(281, store.Dimensions.WidthTiles - 283);
                } while (x > store.Dimensions.WidthTiles * .45 && x < store.Dimensions.WidthTiles * .55);
                y = random.Next(low, high);
            } while (OceanDepths(x, y) || Math.Sqrt((double)(x - shimmer.X) * (x - shimmer.X) +
                         (double)(y - shimmer.Y) * (y - shimmer.Y)) < 150);
            if (!IsNearby(store, x, y)) TryPlace(store, x, y, crimson);
            // Vanilla tests the stored material, not activity, after the placement attempt.
            if (At(x, y).Type == Altar) { placed++; break; }
        }
        return placed;
    }

    public void GenerateCrimsonRegion(int left, int right)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int count = random.Next(10, 15);
        for (int altar = 0; altar < count; altar++)
        {
            int expansion = 0, attempts = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempts++;
                int x, y, samples = 0;
                do
                {
                    CheckSearch(ref samples);
                    x = random.Next(left - expansion, right + expansion);
                    y = random.Next((int)(worldSurface - expansion / 2), (int)(worldSurface + 100 + expansion));
                } while (OceanDepths(x, y));
                // This increment follows sampling: the current attempt uses the previous search bounds.
                if (attempts > 100) { expansion++; attempts = 0; }
                if (!At(x, y).IsActive)
                {
                    while (!At(x, y).IsActive) y++;
                    y--;
                }
                else while (At(x, y).IsActive && y > worldSurface) y--;
                WorldTile support = At(x, y + 1);
                if ((expansion > 10 || (support.IsActive && support.Type == 203)) && !IsNearby(store, x, y))
                {
                    TryPlace(store, x, y, crimson: true);
                    if (At(x, y).Type == Altar) break;
                }
                if (expansion > 100) break;
            }
        }
    }

    private bool OceanDepths(int x, int y) => y <= (worldSurface + rockLayer) / 2 + 40 &&
        (x < 380 || x > store.Dimensions.WidthTiles - 380);

    internal static bool IsNearby(WorldTileStore store, int x, int y)
    {
        for (int dx = -3; dx <= 3; dx++)
        for (int dy = -3; dy <= 3; dy++)
        {
            int tx = x + dx, ty = y + dy;
            if ((uint)tx >= (uint)store.Dimensions.WidthTiles || (uint)ty >= (uint)store.Dimensions.HeightTiles) continue;
            WorldTile tile = store.Get(tx, ty);
            if (tile.IsActive && tile.Type == Altar) return true;
        }
        return false;
    }

    internal static bool TryPlace(WorldTileStore store, int x, int y, bool crimson)
    {
        if (x < 5 || x > store.Dimensions.WidthTiles - 5 || y < 5 || y > store.Dimensions.HeightTiles - 5) return false;
        for (int dx = -1; dx <= 1; dx++)
        {
            WorldTile support = store.Get(x + dx, y + 1);
            // TileID.Sets.Boulders includes 665 too (unlike the QuickWater solidity override).
            if (support.Type is 138 or 484 or 664 or 665 or >= 711 and <= 716 ||
                !support.IsActive || support.IsActuated || !VanillaTileCollisionCatalog.IsSolid(support.TileType)) return false;
            bool platform = support.Type is 19 or 427 or >= 435 and <= 439;
            // Coordinate SolidTile2 permits top slopes on platforms, but never half bricks.
            if (support.Shape != 0 && !(platform && support.Shape is 2 or 3)) return false;
            for (int dy = -1; dy <= 0; dy++)
                if (store.Get(x + dx, y + dy).IsActive) return false;
        }
        for (int dx = 0; dx < 3; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            ref WorldTile cell = ref store.Tiles[store.GetUncheckedIndex(x - 1 + dx, y - 1 + dy)];
            cell.Flags |= WorldTileFlags.Active;
            cell.Type = Altar;
            cell.FrameX = (short)((crimson ? 54 : 0) + dx * 18);
            cell.FrameY = (short)(dy * 18);
        }
        return true;
    }

    private void CheckSearch(ref int samples)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (++samples > SearchBudget) throw new InvalidOperationException("Evil altar coordinate search exhausted its safety budget.");
    }

    private ref WorldTile At(int x, int y)
    {
        if ((uint)x >= (uint)store.Dimensions.WidthTiles || (uint)y >= (uint)store.Dimensions.HeightTiles)
            throw new InvalidOperationException("Evil altar search reached outside the candidate terrain.");
        return ref store.Tiles[store.GetUncheckedIndex(x, y)];
    }
}
