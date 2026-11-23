using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldSourcePass1458
    {
        private static bool IsOrdinaryPlacementFurniture(ushort type) => type is 14 or 15 or 16 or 18;

        private static bool TryGetOrdinaryFurnitureFrame(in WorldTile tile, bool allowObsidian,
            out OrdinaryFurnitureFrame frame)
        {
            frame = default;
            if (!IsOrdinaryPlacementFurniture(tile.Type) || tile.FrameX < 0 || tile.FrameX % 18 != 0 || tile.FrameY < 0)
                return false;
            int width = tile.Type == 14 ? 3 : tile.Type == 15 ? 1 : 2;
            int height = tile.Type is 14 or 15 ? 2 : 1;
            int style = tile.Type == 15 ? tile.FrameY / 40 : tile.FrameX / (width * 18);
            int baseX = tile.Type == 15 ? tile.FrameX : style * width * 18;
            int baseY = tile.Type == 15 ? style * 40 : 0;
            int localY = tile.FrameY - baseY;
            if (tile.Type == 15 && tile.FrameX is not (0 or 18) || localY != 0 && (height != 2 || localY != 18))
                return false;
            // WorldGen.Check3x2, Check1x2 and Check2x1 retain separate style
            // strides. The lava journal admits the obsidian Underworld styles.
            ushort drop = (tile.Type, style) switch
            {
                (14, 0) => 32,
                (15, 0) => 34,
                (16, 0) => 35,
                (18, 0) => 36,
                (14, 13) when allowObsidian => 1460,
                (15, 16) when allowObsidian => 1459,
                (18, 14) when allowObsidian => 1461,
                _ => 0
            };
            if (drop == 0) return false;
            frame = new(width, height, baseX, baseY, drop);
            return true;
        }

        private bool FrameOrdinaryPlacementFurniture(int x, int y, in WorldTile tile)
        {
            if (IsSourceDestroyingObject) return true;
            if (!TryGetOrdinaryFurnitureFrame(in tile, IsLavaDeathFraming, out var frame)) return false;
            int ax = x - (tile.FrameX - frame.BaseX) / 18;
            int ay = y - (tile.FrameY - frame.BaseY) / 18;
            int width = frame.Width, height = frame.Height;
            bool broken = false;
            for (int dx = 0; dx < width; dx++)
            for (int dy = 0; dy < height; dy++)
            {
                if (!Overlay.TryReadSourceSafely(ax + dx, ay + dy, out var part)) return false;
                bool frames = part.Type == tile.Type && part.FrameX == frame.BaseX + dx * 18 && part.FrameY == frame.BaseY + dy * 18;
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
            // Tables drop at the Check3x2 caller cell; chair/anvil/workbench at
            // the normalized top-left. All four drop after recursive clears.
            int px = tile.Type == 14 ? x : ax, py = tile.Type == 14 ? y : ay;
            destroyingFurniture = new(ax, ay, width, height, tile.Type, px * 16, py * 16, 32, 32, frame.DropItem, true);
            return Push(new(StepKind.FurnitureNextCell, ax, ay) { Index = 0 });
        }

        private readonly record struct OrdinaryFurnitureFrame(int Width, int Height, int BaseX, int BaseY, ushort DropItem);
    }
}
