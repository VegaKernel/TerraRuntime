namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal bool TryCaptureSourceContainerPermission(int anchorX, int anchorY,
        out RuntimeChestStore.SourceContainerPermission? permission)
    {
        permission = null;
        return objectMetadata is RuntimeChestObjectMetadataLifecycle metadata &&
            metadata.TryCaptureSourceContainerPermission(anchorX, anchorY, out permission);
    }

    internal sealed partial class ToolWorldMutationPlan
    {
        private byte? sourceDefaultItemOwner;
        internal bool TryBeginSourceDefaultItemOwner(byte slot)
        {
            if (!IsCurrentOwned || sealedPlan || sourceDefaultItemOwner.HasValue || slot == byte.MaxValue) return Fail();
            sourceDefaultItemOwner = slot;
            return true;
        }
        internal void EndSourceDefaultItemOwner() => sourceDefaultItemOwner = null;
        private RuntimeChestStore.ToolObjectRemovalPreparation? containerRemovals;
        internal RuntimeChestStore.ToolObjectRemovalPreparation? SourceContainerRemovals => containerRemovals;

        // Invoked only after a source-qualified whole object footprint and its
        // packet34 handshake have been matched. This does not authorize a tile clear.
        internal bool TryStageSourceContainerMetadataRemoval(int anchorX, int anchorY)
        {
            if (!IsCurrentOwned || sealedPlan || owner.objectMetadata is not RuntimeChestObjectMetadataLifecycle metadata)
                return Fail();
            containerRemovals ??= metadata.TryPrepareToolObjectRemovals();
            return containerRemovals is not null && containerRemovals.TryStage(anchorX, anchorY) && IsCurrentOwned;
        }
        internal bool TryStageSourceItemRectangle(TerraRuntime.Contracts.Gameplay.ItemTypeId item,
            short stack, int x, int y, int width, int height)
        {
            if (!IsCurrentOwned || sealedPlan || item.IsNone || stack <= 0 || width <= 0 || height <= 0) return Fail();
            var drop = new TerraRuntime.Core.Worlds.WorldItemDropStateUpdate(
                x + width / 2 - 8, y + height / 2 - 8,
                Random.NextInt32(-30, 31) * .1f, Random.NextInt32(-40, -15) * .1f,
                stack, 0, TerraRuntime.Contracts.Runtime.WorldItemOwnershipMode.None,
                checked((short)item.Value), false, 0, 0);
            return TryStageDrop(in drop);
        }
        private bool IsContainerMetadataCurrent => containerRemovals is null || containerRemovals.IsCurrent;
        private void AdoptContainerMetadata()
        {
            if (containerRemovals is not null && !containerRemovals.TryAdoptUnpublished())
                throw new InvalidOperationException("Prepared source container metadata changed during callback-free adoption.");
        }
        private bool HasSameContainerMetadata(ToolWorldMutationPlan other) =>
            containerRemovals is null ? other.containerRemovals is null :
            other.containerRemovals is not null && containerRemovals.HasSameSourceEffects(other.containerRemovals);
    }
}
