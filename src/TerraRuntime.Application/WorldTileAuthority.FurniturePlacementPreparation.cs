using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct FurniturePlacementCoordinates1458(short TileX, short TileY, byte Width, byte Height, ushort TileType);

internal sealed partial class WorldTileAuthority
{
    private static bool IsSourceObjectNetworkPosition(int x, int y, WorldDimensions size) =>
        x >= 10 && y >= 10 && x < size.WidthTiles - 10 && y < size.HeightTiles - 10;

    private bool TryPrepareFurniturePlacement(RuntimePlayerToolInput1458 input,
        out FurniturePlacementPreparation1458? preparation)
    {
        preparation = null;
        if (!input.IsPlacement || !input.IsCurrent || input.FurnitureSquare is null && input.FurnitureObjectReport is null ||
            input.Actor is not { } capture || !input.HasKnownPlacementStage(capture) ||
            input.PlacementReads is not { IsCurrent: true } reads ||
            capture.Phase.ToolUse is not { SourcePlacement: { } definition, SourceStagePose: { } pose,
                BuildingPermission: { IsCurrent: true, NoBuilding: false } building,
                Reach: { } reach, RequiresFreshUse: false, ToolStagePending: false } tools ||
            tools.SourceSlot != capture.Actor.Player.SelectedItem || !input.IsPossiblePlacementSlot(tools.SourceSlot) ||
            definition.Item != capture.Selected.ItemType || capture.Selected.Prefix.Value != 0 ||
            !FurniturePlacementShape1458.TryGet(in definition, out var shape) ||
            tools.Clocks.Selected.ItemTime != 0 || tools.Clocks.Selected.Animation <= 0 ||
            (pose.Player.ControlFlags & 32) == 0 ||
            !players.TryPrepareToolAttempt(capture.Connection, out var player) || player is null ||
            !TryCaptureToolTreeWorld(capture, out var context) || context is null ||
            !players.TryCaptureToolTreeActors(out var actors) || actors is null || tiles is null) return false;
        int rawX = input.FurnitureSquare?.TileX ?? input.FurnitureObjectReport!.Value.TileX - shape.OriginX;
        int rawY = input.FurnitureSquare?.TileY ?? input.FurnitureObjectReport!.Value.TileY - shape.OriginY;
        if ((uint)rawX > short.MaxValue || (uint)rawY > short.MaxValue ||
            shape.HasEntity && input.FurnitureSquare is null ||
            !shape.HasEntity && input.FurnitureSquare is not null ||
            input.FurnitureSquare is { } square && (square.TileType != shape.Tile || square.Width != shape.Width || square.Height != shape.Height) ||
            !reach.Contains(pose, rawX + shape.OriginX, rawY + shape.OriginY)) return false;
        var report = new FurniturePlacementCoordinates1458((short)rawX, (short)rawY, shape.Width, shape.Height, shape.Tile);
        if (worldItemSpawnRandom is not SystemWorldItemSpawnRandom liveWorld ||
            !capture.IsRandomDistinctFrom(liveWorld.SourceRandom)) return false;
        var clientTiles = new ToolWorldTileOverlay1458(tiles);
        var serverTiles = new ToolWorldTileOverlay1458(tiles);
        var localRandom = player.CreateActionRandomPreview();
        if (!TryCreateToolMutationPlan(serverTiles, out var world) || world is null) return false;
        bool transferred = false;
        try
        {
            if (!TryCreateToolSourcePass(clientTiles, localRandom, true, context.HardMode, context.Facts,
                    actors.Actors, null, out var client) || client is null ||
                !TryCreateToolSourcePass(serverTiles, world.SourceRandom, false, context.HardMode, context.Facts,
                    actors.Actors, world, out var server) || server is null) return false;
            int clickX = report.TileX + shape.OriginX, clickY = report.TileY + shape.OriginY;
            // Modern470/471/475 are absent from Player's exceptional overlap
            // blocker. Adding Collision.EmptyTile here would reject vanilla use.
            int direction = (pose.Player.ControlFlags & 64) != 0 ? 1 : -1;
            if (!client.TryPlaceFurniture(in shape, clickX, clickY, direction, true,
                    out bool placed, out var objectReport, out var clientFootprint) || !placed) return false;
            WorldTileEntity? entity = null;
            byte[] expectedSquare = [], entityFrame = [];
            if (shape.HasEntity)
            {
                if (!TerrariaTileSquareCodec.TryEncodeFurnitureFrame(clientFootprint, report.TileX, report.TileY,
                        shape.Width, shape.Height, out expectedSquare) || !input.FurnitureSquare!.MatchesEncoded(expectedSquare) ||
                    !server.TryApplyFurnitureFootprint(report.TileX, report.TileY, shape.Width, shape.Height, clientFootprint) ||
                    !server.TryCreateFurnitureEntity(in shape, report.TileX, report.TileY, out entity) || entity is null ||
                    !TerrariaFurnitureEntityCodec1458.TryEncode(entity, out entityFrame)) return false;
            }
            else if (IsSourceObjectNetworkPosition(clickX, clickY, tiles.Dimensions) &&
                (!server.TryPlaceFurniture(in shape, clickX, clickY, direction, true,
                    out bool serverPlaced, out _, out _) || !serverPlaced)) return false;
            var squareBoundary = world.CapturePublicationOffset();
            if (!VanillaLocalPlacement1458.TryPlanTail(definition, tools.Clocks, capture.Selected.Stack, 0,
                    tools.PlacementSpeed, capture.World.WindowsItemPrefixArithmetic, out var tail)) return false;
            var accepted = new WorldTile[clientFootprint.Length];
            int index = 0;
            for (int dx = 0; dx < shape.Width; dx++)
            for (int dy = 0; dy < shape.Height; dy++)
                if (!serverTiles.TryReadSourceSafely(report.TileX + dx, report.TileY + dy, out accepted[index++])) return false;
            byte[] squareFrame = [];
            if (shape.HasEntity && !TerrariaTileSquareCodec.TryEncodeFurnitureServerFrame(accepted, report.TileX, report.TileY,
                    shape.Width, shape.Height, out squareFrame)) return false;
            var candidate = new FurniturePlacementPreparation1458(this, player, input, world, context, actors,
                reads, building, tail, localRandom, report, shape, objectReport, entity, expectedSquare, accepted,
                squareBoundary, squareFrame, entityFrame, IsSourceObjectNetworkPosition(clickX, clickY, tiles.Dimensions));
            if (!candidate.IsCurrent) return false;
            preparation = candidate; transferred = true; return true;
        }
        finally { if (!transferred) world.Dispose(); }
    }

