using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldMutationPlan
    {
        private RuntimeWorldFurnitureStore1458.Preparation? furniture;
        internal bool TryGetFurniture(out RuntimeWorldFurnitureStore1458.Preparation? prepared)
        {
            prepared = null;
            if (!IsCurrentOwned || sealedPlan) return false;
            if (furniture is null && (Overlay.Tiles.RuntimeTileEntities is not { } owner ||
                !owner.TryPrepare(ToolWorldTileOverlay1458.MaximumChangedCells, out furniture))) return Fail();
            prepared = furniture;
            return prepared is not null && IsCurrentOwned;
        }
        private bool IsFurnitureCurrent => furniture is null || furniture.CanAdoptUnpublished;
        private bool HasSameFurniture(ToolWorldMutationPlan other) => furniture is null ? other.furniture is null :
            other.furniture is not null && furniture.HasSameSourceEffects(other.furniture);
    }
}
