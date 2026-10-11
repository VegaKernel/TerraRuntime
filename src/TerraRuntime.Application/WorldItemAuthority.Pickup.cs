using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class WorldItemAuthority
{
    // Server-owned contact pickup is an authority policy. Original GrabItems calls PickupItem
    // only for Main.myPlayer; reservation radius/attraction do not prove contact.
    private bool ownedPickupTickRunning;
    private bool ownedPickupTransactionRunning;
    internal void TickOwnedHumanPickups()
    {
        if (ownedPickupTickRunning || ownedPickupTransactionRunning || worldItems.ActiveCount == 0 ||
            !players.HasRemotePlayerPhaseBinding || !players.HasOwnedPickupContext) return;
        ownedPickupTickRunning = true;
        try
        {
            var scan = new WorldItemSnapshot[RuntimeWorldItemStore.VanillaCapacity];
            int count = worldItems.CopyActive(scan);
            Exception? failure = null;
            for (int index = 0; index < count; index++)
            {
                var item = scan[index];
                if (!worldItems.TryGetActive(item.Handle.Slot, out var current) || current != item) continue;
                try { _ = TryOwnedContactPickup(item, null, null); }
                catch (Exception ex) { failure ??= ex; }
            }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        finally { ownedPickupTickRunning = false; }
    }

    // null means this item is outside represented storage/contact authority and keeps
    // the existing compatibility path; false is a represented proposal refusal.
    private bool? TryOwnedContactPickup(WorldItemSnapshot item, ConnectionHandle? requestedBy,
        WorldItemDropStateUpdate? proposedResidual)
    {
        if (ownedPickupTransactionRunning) return false;
        ownedPickupTransactionRunning = true;
        try { return TryPlanAndAdoptOwnedContactPickup(item, requestedBy, proposedResidual); }
        finally { ownedPickupTransactionRunning = false; }
    }

    private bool? TryPlanAndAdoptOwnedContactPickup(WorldItemSnapshot item, ConnectionHandle? requestedBy,
        WorldItemDropStateUpdate? proposedResidual)
    {
        if (!item.TryGetItemType(out var type) ||
            !VanillaWorldItemPickupCatalog1458.TryGet(type.Value, out var facts) ||
            facts.Pickup || facts.NotInventory) return null;
        // Constructor clocks alone do not establish the source-owned pickup role.
        if (!players.HasRemotePlayerPhaseBinding) return null;
        if (!players.HasOwnedPickupContext) return false;
        if (item.OwnerPlayerId == byte.MaxValue || !players.TryGet(item.OwnerPlayerId, out var member)) return null;
        if (member.ItemPhase is not { } phase) return false;
        var state = member.CaptureSnapshot();
        if (requestedBy is { } sender && sender != member.Connection) return false;
        if (players.IsPickupOutOfRange(in state) || state.IsDead || (state.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) != 0 || item.ShimmerTime != 0f ||
            (item.GrabDelayTime > 0 && (item.GrabDelayPlayer == byte.MaxValue || item.GrabDelayPlayer == state.Player.Slot.Value)) ||
            (item.Shimmered && (players.PickupWindowsArithmetic is not { } windows || !((double)(float)Math.Sqrt(windows
                ? (double)item.VelocityX * item.VelocityX + (double)item.VelocityY * item.VelocityY
                : (float)((float)(item.VelocityX * item.VelocityX) + (float)(item.VelocityY * item.VelocityY))) < .2)))) return false;
        if (!players.TryGetInventoryItem(member.Connection, state.SelectedItem, out var held) ||
            (held.IsEmpty && phase.Selected.Animation > 0)) return false;
        var body = state.HasMount ? VanillaPlayerMountHitbox1458.Resolve(state.MountType) : (Width: 20f, Height: 42f);
        if (!float.IsFinite(state.PositionX) || !float.IsFinite(state.PositionY) ||
            (int)state.PositionX >= (int)item.PositionX + VanillaWorldItemOwner1458.PhysicalWidth ||
            (int)state.PositionX + body.Width <= (int)item.PositionX ||
            (int)state.PositionY >= (int)item.PositionY + VanillaWorldItemOwner1458.PhysicalHeight ||
            (int)state.PositionY + body.Height <= (int)item.PositionY) return false;
        if (!worldItems.TryGetFundedQuantity(in item, out int funded) || funded <= 0) return false;
        // Converted denominations cannot represent a mixed trusted/unknown ground stack.
        if (type.Value is >= 71 and <= 74 && funded != item.Stack) return false;
        // Capture the bounded whole census only after cheap reservation and contact gates.
        // The exact member/phase used above must also match the detached capture.
        var captures = players.CaptureItemOwnerPlayers();
        var capture = captures.FirstOrDefault(c => c.Connection == member.Connection);
        if (capture is null || !capture.HasInventory || capture.State != state || member.ItemPhase != phase) return false;
        if (!facts.IgnoresEncumbering)
            for (int slot = 0; slot < VanillaPlayerItemSlotCatalog.OrdinaryInventoryCount; slot++)
                if (capture.Inventory[slot].ItemType.Value == 4346 && capture.Inventory[slot].Stack > 0) return false;
        var slots = capture.Inventory.Select(i => new WorldItemPickupSlot1458(i.ItemType.Value, i.Stack,
            i.Prefix.Value, (i.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0)).ToArray();
        var incoming = new WorldItemPickupSlot1458(type.Value, funded, item.Prefix, false);
        // No ordinary-to-void fallback is invented for dedicated remote inventory.
        if (!VanillaWorldItemOwner1458.TryPlanStorage(in incoming, slots, false, players.PickupWorld, out var storage) || storage is null) return null;
        if (storage.Taken <= 0) return false;
        if (requestedBy is not null)
        {
            if (proposedResidual is null && item.Stack != storage.Taken) return false;
            if (proposedResidual is { } residual && (item.Stack == storage.Taken || residual != new WorldItemDropStateUpdate(
                item.PositionX, item.PositionY, item.VelocityX, item.VelocityY, checked((short)(item.Stack - storage.Taken)),
                item.Prefix, item.Ownership, item.ItemNetId, item.Shimmered, item.ShimmerTime, item.EnemyGrabDelayTime))) return false;
        }
        var tuple = new RuntimePlayerInventoryItem(type,
            type.Value is >= 71 and <= 74 ? checked((short)funded) : item.Stack, item.PrefixId, 0);
        if (!players.TryPrepareOwnedPickup(capture, storage, tuple, out var inventory) || inventory is null ||
            !TryPrepareTrustedTake(item, storage.Taken, out var world, requireFundedQuantity: true) || world is null) return false;
        using (world)
        {
            if (!world.TryClaim() || !players.IsCurrentItemOwnerPlayers(captures) || !inventory.IsCurrent) return false;
            if (!world.TryAdoptUnpublished(out var publication) || publication is null || !inventory.TryAdoptUnpublished())
                throw new InvalidOperationException("Prepared pickup lost callback-free world adoption.");
            Exception? failure = null;
            try { while (publication.TryPublishNext(out _)) { } }
            catch (Exception ex) { failure = ex; }
            try { inventory.Publish(); }
            catch (Exception ex) { failure ??= ex; }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            return true;
        }
    }
}
