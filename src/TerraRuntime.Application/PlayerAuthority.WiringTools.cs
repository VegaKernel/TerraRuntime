using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal readonly record struct RuntimePlayerWiringReceipt1458(ConnectionHandle Connection,
    short Slot, RuntimePlayerInventoryItem Before, RuntimePlayerInventoryItem After, ulong ActionSerial);

internal sealed partial class PlayerAuthority
{
    internal ToolReportObservationResult1458 ObserveWiringReport(ConnectionHandle connection,
        in TerrariaTileManipulationState report, out RuntimePlayerToolInput1458? input)
    {
        input = null;
        if (report.Action is not (5 or 6 or 8 or 9 or 10 or 11 or 12 or 13 or 16 or 17))
            return ToolReportObservationResult1458.NotApplicable;
        if (!membership.TryGet(connection, out var member) ||
            !inventory.TryGet(connection, member.SelectedItem, out var selected) || selected.IsEmpty || selected.Prefix.Value != 0 ||
            !VanillaWiringToolCatalog1458.TryGet(selected.ItemType, out var definition) ||
            member.ItemPhase is not { ToolUse: { LocalClocksKnown: true } tools } phase)
            return ToolReportObservationResult1458.Rejected;
        if (tools.Input is { IsWiring: true, IsCurrent: true } existing)
        {
            input = existing;
            return input.TryObserve(in report) ? ToolReportObservationResult1458.Retained : ToolReportObservationResult1458.Rejected;
        }
        if (tools.Input is not null) RetireToolInput(member, tools.Input);
        phase = member.ItemPhase!.Value;
        tools = phase.ToolUse!;
        var candidate = new RuntimePlayerToolInput1458(definition);
        bool projection = member.RemotePhaseSnapshot == member.CaptureSnapshot() &&
            member.RemotePhaseItem == phase && member.RemotePhaseInputRevision == member.ProjectileUseInputRevision;
        var attached = phase with { ToolUse = tools with { Input = candidate } };
        member.ItemPhase = attached;
        if (projection) member.MarkRemotePhaseSnapshot();
        // A bounded observation may arrive before this writer's next source frame. Its current
        // known clock preimage grants no operation; the world owner still requires the later
        // source stage, and the ordinary phase adoption must rebase this exact input into it.
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
        return input.TryObserve(in report) ? ToolReportObservationResult1458.Retained : ToolReportObservationResult1458.Rejected;
    }

    private bool TryRetainWiringInventory(PlayerEquipmentRuntimeCommand command)
    {
        if (!membership.TryGet(command.Connection, out var member) ||
            member.ItemPhase?.ToolUse?.Input is not { IsWiring: true, Actor: { } actor,
                WiringDefinition: { } definition } input) return false;
        int resourceSlot = -1;
        if (definition.Kind == WiringToolKind1458.Actuator) resourceSlot = actor.Actor.Player.SelectedItem;
        else if (definition.Kind == WiringToolKind1458.Wire)
            for (int slot = 0; slot < 58; slot++)
                if (actor.InventoryBefore[slot] is { Stack: > 0 } stock &&
                    stock.ItemType.Value == VanillaWiringToolCatalog1458.WireItem) { resourceSlot = slot; break; }
        if (resourceSlot < 0 || command.Request.SlotId != resourceSlot) return false;
        if (!input.TryObserveWiringInventory(command.Request)) RejectedEquipmentUpdates++;
        else toolWorld?.TryCommitObservedWiring(input);
        return true;
    }

    private bool TryAcknowledgeWiringInventory(PlayerEquipmentRuntimeCommand command)
    {
        if (!membership.TryGet(command.Connection, out var member) ||
            member.ItemPhase?.ToolUse is not { WiringReceipt: { } receipt } tools ||
            receipt.Connection != command.Connection || receipt.ActionSerial != tools.ActionSerial ||
            receipt.Slot != command.Request.SlotId || command.Request.PlayerSlot != command.Connection.Player.Slot ||
            !RuntimePlayerInventoryItem.TryFromNormalized(command.Request, out var item) || item != receipt.After ||
            !inventory.TryGet(command.Connection, receipt.Slot, out var current) || current != receipt.After) return false;
        AppliedEquipmentUpdates++;
        return true;
    }

