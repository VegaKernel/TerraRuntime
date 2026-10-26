using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct ToolWallCursor1458(bool MouseXBelowRoundedTile, bool MouseYBelowRoundedTile);

internal sealed partial class WorldTileAuthority
{
    // One detached candidate for one possible tool target. A shovel receipt may
    // have several possible centers; the ingress owner chooses one complete
    // result after comparing equivalent candidates, never adopts each center.
    internal bool TryPrepareToolActionAt(PlayerToolAttemptPreparation player, int x, int y,
        out ToolActionPreparation1458? preparation, ToolWallCursor1458? wallCursor = null)
    {
        preparation = null;
        var capture = player.Capture;
        if (tiles is null || !player.IsCurrent || !capture.HasCurrentSourceStage ||
            player.Before.ToolStagePending || player.Before.RequiresFreshUse ||
            player.Before.SourceStagePose is not { } pose ||
            player.Before.BuildingPermission is not { IsCurrent: true } building ||
            !players.TryCaptureToolReach(capture, out var reach) ||
            !VanillaToolCatalog1458.TryResolve(capture.Selected.ItemType, capture.Selected.Prefix,
                capture.World.RemixWorld, capture.World.WindowsItemPrefixArithmetic, out var tool) ||
            !capture.World.TryCaptureToolDamageWorld(out var damageWorld) ||
            CreateToolReadPreparation() is not { } reads || !reads.TryReadBefore(x, y, out var before) ||
            !VanillaToolTileCatalog1458.TryGet(before.TileType, out var roles)) return false;

        ToolActionKind1458 kind;
        if (wallCursor is not null)
        {
            // ActuallyUseMiningTool suppresses wall work when this same
            // source target already selects the item's tile-damage branch.
            if (tool.Hammer <= 0 || before.IsActive &&
                (roles.IsHammerTarget || roles.IsAxeTarget && tool.Axe > 0 ||
                 !roles.IsAxeTarget && !roles.IsHammerTarget && tool.Pick > 0)) return false;
            kind = ToolActionKind1458.Wall;
        }
        else if (tool.Item.Value == 4711) kind = ToolActionKind1458.Shovel;
        else if (before.IsActive && roles.IsHammerTarget && tool.Hammer > 0) kind = ToolActionKind1458.Hammer;
        else if (before.IsActive && !roles.IsHammerTarget && roles.IsAxeTarget && tool.Axe > 0)
            kind = ToolActionKind1458.Axe;
        else if (before.IsActive && !roles.IsHammerTarget && !roles.IsAxeTarget && tool.Pick > 0)
            kind = ToolActionKind1458.Pick;
        else if (before.IsActive && tool.Hammer > 0 && VanillaToolDamage1458.IsTilePoundable(before.TileType))
            kind = ToolActionKind1458.Pound;
        else return false; // Wall/pound/object actions attach their distinct source participants.

        var startingClocks = player.Before.Clocks;
        bool qualifiedPound = kind != ToolActionKind1458.Wall && tool.Hammer > 0 && before.IsActive &&
            VanillaToolDamage1458.IsTilePoundable(before.TileType) &&
            player.TryQualifyPoundRelease(x, y, out startingClocks);
        if (!qualifiedPound)
        {
            startingClocks = player.Before.Clocks;
            if (kind == ToolActionKind1458.Pound || startingClocks.PoundRelease && tool.Hammer > 0 &&
                before.IsActive && VanillaToolDamage1458.IsTilePoundable(before.TileType)) return false;
        }
        var facts = new ToolActionClockFacts1458(kind, (pose.Player.ControlFlags & 32) != 0,
            building.NoBuilding, reach.Contains(in pose, x, y), before.IsActive,
            VanillaToolDamage1458.IsAHammerTarget(before.TileType),
            player.Before.Clocks.Selected.ReleaseUseItem, capture.PickSpeed, 1f);
        if (!VanillaToolUse1458.TryPrepareTarget(startingClocks, in facts,
                out var clocks, out bool eligible) || !eligible) return false;

        var localRandom = player.CreateActionRandomPreview();
        ToolTileHitPreparation1458? hit = null;
        if (kind is ToolActionKind1458.Axe or ToolActionKind1458.Hammer &&
            (!TryPrepareToolTileHit(player, localRandom, x, y, out hit) || hit is null)) return false;
        var initialHits = hit?.Hits ?? player.Before.Hits;
        int hitSlot = hit?.HitSlot ?? -1;
        if (hit is null && before.IsActive && kind != ToolActionKind1458.Shovel &&
            !initialHits.TryHitObject(x, y, 1, out initialHits, out hitSlot)) return false;
        if (!TryCaptureToolTreeWorld(capture, out var treeWorld) || treeWorld is null ||
            !players.TryCaptureToolTreeActors(out var actors) || actors is null ||
            hit?.SelfHurt is { } hurt && !actors.TryProjectSelfHurt(hurt)) return false;

        var localTiles = new ToolWorldTileOverlay1458(tiles);
        var serverTiles = new ToolWorldTileOverlay1458(tiles);
        if (!TryCreateToolMutationPlan(serverTiles, out var worldPlan) || worldPlan is null) return false;
        bool handedOff = false;
        try
        {
            bool hardMode = hardModeBaseline || progression.IsCompleted(VanillaWorldProgressionId.Hardmode);
            if (!TryCreateToolSourcePass(localTiles, localRandom, true, hardMode,
                    treeWorld.Facts, actors.Actors, null, out var local) || local is null ||
                !TryCreateToolSourcePass(serverTiles, worldPlan.SourceRandom, false, hardMode,
                    treeWorld.Facts, actors.Actors, worldPlan, out var server) || server is null) return false;
            var steps = new ToolActionSteps1458(this, local, server, worldPlan, reads, localRandom, damageWorld,
                initialHits, capture.Connection.Player.Slot.Value, hardMode);
            bool applied = kind switch
            {
                ToolActionKind1458.Shovel => steps.TryShovel(x, y),
                ToolActionKind1458.Pick => steps.TryPickTile(x, y, tool.Pick),
                ToolActionKind1458.Pound => true,
                ToolActionKind1458.Wall => wallCursor is { } cursor && steps.TryHitWallAt(x, y,
                    cursor.MouseXBelowRoundedTile, cursor.MouseYBelowRoundedTile,
                    VanillaToolUse1458.CompleteTileStage(clocks, in facts).PoundRelease, tool.Hammer),
                _ => hit is not null && steps.TryCompleteToolTileHit(hit, in pose, capture.Phase.ToolUse?.Input)
            };
            if (!applied || kind != ToolActionKind1458.Pound && !VanillaToolUse1458.TryApplyActionTime(tool, clocks, kind,
                    capture.PickSpeed, capture.World.WindowsItemPrefixArithmetic, out clocks)) return false;
            bool consumedRelease = false;
            if (qualifiedPound)
            {
                if (!steps.TryPoundAt(x, y, hitSlot, out bool performed, out consumedRelease)) return false;
                if (performed && !VanillaToolUse1458.TryApplyActionTime(tool, clocks, ToolActionKind1458.Pound,
                        capture.PickSpeed, capture.World.WindowsItemPrefixArithmetic, out clocks)) return false;
                if (consumedRelease) clocks = clocks with { PoundRelease = false };
            }
            // CompleteTileStage is separate from wall cadence and may enable
            // the next release. Preserve the existing local source clock order.
            clocks = VanillaToolUse1458.CompleteTileStage(clocks, in facts);
            PlayerAuthority.ToolShapePlayerPreparation? shape = null;
            if (!worldPlan.ShapeEffects.IsEmpty &&
                (hit?.SelfHurt is not null ||
                 !players.TryPrepareToolShapeEffects(worldPlan.ShapeEffects, out shape) || shape is null)) return false;
            var candidate = new ToolActionPreparation1458(this, player, worldPlan, steps, treeWorld,
                actors, building, clocks, hit, shape, consumedRelease,
                !before.IsActive || !VanillaToolDamage1458.IsAHammerTarget(before.TileType));
            if (!candidate.IsCurrent) return false;
            preparation = candidate;
            handedOff = true;
            return true;
        }
        finally { if (!handedOff) worldPlan.Dispose(); }
    }

