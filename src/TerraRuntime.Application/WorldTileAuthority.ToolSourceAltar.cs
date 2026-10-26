using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldSourcePass1458
    {
        // Only the intact failed-hit frame path. Successful altar destruction,
        // ore blessing/progression and unrelated 3x2 objects are separate owners.
        private bool TryVerifyIntactAltar(int x, int y, in WorldTile tile)
        {
            if (IsSourceDestroyingObject) return true;
            if (tile.Type != 26 || tile.FrameX < 0 || tile.FrameY < 0 ||
                tile.FrameX % 18 != 0 || tile.FrameY % 18 != 0) return false;
            int left = x - tile.FrameX / 18 % 3, top = y - tile.FrameY % 36 / 18;
            int frameX = tile.FrameX / 54 * 54, frameY = tile.FrameY / 36 * 36;
            for (int dx = 0; dx < 3; dx++)
            {
                for (int dy = 0; dy < 2; dy++)
                    if (!Overlay.TryReadSourceSafely(left + dx, top + dy, out var cell) || !cell.IsActive ||
                        cell.Type != 26 || cell.FrameX != frameX + dx * 18 || cell.FrameY != frameY + dy * 18) return false;
                if (!Overlay.TryReadSourceSafely(left + dx, top + 2, out var support) ||
                    !VanillaToolTileCatalog1458.TryGet(support.TileType, out var facts)) return false;
                int platformFrame = support.FrameX / 18;
                bool properPlatform = facts.IsPlatform && (platformFrame is >= 0 and <= 7 or >= 12 and <= 16 or >= 25 and <= 26);
                if (!support.IsActive || (!facts.IsSolid && !facts.IsSolidTop) || facts.IsBoulder ||
                    support.IsHalfBrick || support.IsActuated || (support.SourceSlope is 1 or 2 && !properPlatform)) return false;
            }
            return true;
        }
    }
}