    internal sealed class FurniturePlacementPreparation1458(WorldTileAuthority owner,
        PlayerToolAttemptPreparation player, RuntimePlayerToolInput1458 input, ToolWorldMutationPlan world,
        ToolTreeWorldCapture1458 context, PlayerAuthority.ToolTreeActorsPreparation actors,
        ToolWorldReadPreparation reads, RuntimeToolBuildingCapture1458 building, LocalPlacementTail1458 tail,
        VanillaUnifiedRandom1458 localRandom, FurniturePlacementCoordinates1458 report,
        FurniturePlacementShape1458 shape, TerrariaPlaceObjectState objectReport, WorldTileEntity? entity, byte[] expectedSquare,
        WorldTile[] accepted, ToolWorldPublicationOffset1458 squareBoundary, byte[] squareFrame, byte[] entityFrame, bool relayObject) : IDisposable
    {
        private bool sealedPlan, adopted, disposed, publishing;
        private int nextPublication;
        private RuntimePlayerInventoryStore.RemotePhasePreparation? payment;
        private RuntimePlayerInventoryItem after;
        private ToolWorldPublication1458? publication;
        private PlayerAuthority.PlacementInventoryPublication1458? inventoryPublication;
        internal bool IsCurrent => !disposed && !adopted && player.IsCurrent && input.IsCurrent &&
            reads.IsCurrent && building.IsCurrent && context.IsCurrent && actors.IsCurrent && world.IsCurrentOwned;
        internal ToolWireMatch1458 Match()
        {
            if (!IsCurrent || input.Count != 0 ||
                shape.HasEntity && (input.FurnitureSquare is not { } square || !square.MatchesEncoded(expectedSquare)) ||
                !shape.HasEntity && input.FurnitureSquare is not null ||
                input.FurnitureObjectReport is not null && (!shape.HasObjectReport || input.FurnitureObjectReport.Value != objectReport))
                return ToolWireMatch1458.Rejected;
            if (input.PlacementInventoryReport is null || shape.HasObjectReport && input.FurnitureObjectReport is null ||
                shape.HasEntity && input.FurnitureEntityReport is null) return ToolWireMatch1458.Incomplete;
            if (!shape.HasEntity) return input.FurnitureEntityReport is null ? ToolWireMatch1458.Complete : ToolWireMatch1458.Rejected;
            var entityReport = input.FurnitureEntityReport!.Value;
            return entity is not null && entityReport.TileX == entity.X && entityReport.TileY == entity.Y && entityReport.Kind == (byte)entity.Kind
                ? ToolWireMatch1458.Complete : ToolWireMatch1458.Rejected;
        }
        internal bool TrySeal()
        {
            if (sealedPlan || Match() != ToolWireMatch1458.Complete || !world.TrySeal() || !IsCurrent ||
                !owner.players.TryPreparePlacementPayment(player.Capture, tail, out payment, out after) ||
                payment is null || !payment.IsCurrent ||
                !player.TryStagePlacementResult(tail, after, localRandom) || !IsCurrent || !payment.IsCurrent) return false;
            sealedPlan = true; return true;
        }
        internal bool TryAdoptUnpublished()
        {
            if (!sealedPlan || !IsCurrent || payment is null || !payment.IsCurrent || !player.TryAdoptUnpublished()) return false;
            if (!world.TryAdoptWholeUnpublished(out publication) || publication is null)
                throw new InvalidOperationException("Prepared furniture world changed during joint adoption.");
            if (!payment.TryAdoptUnpublished() || !player.TryFinalizePlacementInventory(after, tail.ConsumedQuantity))
                throw new InvalidOperationException("Prepared furniture payment changed during joint adoption.");
            inventoryPublication = owner.players.CapturePlacementInventoryPublication(player.Capture.Connection,
                player.Capture.Actor.Player.SelectedItem, after) ??
                throw new InvalidOperationException("Prepared furniture lost inventory publication.");
            adopted = true; owner.AppliedClientManipulations++;
            if (!player.TryRebaseOtherToolInputs()) throw new InvalidOperationException("Furniture RNG lineage changed during adoption.");
            return true;
        }
        private bool IsAcceptedCurrent()
        {
            if (owner.tiles is not { } tiles || !owner.players.TryGet(player.Capture.Connection, out _)) return false;
            if (entity is not null)
            {
                if (tiles.RuntimeTileEntities is not { } metadata || !metadata.TryCaptureById(entity.PersistedId, out var current) ||
                    current is null || current.X != entity.X || current.Y != entity.Y || current.Kind != entity.Kind) return false;
                bool empty = current.Payload switch
                {
                    WorldDisplayDollPayload doll => doll.Pose == 0 && doll.Misc is null &&
                        doll.Equipment.All(item => item is null) && doll.Dyes.All(item => item is null),
                    WorldHatRackPayload hats => hats.Items.All(item => item is null) && hats.Dyes.All(item => item is null),
                    WorldItemTileEntityPayload rack => rack.Item == default,
                    _ => false
                };
                if (!empty) return false;
            }
            int index = 0;
            for (int dx = 0; dx < shape.Width; dx++)
            for (int dy = 0; dy < shape.Height; dy++)
                if (!tiles.Get(report.TileX + dx, report.TileY + dy).Equals(accepted[index++])) return false;
            return true;
        }
        internal bool TryPublishAll()
        {
            if (!adopted || disposed || publishing || publication is null) return false;
            publishing = true; Exception? failure = null;
            try
            {
                while (nextPublication < 4)
                {
                    int step = nextPublication++;
                    try
                    {
                        if (step == 0)
                        {
                            try { publication.TryPublishThrough(squareBoundary); }
                            catch (Exception error) { failure ??= error; }
                            if (shape.HasEntity && IsAcceptedCurrent()) owner.replication?.TryPublishFurnitureFootprint(player.Capture.Connection.Source, squareFrame);
                        }
                        else if (step == 1)
                        {
                            if (shape.HasEntity && IsAcceptedCurrent()) owner.replication?.TryPublishFurnitureEntity(entityFrame);
                        }
                        else if (step == 2)
                        {
                            if (shape.HasObjectReport && relayObject && IsAcceptedCurrent())
                                owner.replication?.TryPublishPlaceObject(player.Capture.Connection.Source, in objectReport);
                        }
                        else inventoryPublication?.TryPublish();
                    }
                    catch (Exception error) { failure ??= error; }
                }
            }
            finally { publishing = false; }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            return true;
        }
        public void Dispose() { if (disposed) return; disposed = true; world.Dispose(); }
    }

    private bool TryCommitObservedFurniturePlacement(RuntimePlayerToolInput1458 input, ToolActionPublicationBatch1458? deferred)
    {
        FurniturePlacementPreparation1458? preparation = null;
        try
        {
            if (!TryPrepareFurniturePlacement(input, out preparation) || preparation is null ||
                preparation.Match() != ToolWireMatch1458.Complete || !preparation.TrySeal() || !preparation.TryAdoptUnpublished()) return false;
            CommittedToolActions++;
            if (deferred is null) preparation.TryPublishAll();
            else { deferred.AddFurniturePlacement(preparation); preparation = null; }
            return true;
        }
        finally { preparation?.Dispose(); }
    }
}
