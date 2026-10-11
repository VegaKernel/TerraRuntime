using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal bool HasDropFunding(ConnectionHandle connection)
    {
        if (!membership.TryGet(connection, out var member) || !inventory.TryGet(connection, 58, out var mouse)) return false;
        int slot = mouse.IsEmpty ? member.SelectedItem : 58;
        return inventory.TryGetFundedQuantity(connection, slot, out var quantity) && quantity > 0;
    }

    internal bool TryPrepareFundedDrop(ConnectionHandle connection, in WorldItemDropStateUpdate proposal,
        out FundedDropPreparation? preparation)
    {
        preparation = null;
        if (!HasOwnedPickupContext || !membership.TryGet(connection, out var member) || member.ItemPhase is not { } phase || phase.Channel ||
            member.Revision == ulong.MaxValue) return false;
        var state = member.CaptureSnapshot();
        if (state.IsDead || (state.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) != 0 ||
            IsPickupOutOfRange(in state) || !inventory.TryGet(connection, 58, out var mouse)) return false;
        short slot = mouse.IsEmpty ? state.SelectedItem : (short)58;
        if (!inventory.TryPrepareFundedDrop(connection, slot, out var items) || items is null) return false;
        var item = items.Item;
        var body = state.HasMount ? VanillaPlayerMountHitbox1458.Resolve(state.MountType) : (Width: 20f, Height: 42f);
        int direction = (state.ControlFlags & 64) != 0 ? 1 : -1;
        // Source DropSelectedItem requests NewItem at player Center; the world item's 16x16 body is centered there.
        var expected = new WorldItemDropStateUpdate(state.PositionX + body.Width / 2f - VanillaWorldItemOwner1458.PhysicalWidth / 2f,
            state.PositionY + body.Height / 2f - VanillaWorldItemOwner1458.PhysicalHeight / 2f,
            direction * 4f + state.VelocityX, -2f, item.Stack, checked((byte)item.Prefix.Value),
            WorldItemOwnershipMode.GrabDelayForLocalPlayer, checked((short)item.ItemType.Value), false, 0f, 100);
        if (proposal != expected) return false;
        preparation = new(this, member, state, phase, items);
        return true;
    }

    internal sealed class FundedDropPreparation(PlayerAuthority owner, RuntimePlayerMember member,
        PlayerStateSnapshot before, RuntimePlayerItemPhase1458 phase, RuntimePlayerInventoryStore.FundedDropPreparation items)
    {
        private bool adopted;
        private bool published;
        internal bool IsCurrent => !adopted && owner.HasOwnedPickupContext && owner.membership.TryGet(member.Connection, out var current) &&
            ReferenceEquals(current, member) && member.CaptureSnapshot() == before && member.ItemPhase == phase &&
            member.Revision != ulong.MaxValue && items.IsCurrent;
        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent) return false;
            if (!member.TryAdvanceRevision() || !items.TryAdoptUnpublished())
                throw new InvalidOperationException("Prepared funded drop lost callback-free inventory adoption.");
            member.ItemPhase = phase with { Selected = phase.Selected with { Animation = 10, AnimationMax = 10 } };
            member.ItemAnimation = 10;
            adopted = true;
            return true;
        }
        internal void Publish()
        {
            if (!adopted || published) return;
            published = true;
            if (!owner.membership.TryGet(member.Connection, out var current) || !ReferenceEquals(current, member) ||
                !items.IsAcceptedCurrent) return;
            var request = default(RuntimePlayerInventoryItem).ToCommitRequest(member.Connection.Player.Slot, items.Slot);
            owner.events?.PlayerEquipmentUpdated(member.Connection, in request);
        }
    }
}
