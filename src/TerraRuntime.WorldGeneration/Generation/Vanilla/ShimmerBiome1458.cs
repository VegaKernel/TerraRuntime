using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>WorldGen.ShimmerMakeBiome</c> and its two cave openings. The caller owns
/// candidate selection and the retry loop; this type owns the refusal rules, geometry, shimmer pool and decoration.
/// </summary>
internal sealed class ShimmerBiome1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    CancellationToken cancellation)
{
    private const ushort Stone = 1;
    private const ushort LihzahrdBrick = 203;
    private const ushort Ebonstone = 25;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;
    private readonly StoneBiomeTiles1458 framing = new(store, random, crackedBricksSolid: false);

    /// <summary>
    /// Attempts one biome at the candidate centre. A refusal happens before any mutation or RNG beyond the
    /// shape rolls the source itself draws first, exactly as the source's caller expects when it retries.
    /// </summary>
    public bool TryMake(int centerX, int centerY)
    {
        cancellation.ThrowIfCancellationRequested();
        int shape = random.Next(2);
        double verticalScale = .6, poolScale = 1.3, columnSpan = .3;
        if (shape == 0)
        {
            verticalScale = .55;
            poolScale = 2d;
        }

        verticalScale *= 1.05 - random.NextDouble() * .1;
        poolScale *= 1.05 - random.NextDouble() * .1;
        columnSpan *= 1d - random.NextDouble() * .1;
        int radius = random.Next(105, 125);
        int poolRadius = (int)(radius * columnSpan);
        int cavityRadius = (int)(radius * verticalScale);
        int openingSize = random.Next(9, 13);
        int left = centerX - radius, right = centerX + radius;
        int top = centerY - radius, bottom = centerY + radius;
        for (int y = top; y <= bottom; y++)
        for (int x = left; x <= right; x++)
        {
            // InWorld(x, y, 50): the whole square must sit inside the fifty-tile border.
            if (x < 50 || y < 50 || x >= width - 50 || y >= height - 50) return false;
            ushort type = At(x, y).Type;
            if (type == LihzahrdBrick || type == Ebonstone) return false;
        }

        int floorRow = centerY;
        if (random.Next(4) == 0) floorRow = centerY - random.Next(2);
        int ceilingRow = centerY - openingSize;
        if (random.Next(4) == 0) ceilingRow = centerY - openingSize - random.Next(2);

        for (int y = top; y <= bottom; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int x = left; x <= right; x++)
            {
                ref WorldTile tile = ref At(x, y);
                tile.LiquidAmount = 0;
                if (random.Next(4) == 0) floorRow = centerY - random.Next(2);
                if (random.Next(4) == 0) ceilingRow = centerY - openingSize + random.Next(2);
                double wobbleY = y <= centerY ? 1.4 : 1.2;
                int distance = Ellipse(x - centerX, y - centerY, wobbleY);
                if (distance < radius)
                {
                    tile.TileColor = 0;
                    tile.WallColor = 0;
                    tile.Shape = 0;
                    tile.Type = Stone;
                    // The source short-circuits, so the second margin roll only happens when the first passes.
                    if (x > left + 5 + random.Next(2) && x < right - 5 - random.Next(2))
                        tile.Flags |= WorldTileFlags.Active;
                    if (distance < radius * .9) tile.Wall = 0;
                }

                distance = Ellipse(x - centerX, y - centerY, poolScale);
                if (y > ceilingRow && y < floorRow) tile.Flags &= ~WorldTileFlags.Active;
                if (y < floorRow && distance < (int)(cavityRadius * (1d + random.NextDouble() * .02)))
                    tile.Flags &= ~WorldTileFlags.Active;

                distance = Ellipse(x - centerX, (y - centerY) * 2, 1d);
                if (y < centerY - 1 || distance >= (int)(poolRadius * (1d + random.NextDouble() * .025))) continue;
                if (y <= centerY + 2 || distance != poolRadius - 1 || random.Next(2) != 0)
                    tile.Flags &= ~WorldTileFlags.Active;
                if (y < centerY) continue;
                tile.LiquidAmount = y == centerY ? (byte)127 : byte.MaxValue;
                // Tile.shimmer(true) is liquid type three.
                tile.LiquidKind = WorldLiquidKind.Shimmer;
            }
        }

