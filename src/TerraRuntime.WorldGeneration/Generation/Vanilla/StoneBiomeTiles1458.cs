using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Generation-only TileFrame, SmoothSlope and PlaceTight operations used by stone micro-biomes.</summary>
internal sealed class StoneBiomeTiles1458(WorldTileStore tiles, IWorldGenerationVanillaRandom random, bool crackedBricksSolid = true)
{
    private const WorldTileFlags BlockCoatings = WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock;
    private int framingDepth;

    internal static bool IsOre(ushort type) => type is 7 or 166 or 6 or 167 or 9 or 168 or 8 or 169 or
        22 or 204 or 37 or 58 or 107 or 221 or 108 or 222 or 111 or 223 or 211;

    public bool Solid(int x, int y)
    {
        WorldTile cell = At(x, y);
        // Full Desert leaves the generation-only boulder solidity override disabled.
        return cell.IsActive && !cell.IsActuated && cell.Type != 484 && (crackedBricksSolid || cell.Type is not (481 or 482 or 483)) &&
            VanillaTileCollisionCatalog.IsSolid(cell.TileType) && !VanillaTileCollisionCatalog.IsSolidTop(cell.TileType);
    }
    public bool Flat(int x, int y) => Solid(x, y) && At(x, y).Shape == 0;

    public void FrameNeighbours(int x, int y)
    {
        Frame(x, y); Frame(x + 1, y); Frame(x - 1, y); Frame(x, y + 1); Frame(x, y - 1);
    }

    public void Smooth(int x, int y)
    {
        SmoothCell(x + 1, y); SmoothCell(x - 1, y); SmoothCell(x, y + 1); SmoothCell(x, y - 1); SmoothCell(x, y);
    }

    internal void SmoothCell(int x, int y)
    {
        ref WorldTile cell = ref At(x, y);
        WorldTile above = At(x, y - 1);
        if (!Solid(x, y) || cell.Wall == 350 || !WorldSmoothingCatalog1458.CanBePounded(cell.TileType) ||
            above.IsActive && WorldSmoothingCatalog1458.ForbidsSlopingBelow(above.TileType) ||
            !WorldSmoothingCatalog1458.CanRemoveTileBelow(above, cell.TileType)) return;
        bool occupied = above.IsActive && !above.IsActuated;
        int mask = (occupied ? 8 : 0) | (Solid(x, y + 1) ? 4 : 0) |
            (Solid(x - 1, y) ? 2 : 0) | (Solid(x + 1, y) ? 1 : 0);
        if (mask is 10 or 9 && occupied && !Solid(x, y - 1)) return;
        cell.Shape = mask switch { 10 => 4, 9 => 5, 6 => 2, 5 => 3, 4 => 1, _ => 0 };
    }

    public void PlaceTight(int x, int y)
    {
        WorldTile anchor = At(x, y);
        if (anchor.LiquidAmount > 0 && anchor.LiquidKind == WorldLiquidKind.Shimmer || anchor.IsActive && anchor.Type == 231) return;
        bool small = random.Next(2) == 0;
        int variation = random.Next(3);
        PlaceUncheckedTight(x, y, small, variation);
        if (At(x, y).IsActive && At(x, y).Type == 165) CheckTight(x, y);
    }

