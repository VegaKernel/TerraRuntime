using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal enum ToolWireMatch1458 : byte { Rejected, Incomplete, Complete }
internal readonly record struct ToolWirePublication1458(int ObservationEnd,
    ToolWorldPublicationOffset1458 WorldEffects);

internal sealed partial class WorldTileAuthority
{
    // Detached source steps shared by ordinary picks, combined tools and every
    // shovel leg. The enclosing action still owns clock/range/permission proofs,
    // role contexts, entity/metadata participants and the single joint adoption.
    internal sealed partial class ToolActionSteps1458(WorldTileAuthority owner, ToolWorldSourcePass1458 client,
        ToolWorldSourcePass1458 server, ToolWorldMutationPlan serverPlan, ToolWorldReadPreparation reads,
        VanillaUnifiedRandom1458 localRandom, ToolDamageWorld1458 world,
        ToolHitState1458 initialHits, byte playerSlot, bool hardMode)
    {
        private readonly ToolWireObservation1458[] expected =
            new ToolWireObservation1458[RuntimePlayerToolInput1458.MaximumObservations];
        private readonly ToolWirePublication1458[] publications =
            new ToolWirePublication1458[RuntimePlayerToolInput1458.MaximumObservations];
        private int expectedCount;
        private int publicationCount;
        private int pickCalls;
        private bool wallCall;
        private bool toolTileCall;
        private bool failed;
        private bool sealedSteps;
        private readonly List<RuntimeChestStore.SourceContainerPermission> containerPermissions = [];
        private TerrariaChestUpdatesState1458? expectedChest;
        private TerrariaToolTileSquareState1458? expectedSquare;
        internal TerrariaToolTileSquareState1458? ExpectedSquare => expectedSquare;
        private TerrariaPlaceObjectState? expectedObject;
        private TerrariaLucyAxeMessageState1458? expectedLucy;
        private int expectedLucyIndex = -1;
        internal TerrariaPlaceObjectState? ExpectedObject => expectedObject;
        internal ToolHitState1458 Hits { get; private set; } = initialHits;
        internal bool BlockMouse { get; private set; }
        internal int ExpectedCount => expectedCount;
        internal ReadOnlySpan<ToolWireObservation1458> ExpectedObservations => expected.AsSpan(0, expectedCount);
        internal ReadOnlySpan<ToolWirePublication1458> Publications => publications.AsSpan(0, publicationCount);
        internal ToolWorldPublicationOffset1458 FinalWorldEffects => serverPlan.CapturePublicationOffset();
        internal VanillaUnifiedRandom1458 LocalRandom => localRandom;
        internal PlayerAuthority.ToolSelfHurtPreparation? TileSelfHurt { get; private set; }
        internal bool IsCurrent => !failed && ReferenceEquals(server.Overlay, serverPlan.Overlay) &&
            reads.IsCurrent && client.IsCurrentOwned && server.IsCurrentOwned &&
            (TileSelfHurt is null || TileSelfHurt.IsCurrent) && AreContainerPermissionsCurrent;
        private bool AreContainerPermissionsCurrent
        {
            get
            {
                foreach (var permission in containerPermissions)
                    if (!permission.IsCurrentOwned) return false;
                return true;
            }
        }
        private bool CanStage => !sealedSteps && IsCurrent;

        internal bool TrySeal()
        {
            if (sealedSteps || !IsCurrent) return false;
            sealedSteps = true;
            return true;
        }

        private ToolWorldTileOverlay1458 ClientOverlay => client.Overlay;

        internal bool HasSameSourceResult(ToolActionSteps1458 other)
        {
            if (!IsCurrent || !other.IsCurrent || TileSelfHurt is not null || other.TileSelfHurt is not null ||
                BlockMouse != other.BlockMouse || !LocalRandom.HasSameState(other.LocalRandom) ||
                !SameSquare(expectedSquare, other.expectedSquare) ||
                expectedChest != other.expectedChest || expectedObject != other.expectedObject ||
                expectedLucy != other.expectedLucy || expectedLucyIndex != other.expectedLucyIndex ||
                !ExpectedObservations.SequenceEqual(other.ExpectedObservations) ||
                !Publications.SequenceEqual(other.Publications) ||
                !ClientOverlay.HasSameAfterimage(other.ClientOverlay) ||
                Hits.BufferLocation != other.Hits.BufferLocation) return false;
            for (int index = 0; index < ToolHitState1458.SlotCount; index++)
                if (Hits.GetEntry(index) != other.Hits.GetEntry(index) ||
                    Hits.GetOrder(index) != other.Hits.GetOrder(index)) return false;
            return true;
        }

        internal ToolWireMatch1458 Match(RuntimePlayerToolInput1458 input)
        {
            if (!IsCurrent || !input.IsCurrent || input.Count > expectedCount ||
                input.SquareReport is { } square && !SameSquare(expectedSquare, square) ||
                input.ObjectReport is { } placed && expectedObject != placed ||
                input.LucyReport is { } lucy && (expectedLucy != lucy || input.LucyObservationIndex != expectedLucyIndex) ||
                input.ChestReport is { } chest && expectedChest != chest) return ToolWireMatch1458.Rejected;
            var observations = input.Observations;
            for (int index = 0; index < observations.Length; index++)
                if (observations[index] != expected[index]) return ToolWireMatch1458.Rejected;
            return observations.Length == expectedCount &&
                (expectedSquare is null || input.SquareReport is not null) &&
                (expectedChest is null || input.ChestReport is not null) &&
                (expectedObject is null || input.ObjectReport is not null) &&
                (expectedLucy is null || input.LucyReport is not null)
                ? ToolWireMatch1458.Complete : ToolWireMatch1458.Incomplete;
        }

        private static bool SameSquare(TerrariaToolTileSquareState1458? left, TerrariaToolTileSquareState1458? right) =>
            left is { } a ? right is { } b && TerrariaTileSquareCodec.HasSameToolWire(in a, in b) : right is null;

        private bool TryRotateFurnitureAndReport(int x, int y)
        {
            if (expectedSquare is not null || !client.TryRotateToolFurniture(x, y, out var square)) return Reject();
            if (square is not { } report) return true;
            expectedSquare = report;
            return server.TryApplyToolFurnitureSquare(in report) || Reject();
        }

        internal bool TryShovel(int centerX, int centerY)
        {
            // Player.UseShovel visits columns first. Read each cell only when
            // reached: an earlier KillTile/frame cascade can alter a later cell.
            // DamageTileWithShovel retains its second pick even if the grass
            // stripping pick already removed or transformed that same cell.
            if (!CanStage || centerX < 1 || centerY < 1 ||
                centerX >= world.Width - 1 || centerY >= world.Height - 1) return Reject();
            for (int x = centerX - 1; x <= centerX + 1; x++)
                for (int y = centerY - 1; y <= centerY + 1; y++)
                {
                    if (!client.Overlay.TryRead(x, y, out var tile) ||
                        !VanillaToolTileCatalog1458.TryGet(tile.TileType, out var roles)) return Reject();
                    // Source tests the stored type even on an inactive tile.
                    if (!roles.CanBeDugByShovel) continue;
                    if (tile.IsActive && (roles.ConvertsAsGrass || roles.ConvertsAsJungleGrass ||
                        tile.Type is 70 or 633) && !TryPickTile(x, y, 100)) return false;
                    if (!TryPickTile(x, y, 30)) return false;
                }
            return true;
        }

        internal bool TryPickTile(int x, int y, int pickPower, int damageAsBase = -1)
        {
            // Source shovel has at most18 PickTile calls in one action. No
            // network cell can expand into another independent nine-cell use.
            if (!CanStage || ++pickCalls > 18 ||
                !client.Overlay.TryRead(x, y, out var original)) return Reject();
            if (original.Type == 504) return true;
            if (!Hits.TryHitObject(x, y, 1, out var hit, out int slot)) return Reject();
            Hits = hit;
            Span<ToolTargetTile1458> window = stackalloc ToolTargetTile1458[VanillaToolDamage1458.TargetWindowCount];
            if (!reads.TryCaptureTarget(x, y, client.Overlay, window) ||
                !VanillaToolDamage1458.TryGetPickDamage(pickPower, x, y, window, in world, out var preliminary))
                return Reject();
            if (preliminary.UpdateHitPosition) Hits = Hits.UpdatePosition(slot, preliminary.HitX, preliminary.HitY);
            BlockMouse |= preliminary.BlockMouse;

            bool canKill = false;
            if (original.IsActive)
            {
                if (!VanillaToolWorldRules1458.TryEvaluate(client.Overlay.Tiles, client.Overlay,
                    x, y, hardMode, out var permission)) return Reject();
                // Contents permission has its own concrete captured owner;
                // occupied means known CanKill=false, not missing metadata.
                canKill = permission.CanKill;
                if (permission.RequiresChestPermission)
                {
                    if (!owner.TryCaptureSourceContainerPermission(permission.ChestX, permission.ChestY,
                            out var contents) || contents is null) return Reject();
                    containerPermissions.Add(contents);
                    canKill &= contents.CanDestroy;
                }
            }
            if (!VanillaToolDamage1458.TryFinalizePickDamage(in preliminary, original.TileType,
                canKill, world.GetGoodWorld, true, Hits, slot, pickPower, damageAsBase, out int damage) ||
                !Hits.TryAddDamage(slot, damage, true, out hit, out int accumulated, out int samples)) return Reject();
            Hits = hit;
            for (int index = 0; index < samples; index++) localRandom.NextDouble();

            if (accumulated >= 100)
            {
                // Player.ClearMiningCacheAt clears/prunes BEFORE WorldGen.KillTile,
                // then PickTile prunes again after the call when damage!=0.
                if (!TryClearMiningCache(x, y)) return false;
                // Main.tileContainer is exactly these five1458 identities.
                // Its CLIENT17fail→34 /20 chronology belongs to the complete
                // object action participant, not the ordinary successful-kill path.
                if (original.Type is 21 or 467 or 88)
                {
                    if (expectedChest is not null ||
                        !client.TryKillTile(x, y, true, false, false, out _) ||
                        !AppendTile(0, x, y, 1) || !TryServerKillTile(x, y, true, expectedCount)) return Reject();
                    byte action = original.Type == 21 ? (byte)1 : original.Type == 467 ? (byte)5 : (byte)3;
                    expectedChest = new(action, (short)x, (short)y, 0, 0);
                    if (!server.TryBreakContainer(x, y, playerSlot, out _)) return Reject();
                }
                else if (original.Type is 470 or 475) return Reject();
                else if (!client.TryKillTile(x, y, false, false, false, out _) ||
                    !AppendTile(0, x, y, 0) ||
                    !TryServerKillTile(x, y, false, expectedCount)) return Reject();
            }
            else
            {
                if (!client.TryKillTile(x, y, true, false, false, out _) ||
                    !AppendTile(0, x, y, 1) ||
                    !AppendPicking(x, y, damage) ||
                    !TryServerKillTile(x, y, true, expectedCount - 1)) return Reject();
            }
            return damage == 0 || TryPrune();
        }

        internal bool TryHitWallAt(int sourceX, int sourceY, bool mouseXBelowRoundedTile,
            bool mouseYBelowRoundedTile, bool poundRelease, int hammerPower)
        {
            if (!CanStage || !TryFindWallTarget(sourceX, sourceY, mouseXBelowRoundedTile,
                    mouseYBelowRoundedTile, out var target) ||
                !client.Overlay.TryRead(target.X, target.Y, out var tile) || tile.Wall == 0 ||
                !VanillaToolTileCatalog1458.TryGet(tile.TileType, out var roles) ||
                tile.IsActive && target.X == sourceX && target.Y == sourceY &&
                    (roles.IsHammerTarget || poundRelease)) return Reject();
            return TryPickWall(target.X, target.Y, hammerPower);
        }

        internal bool TryPickWall(int x, int y, int hammerPower)
        {
            // Reach and cursor-neighbor choice belong to the enclosing
            // admission. PickWall itself has no packet125 report.
            if (!CanStage || wallCall ||
                !TryCanSmashWall(x, y, out bool canSmash) || !canSmash ||
                !VanillaToolDamage1458.TryGetWallDamage(hammerPower, out int damage) ||
                !Hits.TryHitObject(x, y, 2, out var hit, out int slot)) return Reject();
            wallCall = true;
            Hits = hit;
            if (!Hits.TryAddDamage(slot, damage, true, out hit, out int accumulated,
                out int samples)) return Reject();
            Hits = hit;
            for (int index = 0; index < samples; index++) localRandom.NextDouble();
            bool fail = accumulated < 100;
            if (!fail)
            {
                // Preserve the explicit wall Clear first. Source Player's
                // ClearMiningCacheAt ignores its cache-type argument and clears
                // TILE-kind1 at these coordinates, even when called by PickWall.
                Hits = Hits.Clear(slot);
                if (!TryClearMiningCache(x, y)) return false;
            }
            if (!client.TryKillWall(x, y, fail, out _) ||
                !AppendTile(2, x, y, fail ? (short)1 : (short)0) ||
                !server.TryKillWall(x, y, fail, out _) ||
                !RecordPublication(expectedCount)) return Reject();
            return damage == 0 || TryPrune();
        }

        internal bool TryCompleteToolTileHit(ToolTileHitPreparation1458 hit,
            in RuntimePlayerToolStagePose1458 pose, RuntimePlayerToolInput1458? input)
        {
            // Construct the source-role passes AFTER projecting hit.SelfHurt
            // into their captured actor census. This method never applies HP.
            if (!CanStage || toolTileCall || !hit.IsCurrent ||
                !ReferenceEquals(Hits, hit.Hits) || !ReferenceEquals(localRandom, hit.LocalRandom) ||
                !client.Overlay.TryRead(hit.X, hit.Y, out var before) || !before.Equals(hit.Before)) return Reject();
            toolTileCall = true;
            TileSelfHurt = hit.SelfHurt;
            bool fail = hit.Accumulated < 100;
            if (!TryStageLucyMessage(hit, in pose, input)) return Reject();
            if (!fail && !TryClearMiningCache(hit.X, hit.Y)) return false;
            if (!client.TryKillTile(hit.X, hit.Y, fail, false, false, out _) ||
                !AppendTile(0, hit.X, hit.Y, fail ? (short)1 : (short)0) ||
                !TryServerKillTile(hit.X, hit.Y, fail, expectedCount)) return Reject();
            if (hit.RequiresTreeReplant && !TryReplantAfterTree(hit.X, hit.Y,
                (pose.Player.ControlFlags & 64) != 0 ? 1 : -1, input?.ObjectReport))
                return Reject();
            // Unlike PickTile, axe and hammer target branches never send125.
            return hit.Damage == 0 || TryPrune();
        }

        internal bool TryCanSmashWall(int x, int y, out bool canSmash)
        {
            canSmash = false;
            // Read the CLIENT working world, including earlier mutations in
            // this same action. Reading live tiles here loses those changes.
            if (!CanStage || !client.Overlay.TryRead(x, y, out var target)) return Reject();
            if (target.Wall is 0 or 350) return true;
            if (!VanillaWallDefinitionCatalog.TryGet(target.WallType, out var definition)) return Reject();
            if (definition.IsHousingWall) { canSmash = true; return true; }
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (!client.Overlay.TryRead(x + dx, y + dy, out var neighbor)) return Reject();
                    if (neighbor.Wall == 0) { canSmash = true; return true; }
                    if (!VanillaWallDefinitionCatalog.TryGet(neighbor.WallType, out var neighborDefinition))
                        return Reject();
                    if (neighborDefinition.IsHousingWall) { canSmash = true; return true; }
                }
            return true;
        }

