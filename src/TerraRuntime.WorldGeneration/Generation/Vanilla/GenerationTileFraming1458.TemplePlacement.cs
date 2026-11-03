using TerraRuntime.World;
using TerraRuntime.Contracts.Gameplay;
namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    internal bool PrepareTempleTrigger(int x, int y)
    {
        if (!Contains(x, y) || !Contains(x, y + 1)) return false;
        if (!SolidTileAllowBottomSlope(x, y + 1))
        {
            ref WorldTile floor = ref At(x, y + 1); WorldTile above = At(x, y);
            if (floor.IsActive && WorldSmoothingCatalog1458.CanBePounded(floor.TileType) &&
               floor.WallType != VanillaWallIds.UnbreakableTemple && CanRemoveTile(x, y + 1) &&
               (!above.IsActive || !WorldSmoothingCatalog1458.ForbidsSlopingBelow(above.TileType))) floor.ClearSourceShape();
        }
        return x >= 3 && y >= 3 && x < width - 3 && y < height - 3 && SolidTileAllowBottomSlope(x, y + 1) &&
            !GenerationObjectSupport1458.IsBoulder(At(x, y + 1).Type) && At(x, y + 1).Wall != 350;
    }
    internal void PlaceTempleMechanism(int x, int y, ushort type, int style)
    {
        if (!Contains(x, y)) return;
        ref WorldTile tile = ref At(x, y);
        if (tile.IsActive && tile.Type == 488) return;
        if (!tile.IsActive) HellFortGenerator1458.ClearPlacementAnchor(ref tile);
        if (type == 135)
        {
            WorldTile floor = At(x, y + 1);
            bool supports = floor.IsActive && !floor.IsActuated && !floor.IsHalfBrick && IsFramingSolid(floor.TileType) &&
                (floor.SourceSlope == 0 || VanillaTileIds.IsPlatform(floor.TileType) && floor.SourceSlope is 1 or 2);
            if (x >= 2 && y >= 2 && x < width - 2 && y < height - 2 && supports && !tile.IsActive &&
                !GenerationObjectSupport1458.IsBoulder(floor.Type))
            { tile.Flags |= WorldTileFlags.Active; tile.Type = 135; tile.FrameY = checked((short)(style * 18)); }
            SquareTileFrame(x, y);
        }
        else
        { tile.Flags |= WorldTileFlags.Active; tile.Type = 137; tile.FrameY = checked((short)(style * 18)); }
        if (At(x, y).IsActive) SquareTileFrame(x, y);
    }
}
