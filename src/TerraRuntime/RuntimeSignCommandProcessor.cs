using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Authoritative-thread processor for the protocol-326 sign slice. Packet sinks only decode and enqueue; this
/// processor owns loaded-sign lookup and text mutation before committed state is projected to transport endpoints.
/// </summary>
internal sealed class RuntimeSignCommandProcessor
{
    private readonly RuntimeSignStore store;
    private readonly RuntimeSignReplicationRegistry replication;

    public RuntimeSignCommandProcessor(
        RuntimeSignStore store,
        RuntimeSignReplicationRegistry replication)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(replication);
        this.store = store;
        this.replication = replication;
    }

    public long AppliedReads { get; private set; }
    public long RejectedReads { get; private set; }
    public long AppliedUpdates { get; private set; }
    public long RejectedUpdates { get; private set; }

    public bool TryApply(RuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        switch (command)
        {
            case ClientSignReadRuntimeCommand read:
                ApplyRead(read);
                return true;

            case ClientSignUpdateRuntimeCommand update:
                ApplyUpdate(update);
                return true;

            default:
                return false;
        }
    }

    private void ApplyRead(ClientSignReadRuntimeCommand command)
    {
        TerrariaSignReadRequest request = command.Request;
        if (!store.TryRead(request.TileX, request.TileY, out WorldSign sign) ||
            !replication.TrySendRead(command.Connection, sign))
        {
            RejectedReads++;
            return;
        }

        AppliedReads++;
    }

    private void ApplyUpdate(ClientSignUpdateRuntimeCommand command)
    {
        TerrariaSignState submitted = command.State;
        if (!store.TryApply(in submitted, out WorldSign committed, out bool changed))
        {
            RejectedUpdates++;
            return;
        }

        if (changed)
            replication.PublishChanged(command.Connection, committed, submitted.Flags);

        AppliedUpdates++;
    }
}