        internal bool TryFindWallTarget(int targetX, int targetY,
            bool mouseXBelowRoundedTile, bool mouseYBelowRoundedTile,
            out ToolWireCell1458 target)
        {
            // The caller must supply a certified cursor quadrant or enumerate
            // its bounded possible preimages. Packet13 contains no cursor.
            // Wall-edge selection is distinct from CanPlayerSmashWall: a
            // DIFFERENT unsafe wall qualifies here, but may still not smash.
            target = new(targetX, targetY);
            if (!CanStage || !client.Overlay.TryRead(targetX, targetY, out var first) ||
                !TryIsWallBoundary(targetX, targetY, in first, out bool boundary)) return Reject();
            if (boundary || first.IsActive) return true;
            int left = targetX + (mouseXBelowRoundedTile ? 0 : -1);
            int top = targetY + (mouseYBelowRoundedTile ? 0 : -1);
            for (int x = left; x <= left + 1; x++)
                for (int y = top; y <= top + 1; y++)
                {
                    target = new(x, y);
                    if (!client.Overlay.TryRead(x, y, out var tile) ||
                        !TryIsWallBoundary(x, y, in tile, out boundary)) return Reject();
                    if (boundary) return true;
                }
            // Source retains the last inspected coordinate even if none of
            // these four candidates exposes an edge; the later gate decides.
            return true;
        }

