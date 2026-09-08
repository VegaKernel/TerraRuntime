using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary AddHellHouses wall-art and ceiling-decoration phases (TerrariaServer 1.4.5.8).
/// Runs only on the generation-owned tile store, after ground furniture, using the shared RNG.</summary>
internal static class HellFortDecoration1458
{
    internal static int AttemptCount(int width) => (int)Math.Ceiling(420_000d / width);

    public static void Generate(WorldTileStore store, IWorldGenerationVanillaRandom random, CancellationToken cancellationToken)
    {
        for (int request = 0; request < AttemptCount(store.Dimensions.WidthTiles); request++)
            if (SampleHouse(store, random, painting: true, cancellationToken, out int x, out int y))
                TryPlacePaintingCandidate(store, x, y, random);
        Span<int> palette = stackalloc int[3];
        SelectBannerPalette(random, palette, cancellationToken);
        for (int request = 0; request < AttemptCount(store.Dimensions.WidthTiles); request++)
            if (SampleHouse(store, random, painting: false, cancellationToken, out int x, out int y))
                TryPlaceCeilingCandidate(store, x, y, palette, random);
    }

    private static bool SampleHouse(WorldTileStore store, IWorldGenerationVanillaRandom random, bool painting,
        CancellationToken cancellationToken, out int x, out int y)
    {
        int width = store.Dimensions.WidthTiles, height = store.Dimensions.HeightTiles, quarter = (int)(width * .25);
        // Painting has an initial sample plus 100001 retries; ceiling has a counted do/while sample.
        int last = painting ? 100_001 : 100_000;
        for (int sample = 0; ; sample++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            x = random.Next(quarter, width - quarter); y = random.Next(height - 250, height - 20);
            if (sample == last) return false; // Source discards even a valid final over-budget sample.
            if (HouseAir(store, x, y)) return true;
        }
    }

