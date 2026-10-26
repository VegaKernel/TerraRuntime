using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldSourcePass1458
    {
        internal bool TryPoundTrack(int x, int y, out bool sourceResult, out ToolWorldSourceResult1458 result)
        {
            sourceResult = false; result = default;
            if (work.Count != 0 || !IsCurrentOwned || !Overlay.TryRead(x, y, out var tile) ||
                !tile.IsActive || tile.Type != VanillaMinecartTrackFrame1458.TileType) return Reject();
            int before = mutationOrdinal;
            if (!TryTrackFrame(x, y, in tile, true, out sourceResult) || !Drain()) return Reject();
            result = new(mutationOrdinal != before, !sourceResult, false);
            return IsCurrentOwned;
        }

        private bool FrameTrack(int x, int y, in WorldTile tile) =>
            TryTrackFrame(x, y, in tile, false, out _);

        private bool TryTrackFrame(int x, int y, in WorldTile tile, bool pound, out bool sourceResult)
        {
            sourceResult = false;
            var neighbors = MinecartTrackNeighbors1458.None;
            if (!ReadNeighbor(x - 1, y - 1, MinecartTrackNeighbors1458.UpperLeft) ||
                !ReadNeighbor(x - 1, y, MinecartTrackNeighbors1458.Left) ||
                !ReadNeighbor(x - 1, y + 1, MinecartTrackNeighbors1458.LowerLeft) ||
                !ReadNeighbor(x + 1, y - 1, MinecartTrackNeighbors1458.UpperRight) ||
                !ReadNeighbor(x + 1, y, MinecartTrackNeighbors1458.Right) ||
                !ReadNeighbor(x + 1, y + 1, MinecartTrackNeighbors1458.LowerRight)) return false;
            var input = new MinecartTrackFrameInput1458(tile.FrameX, tile.FrameY, neighbors);
            if (!VanillaMinecartTrackFrame1458.TryPlan(in input, pound, false, out var outcome)) return false;
            var after = tile;
            after.FrameX = outcome.Front; after.FrameY = outcome.Back;
            if (!after.Equals(tile) && !Stage(WorldTileMutationKind.TransformTile, x, y, in tile, in after)) return false;
            sourceResult = outcome.SourceResult;
            return !outcome.InvokeFailedKill || Push(new(StepKind.Kill, x, y, Fail: true));

            bool ReadNeighbor(int tx, int ty, MinecartTrackNeighbors1458 flag)
            {
                if (!Overlay.TryReadSourceSafely(tx, ty, out var neighbor)) return false;
                // Original GetNearbyTilesSetLookupIndex checks TYPE, including inactive saved cells.
                if (neighbor.Type == VanillaMinecartTrackFrame1458.TileType) neighbors |= flag;
                return true;
            }
        }
    }
}