    internal sealed class ToolActionPreparation1458(WorldTileAuthority owner, PlayerToolAttemptPreparation player,
        ToolWorldMutationPlan world, ToolActionSteps1458 steps, ToolTreeWorldCapture1458 treeWorld,
        PlayerAuthority.ToolTreeActorsPreparation actors, RuntimeToolBuildingCapture1458 building,
        ToolClockState1458 clocks, ToolTileHitPreparation1458? hit,
        PlayerAuthority.ToolShapePlayerPreparation? shape, bool consumedRelease, bool invalidateRelease) : IDisposable
    {
        private bool sealedPlan;
        private bool adopted;
        private bool disposed;
        private bool publishing;
        private bool healthPublished;
        private bool shapePublished;
        private bool objectPublished;
        private bool squarePublished;
        private int nextObservation;
        private int nextBoundary;
        private readonly ToolWireObservation1458[] observations = steps.ExpectedObservations.ToArray();
        private readonly ToolWirePublication1458[] boundaries = steps.Publications.ToArray();
        private readonly WorldTile[] acceptedTiles = new WorldTile[steps.ExpectedCount];
        private readonly ToolWorldPublicationOffset1458 finalWorldEffects = steps.FinalWorldEffects;
        private readonly TerrariaPlaceObjectState? expectedObject = steps.ExpectedObject;
        private readonly TerrariaToolTileSquareState1458? expectedSquare = steps.ExpectedSquare;
        private readonly WorldTile[] squareFootprint = new WorldTile[36];
        private WorldTile acceptedSquare;
        private WorldTile objectUpper, objectLower;
        internal ToolActionSteps1458 Steps => steps;
        internal PlayerToolAttemptPreparation Player => player;
        internal ToolWorldPublication1458? Publication { get; private set; }
        internal PlayerAuthority.ToolSelfHurtPreparation? SelfHurt => hit?.SelfHurt;
        private ToolWorldMutationPlan WorldPlan => world;
        private ToolClockState1458 ResultClocks => clocks;
        private bool ConsumedRelease => consumedRelease;
        private bool InvalidatedRelease => invalidateRelease;
        internal bool IsCurrent => !disposed && !adopted && player.IsCurrent && world.IsCurrentOwned &&
            steps.IsCurrent && treeWorld.IsCurrent && actors.IsCurrent && building.IsCurrent &&
            (hit is null || hit.IsCurrent) && (shape is null || shape.IsCurrent);

        internal ToolWireMatch1458 Match(RuntimePlayerToolInput1458 input) =>
            IsCurrent ? steps.Match(input) : ToolWireMatch1458.Rejected;

        internal bool HasSameSourceResult(ToolActionPreparation1458 other) =>
            IsCurrent && other.IsCurrent && ReferenceEquals(player.Capture, other.Player.Capture) &&
            clocks == other.ResultClocks && consumedRelease == other.ConsumedRelease &&
            invalidateRelease == other.InvalidatedRelease && steps.HasSameSourceResult(other.Steps) &&
            world.HasSameSourceEffects(other.WorldPlan);

        internal bool TrySeal(RuntimePlayerToolInput1458 input)
        {
            if (sealedPlan || Match(input) != ToolWireMatch1458.Complete ||
                !world.TrySeal() || !IsCurrent ||
                !TryCaptureAcceptedTiles() || !steps.TrySeal() ||
                hit?.SelfHurt is { } hurt && !hurt.TrySealForJointAdoption() ||
                shape is not null && !shape.TrySealForJointAdoption(player.Capture.Connection.Player) ||
                !player.TryStageSourceResult(clocks, steps.Hits, true, steps.LocalRandom,
                    consumePoundRelease: consumedRelease, invalidatePoundRelease: invalidateRelease) || !IsCurrent)
                return false;
            sealedPlan = true;
            return true;
        }

        private bool TryCaptureAcceptedTiles()
        {
            for (int index = 0; index < observations.Length; index++)
                if (observations[index].Tile is { } tile &&
                    !world.Overlay.TryRead(tile.TileX, tile.TileY, out acceptedTiles[index])) return false;
            if (expectedSquare is { } square)
            {
                if (!world.Overlay.TryRead(square.TileX, square.TileY, out acceptedSquare)) return false;
                int at = 0;
                for (int x = square.TileX - 2; x <= square.TileX + 3; x++)
                    for (int y = square.TileY - 2; y <= square.TileY + 3; y++)
                        if (!world.Overlay.TryRead(x, y, out squareFootprint[at++])) return false;
            }
            if (expectedObject is { } placed &&
                (!world.Overlay.TryRead(placed.TileX, placed.TileY - 1, out objectUpper) ||
                 !world.Overlay.TryRead(placed.TileX, placed.TileY, out objectLower))) return false;
            return true;
        }

        internal bool TryAdoptUnpublished()
        {
            // External owner/provider guards ran during Seal. This tail queries
            // only owned state and invokes no observers between participants.
            if (!sealedPlan || !IsCurrent || !player.TryAdoptUnpublished()) return false;
            if (hit?.SelfHurt is { } hurt && !hurt.TryAdoptUnpublished())
                throw new InvalidOperationException("Prepared tool self-hurt changed during joint adoption.");
            if (!world.TryAdoptWholeUnpublished(out var publication) || publication is null)
                throw new InvalidOperationException("Prepared tool world changed during joint adoption.");
            if (shape is not null && !shape.TryAdoptUnpublished())
                throw new InvalidOperationException("Prepared tool shape players changed during joint adoption.");
            Publication = publication;
            adopted = true;
            // Account accepted source17 calls only after the entire joint image
            // exists. Neither speculative cursor candidates nor packet125 count.
            for (int index = 0; index < observations.Length; index++)
            {
                if (observations[index].Tile is not { } tile) continue;
                owner.AppliedClientManipulations++;
                if (tile.Action != (byte)TerrariaTileManipulationAction.KillTile || tile.Data != 0 ||
                    acceptedTiles[index].IsActive ||
                    player.Before.SourceDefinition is not { } definition ||
                    definition.Pick <= 0 && definition.Item.Value != 4711 ||
                    player.Before.SourceStagePose is not { } sourcePose ||
                    owner.tiles?.WorldSurfaceTiles is not double surface) continue;
                var pose = sourcePose.Player;
                if (pose.PositionY > surface * 16d && tile.TileY > surface &&
                    Math.Abs(tile.TileX * 16f + 8f - (pose.PositionX + 10f)) <= 96f &&
                    Math.Abs(tile.TileY * 16f + 8f - (pose.PositionY + 21f)) <= 64f)
                    owner.observedMining[player.Capture.Connection.Player] =
                        (owner.currentTick, tile.TileX, tile.TileY);
            }
            if (!player.TryRebaseOtherToolInputs())
                throw new InvalidOperationException("Prepared tool RNG lineage changed during joint adoption.");
            return true;
        }

        internal bool TryPublishAll()
        {
            if (!adopted || disposed || publishing || Publication is not { } publication) return false;
            publishing = true;
            Exception? failure = null;
            try
            {
                if (!healthPublished)
                {
                    healthPublished = true;
                    // The source Hurt notifications precede WorldGen outcomes.
                    // Each health notification consumes its own cursor before
                    // invoking an observer; retain the first thrown exception.
                    if (SelfHurt is { } hurt)
                        for (int attempt = 0; attempt < 3; attempt++)
                            try { if (!hurt.TryPublishNext()) break; }
                            catch (Exception exception) { failure ??= exception; }
                }
                while (nextObservation < observations.Length)
                {
                    int index = nextObservation++;
                    if (nextBoundary < boundaries.Length && boundaries[nextBoundary].ObservationEnd == index + 1)
                    {
                        var boundary = boundaries[nextBoundary++];
                        try { publication.TryPublishThrough(boundary.WorldEffects); }
                        catch (Exception exception) { failure ??= exception; }
                    }
                    try
                    {
                        if (observations[index].Tile is { } tile)
                        {
                            if (!shapePublished && shape is not null && tile.Action is 7 or 14 or 23)
                            {
                                shapePublished = true;
                                for (int attempt = 0; attempt < byte.MaxValue; attempt++)
                                    try { if (!shape.TryPublishNext()) break; }
                                    catch (Exception exception) { failure ??= exception; }
                            }
                            // Child observers may reenter and change this cell.
                            // Compare its accepted value, not the section's
                            // version: unrelated edits must not suppress17.
                            if (owner.tiles is not { } tiles ||
                                !tiles.Get(tile.TileX, tile.TileY).Equals(acceptedTiles[index]))
                            {
                                if (owner.tiles is { } current)
                                    owner.replication?.TryPublishTileSquareToAll(current, tile.TileX, tile.TileY);
                                continue;
                            }
                            owner.TryPublishToolTile(player.Capture.Connection.Source,
                                player.Capture.Connection.Player, in tile);
                        }
                        else if (observations[index].Picking is { } picking)
                            owner.replication?.TryPublishToolPicking(player.Capture.Connection.Source, in picking);
                    }
                    catch (Exception exception) { failure ??= exception; }
                }
            }
            finally
            {
                try { publication.TryPublishThrough(finalWorldEffects); }
                catch (Exception exception) { failure ??= exception; }
                try { TryPublishFurniture(); }
                catch (Exception exception) { failure ??= exception; }
                try { TryPublishReplant(); }
                catch (Exception exception) { failure ??= exception; }
                publishing = false;
            }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            return true;
        }

        private bool TryPublishFurniture()
        {
            if (squarePublished || expectedSquare is not { } square) return false;
            squarePublished = true; // Consume before currentness checks and observers.
            if (owner.tiles is not { } tiles) return false;
            int at = 0;
            for (int x = square.TileX - 2; x <= square.TileX + 3; x++)
                for (int y = square.TileY - 2; y <= square.TileY + 3; y++)
                    if (!tiles.Get(x, y).Equals(squareFootprint[at++]))
                    {
                        owner.replication?.TryPublishTileSquareToAll(tiles, square.TileX, square.TileY);
                        return false;
                    }
            // Relay the actual owned SERVER afterimage. RangeFrame may have
            // corrected442 or killed invalid furniture. CLIENT liquid claims
            // were never applied; this image carries current owned liquid.
            var report = new TerrariaToolTileSquareState1458(square.TileX, square.TileY, acceptedSquare);
            return owner.replication?.TryPublishToolFurnitureSquare(player.Capture.Connection.Source, tiles, in report) == true;
        }

        private bool TryPublishReplant()
        {
            if (objectPublished || expectedObject is not { } placed) return false;
            objectPublished = true;
            // Source79 follows its preceding17 cuts. Earlier drop/tree observers
            // can reenter: publish only while the accepted two-cell footprint
            // remains current, even when source server placement itself failed.
            if (owner.tiles is not { } tiles ||
                !tiles.Get(placed.TileX, placed.TileY - 1).Equals(objectUpper) ||
                !tiles.Get(placed.TileX, placed.TileY).Equals(objectLower))
            {
                if (owner.tiles is { } current)
                {
                    owner.replication?.TryPublishTileSquareToAll(current, placed.TileX, placed.TileY - 1);
                    owner.replication?.TryPublishTileSquareToAll(current, placed.TileX, placed.TileY);
                }
                return false;
            }
            return owner.replication?.TryPublishPlaceObject(player.Capture.Connection.Source, in placed) == true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            world.Dispose();
        }
    }
}
