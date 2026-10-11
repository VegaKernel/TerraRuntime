using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class WorldItemAuthority
{
    private bool TryApplyInventoryClose(WorldItemAllocateRuntimeCommand command,
        out WorldItemSnapshot snapshot)
    {
        snapshot = default;
        if (ownedPickupTransactionRunning ||
            !players.TryPrepareInventoryClose(command.Connection, command.State, out var inventory) ||
            inventory is null || !command.State.TryGetItemType(out var type) ||
            !VanillaWorldItemOwner1458.TryGetSourceCreationNoGravity(type, out bool noGravity) ||
            !IsSourceSpawnVelocity(command.State.VelocityX, -30, 31) ||
            !IsSourceSpawnVelocity(command.State.VelocityY, noGravity ? -30 : -40, noGravity ? 31 : -15))
            return false;

        // Client NewItem and server NewItem own different random streams. Retain
        // the two server offers followed by the source-compatible velocity overlay.
        if (SpawnRandom is not SystemWorldItemSpawnRandom source ||
            command.State.Prefix != VanillaPrefixIds.NoneValue)
            return false;
        var live = source.SourceRandom;
        var before = live.Clone();
        var after = live.Clone();
        _ = after.Next(-30, 31);
        _ = noGravity ? after.Next(-30, 31) : after.Next(-40, -15);
        var created = command.State;
        ownedPickupTransactionRunning = true;
        try
        {
            var captures = players.CaptureItemOwnerPlayers();
            var views = captures.Select(capture =>
            {
                var state = capture.State;
                var body = state.HasMount
                    ? VanillaPlayerMountHitbox1458.Resolve(state.MountType)
                    : (Width: 20f, Height: 42f);
                return new WorldItemAllocationPlayer1458(state.Player.Slot.Value,
                    state.PositionX, state.PositionY, (int)body.Width, (int)body.Height);
            }).ToArray();
            using var allocation = worldItems.CreateAllocationPreview(views);
            if (!allocation.TrySpawnSource(created, 0, out var slot,
                    command.Connection.Player.Slot.Value, WorldItemCreationSource1458.NewItem) ||
                slot >= worldItems.Capacity)
                return false;
            if (!allocation.TryClaim() || !players.IsCurrentItemOwnerPlayers(captures) ||
                !inventory.IsCurrent || !live.HasSameState(before))
                return false;

            // Providers have returned before this pure tail. Ground funding,
            // cursor debit, complete storage journal and RNG precede all observers.
            if (!allocation.TryAdoptUnpublished(out var publication) || publication is null ||
                !inventory.TryAdoptUnpublished())
                throw new InvalidOperationException("Prepared inventory close lost callback-free joint adoption.");
            live.CopyStateFrom(after);
            if (!worldItems.TryGetActive(slot, out snapshot))
                throw new InvalidOperationException("Adopted inventory close has no active ground item.");

            Exception? failure = null;
            try
            {
                while (publication.TryPublishNext(out _)) { }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            try
            {
                inventory.Publish();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
            if (failure is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            return true;
        }
        finally
        {
            ownedPickupTransactionRunning = false;
        }
    }

    private static bool IsSourceSpawnVelocity(float value, int inclusiveMin, int exclusiveMax)
    {
        if (!float.IsFinite(value))
            return false;
        int bits = BitConverter.SingleToInt32Bits(value);
        for (int offer = inclusiveMin; offer < exclusiveMax; offer++)
            if (BitConverter.SingleToInt32Bits(offer * 0.1f) == bits)
                return true;
        return false;
    }
}