    public void PlaceUncheckedTight(int x, int y, bool small, int variation)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(variation);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(variation, 2);
        WorldTile anchor = At(x, y);
        bool ceiling = Flat(x, y - 1) && !anchor.IsActive && !At(x, y + 1).IsActive;
        bool floor = !ceiling && Flat(x, y + 1) && !anchor.IsActive && !At(x, y - 1).IsActive;
        if (ceiling || floor)
        {
            WorldTile support = At(x, y + (ceiling ? -1 : 1));
            int frameBase = support.Type switch
            {
                147 or 161 or 163 or 164 or 200 when ceiling => 0,
                1 or 117 or 25 or 203 => 54,
                225 => 162, 396 or 397 => 378, 368 => 432, 367 => 486,
                _ when IsMoss(support.Type) => 54,
                _ => -1
            };
            // Unsupported material is an explicit non-placement in PlaceUncheckedStalactite.
            if (frameBase >= 0)
            {
                small |= support.Type == 225;
                if (small) SetPart(y, ceiling ? 72 : 90);
                else if (ceiling) { SetPart(y, 0); SetPart(y + 1, 18); }
                else { SetPart(y - 1, 36); SetPart(y, 54); }
            }

            void SetPart(int row, int frameY)
            {
                ref WorldTile part = ref At(x, row);
                part.Type = 165; part.Shape = 0; part.TileColor = support.TileColor;
                part.Flags = (part.Flags & ~BlockCoatings) | WorldTileFlags.Active | (support.Flags & BlockCoatings);
                part.FrameX = (short)(frameBase + variation * 18); part.FrameY = (short)frameY;
            }
        }
    }

    public void Frame(int x, int y)
    {
        if (x <= 5 || y <= 5 || x >= tiles.Dimensions.WidthTiles - 5 || y >= tiles.Dimensions.HeightTiles - 5) return;
        ref WorldTile cell = ref At(x, y);
        if (!cell.IsActive) { cell.Shape = cell.TileColor = 0; cell.Flags &= ~BlockCoatings; return; }
        if (cell.Type == 165) CheckTight(x, y);
        else if (VanillaWorldFrameImportance326.IsFrameImportant(cell.Type))
            throw new InvalidOperationException($"Unverified stone-biome object framing: {cell.Type} at {x},{y}.");
        // WorldGen.TileFrame has no non-cosmetic branch for Cobweb51; unlike vines/cacti it remains unchanged
        // while generatingWorld is true. Evil surface SpreadGrass can frame an adjacent pre-existing web.
        else if (cell.Type != 51 && !VanillaTileCollisionCatalog.IsSolid(cell.TileType))
            throw new InvalidOperationException($"Unverified stone-biome non-solid framing: {cell.Type}.");
        // Ordinary solid cosmetic framing is skipped while generatingWorld is true.
    }

    private void CheckTight(int x, int y)
    {
        WorldTile anchor = At(x, y);
        int top = y - (anchor.FrameY is 18 or 54 ? 1 : 0);
        bool small = anchor.FrameY is 72 or 90;
        bool floor = anchor.FrameY >= 36 && anchor.FrameY != 72;
        int supportY = floor ? top + (small ? 1 : 2) : top - 1;
        bool valid = Flat(x, supportY);
        if (!small)
        {
            WorldTile first = At(x, top), second = At(x, top + 1);
            valid &= first.IsActive && second.IsActive && first.Type == second.Type && first.FrameX == second.FrameX;
        }
        // Every boulder material is outside the desired-style table below, so it is
        // rejected without consuming a style draw, just as the source's earlier gate.
        if (valid) valid = UpdateStyle(x, top, small, floor);
        if (valid) return;
        if (++framingDepth > 64) throw new InvalidOperationException("Stone-biome framing recursion exceeded its safety bound.");
        try
        {
            // Source re-reads the anchor identity after recursive neighbour framing.
            if (At(x, top).Type == At(x, y).Type) KillTight(x, top);
            if (!small && At(x, top + 1).Type == At(x, y).Type) KillTight(x, top + 1);
        }
        finally { framingDepth--; }
    }

    private bool UpdateStyle(int x, int top, bool small, bool floor)
    {
        WorldTile cell = At(x, top);
        int current = cell.FrameX / 54;
        if (current is < 0 or > 12) return false;
        ushort support = At(x, floor ? top + (small ? 1 : 2) : top - 1).Type;
        int desired = support switch
        {
            1 => !small && !floor && cell.Wall == 62 ? 2 : 1,
            200 => 12, 164 => 10, 163 => 11,
            117 or 402 or 403 => 4, 25 or 398 or 400 => 5, 203 or 399 or 401 => 6,
            396 or 397 => 7, 367 => 9, 368 => 8, 147 or 161 => 0, 225 when small => 3,
            _ when IsMoss(support) => !small && !floor && cell.Wall == 62 ? 2 : 1,
            _ => -1
        };
        if (desired < 0) return false;
        if (current != desired)
        {
            short frame = (short)(desired * 54 + random.Next(3) * 18);
            At(x, top).FrameX = frame;
            if (!small) At(x, top + 1).FrameX = frame;
        }
        return true;
    }

    private void KillTight(int x, int y)
    {
        ref WorldTile cell = ref At(x, y);
        if (!cell.IsActive) return;
        if (cell.Type != 165) throw new InvalidOperationException("Stone-biome framing attempted to destroy a non-speleothem.");
        cell.Type = 0; cell.FrameX = cell.FrameY = -1; cell.Shape = cell.TileColor = 0;
        cell.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive | BlockCoatings);
        for (int col = x - 1; col <= x + 1; col++)
        for (int row = y - 1; row <= y + 1; row++) Frame(col, row);
    }

    private static bool IsMoss(ushort type) => type is 179 or 180 or 181 or 182 or 183 or 381 or 534 or 536 or 539 or 625 or 627;

    public void ClearTile(int x, int y, bool neighbours = false)
    {
        ref WorldTile cell = ref At(x, y);
        cell.Shape = 0; cell.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive);
        if (!neighbours) return;
        Frame(x + 1, y); Frame(x - 1, y); Frame(x, y + 1); Frame(x, y - 1);
    }

    internal static bool BiomeTileCheck(WorldTileStore tiles, int x, int y)
    {
        for (int col = Math.Max(0, x - 50); col <= Math.Min(tiles.Dimensions.WidthTiles - 1, x + 50); col++)
        for (int row = Math.Max(0, y - 50); row <= Math.Min(tiles.Dimensions.HeightTiles - 1, y + 50); row++)
        {
            WorldTile tile = tiles.Tiles[tiles.GetUncheckedIndex(col, row)];
            if (tile.IsActive && tile.Type is 368 or 367 or 147 or 161 or 162 or 70 or 72 or 396 or 397 || tile.Wall is 187 or 216) return true;
        }
        return false;
    }

    private ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
}
