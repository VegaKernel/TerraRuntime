using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerToolInput1458
{
    private RuntimePlayerToolActorCapture1458? placementSourceStage;
    private int placementReleasedMovements;

    // Source-stage preservation, never a new stage from packet13. Only exact
    // same-update release-only writes can connect an earlier qualified stage
    // to the delayed economic tail on this same bounded input.
    internal bool HasKnownPlacementStage(RuntimePlayerToolActorCapture1458 capture) =>
        IsPlacement && ReferenceEquals(actor, capture) && capture.IsCurrent &&
        (capture.HasCurrentSourceStage || placementSourceStage is { } source &&
            placementReleasedMovements is > 0 and <= MaximumObservations &&
            capture.HasPreservedPlacementEndpoint(source, placementReleasedMovements));
    internal RuntimePlayerToolInput1458(ulong placementSlots) : this(default(ToolDefinition1458))
    {
        if (placementSlots == 0 || (placementSlots >> 59) != 0)
            throw new ArgumentOutOfRangeException(nameof(placementSlots));
        PlacementSlots = placementSlots;
    }

    internal ulong PlacementSlots { get; }
    internal bool IsPlacement => PlacementSlots != 0;
    internal PlayerEquipmentCommitRequest? PlacementInventoryReport { get; private set; }
    internal WorldTileAuthority.ToolWorldReadPreparation? PlacementReads { get; private set; }

    internal bool IsPossiblePlacementSlot(int slot) => (uint)slot < 59 &&
        (PlacementSlots & (1UL << slot)) != 0;

    internal bool TryBindPlacementReads(WorldTileAuthority.ToolWorldReadPreparation reads)
    {
        if (!IsPlacement || !IsCurrent || !reads.IsCurrent || PlacementReads is not null) return false;
        PlacementReads = reads;
        return true;
    }

    internal bool TryObservePlacementInventory(in PlayerEquipmentCommitRequest report)
    {
        if (!IsPlacement || !IsCurrent || PlacementReads is not { IsCurrent: true } ||
            report.Stack < 0 || !IsPossiblePlacementSlot(report.SlotId) || actor is null || report.PlayerSlot != actor.Connection.Player.Slot ||
            !RuntimePlayerInventoryItem.TryFromNormalized(in report, out var reported) || !reported.IsCanonical)
            return false;
        if (PlacementInventoryReport is { } existing) return existing == report;
        if (Revision == ulong.MaxValue) { Invalid = true; return false; }
        PlacementInventoryReport = report;
        Revision++;
        return true;
    }
    internal bool TryRebasePlacementSelection(RuntimePlayerToolActorCapture1458 before,
        ulong revision, RuntimePlayerToolActorCapture1458 observed, bool hadSourceStage = false)
    {
        if (!IsPlacement || Invalid || Revision != revision || Revision == ulong.MaxValue ||
            !ReferenceEquals(actor, before) || PlacementReads is not { IsCurrent: true } ||
            !observed.IsCurrent || observed.Connection != before.Connection ||
            !IsPossiblePlacementSlot(observed.Actor.Player.SelectedItem) ||
            !ReferenceEquals(observed.Phase.ToolUse?.Input, this)) return false;
        bool releaseOnly = observed.HasPreservedPlacementEndpoint(before, 1);
        if (releaseOnly && hadSourceStage)
        {
            placementSourceStage = before;
            placementReleasedMovements = 1;
        }
        else if (releaseOnly && placementSourceStage is not null &&
            placementReleasedMovements < MaximumObservations)
            placementReleasedMovements++;
        else
        {
            placementSourceStage = null;
            placementReleasedMovements = 0;
        }
        actor = observed;
        Revision++;
        return true;
    }

}
