using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldSourcePass1458
    {
        // The caller supplies source Collision.EmptyTile after a complete player
        // and NPC census. It is not the packet's claim that a cell is empty.
        internal bool TryPlaceSimpleTile(int x, int y, TileTypeId type,
            bool sourceEmpty, out bool placed)
        {
            placed = false;
            if (!IsCurrentOwned || work.Count != 0 ||
                !Overlay.TryReadSourceSafely(x, y, out var before)) return Reject();
            // These retained ordinary solid cells take the default PlaceTile
            // branch. Frame-important objects, plants, conversion, falling and
            // special-seed sand colors attach their own source participants.
            if (type.Value is not (0 or 1 or 30 or 38 or 40)) return Reject();
            if (!sourceEmpty || before.IsActive) return true;
            var after = before;
            after.Type = checked((ushort)type.Value);
            after.FrameX = 0; after.FrameY = 0;
            after.Flags |= WorldTileFlags.Active;
            after.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
            after.TileColor = 0;
            after.ClearSourceShape();
            // PlaceTile Clear(Tile|TilePaint|Slope) preserves wiring, actuator,
            // inactive, liquid and the wall. Do not use normalized placement's
            // broader Inactive reset here.
            if (!Stage(WorldTileMutationKind.PlaceTile, x, y, in before, in after) ||
                !PushSquare(x, y) || !Drain() || !Overlay.TryRead(x, y, out after)) return Reject();
            placed = after.IsActive && after.TileType == type;
            return IsCurrentOwned;
        }

        internal bool TryPlaceSourceWall(int x, int y, WallTypeId wall, out bool placed)
        {
            placed = false;
            if (!IsCurrentOwned || work.Count != 0) return Reject();
            var size = Overlay.Tiles.Dimensions;
            if (x <= 1 || y <= 1 || x >= size.WidthTiles - 2 || y >= size.HeightTiles - 2) return true;
            if (!Overlay.TryReadSourceSafely(x, y, out var before) || wall == VanillaWallIds.None ||
                !VanillaWallDefinitionCatalog.TryGet(wall, out var definition) || !definition.IsPresent) return Reject();
            if (before.Wall != 0) return true;
            var after = before; after.Wall = checked((ushort)wall.Value);
            // Original PlaceWall sets only wall; retained paint/coating is not
            // cleared until the actual framing/painting branch does so.
            if (!Stage(WorldTileMutationKind.PlaceWall, x, y, in before, in after) ||
                !FrameWalls(x, y)) return Reject();
            placed = true;
            return IsCurrentOwned;
        }
    }
}