        private bool TryIsWallBoundary(int x, int y, in WorldTile tile, out bool boundary)
        {
            boundary = false;
            if (tile.Wall == 0) return true;
            if (!VanillaWallDefinitionCatalog.TryGet(tile.WallType, out var definition)) return false;
            if (definition.IsHousingWall) { boundary = true; return true; }
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (!client.Overlay.TryRead(x + dx, y + dy, out var neighbor)) return false;
                    if (neighbor.Wall != tile.Wall) { boundary = true; return true; }
                }
            return true;
        }

        private bool TryClearMiningCache(int x, int y)
        {
            int slot = Hits.Find(x, y, 1);
            if (slot < 0) return true;
            Hits = Hits.Clear(slot);
            return TryPrune();
        }

        private bool TryServerKillTile(int x, int y, bool fail, int observationEnd) =>
            server.TryKillTile(x, y, fail, false, false, out _) && RecordPublication(observationEnd);

        private bool RecordPublication(int observationEnd)
        {
            // After joint adoption, drain these world effects before publishing
            // the corresponding packet17. A following125 belongs AFTER17,
            // even though both observations were matched in the same cohort.
            if (!IsCurrent || publicationCount == publications.Length ||
                observationEnd <= 0 || observationEnd > expectedCount ||
                expected[observationEnd - 1].Tile is null ||
                publicationCount > 0 && observationEnd <= publications[publicationCount - 1].ObservationEnd)
                return Reject();
            publications[publicationCount++] = new(observationEnd, serverPlan.CapturePublicationOffset());
            return true;
        }

        internal bool TryPrune()
        {
            Span<bool> presence = stackalloc bool[ToolHitState1458.SlotCount];
            if (!CanStage || !reads.TryCapturePrune(Hits, client.Overlay, presence) ||
                !Hits.TryPrune(presence, out var after)) return Reject();
            Hits = after;
            return true;
        }

        private bool AppendTile(byte action, int x, int y, short data)
        {
            if (expectedCount == expected.Length || x is < 0 or > short.MaxValue ||
                y is < 0 or > short.MaxValue) return Reject();
            expected[expectedCount++] = new(new TerrariaTileManipulationState(action, (short)x, (short)y, data, 0), null);
            return true;
        }

        private bool AppendPicking(int x, int y, int damage)
        {
            if (expectedCount == expected.Length || damage is < 0 or > byte.MaxValue) return Reject();
            expected[expectedCount++] = new(null, new TerrariaTilePickingState1458(playerSlot, (short)x, (short)y, (byte)damage));
            return true;
        }

        private bool Reject() { failed = true; return false; }
    }
}
