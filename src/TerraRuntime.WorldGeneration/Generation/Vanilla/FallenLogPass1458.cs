using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>GenPassNameID.FallenLogsAndWaterFeatures</c>, fallen-log half.
/// </summary>
/// <remarks>
/// <para>
/// One log per 2100 tiles of width, jittered by <c>Next(-1, 2)</c>, and each log gets up to thirty thousand
/// attempts of its own. An attempt draws a column inside the beaches and a row above the surface, drops down
/// the column to the first cell that is either active or walled, and offers the log to the cell above when all
/// three columns of its footprint stand on grass.
/// </para>
/// <para>
/// The attempt counter is not just a budget, it is the difficulty. While more than half the budget is left the
/// column is redrawn until it falls outside the middle fifth of the world, which is what keeps logs away from
/// the spawn; and once fewer than five thousand attempts remain the two neighbourhood scans are skipped
/// entirely, so a log that could not find a clean site settles for a dirty one. Below a thousand attempts even
/// standing water stops being a refusal.
/// </para>
/// <para>
/// A placed log records itself in <c>GenVars.logX</c>/<c>logY</c> on a one-in-two draw. That is not bookkeeping:
/// the Flowers pass reads it later and moves its FIRST patch onto the log, so whether the draw comes up decides
/// where a whole flower patch lands.
/// </para>
/// </remarks>
internal sealed class FallenLogPass1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    double worldSurface,
    int beachDistance,
    CancellationToken cancellation)
{
    private const ushort FallenLog = 488;
    private const ushort Grass = 2;
    private const ushort Cloud = 189;
    private const ushort Sand = 53;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    public long Placed { get; private set; }

    /// <summary>The last log that won its one-in-two draw, in world tiles, or null when none did.</summary>
    public (int X, int Y)? Anchor { get; private set; }

    public void Apply()
    {
        int logs = width / 2100 + random.Next(-1, 2);
        for (int log = 0; log < logs; log++)
        {
            int margin = beachDistance + 20;
            int attempts = 30000;
            const int relaxed = 5000;
            while (attempts > 0)
            {
                if ((attempts & 0x3FF) == 0)
                    cancellation.ThrowIfCancellationRequested();

                attempts--;
                int column = random.Next(margin, width - margin);
                int row = random.Next(10, (int)worldSurface);
                bool skipScans = attempts < relaxed;
                if (attempts > relaxed / 2)
                {
                    while (column > width * 0.4 && column < width * 0.6)
                        column = random.Next(margin, width - margin);
                }

                if (IsActive(column, row) || WallAt(column, row) != 0)
                    continue;

                bool fits = true;
                while (!IsActive(column, row) && WallAt(column, row) == 0 && row <= worldSurface)
                    row++;

                if (row > worldSurface - 10.0)
                {
                    fits = false;
                }
                else if (!skipScans)
                {
                    fits = NeighbourhoodIsClean(column, row) && HeadroomIsClear(column, row);
                }

                if (!fits)
                    continue;

                if (LiquidAt(column, row - 1) != 0 && attempts >= relaxed / 5)
                    continue;

                if (TypeAt(column, row) != Grass || TypeAt(column - 1, row) != Grass ||
                    TypeAt(column + 1, row) != Grass)
                {
                    continue;
                }

                row--;
                GenerationDecorationPlacement1458.TryPlaceTile3x2(store, random, column, row, FallenLog, 0);
                if (!IsActive(column, row) || TypeAt(column, row) != FallenLog)
                    continue;

                Placed++;
                if (random.Next(2) == 0)
                    Anchor = (column, row);
                attempts = -1;
            }
        }
    }

    /// <summary>
    /// The fifty-tile scan. A log refuses a cloud, sand, a dungeon brick or either evil anywhere inside it -
    /// and it reads the raw identity, so an inactive cell answers with zero and refuses nothing.
    /// </summary>
    private bool NeighbourhoodIsClean(int column, int row)
    {
        const int reach = 50;
        bool clean = true;
        for (int x = column - reach; x < column + reach; x++)
        {
            if (x <= 10 || x >= width - 10)
                continue;

            for (int y = row - reach; y < row + reach; y++)
            {
                if (y <= 10 || y >= height - 10)
                    continue;

                ushort type = TypeAtRaw(x, y);
                if (type is Cloud or Sand || IsDungeonBrick(type) || IsCrimson(type) || IsCorrupt(type))
                    clean = false;
            }
        }

        return clean;
    }

    /// <summary>The ten-by-ten headroom above the site: no solid cell and no wall at all.</summary>
    private bool HeadroomIsClear(int column, int row)
    {
        const int reach = 10;
        bool clear = true;
        for (int x = column - reach; x < column + reach; x++)
        {
            for (int y = row - reach; y < row - 1; y++)
            {
                if (IsActive(x, y) && VanillaTileCollisionCatalog.IsSolid(At(x, y).TileType))
                    clear = false;
                if (WallAt(x, y) != 0)
                    clear = false;
            }
        }

        return clear;
    }

    /// <summary>Source <c>Main.tileDungeon</c>.</summary>
    private static bool IsDungeonBrick(ushort type) => type is 41 or 43 or 44 or 677 or 678 or 679;

    /// <summary>Source <c>TileID.Sets.Corrupt</c>.</summary>
    private static bool IsCorrupt(ushort type) =>
        type is 23 or 661 or 25 or 112 or 163 or 398 or 400 or 636;

    /// <summary>Source <c>TileID.Sets.Crimson</c>.</summary>
    private static bool IsCrimson(ushort type) =>
        type is 199 or 662 or 203 or 234 or 200 or 399 or 401 or 205;

    private bool IsActive(int x, int y) => Contains(x, y) && At(x, y).IsActive;

    private ushort TypeAt(int x, int y) => Contains(x, y) && At(x, y).IsActive ? At(x, y).Type : (ushort)0;

    private ushort TypeAtRaw(int x, int y) => Contains(x, y) ? At(x, y).Type : (ushort)0;

    private ushort WallAt(int x, int y) => Contains(x, y) ? At(x, y).Wall : (ushort)0;

    private byte LiquidAt(int x, int y) => Contains(x, y) ? At(x, y).LiquidAmount : (byte)0;

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
