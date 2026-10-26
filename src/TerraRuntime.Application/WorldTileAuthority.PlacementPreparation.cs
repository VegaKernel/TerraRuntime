using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    // Definition metadata does not itself admit a local world-use participant.
    // Other placeable families retain their existing remote projection until
    // their concrete world/clock/payment effects have source-backed owners.
    internal static bool SupportsLocalPlacementDefinition(in LocalPlacementDefinition1458 definition) =>
        SupportsFurniturePlacement(in definition) || definition.IsWall || definition.PlaceStyle == 0 &&
        TerraRuntime.Gameplay.Tiles.VanillaLocalPlacementTiles1458.IsOrdinarySolid(new(definition.CreateTile));

    private bool TryPreparePlacementAt(RuntimePlayerToolInput1458 input,
        out PlacementPreparation1458? preparation)
    {
        preparation = null;
        if (!input.IsPlacement || !input.IsCurrent || input.Actor is not { } capture || !input.HasKnownPlacementStage(capture) ||
            input.PlacementReads is not { IsCurrent: true } reads || input.Count == 0 ||
            input.Observations[0].Tile is not { } first ||
            capture.Phase.ToolUse is not { SourcePlacement: { } definition, SourceStagePose: { } pose,
                BuildingPermission: { IsCurrent: true, NoBuilding: false } building,
                Reach: { } reach, RequiresFreshUse: false, ToolStagePending: false } tools ||
            tools.SourceSlot != capture.Actor.Player.SelectedItem || !input.IsPossiblePlacementSlot(tools.SourceSlot) ||
            definition.Item != capture.Selected.ItemType || capture.Selected.Prefix.Value != 0 ||
            !SupportsLocalPlacementDefinition(definition) ||
            tools.Clocks.Selected.ItemTime != 0 || tools.Clocks.Selected.Animation <= 0 ||
            (pose.Player.ControlFlags & 32) == 0 || !reach.Contains(pose, first.TileX, first.TileY) ||
            !players.TryPrepareToolAttempt(capture.Connection, out var player) || player is null ||
            !TryCaptureToolTreeWorld(capture, out var context) || context is null ||
            !players.TryCaptureToolTreeActors(out var actors) || actors is null || tiles is null) return false;
        // Metadata identifies a possible selected source item; the world pass
        // still rejects every unrepresented target/effect instead of raw fallback.
        if (!(definition.IsWall ? first.Action is 3 or 22 : first.Action is 1 or 21) ||
            first.Data != (definition.IsWall ? definition.CreateWall : definition.CreateTile)) return false;
        var clientTiles = new ToolWorldTileOverlay1458(tiles);
        var serverTiles = new ToolWorldTileOverlay1458(tiles);
        var localRandom = player.CreateActionRandomPreview();
        if (!TryCreateToolMutationPlan(serverTiles, out var world) || world is null) return false;
        bool handedOff = false;
        try
        {
            if (!TryCreateToolSourcePass(clientTiles, localRandom, true, context.HardMode, context.Facts,
                    actors.Actors, null, out var client) || client is null ||
                !TryCreateToolSourcePass(serverTiles, world.SourceRandom, false, context.HardMode, context.Facts,
                    actors.Actors, world, out var server) || server is null) return false;
            var steps = new PlacementSteps1458(this, client, server, world, actors, definition, capture.Selected.Stack);
            ToolDefinition1458? bestPick = null;
            if (!definition.IsWall)
            {
                for (int slot = 0; slot < 50; slot++)
                {
                    var stock = capture.InventoryBefore[slot];
                    if (stock.IsEmpty || !VanillaToolCatalog1458.TryGet(stock.ItemType, out var baseTool) ||
                        baseTool.Pick <= 0) continue;
                    if (!VanillaToolCatalog1458.TryResolve(stock.ItemType, stock.Prefix,
                        capture.World.RemixWorld, capture.World.WindowsItemPrefixArithmetic, out var pick)) return false;
                    if (bestPick is null || pick.Pick > bestPick.Value.Pick) bestPick = pick;
                }
            }
            ToolDamageWorld1458 damageWorld = default;
            if (!definition.IsWall && !capture.World.TryCaptureToolDamageWorld(out damageWorld)) return false;
            steps.ConfigureReplacement(bestPick, damageWorld, reads, tools.Hits, tools.ReplacementHits);
            if (!steps.TryPlaceAt(first.TileX, first.TileY)) return false;
            LocalPlacementTail1458 tail;
            bool clock = steps.ReplacementPickUseTime is { } pickTime
                ? VanillaLocalPlacement1458.TryPlanTileReplacementTail(definition, tools.Clocks,
                    capture.Selected.Stack, pickTime, capture.PickSpeed, tools.PlacementSpeed,
                    capture.World.WindowsItemPrefixArithmetic, out tail)
                : VanillaLocalPlacement1458.TryPlanTail(definition, tools.Clocks, capture.Selected.Stack,
                    steps.AdditionalWalls, tools.PlacementSpeed, capture.World.WindowsItemPrefixArithmetic, out tail);
            if (!clock) return false;
            PlayerAuthority.ToolShapePlayerPreparation? shape = null;
            if (!world.ShapeEffects.IsEmpty &&
                (!players.TryPrepareToolShapeEffects(world.ShapeEffects, out shape) || shape is null)) return false;
            var candidate = new PlacementPreparation1458(this, player, input, world, steps, context, actors,
                reads, building, tail, localRandom, shape, first.TileX, first.TileY);
            if (!candidate.IsCurrent) return false;
            preparation = candidate; handedOff = true; return true;
        }
        finally { if (!handedOff) world.Dispose(); }
    }

    internal sealed class PlacementPreparation1458(WorldTileAuthority owner, PlayerToolAttemptPreparation player,
        RuntimePlayerToolInput1458 input, ToolWorldMutationPlan world, PlacementSteps1458 steps,
        ToolTreeWorldCapture1458 context, PlayerAuthority.ToolTreeActorsPreparation actors,
        ToolWorldReadPreparation reads, RuntimeToolBuildingCapture1458 building, LocalPlacementTail1458 tail,
        VanillaUnifiedRandom1458 localRandom, PlayerAuthority.ToolShapePlayerPreparation? shape, int centerX, int centerY) : IDisposable
    {
        private bool sealedPlan, adopted, disposed, publishing, inventoryPublished, shapePublished;
        private int next;
        private readonly TerrariaTileManipulationState[] expected = steps.Expected.ToArray();
        private readonly ToolWorldPublicationOffset1458[] boundaries = steps.Boundaries.ToArray();
        private readonly WorldTile[] footprint = new WorldTile[49];
        private RuntimePlayerInventoryStore.RemotePhasePreparation? payment;
        private RuntimePlayerInventoryItem after;
        private ToolWorldPublication1458? publication;
        private PlayerAuthority.PlacementInventoryPublication1458? inventoryPublication;
        internal bool IsCurrent => !disposed && !adopted && player.IsCurrent && input.IsCurrent &&
            reads.IsCurrent && building.IsCurrent && context.IsCurrent && actors.IsCurrent &&
            steps.IsCurrent && world.IsCurrentOwned && (shape is null || shape.IsCurrent);
        internal ToolWireMatch1458 Match()
        {
            if (!IsCurrent || input.Count > expected.Length) return ToolWireMatch1458.Rejected;
            for (int index = 0; index < input.Count; index++)
                if (input.Observations[index].Picking is not null || input.Observations[index].Tile != expected[index])
                    return ToolWireMatch1458.Rejected;
            return input.Count < expected.Length || input.PlacementInventoryReport is null
                ? ToolWireMatch1458.Incomplete : ToolWireMatch1458.Complete;
        }
        internal bool TrySeal()
        {
            if (sealedPlan || Match() != ToolWireMatch1458.Complete || !world.TrySeal() || !IsCurrent ||
                !owner.players.TryPreparePlacementPayment(player.Capture, tail, out payment, out after) ||
                payment is null || !payment.IsCurrent ||
                shape is not null && !shape.TrySealForJointAdoption(player.Capture.Connection.Player) ||
                !player.TryStagePlacementResult(tail, after, localRandom, steps.Hits, steps.ReplacementHits)) return false;
            int index = 0;
            for (int x = centerX - 3; x <= centerX + 3; x++)
            for (int y = centerY - 3; y <= centerY + 3; y++)
                if (!world.Overlay.TryReadSourceSafely(x, y, out footprint[index++])) return false;
            if (!IsCurrent || !payment.IsCurrent) return false;
            sealedPlan = true; return true;
        }
        internal bool TryAdoptUnpublished()
        {
            if (!sealedPlan || !IsCurrent || payment is null || !payment.IsCurrent ||
                !player.TryAdoptUnpublished()) return false;
            if (!world.TryAdoptWholeUnpublished(out publication) || publication is null)
                throw new InvalidOperationException("Prepared placement world changed during joint adoption.");
            if (!payment.TryAdoptUnpublished() || !player.TryFinalizePlacementInventory(after, tail.ConsumedQuantity))
                throw new InvalidOperationException("Prepared placement payment changed during joint adoption.");
            if (shape is not null && !shape.TryAdoptUnpublished())
                throw new InvalidOperationException("Prepared placement shape changed during joint adoption.");
            inventoryPublication = owner.players.CapturePlacementInventoryPublication(player.Capture.Connection,
                player.Capture.Actor.Player.SelectedItem, after) ??
                throw new InvalidOperationException("Prepared placement lost its inventory publication.");
            adopted = true;
            owner.AppliedClientManipulations += expected.Length;
            if (!player.TryRebaseOtherToolInputs())
                throw new InvalidOperationException("Placement RNG lineage changed during joint adoption.");
            return true;
        }
        private bool IsFootprintCurrent()
        {
            if (owner.tiles is not { } tiles) return false;
            int index = 0;
            for (int x = centerX - 3; x <= centerX + 3; x++)
            for (int y = centerY - 3; y <= centerY + 3; y++)
            {
                var accepted = footprint[index++];
                if ((uint)x < (uint)tiles.Dimensions.WidthTiles && (uint)y < (uint)tiles.Dimensions.HeightTiles &&
                    !tiles.Get(x, y).Equals(accepted)) return false;
            }
            return true;
        }
        internal bool TryPublishAll()
        {
            if (!adopted || disposed || publishing || publication is null) return false;
            publishing = true;
            Exception? failure = null;
            try
            {
                while (next < expected.Length)
                {
                    int index = next++;
                    try { publication.TryPublishThrough(boundaries[index]); }
                    catch (Exception error) { failure ??= error; }
                    if (!shapePublished && shape is not null && expected[index].Action == 14)
                    {
                        shapePublished = true;
                        for (int attempt = 0; attempt < byte.MaxValue; attempt++)
                            try { if (!shape.TryPublishNext()) break; }
                            catch (Exception error) { failure ??= error; }
                    }
                    try
                    {
                        if (IsFootprintCurrent())
                        {
                            // MessageBuffer17 suppresses the primary1/21 relay
                            // when its source falling frame already removed the
                            // cell. The prepared world participant has published
                            // that falling birth and source square instead.
                            var wire = expected[index];
                            bool suppressed = wire.Action is 1 or 21 &&
                                ToolWorldSourcePass1458.IsSourceFallingTile(checked((ushort)wire.Data)) &&
                                world.Overlay.TryRead(wire.TileX, wire.TileY, out var finalTile) && !finalTile.IsActive;
                            if (!suppressed) owner.TryPublishToolTile(player.Capture.Connection.Source,
                                player.Capture.Connection.Player, wire);
                        }
                        else if (owner.tiles is { } tiles) owner.replication?.TryPublishTileSquareToAll(tiles,
                            expected[index].TileX, expected[index].TileY);
                    }
                    catch (Exception error) { failure ??= error; }
                }
                if (!inventoryPublished)
                {
                    inventoryPublished = true;
                    try { inventoryPublication?.TryPublish(); }
                    catch (Exception error) { failure ??= error; }
                }
            }
            finally { publishing = false; }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            return true;
        }
        public void Dispose() { if (disposed) return; disposed = true; world.Dispose(); }
    }
}
