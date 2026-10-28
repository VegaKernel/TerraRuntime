using TerraRuntime.Gameplay.Players;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    // Extends the existing player/world/inventory joint adoption. No callback
    // runs between the last guards and adoption of all three participants.
    internal sealed class WiringPreparation1458(WorldTileAuthority owner,
        PlayerToolAttemptPreparation player, RuntimePlayerToolInput1458 input,
        ToolWorldReadPreparation reads, ToolWorldMutationPlan world,
        TerrariaTileManipulationState report, WorldTile before, WorldTile after,
        ToolClockState1458 clocks, short resourceSlot, bool golem) : IDisposable
    {
        private bool sealedPlan, adopted, disposed, publishing, tilePublished, inventoryPublished;
        private RuntimePlayerInventoryStore.RemotePhasePreparation? payment;
        private RuntimePlayerInventoryItem inventoryAfter;
        private ToolWorldPublication1458? publication;
        private readonly ToolWorldPublicationOffset1458 end = world.CapturePublicationOffset();
        internal bool IsCurrent => !disposed && !adopted && player.IsCurrent && input.IsCurrent &&
            reads.IsCurrent && world.IsCurrentOwned &&
            (owner.golemDownedBaseline || owner.progression.IsCompleted(VanillaWorldProgressionId.Golem)) == golem &&
            owner.tiles is { } tiles && tiles.Get(report.TileX, report.TileY).Equals(before);

        internal bool TrySeal()
        {
            if (sealedPlan || !IsCurrent || !world.TrySeal()) return false;
            if (resourceSlot >= 0 &&
                (!owner.players.TryPrepareWiringPayment(player.Capture, resourceSlot, out payment, out inventoryAfter) ||
                    payment is null || !payment.IsCurrent)) return false;
            if (!player.TryStageWiringResult(clocks, player.CreateActionRandomPreview(), resourceSlot,
                resourceSlot < 0 ? null : inventoryAfter) || !IsCurrent || payment is { IsCurrent: false }) return false;
            sealedPlan = true;
            return true;
        }

        internal bool TryAdoptUnpublished()
        {
            if (!sealedPlan || !IsCurrent || payment is { IsCurrent: false } || !player.TryAdoptUnpublished()) return false;
            if (!world.TryAdoptWholeUnpublished(out publication) || publication is null)
                throw new InvalidOperationException("Prepared wiring world changed during joint adoption.");
            if (payment is not null && (!payment.TryAdoptUnpublished() ||
                !player.TryFinalizeWiringInventory(resourceSlot, inventoryAfter)))
                throw new InvalidOperationException("Prepared wiring payment changed during joint adoption.");
            adopted = true;
            owner.AppliedClientManipulations++;
            if (!player.TryRebaseOtherToolInputs())
                throw new InvalidOperationException("Wiring RNG lineage changed during joint adoption.");
            return true;
        }

        internal bool TryPublishAll()
        {
            if (!adopted || disposed || publishing || publication is null) return false;
            publishing = true;
            Exception? failure = null;
            try
            {
                // Drain the existing world publication journal before the primary relay.
                try { publication.TryPublishThrough(end); }
                catch (Exception error) { failure = error; }
                if (!tilePublished)
                {
                    tilePublished = true;
                    try
                    {
                        if (owner.tiles is { } tiles)
                        {
                            if (tiles.Get(report.TileX, report.TileY).Equals(after))
                                owner.TryPublishToolTile(player.Capture.Connection.Source,
                                    player.Capture.Connection.Player, report);
                            else owner.replication?.TryPublishTileSquareToAll(tiles, report.TileX, report.TileY);
                        }
                    }
                    catch (Exception error) { failure ??= error; }
                }
                if (!inventoryPublished && resourceSlot >= 0)
                {
                    inventoryPublished = true;
                    try { owner.players.TryPublishWiringInventory(player.Capture.Connection, resourceSlot, inventoryAfter); }
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