    internal bool TryPrepareWiringPayment(RuntimePlayerToolActorCapture1458 capture, short resourceSlot,
        out RuntimePlayerInventoryStore.RemotePhasePreparation? preparation, out RuntimePlayerInventoryItem after)
    {
        preparation = null; after = default;
        if (!capture.IsCurrent || !capture.HasCurrentSourceStage || capture.Inventory.HasPendingCursorSource ||
            resourceSlot is < 0 or >= 58 || capture.Phase.ToolUse is not { SourceWiring: { } definition,
                Input: { IsWiring: true, WiringInventoryReport: { } report } } ||
            definition.Item != capture.Selected.ItemType || capture.Selected.Prefix.Value != 0 ||
            report.SlotId != resourceSlot || report.PlayerSlot != capture.Connection.Player.Slot ||
            !inventory.TryGetFundedQuantity(capture.Connection, resourceSlot, out var funding) ||
            funding <= 0 || funding != capture.FundedQuantitiesBefore[resourceSlot]) return false;
        var before = capture.InventoryBefore[resourceSlot];
        if (before.IsEmpty || !before.IsCanonical || before.Prefix.Value != 0 ||
            (definition.Kind == WiringToolKind1458.Wire
                ? before.ItemType.Value != VanillaWiringToolCatalog1458.WireItem
                : definition.Kind != WiringToolKind1458.Actuator || resourceSlot != capture.Actor.Player.SelectedItem ||
                    before.ItemType.Value != VanillaWiringToolCatalog1458.ActuatorItem)) return false;
        after = before.Stack == 1 ? default : before with { Stack = (short)(before.Stack - 1) };
        if (!RuntimePlayerInventoryItem.TryFromNormalized(report, out var claimed) || claimed != after) return false;
        var mutation = new RemotePhaseInventoryMutation(capture.Connection, resourceSlot, before, after)
            { ConsumeFundedQuantity = true, WiringResourceDebit = true };
        return inventory.TryPrepareRemotePhase([mutation], inventory.Serial, out preparation);
    }

    internal bool TryCaptureAcceptedWiringInventory(RuntimePlayerToolActorCapture1458 original,
        RuntimePlayerToolActorCapture1458 accepted, short resourceSlot, in RuntimePlayerInventoryItem inventoryAfter,
        out RuntimePlayerToolActorCapture1458? refreshed)
    {
        refreshed = null;
        if (resourceSlot is < 0 or >= 58 || !membership.TryGet(original.Connection, out var member) ||
            member.CaptureSnapshot() != accepted.Actor.Player || member.ItemPhase != accepted.Actor.ItemPhase ||
            member.ProjectileUseInputRevision != accepted.Actor.InputRevision || playerUpdateWorld != original.World ||
            !transferProfiles.IsCurrentBuffState(original.Connection, original.BeforeBuffs) ||
            !accepted.IsCurrentRandom(playerUpdateRandom, playerUpdateRandomOwned) ||
            !inventory.TryCaptureShopJournal(original.Connection, out var image) || image is null ||
            !TryCaptureProjectileUse(original.Connection, out var actor) || actor is null ||
            image.Before.Length != original.InventoryBefore.Length ||
            !image.Copper.AsSpan().SequenceEqual(original.FundedCopperBefore) ||
            !image.Caps.AsSpan().SequenceEqual(original.QuantityCapsBefore)) return false;
        var quantities = image.Quantity;
        if (quantities.Length != original.FundedQuantitiesBefore.Length) return false;
        for (int index = 0; index < image.Before.Length; index++)
            if (image.Before[index] != (index == resourceSlot ? inventoryAfter : original.InventoryBefore[index]) ||
                quantities[index] != original.FundedQuantitiesBefore[index] - (index == resourceSlot ? 1 : 0)) return false;
        var candidate = new RuntimePlayerToolActorCapture1458(this, actor, image, original.BeforeBuffs,
            image.Before[actor.Player.SelectedItem], original.CapturedUpdate, playerUpdateRandom!, original.World);
        if (!candidate.IsCurrent) return false;
        refreshed = candidate;
        return true;
    }

    internal bool TryPublishWiringInventory(ConnectionHandle connection, short resourceSlot,
        in RuntimePlayerInventoryItem expected)
    {
        if (!membership.TryGet(connection, out var member) ||
            member.ItemPhase?.ToolUse is not { WiringReceipt: { } receipt } tools ||
            receipt.Connection != connection || receipt.Slot != resourceSlot || receipt.After != expected ||
            receipt.ActionSerial != tools.ActionSerial || !inventory.TryGet(connection, resourceSlot, out var current) || current != expected) return false;
        AppliedEquipmentUpdates++;
        var report = expected.ToCommitRequest(connection.Player.Slot, resourceSlot);
        events?.PlayerEquipmentUpdated(connection, in report);
        return true;
    }
}
