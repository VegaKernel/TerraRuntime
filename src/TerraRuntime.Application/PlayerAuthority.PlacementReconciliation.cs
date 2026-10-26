using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal ToolReportObservationResult1458 ObservePlacementReport(ConnectionHandle connection,
        in TerrariaTileManipulationState report, out RuntimePlayerToolInput1458? input)
    {
        input = null;
        bool primary = report.Action is 1 or 3 or 21 or 22;
        if (!primary && report.Action is not (0 or 14)) return ToolReportObservationResult1458.NotApplicable;
        if (!membership.TryGet(connection, out var member) || member.ItemPhase is not { ToolUse: { } tools } phase)
            return primary ? ToolReportObservationResult1458.Rejected : ToolReportObservationResult1458.NotApplicable;
        if (tools.Input is { IsPlacement: true, IsCurrent: true } existing)
        {
            input = existing;
            return input.TryObserve(in report) ? ToolReportObservationResult1458.Retained :
                ToolReportObservationResult1458.Rejected;
        }
        // Source PlaceIt sends its primary17 before unslope/grass reports.
        // Only an existing placement cohort may retain those tails; they do
        // not start a new placement or borrow a mining entitlement.
        if (!primary) return ToolReportObservationResult1458.NotApplicable;
        ulong possibleSlots = 0;
        for (short slot = 0; slot < 59; slot++)
        {
            if (!inventory.TryGet(connection, slot, out var item) || item.IsEmpty ||
                !item.IsCanonical || item.Prefix.Value != 0 ||
                !VanillaLocalPlacementCatalog1458.TryGet(item.ItemType, out var placement)) continue;
            bool wall = report.Action is 3 or 22;
            if (placement.IsWall == wall && (wall ? placement.CreateWall : placement.CreateTile) == report.Data)
                possibleSlots |= 1UL << slot;
        }
        if (possibleSlots == 0) return ToolReportObservationResult1458.Rejected;
        if (tools.Input is not null) RetireToolInput(member, tools.Input);
        phase = member.ItemPhase!.Value;
        tools = phase.ToolUse!;
        var candidate = new RuntimePlayerToolInput1458(possibleSlots);
        bool projection = member.RemotePhaseSnapshot == member.CaptureSnapshot() &&
            member.RemotePhaseItem == phase && member.RemotePhaseInputRevision == member.ProjectileUseInputRevision;
        var attached = phase with { ToolUse = tools with { Input = candidate } };
        member.ItemPhase = attached;
        if (projection) member.MarkRemotePhaseSnapshot();
        if (!TryCaptureToolActor(connection, out var capture) || capture is null)
        {
            if (member.ItemPhase == attached)
            {
                member.ItemPhase = phase;
                if (projection) member.MarkRemotePhaseSnapshot();
            }
            return ToolReportObservationResult1458.Rejected;
        }
        candidate.BindActor(capture);
        input = candidate;
        return input.TryObserve(in report) ? ToolReportObservationResult1458.Retained :
            ToolReportObservationResult1458.Rejected;
    }

    private bool TryRetainPlacementInventory(PlayerEquipmentRuntimeCommand equipment)
    {
        if (!membership.TryGet(equipment.Connection, out var member) ||
            member.ItemPhase?.ToolUse?.Input is not { IsPlacement: true } input ||
            !input.IsPossiblePlacementSlot(equipment.Request.SlotId)) return false;
        // Consume this report through the source operation, never the legacy raw
        // inventory receiver. No physical or private inventory changes here.
        if (!input.TryObservePlacementInventory(equipment.Request)) RejectedEquipmentUpdates++;
        else toolWorld?.TryCommitObservedPlacement(input);
        return true;
    }
    private readonly record struct PlacementSelectionObservation(RuntimePlayerToolInput1458 Input,
        RuntimePlayerToolActorCapture1458 Actor, ulong Revision);

    private PlacementSelectionObservation? CapturePlacementSelection(RuntimePlayerMember member,
        in PlayerMovementCommitRequest request)
    {
        if (member.ItemPhase?.ToolUse?.Input is not { IsPlacement: true, IsCurrent: true } input ||
            input.Actor is not { } actor || !input.IsPossiblePlacementSlot(request.SelectedItem) ||
            input.PlacementReads is not { IsCurrent: true } ||
            actor.Actor.InputRevision == ulong.MaxValue || actor.Actor.Player.Revision.Value == ulong.MaxValue)
            return null;
        return new(input, actor, input.Revision);
    }

    private void ObservePlacementSelection(RuntimePlayerMember member, PlacementSelectionObservation? pending)
    {
        if (pending is not { } retained || !ReferenceEquals(member.ItemPhase?.ToolUse?.Input, retained.Input)) return;
        var before = retained.Actor;
        var old = before.Actor.Player;
        var current = member.CaptureSnapshot();
        // Only the already normalized packet13 write is allowed between these
        // adjacent calls. It supplies selection/control evidence, not a clock,
        // local stage pose, payment or permission to mutate the world.
        bool exact = current.Revision.Value == old.Revision.Value + 1 &&
            member.ProjectileUseInputRevision == before.Actor.InputRevision + 1 &&
            member.ItemPhase == before.Actor.ItemPhase && before.Inventory.IsCurrent &&
            transferProfiles.IsCurrentBuffState(member.Connection, before.BeforeBuffs) &&
            before.IsCurrentRandom(playerUpdateRandom, playerUpdateRandomOwned) &&
            playerUpdateWorld == before.World;
        if (exact && TryCaptureToolActor(member.Connection, out var observed) && observed is not null &&
            retained.Input.TryRebasePlacementSelection(before, retained.Revision, observed)) return;
        RetireToolInput(member, retained.Input);
    }

}
