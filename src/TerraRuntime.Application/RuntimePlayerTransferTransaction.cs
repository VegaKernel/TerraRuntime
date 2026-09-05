using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

/// <summary>
/// Owns one detached player payload while ownership is between WorldRuntime authoritative loops.
/// Connection/routing code can move or restore the player through this token without receiving the mutable
/// transfer payload itself. The payload can be committed exactly once; a rejected attach leaves it detached so the
/// source runtime can be restored.
/// </summary>
internal sealed class RuntimePlayerTransferTransaction
{
    private readonly WorldRuntime sourceRuntime;
    private readonly ConnectionHandle sourceConnection;
    private readonly RuntimePlayerTransferState transfer;
    private bool completed;

    private RuntimePlayerTransferTransaction(
        WorldRuntime sourceRuntime,
        ConnectionHandle sourceConnection,
        RuntimePlayerTransferState transfer)
    {
        this.sourceRuntime = sourceRuntime;
        this.sourceConnection = sourceConnection;
        this.transfer = transfer;
    }

    public string? PlayerName => transfer.PlayerName;

    public static RuntimePlayerTransferTransaction? Detach(
        WorldRuntime sourceRuntime,
        ConnectionHandle sourceConnection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceRuntime);
        if (!sourceConnection.IsAssigned)
            throw new ArgumentException("An assigned source connection is required.", nameof(sourceConnection));

        RuntimePlayerTransferState? transfer = sourceRuntime.TransferIngress
            .DetachAsync(sourceConnection, cancellationToken)
            .AsTask().GetAwaiter().GetResult();
        return transfer is null
            ? null
            : new RuntimePlayerTransferTransaction(sourceRuntime, sourceConnection, transfer);
    }

    public bool TryAttach(
        WorldRuntime destinationRuntime,
        ConnectionHandle destinationConnection,
        bool preserveWorldPosition,
        bool forceRespawn,
        CancellationToken cancellationToken)
    {
        EnsureDetached();
        ArgumentNullException.ThrowIfNull(destinationRuntime);
        if (!destinationConnection.IsAssigned)
            throw new ArgumentException("An assigned destination connection is required.", nameof(destinationConnection));
        if (destinationConnection.Player.Slot != transfer.Slot)
            throw new InvalidOperationException("A player transfer must preserve the client-visible player slot.");

        bool attached = destinationRuntime.TransferIngress.AttachAsync(
                destinationConnection,
                transfer,
                checked((short)destinationRuntime.World.RuntimeMetadata.SpawnX),
                checked((short)destinationRuntime.World.RuntimeMetadata.SpawnY),
                preserveWorldPosition,
                forceRespawn,
                cancellationToken)
            .AsTask().GetAwaiter().GetResult();
        if (attached)
            completed = true;
        return attached;
    }

    public void Discard()
    {
        EnsureDetached();
        completed = true;
    }

    public void RestoreSource(CancellationToken cancellationToken)
    {
        EnsureDetached();
        bool restored = sourceRuntime.TransferIngress.AttachAsync(
                sourceConnection,
                transfer,
                checked((short)sourceRuntime.World.RuntimeMetadata.SpawnX),
                checked((short)sourceRuntime.World.RuntimeMetadata.SpawnY),
                preserveWorldPosition: true,
                forceRespawn: false,
                cancellationToken)
            .AsTask().GetAwaiter().GetResult();
        if (!restored)
            throw new InvalidOperationException("Source runtime could not restore player state after failed transfer.");
        completed = true;
    }

    private void EnsureDetached()
    {
        if (completed)
            throw new InvalidOperationException("The detached player transfer has already been committed.");
    }
}
