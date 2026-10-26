using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldSourcePass1458
    {
        internal static bool IsSourceFallingTile(ushort type) => type is 53 or 112 or 116 or 123 or 224 or 234 or 330 or 331 or 332 or 333 or 495;
        private bool TrySourceFalling(int x, int y, in WorldTile tile, out bool fell)
        {
            fell = false;
            if (server?.SourceProjectiles is not { } store ||
                !Overlay.TryReadSourceSafely(x, y - 1, out var above) ||
                !Overlay.TryReadSourceSafely(x, y + 1, out var below) ||
                !Overlay.TryReadSourceSafely(x, y + 2, out var second) ||
                !VanillaToolTileCatalog1458.TryGet(below.TileType, out var belowFacts) ||
                !VanillaToolTileCatalog1458.TryGet(above.TileType, out var aboveFacts)) return false;
            bool makesFall = !below.IsActive || below.IsActuated ||
                ((!second.IsActive || second.IsActuated) && (!below.IsActive || !belowFacts.IsSolid)) ||
                below.IsActive && below.Type == 165;
            bool blocks = above.IsActive && (aboveFacts.IsBasicChest || aboveFacts.IsFakeBasicChest ||
                above.Type is 323 or 88 or 80 or 77 or 26 or 475 or 470 or 597);
            if (!makesFall || blocks) return true;
            int type = tile.Type switch
            { 112 => 56, 116 => 67, 123 => 71, 224 => 179, 234 => 241, 330 => 411, 331 => 412, 332 => 413, 333 => 414, 495 => 812, _ => 31 };
            if (!VanillaDefinitionCatalog.TryGet(new(type), out var definition) ||
                !server.TryHasSourceFallingDuplicate(new(type), x * 16 + 8, y * 16 + 10, out bool duplicate)) return false;
            var after = tile;
            after.ClearSourceShape();
            after.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive);
            // Source Tile.ClearTile preserves type/frame/paint; inactive framing
            // clears block paint/coating later in the ordered square pass.
            if (!Stage(WorldTileMutationKind.ClearTile, x, y, in tile, in after)) return false;
            if (!duplicate)
            {
                short damage = tile.Type is >= 330 and <= 333 ? (short)0 : (short)10;
                var initial = new ProjectileStateUpdate(new(type), byte.MaxValue,
                    x * 16 + 8 - definition.Width * .5f, y * 16 + 8 - definition.Height * .5f,
                    0, 2.5f, default, 0, damage, 0, 0);
                if (!server.TryStageSourceProjectile(store, in initial, false, out var birth)) return false;
                var final = initial with { VelocityY = .5f, PositionY = initial.PositionY + 2f };
                if (!server.TryFinishSourceProjectile(in birth, in final, out _)) return false;
            }
            fell = true;
            // LIFO: complete recursive source SquareTileFrame before its packet20.
            return Push(new(StepKind.FallingAfterFrame, x, y)) && PushSquare(x, y);
        }
    }
}
