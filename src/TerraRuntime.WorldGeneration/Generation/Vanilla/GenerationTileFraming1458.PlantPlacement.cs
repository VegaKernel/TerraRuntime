using TerraRuntime.World;
using TerraRuntime.Contracts.Gameplay;
namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    internal bool TryPlaceSunflower(int x, int y, double worldSurface)
    {
        if (!PreparePlantPlacement(x, y, refuseLiquid: true)) return false;
        PlaceSunflower(x, y, worldSurface);
        SquareTileFrame(x, y);
        return FinishPlantPlacement(x, y);
    }
    private bool PreparePlantPlacement(int x, int y, bool refuseLiquid)
    {
        if (!Contains(x, y)) return false;
        ref WorldTile anchor = ref At(x, y);
        if (anchor.IsActive && anchor.Type == 488 || refuseLiquid && (anchor.LiquidAmount > 0 || store.LiquidUpdates.IsCheckingLiquid(x, y))) return false;
        if (!anchor.IsActive) HellFortGenerator1458.ClearPlacementAnchor(ref anchor);
        else if (!VanillaWorldFrameImportance326.IsFrameImportant(anchor.Type))
        { anchor.TrySetSourceShape(false, anchor.SourceSlope); anchor.FrameX = 0; anchor.FrameY = 0; }
        return true;
    }
    private bool FinishPlantPlacement(int x, int y)
    {
        bool active = At(x, y).IsActive;
        if (active)
        {
            if (At(x, y).Type is 54 or 328 or 459 or 748) new GenerationWallFraming1458(store, random).SquareWallFrame(x, y);
            SquareTileFrame(x, y);
        }
        return active;
    }
    internal bool TryPlaceDyePlant(int x, int y, int style)
    {
        if (!PreparePlantPlacement(x, y, refuseLiquid: false)) return false;
        PlaceDyePlant(x, y, style); SquareTileFrame(x, y); return FinishPlantPlacement(x, y);
    }
    internal void PlaceDyePlant(int x, int y, int style)
    {
        if (!Contains(x, y - 1) || !Contains(x, y + 1)) return;
        WorldTile above = At(x, y - 1), below = At(x, y + 1); bool allowed;
        if (style == 7)
        {
            if (below.IsActive && !DyeReplaceable(below.Type)) return;
            allowed = SolidTile(x, y - 1) && !below.IsActive;
        }
        else
        {
            if (above.IsActive && !DyeReplaceable(above.Type)) return;
            if (style == 6) allowed = below.IsActive && !below.IsActuated && below.Type == 80 && !At(x - 1, y + 1).IsActive && !At(x + 1, y + 1).IsActive;
            else if (SolidTile(x, y + 1) && !above.IsActive)
            {
                WorldTile target = At(x, y);
                allowed = style switch
                {
                    5 => target.LiquidAmount == 255 && target.LiquidKind == WorldLiquidKind.Water,
                    >= 8 and <= 11 => true,
                    3 or 4 => target.LiquidAmount == 0 && target.Wall == 0,
                    _ => target.LiquidAmount == 0
                };
            }
            else allowed = false;
        }
        if (!allowed) return;
        ref WorldTile tile = ref At(x, y); tile.Type = 227; tile.Flags |= WorldTileFlags.Active; tile.ClearSourceShape(); tile.FrameX = unchecked((short)(34 * style)); tile.FrameY = 0;
    }
    private static bool DyeReplaceable(ushort type) => type is 3 or 51 or 697 or 61 or 703 or 73 or 74 or 184;
    internal bool NaturalPlantGround(int x, int y)
    {
        if (x < 2 || y < 2 || x >= width - 2 || y >= height - 2) return false;
        WorldTile ground = At(x, y + 1); if (!ground.IsActive) return false;
        return (ground.Type is 0 or 70 or 633 or 59 or 225 or 2 or 23 or 199 or 109 or 477 or 492 || SettingsTreeGrower1458.IsGemTreeGround(ground.TileType)) && SolidTileAllowBottomSlope(x, y + 1);
    }
    internal bool TryPlaceGlowTulip(int x, int y)
    {
        if (!PreparePlantPlacement(x, y, refuseLiquid: true)) return false;
        WorldTile before = At(x, y);
        if ((!before.IsActive || VanillaProjectileTileCutFacts.IsCuttable(before.TileType) || GenerationObjectSupport1458.BreakableWhenPlacing(before.Type)) && NaturalPlantGround(x, y))
        {
            ref WorldTile tile = ref At(x, y); tile.Flags |= WorldTileFlags.Active; tile.Type = 656; tile.ClearSourceShape(); tile.FrameX = tile.FrameY = 0;
        }
        return FinishPlantPlacement(x, y);
    }
    // Place2x2 uses the Tile overload; its platform rule differs from SolidTile2(x,y).
    private bool SolidTile2(in WorldTile tile) => tile.IsActive && IsFramingSolid(tile.TileType) && tile.SourceSlope == 0 && !tile.IsHalfBrick && !tile.IsActuated;
    internal bool TryPlaceChilletEgg(int x, int y)
    {
        if (!PreparePlantPlacement(x, y, refuseLiquid: false)) return false;
        if (x >= 5 && y >= 5 && x <= width - 5 && y <= height - 5)
        {
            bool canPlace = true;
            for (int column = x - 1; column <= x; column++)
            {
                for (int row = y - 1; row <= y; row++) if (At(column, row).IsActive) canPlace = false;
                WorldTile floor = At(column, y + 1);
                if (!floor.IsActive || floor.IsActuated || !SolidTile2(in floor) && !GenerationFurnitureTiles1458.IsTable(floor.Type)) canPlace = false;
            }
            if (canPlace) for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 2; dy++)
            {
                ref WorldTile cell = ref At(x - 1 + dx, y - 1 + dy); cell.Flags |= WorldTileFlags.Active; cell.Type = 752; cell.FrameX = (short)(dx * 18); cell.FrameY = (short)(dy * 18);
            }
        }
        return FinishPlantPlacement(x, y);
    }
    internal void PlaceSunflower(int x, int y, double worldSurface)
    {
        if (y > worldSurface - 1) return;
        if (!Contains(x, y - 3) || !Contains(x + 1, y + 1)) throw new NotSupportedException("Sunflower footprint reaches the source's invalid tile-read boundary.");
        for (int dx = 0; dx < 2; dx++)
        {
            for (int row = y - 3; row <= y; row++) if (At(x + dx, row).IsActive || At(x + dx, row).Wall > 0) return;
            WorldTile floor = At(x + dx, y + 1);
            if (!floor.IsActive || floor.IsActuated || floor.IsHalfBrick || floor.SourceSlope != 0 || floor.Type is not (2 or 109)) return;
        }
        int crown = random.Next(3);
        for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 4; dy++)
        {
            int variation = random.Next(3);
            ref WorldTile cell = ref At(x + dx, y - 3 + dy); cell.Flags |= WorldTileFlags.Active; cell.Type = 27;
            cell.FrameX = (short)(dx * 18 + (dy < 2 ? crown : variation) * 36); cell.FrameY = (short)(dy * 18);
        }
    }
}
