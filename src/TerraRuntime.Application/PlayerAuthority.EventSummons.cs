using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private readonly EventUseReceipt?[] eventUseReceipts = new EventUseReceipt?[MaxPlayerSlots];
    private readonly record struct EventUseReceipt(ConnectionHandle Connection, long Tick);

    // Player.ItemCheck_UseEventItems sends packet61 before the later inventory sync.
    // Treat it as an intent: only existing server-funded quantity pays for the event.
    internal bool TryPrepareEventSummon(ConnectionHandle connection, int itemType,
        out EventSummonPreparation? preparation)
    {
        preparation = null;
        return itemType is 361 or 602 or 1315 or 1844 or 1958 or 2767 or 4271 &&
            TryPreparePaidSummon(connection, itemType, 45, out preparation);
    }

    private bool TryPreparePaidSummon(ConnectionHandle connection, int itemType, int useTicks,
        out EventSummonPreparation? preparation)
    {
        preparation = null;
        if (useTicks <= 0 || !TryCaptureProjectileUse(connection, out var capture) || capture is null ||
            capture.Member is not { HasHealth: true, IsDead: false } member ||
            member.ItemPhase is not { } phase || phase.ShopOpen ||
            capture.Buffs is null || capture.Buffs.Any(type => type.Value == 23) ||
            member.SelectedItem >= 58 ||
            !inventory.TryGet(connection, member.SelectedItem, out var selected) ||
            !selected.IsCanonical || selected.IsEmpty || selected.ItemType.Value != itemType ||
            selected.Prefix.Value != 0 ||
            !inventory.TryGetFundedQuantity(connection, member.SelectedItem, out short quantity) || quantity <= 0 ||
            inventory.Serial >= ulong.MaxValue - 1) return false;
        // All seven source event items have useAnimation/useTime45 (Item.SetDefaults1..5).
        // A repeated report cannot spend another item inside the same accepted use.
        if (eventUseReceipts[connection.Player.Slot.Value] is { } receipt && receipt.Connection == connection &&
            (currentCombatTick < receipt.Tick || currentCombatTick - receipt.Tick < useTicks)) return false;
        var after = selected.Stack == 1 ? default : selected with { Stack = (short)(selected.Stack - 1) };
        preparation = new(this, capture, new(member.SelectedItem, after), currentCombatTick);
        return preparation.IsCurrent;
    }

    internal sealed class EventSummonPreparation(PlayerAuthority owner,
        RuntimePlayerProjectileUseCapture capture, RuntimePlayerInventoryMutation mutation, long tick)
    {
        private bool adopted;
        private bool published;
        private ulong acceptedSerial;
        internal bool IsCurrent => !adopted && owner.currentCombatTick == tick &&
            owner.IsCurrentProjectileUse(capture) &&
            owner.inventory.TryGetFundedQuantity(capture.Connection, mutation.Slot, out var quantity) && quantity > 0 &&
            owner.inventory.CanApplyProjectileConsumption(capture.Connection, mutation, capture.InventorySerial);

        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent || !owner.inventory.TryApplyProjectileConsumption(capture.Connection, mutation,
                capture.InventorySerial)) return false;
            adopted = true;
            acceptedSerial = owner.inventory.Serial;
            owner.eventUseReceipts[capture.Connection.Player.Slot.Value] = new(capture.Connection, tick);
            return true;
        }

        internal void Publish()
        {
            if (!adopted || published || !owner.membership.TryGet(capture.Connection, out var member) ||
                !ReferenceEquals(member, capture.Member) || owner.inventory.Serial != acceptedSerial ||
                !owner.inventory.TryGet(capture.Connection, mutation.Slot, out var current) || current != mutation.Item)
                return;
            published = true;
            var request = mutation.Item.ToCommitRequest(capture.Connection.Player.Slot, mutation.Slot);
            owner.events?.PlayerEquipmentUpdated(capture.Connection, in request);
        }
    }
}