    internal static void SelectBannerPalette(IWorldGenerationVanillaRandom random, Span<int> palette, CancellationToken cancellationToken)
    {
        if (palette.Length != 3) throw new ArgumentException("Three banner styles are required.", nameof(palette));
        for (int i = 0; i < 3; i++) palette[i] = random.Next(16, 22);
        for (int i = 1; i < 3; i++)
        {
            int retries = 0;
            while (palette[..i].Contains(palette[i]))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Ordinary source retries indefinitely. A broken RNG must abort, not invent a palette.
                if (++retries > 100_000) throw new InvalidOperationException("Hell banner palette search exhausted.");
                palette[i] = random.Next(16, 22);
            }
        }
    }

    internal static bool TryPlacePaintingCandidate(WorldTileStore store, int x, int y, IWorldGenerationVanillaRandom random)
    {
        if (!HouseAir(store, x, y)) return false;
        for (int pass = 0; pass < 2; pass++)
        {
            if (!Scan(store, x, y, horizontal: true, house: true, out int left, out int right)) return false;
            x = (left + right) / 2;
            if (!Scan(store, x, y, horizontal: false, house: true, out int top, out int bottom)) return false;
            y = (top + bottom) / 2;
        }
        // The final three-cell strips ignore walls; both scans use the same pre-recentered point.
        if (!Scan(store, x, y, horizontal: true, house: false, out int l, out int r) ||
            !Scan(store, x, y, horizontal: false, house: false, out int t, out int b)) return false;
        x = (l + r) / 2; y = (t + b) / 2;
        if (r - l <= 7 || b - t <= 5 || NearPicture(store, x, y, picturesOnly: true)) return false;
        (ushort type, int style) = SelectPainting(random);
        // RandHellPicture precedes the tighter, all-active-tile exclusion, even on rejection.
        return !NearPicture(store, x, y, picturesOnly: false) && Place(store, x, y, type, style);
    }

    private static bool Scan(WorldTileStore store, int x, int y, bool horizontal, bool house, out int low, out int high)
    {
        int limit = horizontal ? store.Dimensions.WidthTiles : store.Dimensions.HeightTiles;
        low = high = horizontal ? x : y;
        while (Open(low) && low > (horizontal ? 10 : 1)) low--;
        while (Open(high) && high < limit - (horizontal ? 10 : 2)) high++;
        // Vanilla assumes closed vertical rooms. Refuse malformed open world edges instead of indexing outside.
        if (!horizontal && (low == 1 || high == limit - 2)) return false;
        low++; high--;
        return true;

        bool Open(int position)
        {
            int sx = horizontal ? position : x, sy = horizontal ? y : position;
            if (house) return HouseAir(store, sx, sy);
            return Air(store, sx, sy) && Air(store, sx + (horizontal ? 0 : -1), sy + (horizontal ? -1 : 0)) &&
                Air(store, sx + (horizontal ? 0 : 1), sy + (horizontal ? 1 : 0));
        }
    }

    internal static bool NearPicture(WorldTileStore store, int x, int y, bool picturesOnly)
    {
        bool dungeon = InBounds(store, x, y) && At(store, x, y).Wall is 7 or 8 or 9;
        int left = picturesOnly ? dungeon ? 15 : 8 : 4, right = picturesOnly ? left : 3;
        int above = picturesOnly ? dungeon ? 10 : 5 : 3, below = picturesOnly ? above : 2;
        for (int sx = x - left; sx <= x + right; sx++)
        for (int sy = y - above; sy <= y + below; sy++)
        {
            if (!InBounds(store, sx, sy)) return true;
            WorldTile cell = At(store, sx, sy);
            // Source intentionally does not include the small paintings 245/246 in nearPicture2.
            if (cell.IsActive && (!picturesOnly || cell.Type is 240 or 241 or 242)) return true;
        }
        return false;
    }

    internal static (ushort Type, int Style) SelectPainting(IWorldGenerationVanillaRandom random)
    {
        int family = random.Next(4);
        if (family == 1) family = random.Next(4); // One redraw, not a rejection loop.
        return family switch
        {
            0 => (240, random.Next(5) switch { 0 => 27, 1 => 29, 2 => 30, 3 => 31, _ => 32 }),
            1 => (242, 14),
            2 => (245, random.Next(3) switch { 0 => 1, 1 => 2, _ => 4 }),
            _ => (246, random.Next(3) switch { 0 => 0, 1 => 16, _ => 17 })
        };
    }

    internal static bool TryPlaceCeilingCandidate(WorldTileStore store, int x, int y, ReadOnlySpan<int> palette,
        IWorldGenerationVanillaRandom random)
    {
        if (!HouseAir(store, x, y)) return false;
        while (y > 10 && !SolidCeiling(At(store, x, y), requireFullShape: true)) y--;
        y++;
        if (!InBounds(store, x, y) || At(store, x, y).Wall is not (13 or 14)) return false;
        int choice = random.Next(3);
        // AddHellHouses' apparent clearance loops repeatedly read [x,y], not their scan coordinates.
        // Every choice has an interior iteration, so any active anchor rejects; placement checks the footprint.
        if (At(store, x, y).IsActive) return false;
        return choice switch
        {
            0 => Place(store, x, y, 91, palette[random.Next(3)]),
            1 => Place(store, x, y, 34, 32),
            _ => Place(store, x, y, 42, 32)
        };
    }

    // Source PlaceTile -> Place3x3Wall/6x4Wall/2x3Wall/3x2Wall/Banner/Chand/1x2Top.
    // Only AddHellHouses' verified types/styles are admitted; this is not a player placement service.
    internal static bool Place(WorldTileStore store, int x, int y, ushort type, int style)
    {
        (int ox, int oy, int width, int height, int frameX, int frameY) = (type, style) switch
        {
            (240, 27 or 29 or 30 or 31 or 32) => (-1, -1, 3, 3, style * 54, 0),
            (242, 14) => (-2, -2, 6, 4, 0, 1008),
            (245, 1 or 2 or 4) => (0, -1, 2, 3, style * 36, 0),
            (246, 0 or 16 or 17) => (-1, 0, 3, 2, 0, style * 36),
            (91, >= 16 and < 22) => (0, 0, 1, 3, style * 18, 0),
            (34, 32) => (-1, 0, 3, 3, 0, 1728),
            (42, 32) => (0, 0, 1, 2, 0, 1152),
            _ => (0, 0, 0, 0, 0, 0)
        };
        if (width == 0 || x < 5 || y < 5 || x > store.Dimensions.WidthTiles - 5 || y > store.Dimensions.HeightTiles - 5) return false;
        if (!At(store, x, y).IsActive) HellFortGenerator1458.ClearPlacementAnchor(ref At(store, x, y));
        bool painting = type is 240 or 242 or 245 or 246;
        if (!painting && !SolidCeiling(At(store, x, y - 1), requireFullShape: false)) return false;
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < height; dy++)
        {
            WorldTile tile = At(store, x + ox + dx, y + oy + dy);
            if (tile.IsActive || (painting && tile.Wall == 0)) return false;
        }
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < height; dy++)
        {
            ref WorldTile tile = ref At(store, x + ox + dx, y + oy + dy);
            tile.Flags |= WorldTileFlags.Active; tile.Type = type;
            tile.FrameX = (short)(frameX + dx * 18); tile.FrameY = (short)(frameY + dy * 18);
        }
        return true;
    }

    private static bool SolidCeiling(in WorldTile tile, bool requireFullShape) => tile.IsActive && !tile.IsActuated &&
        (!requireFullShape || tile.Shape == 0) && VanillaTileCollisionCatalog.IsSolid(tile.TileType) && !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
    private static bool InBounds(WorldTileStore store, int x, int y) => (uint)x < (uint)store.Dimensions.WidthTiles && (uint)y < (uint)store.Dimensions.HeightTiles;
    private static bool Air(WorldTileStore store, int x, int y) => InBounds(store, x, y) && !At(store, x, y).IsActive;
    private static bool HouseAir(WorldTileStore store, int x, int y) => Air(store, x, y) && At(store, x, y).Wall is 13 or 14;
    private static ref WorldTile At(WorldTileStore store, int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
