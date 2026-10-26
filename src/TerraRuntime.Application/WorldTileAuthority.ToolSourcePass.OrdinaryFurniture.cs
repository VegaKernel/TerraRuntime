using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldSourcePass1458
    {
        private static bool IsOrdinaryPlacementFurniture(ushort type) => type is 14 or 15 or 16 or 18;

        // Source Check3x2/Check1x2/Check2x1. This initial paid-placement block
        // admits their base item styles; unknown imported atlas styles retain
        // refusal instead of being mistaken for another material's drop.
        private bool FrameOrdinaryPlacementFurniture(int x, int y, in WorldTile tile)
        {
            if (IsSourceDestroyingObject) return true;
            int width = tile.Type == 14 ? 3 : tile.Type == 15 ? 1 : 2;
            int height = tile.Type is 14 or 15 ? 2 : 1;
            if (tile.FrameX < 0 || tile.FrameY < 0 || tile.FrameX % 18 != 0 || tile.FrameY is not (0 or 18)) return false;
            int baseX = 0, ax = x, ay = y - tile.FrameY / 18;
            if (tile.Type == 15)
            {
                if (tile.FrameX is not (0 or 18)) return false;
                baseX = tile.FrameX;
            }
            else
            {
                if (tile.FrameX >= width * 18 || height == 1 && tile.FrameY != 0) return false;
                ax -= tile.FrameX / 18;
            }
            bool broken = false;
            for (int dx = 0; dx < width; dx++)
            for (int dy = 0; dy < height; dy++)
            {
                if (!Overlay.TryReadSourceSafely(ax + dx, ay + dy, out var part)) return false;
                bool frames = part.Type == tile.Type && part.FrameX == baseX + dx * 18 && part.FrameY == dy * 18;
                // Check1x2 checks the type/atlas, while the other two methods
                // additionally require the represented active footprint.
                broken |= !frames || tile.Type != 15 && !part.IsActive;
            }
            for (int dx = 0; dx < width; dx++)
            {
                if (!Overlay.TryReadSourceSafely(ax + dx, ay + height, out var ground) ||
                    !TerraRuntime.Gameplay.Tiles.VanillaToolTileCatalog1458.TryGet(ground.TileType, out var facts)) return false;
                bool support = ground.IsActive && !ground.IsActuated && !ground.IsHalfBrick &&
                    (facts.IsSolid || facts.IsSolidTop) &&
                    (ground.SourceSlope is not (1 or 2) || facts.IsPlatform && IsProperPlatform(ground.FrameX));
                // The chair's explicit Platforms fallback reads type even when
                // inactive; do not replace it with the ordinary solid predicate.
                broken |= !(support || tile.Type == 15 && facts.IsPlatform);
            }
            if (!broken) return true;
            ushort drop = tile.Type switch { 14 => 32, 15 => 34, 16 => 35, _ => 36 };
            // Tables drop at the Check3x2 caller cell; chair/anvil/workbench at
            // the normalized top-left. All four drop after recursive clears.
            int px = tile.Type == 14 ? x : ax, py = tile.Type == 14 ? y : ay;
            destroyingFurniture = new(ax, ay, width, height, tile.Type, px * 16, py * 16, 32, 32, drop, true);
            return Push(new(StepKind.FurnitureNextCell, ax, ay) { Index = 0 });
        }
    }
}
