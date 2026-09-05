using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// Authoritative-thread processor for the protocol-326 sign slice. Packet sinks only decode and enqueue; this
/// processor owns loaded-sign lookup and text mutation before committed state is projected to transport endpoints.
/// Exact playing-session generation is revalidated before every client-originated sign command reaches the store.
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
        if (!replication.IsPlaying(command.Connection) ||
            !store.TryRead(request.TileX, request.TileY, out WorldSign sign) ||
            !replication.TrySendRead(command.Connection, sign))
        {
            RejectedReads++;
            return;
        }

        AppliedReads++;
    }

    private void ApplyUpdate(ClientSignUpdateRuntimeCommand command)
    {
        if (!replication.IsPlaying(command.Connection))
        {
            RejectedUpdates++;
            return;
        }

        TerrariaSignState submitted = command.State;
        if (!store.TryApply(in submitted, out WorldSign? committed, out bool textChanged))
        {
            RejectedUpdates++;
            return;
        }

        if (textChanged && committed is not null)
        {
            // TerrariaServer 1.4.5.8 rewrites packet 47's player field to whoAmI and sends it with the default
            // number3 argument, so the replicated flags byte is always zero rather than the client-submitted value.
            replication.PublishChanged(command.Connection, committed);
        }

        // TextSign may deliberately clear the slot when submitted coordinates do not point at an active sign tile.
        // That is still an applied authoritative mutation. There is no valid sign object to serialize in that case.
        AppliedUpdates++;
    }
}
