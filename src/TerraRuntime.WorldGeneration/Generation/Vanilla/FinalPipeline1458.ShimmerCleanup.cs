using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class FinalPass1458
{
    /// <summary>The ordinary ShimmerCleanUp slot after final seed cleanup and before liquid interfaces.</summary>
    internal static bool CleanupShimmer(Workspace workspace, IWorldGenerationVanillaRandom random,
        GenerationTileFraming1458 framing, CancellationToken cancellation)
    {
        if (workspace.VanillaShimmerPosition is not { } position || position.X == 0 && position.Y == 0) return false;
        if (!workspace.TryGetLayers(out var layers)) throw new InvalidOperationException("Shimmer cleanup requires retained layers.");
        WorldTileStore store = workspace.TileStore;
        int width = workspace.WidthTiles, height = workspace.HeightTiles, cx = position.X, cy = position.Y;
        ref WorldTile At(int x, int y)
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height) throw new IndexOutOfRangeException("Shimmer cleanup exceeded its retained biome bounds.");
            return ref store.Tiles[store.GetUncheckedIndex(x, y)];
        }
        static int Distance(double x, double y) => (int)Math.Sqrt(Math.Pow(x, 2) + Math.Pow(y, 2));

        for (int y = cy - 150; y <= cy + 75; y++)
        for (int x = cx - 150; x <= cx + 150; x++)
            if ((uint)x < (uint)width && (uint)y < (uint)height && Distance(Math.Abs(x - cx), Math.Abs(y - cy)) < 150)
            {
                ref WorldTile tile = ref At(x, y);
                if (tile.LiquidKind != WorldLiquidKind.Shimmer) tile.LiquidAmount = 0;
                if (tile.Type == 162) tile.Flags &= ~WorldTileFlags.Active;
            }

        for (int y = cy - 120; y <= cy + 120; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int x = cx - 120; x <= cx + 120; x++)
            {
                ref WorldTile tile = ref At(x, y);
                if (Distance(Math.Abs(x - cx), Math.Abs(y - cy)) < 120)
                {
                    if (tile.Type is 22 or 204 || SettingsTreeGrower1458.IsGemTreeGround(tile.TileType) &&
                        tile.Type is not (1 or 25 or 117 or 203)) tile.Type = 1;
                    if (tile.Type is 51 or 56 or 229 or 230 or 659 or 184) tile.Flags &= ~WorldTileFlags.Active;
                }
                // Two samples per square, including cells outside either elliptical region.
                double dx = Math.Abs(x - cx) * (1 + random.NextDouble() * 0.02);
                double dy = Math.Abs(y - cy) * (y > cy ? 1.2 : 1.4) * (1 + random.NextDouble() * 0.02);
                if (Distance(dx, dy) < 90)
                {
                    if (tile.Type == 162) tile.Flags &= ~WorldTileFlags.Active;
                    tile.Wall = 0;
                    framing.Purify(x, y, layers.WorldSurface, layers.RockLayer);
                }
            }
        }

        int column = cx, row = cy;
        byte amount = 127;
        // Liquid.tilesIgnoreWater(true) changes only collision flags during this active-only
        // fill. Restore its shared framing phase before the subsequent tree destruction scan.
        while (At(column, row).LiquidAmount <= amount || At(column, row).LiquidKind != WorldLiquidKind.Shimmer)
        {
            while (!At(column, row).IsActive)
            {
                At(column, row).LiquidAmount = amount;
                At(column, row).LiquidKind = WorldLiquidKind.Shimmer;
                column--;
            }
            for (column = cx; !At(column, row).IsActive; column++)
            {
                At(column, row).LiquidAmount = amount;
                At(column, row).LiquidKind = WorldLiquidKind.Shimmer;
            }
            column = cx;
            row++;
            amount = byte.MaxValue;
            if (At(column, row).IsActive) break;
        }
        framing.UsesPostShimmerSolidity = true;
        for (int x = 10; x < width - 10; x++)
        for (int y = 10; y < height - 10; y++)
            if (At(x, y).LiquidAmount > 0 && At(x, y).LiquidKind == WorldLiquidKind.Shimmer && At(x, y).Type == 5)
                framing.KillTile(x, y);
        return true;
    }
}
