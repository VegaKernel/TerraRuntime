using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal readonly record struct RuntimePlayerPlacementReceipt1458(ConnectionHandle Connection,
    short Slot, RuntimePlayerInventoryItem Before, RuntimePlayerInventoryItem After,
    ulong ActionSerial);

internal sealed partial class PlayerAuthority
{
    internal bool TryPreparePlacementPayment(RuntimePlayerToolActorCapture1458 capture,
        in LocalPlacementTail1458 tail, out RuntimePlayerInventoryStore.RemotePhasePreparation? preparation,
        out RuntimePlayerInventoryItem after)
    {
        preparation = null; after = default;
        if (!capture.IsCurrent || !(capture.HasCurrentSourceStage ||
                capture.Phase.ToolUse?.Input is { IsPlacement: true } preserved && preserved.HasKnownPlacementStage(capture)) ||
            capture.Inventory.HasPendingCursorSource ||
            capture.Phase.ToolUse is not { SourcePlacement: { } definition } tools ||
            definition.Item != capture.Selected.ItemType || capture.Selected.Prefix.Value != 0 ||
            capture.InventoryBefore[capture.Actor.Player.SelectedItem] != capture.Selected ||
            !inventory.TryGetFundedQuantity(capture.Connection, capture.Actor.Player.SelectedItem, out var currentFunding) ||
            currentFunding != capture.FundedQuantitiesBefore[capture.Actor.Player.SelectedItem] ||
            capture.Actor.Player.SelectedItem != tools.SourceSlot ||
            tail.ConsumedQuantity is < 1 or > 5 || tail.RemainingStack < 0 ||
            tail.ConsumedQuantity + tail.RemainingStack != capture.Selected.Stack ||
            tools.Input is not { IsPlacement: true, PlacementInventoryReport: { } report } input ||
            !input.IsPossiblePlacementSlot(tools.SourceSlot) || report.SlotId != tools.SourceSlot ||
            report.PlayerSlot != capture.Connection.Player.Slot || report.Stack < 0) return false;
        after = tail.RemainingStack == 0 ? default : capture.Selected with { Stack = checked((short)tail.RemainingStack) };
        if (!RuntimePlayerInventoryItem.TryFromNormalized(report, out var claimed) || claimed != after) return false;
        var mutation = new RemotePhaseInventoryMutation(capture.Connection, tools.SourceSlot,
            capture.Selected, after) { ConsumeFundedQuantity = true,
                PlacementQuantity = checked((short)tail.ConsumedQuantity) };
        return inventory.TryPrepareRemotePhase([mutation], inventory.Serial, out preparation);
    }

    private bool TryAcknowledgePlacementInventory(PlayerEquipmentRuntimeCommand command)
    {
        if (!membership.TryGet(command.Connection, out var member) ||
            member.ItemPhase?.ToolUse is not { PlacementReceipt: { } receipt } tools ||
            receipt.Connection != command.Connection || receipt.ActionSerial != tools.ActionSerial ||
            receipt.Slot != command.Request.SlotId || command.Request.Stack < 0 ||
            command.Request.PlayerSlot != command.Connection.Player.Slot ||
            !RuntimePlayerInventoryItem.TryFromNormalized(command.Request, out var item) || item != receipt.After ||
            !inventory.TryGet(command.Connection, receipt.Slot, out var current) || current != receipt.After) return false;
        AppliedEquipmentUpdates++;
        return true;
    }

    internal bool TryPublishPlacementInventory(ConnectionHandle connection, short slot,
        in RuntimePlayerInventoryItem expected)
    {
        if (!membership.TryGet(connection, out _) || !inventory.TryGet(connection, slot, out var current) ||
            current != expected) return false;
        var report = expected.ToCommitRequest(connection.Player.Slot, slot);
        events?.PlayerEquipmentUpdated(connection, in report);
        return true;
    }
    internal bool TryCaptureAcceptedPlacementInventory(RuntimePlayerToolActorCapture1458 original,
        RuntimePlayerToolActorCapture1458 accepted, in RuntimePlayerInventoryItem inventoryAfter,
        int consumed, out RuntimePlayerToolActorCapture1458? refreshed)
    {
        refreshed = null;
        if (consumed is < 1 or > 5 || !membership.TryGet(original.Connection, out var member) ||
            member.CaptureSnapshot() != accepted.Actor.Player || member.ItemPhase != accepted.Actor.ItemPhase ||
            member.ProjectileUseInputRevision != accepted.Actor.InputRevision || playerUpdateWorld != original.World ||
            !transferProfiles.IsCurrentBuffState(original.Connection, original.BeforeBuffs) ||
            !accepted.IsCurrentRandom(playerUpdateRandom, playerUpdateRandomOwned) ||
            !inventory.TryCaptureShopJournal(original.Connection, out var image) || image is null ||
            !TryCaptureProjectileUse(original.Connection, out var actor) || actor is null) return false;
        int slot = original.Actor.Player.SelectedItem;
        if (image.Before.Length != original.InventoryBefore.Length ||
            !image.Copper.AsSpan().SequenceEqual(original.FundedCopperBefore) ||
            !image.Caps.AsSpan().SequenceEqual(original.QuantityCapsBefore)) return false;
        var quantities = image.Quantity;
        if (quantities.Length != original.FundedQuantitiesBefore.Length) return false;
        for (int index = 0; index < image.Before.Length; index++)
        {
            if (image.Before[index] != (index == slot ? inventoryAfter : original.InventoryBefore[index]) ||
                quantities[index] != original.FundedQuantitiesBefore[index] - (index == slot ? consumed : 0)) return false;
        }
        var candidate = new RuntimePlayerToolActorCapture1458(this, actor, image, original.BeforeBuffs,
            inventoryAfter, original.CapturedUpdate, playerUpdateRandom!, original.World);
        if (!candidate.IsCurrent) return false;
        refreshed = candidate;
        return true;
    }

    internal PlacementInventoryPublication1458? CapturePlacementInventoryPublication(
        ConnectionHandle connection, short slot, in RuntimePlayerInventoryItem after)
    {
        if (!membership.TryGet(connection, out var member) ||
            member.ItemPhase?.ToolUse is not { PlacementReceipt: { } receipt } tools ||
            receipt.Connection != connection || receipt.Slot != slot || receipt.After != after ||
            receipt.ActionSerial != tools.ActionSerial ||
            !inventory.TryCaptureShopJournal(connection, out var image) || image is null ||
            image.Before[slot] != after) return null;
        AppliedEquipmentUpdates++;
        return new(this, connection, member, member.CaptureSnapshot(), member.ItemPhase, image, slot, after);
    }

    internal sealed class PlacementInventoryPublication1458(PlayerAuthority owner,
        ConnectionHandle connection, RuntimePlayerMember member, PlayerStateSnapshot player,
        RuntimePlayerItemPhase1458? phase, RuntimePlayerInventoryStore.ShopJournalCapture image,
        short slot, RuntimePlayerInventoryItem after)
    {
        private bool published;
        internal bool TryPublish()
        {
            if (published) return false;
            published = true;
            if (!owner.membership.TryGet(connection, out var current) || !ReferenceEquals(current, member) ||
                current.CaptureSnapshot() != player || current.ItemPhase != phase || !image.IsCurrent) return false;
            return owner.TryPublishPlacementInventory(connection, slot, after);
        }
    }

}
