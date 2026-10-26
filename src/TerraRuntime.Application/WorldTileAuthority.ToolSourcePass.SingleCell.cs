using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldSourcePass1458
    {
        private bool FrameSingleCell(int x, int y, in WorldTile tile)
        {
            Span<ToolSingleCellFrameTile1458> window = stackalloc ToolSingleCellFrameTile1458[9];
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                if (!Overlay.TryReadSourceSafely(x + dx, y + dy, out var cell)) return false;
                window[(dy + 1) * 3 + dx + 1] = new(cell.TileType, cell.FrameX, cell.IsActive,
                    cell.IsActuated, cell.IsHalfBrick, cell.SourceSlope);
            }
            if (!VanillaToolSingleCellFrame1458.TryPlan(tile.TileType, tile.FrameX, tile.Wall,
                window, out short frame, out bool kill)) return false;
            if (kill) return Push(new(StepKind.Kill, x, y)) && Drain();
            var after = tile; after.FrameX = frame;
            return after.Equals(tile) || Stage(WorldTileMutationKind.TransformTile, x, y, in tile, in after);
        }
    }
}