        if (shape == 0) BuildColumns(centerX, centerY, radius, columnSpan);
        MakeOpening(-1, centerX - radius, centerY, openingSize);
        MakeOpening(1, centerX + radius, centerY, openingSize);
        GrowGemTrees(centerX, centerY);
        return true;
    }

    // The shape-zero variant grows a row of tapering stone columns hanging from the cavity ceiling.
    private void BuildColumns(int centerX, int centerY, int radius, double columnSpan)
    {
        // The source reuses its square bounds here; nothing reads them again afterwards.
        int left = (int)(centerX - radius * columnSpan) - random.Next(-15, 1) - 5;
        int right = (int)(centerX + radius * columnSpan) + random.Next(0, 16);
        int consecutive = 0;
        for (int column = left; column < right; column += random.Next(9, 14))
        {
            cancellation.ThrowIfCancellationRequested();
            int row = centerY - 3;
            while (!At(column, row).IsActive)
            {
                if (--row < 0)
                    throw new InvalidOperationException("Shimmer column search left the world.");
            }

            row -= 4;
            int halfWidth = random.Next(5, 10);
            int remaining = random.Next(15, 21);
            int tip = column - halfWidth;
            while (halfWidth > 0)
            {
                for (tip = column - halfWidth; tip < column + halfWidth; tip++)
                {
                    ref WorldTile tile = ref At(tip, row);
                    tile.Flags |= WorldTileFlags.Active;
                    tile.Type = Stone;
                }

                consecutive++;
                if (random.Next(3) < consecutive)
                {
                    consecutive = 0;
                    halfWidth--;
                    column += random.Next(-1, 2);
                }

                if (remaining <= 0) halfWidth--;
                remaining--;
                row++;
            }

            tip -= random.Next(1, 3);
            for (int offset = -2; offset <= 0; offset++)
            {
                ref WorldTile tile = ref At(tip, row + offset);
                tile.Flags |= WorldTileFlags.Active;
                tile.Type = Stone;
            }

            if (random.Next(2) == 0)
            {
                ref WorldTile tile = ref At(tip, row + 1);
                tile.Flags |= WorldTileFlags.Active;
                tile.Type = Stone;
                framing.PlaceTight(tip, row + 2);
            }
            else
            {
                framing.PlaceTight(tip, row + 1);
            }
        }
    }

    // Walks outward until three consecutive columns are clear, carving an entrance corridor as it goes.
    private void MakeOpening(int direction, int startX, int startY, int openingSize)
    {
        int x = startX;
        int y = startY;
        openingSize--;
        bool clear;
        do
        {
            cancellation.ThrowIfCancellationRequested();
            x += direction;
            clear = true;
            for (int row = y - openingSize + 1; row < y - 1; row++)
            {
                if (Solid(x, row)) clear = false;
                if (Solid(x + direction, row)) clear = false;
                if (Solid(x + direction * 2, row)) clear = false;
                At(x, row).Flags &= ~WorldTileFlags.Active;
            }

            for (int row = y - openingSize; row < y; row++)
                At(x - direction, row).Flags &= ~WorldTileFlags.Active;
            if (Solid(x - direction, y - openingSize - 1)) At(x - direction, y - openingSize - 1).Wall = 0;
            if (Solid(x - direction, y)) At(x - direction, y).Wall = 0;
            if (random.Next(2) == 0) y += random.Next(-1, 2);
        }
        while (!clear && x >= 50 && x <= width - 50 && Math.Abs(x - startX) <= 100);
    }

    // Five hundred attempts to plant one of the seven gem trees on the new stone floor.
    private void GrowGemTrees(int centerX, int centerY)
    {
        const int spread = 70;
        for (int attempt = 0; attempt < 500; attempt++)
        {
            if ((attempt & 63) == 0) cancellation.ThrowIfCancellationRequested();
            int x = random.Next(centerX - spread, centerX + spread);
            int y = random.Next(centerY - 2, centerY + 3);
            var treeTileType = (ushort)(583 + random.Next(7));
            if (Solid(x - 1, y) && Solid(x + 1, y))
                SettingsTreeGrower1458.TryGrow(store, SettingsTreeGrower1458.GemTree(treeTileType), x, y, random);
        }
    }

    // Both radii are perturbed per axis by up to two percent, and the source truncates the result to an int.
    private int Ellipse(int dx, int dy, double verticalScale)
    {
        double horizontal = Math.Abs(dx) * (1d + random.NextDouble() * .02);
        double vertical = Math.Abs(dy) * verticalScale * (1d + random.NextDouble() * .02);
        return (int)Math.Sqrt(Math.Pow(horizontal, 2d) + Math.Pow(vertical, 2d));
    }

    private bool Solid(int x, int y) =>
        (uint)x < (uint)width && (uint)y < (uint)height && DungeonGenerationTiles1458.SolidTile(At(x, y));

    private ref WorldTile At(int x, int y)
    {
        // The source indexes Main.tile directly here, so leaving the world is a generation fault, not a clamp.
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            throw new InvalidOperationException($"Shimmer biome touched ({x},{y}) outside the world.");
        return ref store.Tiles[store.GetUncheckedIndex(x, y)];
    }
}
