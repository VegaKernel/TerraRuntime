using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldSourcePass1458
    {
        internal bool TryRotateToolFurniture(int x, int y, out TerrariaToolTileSquareState1458? square)
        {
            square = null;
            if (!clientRole || !IsCurrentOwned || work.Count != 0 ||
                !Overlay.TryRead(x, y, out var tile) || tile.Type is not (137 or 424 or 442)) return Reject();
            Span<ToolFrameTile1458> window = stackalloc ToolFrameTile1458[9];
            if (!TryReadFurnitureWindow(x, y, window) ||
                !VanillaToolFurniture1458.TryRotate(ToFurniture(in tile), window, out short frameX, out bool report)) return Reject();
            if (!report) return true; // Source442 may consume its local pound but emits no20.
            var after = tile; after.FrameX = frameX;
            if (!Stage(WorldTileMutationKind.TransformTile, x, y, in tile, in after)) return Reject();
            square = new(checked((short)x), checked((short)y), after);
            return IsCurrentOwned;
        }

        internal bool TryApplyToolFurnitureSquare(in TerrariaToolTileSquareState1458 square)
        {
            if (clientRole || !IsCurrentOwned || work.Count != 0 ||
                !Overlay.TryRead(square.TileX, square.TileY, out var before) ||
                !before.IsActive || before.Type != square.Tile.Type || before.Type is not (137 or 424 or 442)) return Reject();
            // The caller has proved the complete CLIENT wire image, not merely
            // frameX. Every other transmitted field equals this working before
            // image. Omitted liquid/stale paint fields retain SERVER semantics.
            var after = before; after.FrameX = square.Tile.FrameX; after.FrameY = square.Tile.FrameY;
            var expected = new TerrariaToolTileSquareState1458(square.TileX, square.TileY, after);
            if (!TerrariaTileSquareCodec.HasSameToolWire(in expected, in square) ||
                !Stage(WorldTileMutationKind.TransformTile, square.TileX, square.TileY, in before, in after)) return Reject();
            // MessageBuffer20 RangeFrame(x,y,x+1,y+1): source x-major/y-minor
            // 4x4 TileFrame then WallFrame, each resetFrame=false. A3x3 square
            // or batched tile-then-all-walls changes source read/order/RNG.
            for (int x = square.TileX - 1; x <= square.TileX + 2; x++)
                for (int y = square.TileY - 1; y <= square.TileY + 2; y++)
                    if (!Push(new(StepKind.Frame, x, y)) || !Drain() || !TryFrameToolWallCell(x, y)) return Reject();
            return IsCurrentOwned;
        }

        private bool TryFrameToolWallCell(int x, int y)
        {
            var size = Overlay.Tiles.Dimensions;
            if (x <= 0 || y <= 0 || x >= size.WidthTiles - 1 || y >= size.HeightTiles - 1) return true;
            if (!Overlay.TryRead(x, y, out var tile) ||
                !VanillaToolTileCatalog1458.TryGetWallFrameMode(tile.WallType, out _)) return false;
            if (tile.Wall == 0)
            {
                var after = tile; after.WallColor = 0;
                after.Flags &= ~(WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall);
                return Stage(WorldTileMutationKind.TransformTile, x, y, in tile, in after);
            }
            // WallFrame's stored cosmetic indices are not WorldTile fields;
            // falseReset consumes no random, but its four neighbor reads remain
            // guarded independently of whether any physical field changes.
            return Overlay.RecordRead(x, y - 1) && Overlay.RecordRead(x - 1, y) &&
                Overlay.RecordRead(x + 1, y) && Overlay.RecordRead(x, y + 1);
        }

        private bool TryFrameToolFurniture(int x, int y, in WorldTile tile)
        {
            if (tile.Type == 137) return true; // No special TileFrame branch.
            if (tile.Type == 424)
                return tile.FrameX % 18 == 0 && tile.FrameY % 18 == 0 ||
                    Push(new(StepKind.Kill, x, y)) && Drain();
            Span<ToolFrameTile1458> window = stackalloc ToolFrameTile1458[9];
            if (tile.Type != 442 || !TryReadFurnitureWindow(x, y, window) ||
                !VanillaToolFurniture1458.TryFramePressurePad(ToFurniture(in tile), window, out short frameX, out bool kill)) return false;
            if (kill) return Push(new(StepKind.Kill, x, y)) && Drain();
            var after = tile; after.FrameX = frameX;
            return Stage(WorldTileMutationKind.TransformTile, x, y, in tile, in after);
        }

        private bool TryReadFurnitureWindow(int x, int y, Span<ToolFrameTile1458> window)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!Overlay.TryReadSourceSafely(x + dx, y + dy, out var tile)) return false;
                    window[(dy + 1) * 3 + dx + 1] = ToFurniture(in tile);
                }
            return true;
        }
        private static ToolFrameTile1458 ToFurniture(in WorldTile tile) =>
            new(tile.TileType, tile.FrameX, tile.FrameY, tile.IsActive, tile.IsActuated, tile.IsHalfBrick, tile.SourceSlope);
    }
}
