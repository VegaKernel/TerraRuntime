using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class WorldItemAuthority
{
    private bool TryApplyFundedDrop(WorldItemAllocateRuntimeCommand command, out WorldItemSnapshot snapshot)
    {
        snapshot = default;
        if (command.State.Ownership == WorldItemOwnershipMode.ReserveForLocalPlayer)
            return TryApplyInventoryClose(command, out snapshot);
        if (ownedPickupTransactionRunning || !players.TryPrepareFundedDrop(command.Connection, command.State, out var inventory) ||
            inventory is null) return false;
        ownedPickupTransactionRunning = true;
        try
        {
            var captures = players.CaptureItemOwnerPlayers();
            var views = captures.Select(c =>
            {
                var body = c.State.HasMount ? VanillaPlayerMountHitbox1458.Resolve(c.State.MountType) : (Width: 20f, Height: 42f);
                return new WorldItemAllocationPlayer1458(c.State.Player.Slot.Value, c.State.PositionX, c.State.PositionY, (int)body.Width, (int)body.Height);
            }).ToArray();
            using var allocation = worldItems.CreateAllocationPreview(views);
            if (!allocation.TrySpawnSource(command.State, 0, out var slot, command.Connection.Player.Slot.Value,
                    WorldItemCreationSource1458.NewItem) || slot >= worldItems.Capacity || !allocation.TryClaim() ||
                !players.IsCurrentItemOwnerPlayers(captures) || !inventory.IsCurrent) return false;
            if (!allocation.TryAdoptUnpublished(out var publication) || publication is null || !inventory.TryAdoptUnpublished())
                throw new InvalidOperationException("Prepared funded drop lost callback-free joint adoption.");
            if (slot < worldItems.Capacity) _ = worldItems.TryGetActive(slot, out snapshot);
            Exception? failure = null;
            try { while (publication.TryPublishNext(out _)) { } }
            catch (Exception ex) { failure = ex; }
            try { inventory.Publish(); }
            catch (Exception ex) { failure ??= ex; }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            return true;
        }
        finally { ownedPickupTransactionRunning = false; }
    }
}
