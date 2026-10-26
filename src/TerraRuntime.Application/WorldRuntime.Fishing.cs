using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

/// <summary>An owner-thread observation used to bind one crate-open intent to existing stock.</summary>
public readonly record struct FishingCrateSlot1458(ConnectionHandle Connection, short Slot,
    ulong InventoryRevision, int ItemType, short Stack);

internal sealed record CaptureFishingCrateRuntimeCommand(ConnectionHandle Connection, short Slot,
    TaskCompletionSource<FishingCrateSlot1458?> Completion) : RuntimeCommand;

public sealed partial class WorldRuntime
{
    /// <summary>Captures the existing inventory revision; it does not reserve or spend a crate.</summary>
    public async ValueTask<FishingCrateSlot1458?> CaptureFishingCrateAsync(ConnectionHandle connection,
        short slot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<FishingCrateSlot1458?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        if (!CommandIngress.TryPost(GameCommandSourceId.System, new CaptureFishingCrateRuntimeCommand(connection, slot, completion)))
            throw new InvalidOperationException("The authoritative queue rejected the crate observation.");
        return await completion.Task.ConfigureAwait(false);
    }

    /// <summary>Opens one funded crate only if the captured occupation and inventory revision remain current.</summary>
    public async ValueTask<bool> OpenFishingCrateAsync(FishingCrateSlot1458 observed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        if (!CommandIngress.TryPost(GameCommandSourceId.System, new OpenFishingCrateRuntimeCommand(observed.Connection,
            observed.Slot, observed.InventoryRevision, completion)))
            throw new InvalidOperationException("The authoritative queue rejected the crate-open intent.");
        return await completion.Task.ConfigureAwait(false);
    }
}

internal sealed partial class PlayerAuthority
{
    private FishingCrateSlot1458? CaptureFishingCrate(ConnectionHandle connection, short slot)
    {
        if (slot is < 0 or >= 58 || !membership.IsCurrent(connection) ||
            !inventory.TryGet(connection, slot, out var item) || item.IsEmpty ||
            !TerraRuntime.Gameplay.Items.VanillaFishingCrate1458.IsCrate(item.ItemType.Value) ||
            !inventory.CanSpendFishingStock(connection, slot, item.ItemType.Value)) return null;
        return new(connection, slot, inventory.Serial, item.ItemType.Value, item.Stack);
    }
}
