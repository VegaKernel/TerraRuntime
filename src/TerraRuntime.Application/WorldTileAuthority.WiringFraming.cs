using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    private bool TryApplyWiringActuator(int x, int y)
    {
        // This source transaction is also used without a human item phase. Missing world/RNG/actor
        // prerequisites reject before the inactive bit becomes visible; there is no raw setter fallback.
        if (tiles is null || !TryCaptureToolTreeWorld(out var context) || context is null ||
            !players.TryCaptureToolTreeActors(out var actors) || actors is null) return false;
        var overlay = new ToolWorldTileOverlay1458(tiles);
        if (!TryCreateToolMutationPlan(overlay, out var plan) || plan is null) return false;
        using (plan)
        {
            if (!TryCreateToolSourcePass(overlay, plan.SourceRandom, false, context.HardMode,
                    context.Facts, actors.Actors, plan, out var source) || source is null ||
                !source.TryStageWiringActuation(x, y) || !plan.PreserveLiquidQueueAfterDryWiringFrames() ||
                !plan.TryStageSourceTileSquare(x, y)) return false;
            var end = plan.CapturePublicationOffset();
            if (!plan.TrySeal() || !context.IsCurrent || !actors.IsCurrent || !source.IsCurrentOwned ||
                !plan.TryAdoptWholeUnpublished(out var publication) || publication is null) return false;
            // Wiring.DeActive/ReActive frame first, then SendTileSquare's default1x1. Recursive
            // source KillTile drops retain their earlier positions in the existing publication journal.
            if (!publication.TryPublishThrough(in end))
                throw new InvalidOperationException("Accepted actuator transaction lost its source publication boundary.");
            return true;
        }
    }

    internal sealed partial class ToolWorldSourcePass1458
    {
        private const ushort WiringTorchTile = 4;
        private const int WiringTorchOffFrame = 66;
        private const int WiringTorchFacingFrame = 22;
        private const int WiringPlatformFramePitch = 18;
        // TileID.Sets.IsBeam1458; this attachment rule is independent of solidity/noAttach.
        private const ushort WiringWoodenBeam = 124, WiringMarbleColumn = 561, WiringBorealBeam = 574;
        private const ushort WiringMahoganyBeam = 575, WiringGraniteColumn = 576, WiringSandstoneColumn = 577, WiringMushroomBeam = 578;
        private bool wiringActuationFrame;

        internal bool TryStageWiringActuation(int x, int y)
        {
            if (!IsCurrentOwned || work.Count != 0 ||
                !Overlay.TryReadSourceSafely(x, y, out var before) || !before.IsActive || !before.HasActuator)
                return Reject();
            var after = before;
            after.TrySetFlags(WorldTileFlags.Inactive, !before.IsActuated);
            if (!Stage(WorldTileMutationKind.TransformTile, x, y, in before, in after)) return Reject();
            // SquareTileFrame(resetFrame:false): source column-major nine calls, including no reset
            // draw at the center. Recursive KillTile calls retain their own default reset behavior.
            for (int dx = 1; dx >= -1; dx--)
            for (int dy = 1; dy >= -1; dy--)
                if (!Push(new(StepKind.Frame, x + dx, y + dy))) return Reject();
            wiringActuationFrame = true;
            try { return Drain() && IsCurrentOwned; }
            finally { wiringActuationFrame = false; }
        }

        private bool FrameWiringTorch(int x, int y, in WorldTile tile)
        {
            Span<WorldTile> cells = stackalloc WorldTile[9];
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (!Overlay.TryReadSourceSafely(x + dx, y + dy, out cells[(dy + 1) * 3 + dx + 1]) ||
                    !VanillaToolTileCatalog1458.TryGet(cells[(dy + 1) * 3 + dx + 1].TileType, out _)) return false;
            WorldTile down = cells[7], left = cells[3], right = cells[5];
            VanillaToolTileCatalog1458.TryGet(down.TileType, out var downFacts);
            VanillaToolTileCatalog1458.TryGet(left.TileType, out var leftFacts);
            VanillaToolTileCatalog1458.TryGet(right.TileType, out var rightFacts);
            bool downTopSlope = down.SourceSlope is 1 or 2;
            int platformFrame = down.FrameX / WiringPlatformFramePitch;
            bool properTop = platformFrame is >= 0 and <= 7 or >= 12 and <= 16 or >= 25 and <= 26;
            bool platformTop = downFacts.IsPlatform && !down.IsActuated &&
                (downFacts.IsSolid || downFacts.IsSolidTop) && !down.IsHalfBrick && (!downTopSlope || properTop);
            bool downCandidate = down.IsActive && (platformTop || !down.IsHalfBrick && !downTopSlope);
            bool leftCandidate = left.IsActive && left.SourceSlope is 0 or 2 or 4;
            bool rightCandidate = right.IsActive && right.SourceSlope is 0 or 1 or 3;
            int facing;
            // CheckTorch intentionally uses active(), unlike switch136's nactive(). Do not make an
            // actuated ordinary solid lose its torch merely because collision became empty.
            if (downCandidate && downFacts.IsSolid && (!downFacts.NoAttach || downFacts.IsPlatform)) facing = 0;
            else if (leftCandidate && (leftFacts.IsSolid && !leftFacts.NoAttach ||
                IsWiringBeam(left.Type) || leftFacts.IsTreeTrunk &&
                IsActiveWiringTree(in cells[0]) && IsActiveWiringTree(in cells[6]))) facing = 1;
            else if (rightCandidate && (rightFacts.IsSolid && !rightFacts.NoAttach ||
                IsWiringBeam(right.Type) || rightFacts.IsTreeTrunk &&
                IsActiveWiringTree(in cells[2]) && IsActiveWiringTree(in cells[8]))) facing = 2;
            else if (tile.Wall > 0) facing = 0;
            else return false; // Full frameY-dependent torch removal/drop is not admitted by this limited pass.
            var after = tile;
            after.FrameX = (short)((tile.FrameX >= WiringTorchOffFrame ? WiringTorchOffFrame : 0) + facing * WiringTorchFacingFrame);
            return after.Equals(tile) || Stage(WorldTileMutationKind.TransformTile, x, y, in tile, in after);
        }

        private static bool IsActiveWiringTree(in WorldTile tile) => tile.IsActive &&
            VanillaToolTileCatalog1458.TryGet(tile.TileType, out var facts) && facts.IsTreeTrunk;

        private static bool IsWiringBeam(ushort type) => type is WiringWoodenBeam or WiringMarbleColumn or
            WiringBorealBeam or WiringMahoganyBeam or WiringGraniteColumn or WiringSandstoneColumn or WiringMushroomBeam;
    }
}
