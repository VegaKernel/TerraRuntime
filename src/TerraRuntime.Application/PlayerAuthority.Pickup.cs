using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal bool HasOwnedPickupContext => playerUpdateRandomOwned && playerUpdateWorld is not null;
    internal VanillaItemPrefixWorld1458? PickupWorld => receivePrefixWorld;
    internal bool? PickupWindowsArithmetic => playerUpdateWorld?.WindowsItemPrefixArithmetic;
    internal bool IsPickupOutOfRange(in PlayerStateSnapshot state) => playerUpdateWorld is { } world &&
        VanillaPlayerHealthContext1458.IsRemoteOutOfRange(state.PositionX, state.PositionY, 20, 42, world.MaxTilesX, world.MaxTilesY);
    internal bool TryPrepareOwnedPickup(RuntimeItemOwnerPlayerCapture1458 capture,
        WorldItemStoragePlan1458 storage, RuntimePlayerInventoryItem incoming, out OwnedPickupPreparation? prepared, long fundedCoinCredit = 0)
    {
        prepared = null;
        if (!membership.TryGet(capture.Connection, out var member) || member.CaptureSnapshot() != capture.State ||
            member.ItemPhase is not { } phase || member.Revision == ulong.MaxValue) return false;
        var mutations = storage.Mutations.Select(m => new RemotePhaseInventoryMutation(capture.Connection, m.Slot,
            capture.Inventory[m.Slot], incoming.ItemType.Value is >= 71 and <= 74 ? FromStorage(m.After) :
            FromStorage(m.After) with { ItemFlags = capture.Inventory[m.Slot].IsEmpty ? incoming.ItemFlags : capture.Inventory[m.Slot].ItemFlags })).ToArray();
        if (!inventory.TryPreparePickup(capture.Connection, capture.Inventory, mutations, incoming, storage.Taken, out var items, fundedCoinCredit) || items is null) return false;
        prepared = new(this, member, capture.State, phase, items, mutations);
        return true;
    }
    private static RuntimePlayerInventoryItem FromStorage(WorldItemPickupSlot1458 item) => item.Type == 0 ? default :
        new(new(item.Type), checked((short)item.Stack), new(item.Prefix), item.Favorite ? PlayerEquipmentCommitRequest.FavoriteItemFlag : (byte)0);

    internal sealed class OwnedPickupPreparation(PlayerAuthority owner, RuntimePlayerMember member,
        PlayerStateSnapshot before, RuntimePlayerItemPhase1458 phase,
        RuntimePlayerInventoryStore.PickupPreparation items, RemotePhaseInventoryMutation[] mutations)
    {
        private bool adopted;
        private bool published;
        internal bool IsCurrent => !adopted && owner.membership.TryGet(member.Connection, out var current) &&
            ReferenceEquals(current, member) && member.CaptureSnapshot() == before && member.ItemPhase == phase &&
            member.Revision != ulong.MaxValue && items.IsCurrent;
        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent) return false;
            if (!member.TryAdvanceRevision() || !items.TryAdoptUnpublished())
                throw new InvalidOperationException("Prepared owned pickup lost callback-free inventory adoption.");
            adopted = true;
            return true;
        }
        internal void Publish()
        {
            if (!adopted || published) return;
            published = true;
            Exception? failure = null;
            for (int index = 0; index < mutations.Length; index++)
            {
                if (!owner.membership.TryGet(member.Connection, out var current) || !ReferenceEquals(current, member) ||
                    !items.IsAcceptedItemCurrent(index)) continue;
                var request = mutations[index].After.ToCommitRequest(member.Connection.Player.Slot, mutations[index].Slot);
                try { owner.events?.PlayerEquipmentUpdated(member.Connection, in request); }
                catch (Exception ex) { failure ??= ex; }
            }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
