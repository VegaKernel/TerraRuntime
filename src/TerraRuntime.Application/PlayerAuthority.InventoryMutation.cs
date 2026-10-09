using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    // A bounded inventory write for a coupled world operation. Public component writes retain
    // their existing immediate publication behavior; this token has no callback during adoption.
    internal bool TryPrepareInventoryMutation(ConnectionHandle connection, short slot,
        in RuntimePlayerInventoryItem expectedItem, in RuntimePlayerInventoryItem nextItem,
        out InventoryMutationPreparation? preparation)
    {
        preparation = null;
        ulong serial = inventory.Serial;
        if (serial >= ulong.MaxValue - 1 ||
            !membership.TryGet(connection, out var member) || member.Revision == ulong.MaxValue ||
            member.ProjectileUseInputRevision == ulong.MaxValue ||
            !inventory.TryIsCurrent(connection, serial) || !nextItem.IsCanonical ||
            !inventory.TryGet(connection, slot, out var current) || current != expectedItem)
            return false;

        preparation = new(this, member, slot, expectedItem, nextItem, serial);
        return true;
    }

    internal sealed class InventoryMutationPreparation
    {
        private readonly PlayerAuthority owner;
        private readonly RuntimePlayerMember member;
        private readonly ConnectionHandle connection;
        private readonly PlayerStateSnapshot player;
        private readonly RuntimePlayerItemPhase1458? itemPhase;
        private readonly ulong inputRevision;
        private readonly ulong expectedSerial;
        private readonly RuntimePlayerInventoryItem expectedItem;
        private readonly RuntimePlayerInventoryMutation mutation;
        private ulong acceptedSerial;
        private bool adopted;
        private bool published;

        internal InventoryMutationPreparation(PlayerAuthority owner, RuntimePlayerMember member,
            short slot, RuntimePlayerInventoryItem expectedItem, RuntimePlayerInventoryItem nextItem, ulong serial)
        {
            this.owner = owner;
            this.member = member;
            connection = member.Connection;
            player = member.CaptureSnapshot();
            itemPhase = member.ItemPhase;
            inputRevision = member.ProjectileUseInputRevision;
            expectedSerial = serial;
            this.expectedItem = expectedItem;
            mutation = new(slot, nextItem);
        }

        private bool IsMemberCurrent =>
            owner.membership.TryGet(connection, out var current) && ReferenceEquals(current, member) &&
            current.CaptureSnapshot() == player && current.ItemPhase == itemPhase &&
            current.ProjectileUseInputRevision == inputRevision;

        // Reserve a serial after adoption. At the saturated value legacy component writes
        // cannot advance the stamp, so it cannot prove currentness across publication.
        internal bool IsCurrent => !adopted && expectedSerial < ulong.MaxValue - 1 && IsMemberCurrent &&
            owner.inventory.TryIsCurrent(connection, expectedSerial) &&
            owner.inventory.TryGet(connection, mutation.Slot, out var current) && current == expectedItem;

        internal bool IsAcceptedCurrent => adopted && IsMemberCurrent &&
            owner.inventory.Serial == acceptedSerial &&
            owner.inventory.TryGet(connection, mutation.Slot, out var current) && current == mutation.Item;

        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent) return false;
            Span<RuntimePlayerInventoryMutation> writes = stackalloc RuntimePlayerInventoryMutation[1];
            writes[0] = mutation;
            if (!owner.inventory.TryApplyAtomic(connection, writes, expectedSerial)) return false;
            adopted = true;
            acceptedSerial = owner.inventory.Serial;
            return true;
        }

        internal bool TryPublish()
        {
            if (published || !IsAcceptedCurrent) return false;
            published = true;
            var request = mutation.Item.ToCommitRequest(connection.Player.Slot, mutation.Slot);
            owner.events?.PlayerEquipmentUpdated(connection, in request);
            return true;
        }
    }
}
