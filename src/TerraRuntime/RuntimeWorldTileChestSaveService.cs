using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

internal enum RuntimeWorldTileChestSaveTickResult : byte
{
    Synchronizing = 0,
    Idle = 1,
    SaveWaitingForSynchronization = 2,
    SaveQueued = 3
}

/// <summary>
/// Bridges thread-safe save requests into game-thread-owned snapshot capture. Tile shadow maintenance and chest cloning
/// happen only from <see cref="Tick"/> (or after the authoritative owner has stopped); serialization and atomic file
/// replacement happen on the save coordinator's background worker.
/// </summary>
internal sealed class RuntimeWorldTileChestSaveService : IAsyncDisposable
{
    private readonly RuntimeWorldTileChestSaveSnapshotSource snapshotSource;
    private readonly WorldSaveCoordinator<RuntimeWorldTileChestSaveSnapshot> coordinator;
    private readonly int synchronizationSectionsPerTick;
    private int saveRequested;
    private int acceptingRequests = 1;

    public RuntimeWorldTileChestSaveService(
        string destinationPath,
        WorldFileEnvelope sourceEnvelope,
        WorldFileHeader sourceHeader,
        WorldFilePreservedSections preserved,
        WorldTileStore tiles,
        RuntimeChestStore chestStore,
        int synchronizationSectionsPerTick)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(sourceEnvelope);
        ArgumentNullException.ThrowIfNull(sourceHeader);
        ArgumentNullException.ThrowIfNull(preserved);
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(chestStore);
        ArgumentOutOfRangeException.ThrowIfLessThan(synchronizationSectionsPerTick, 1);

        this.synchronizationSectionsPerTick = synchronizationSectionsPerTick;
        snapshotSource = new RuntimeWorldTileChestSaveSnapshotSource(
            tiles,
            chestStore,
            synchronizationSectionsPerTick);
        coordinator = new WorldSaveCoordinator<RuntimeWorldTileChestSaveSnapshot>(
            destinationPath,
            CaptureSnapshotOnOwner,
            (snapshot, stream, cancellationToken) => SerializeAsync(
                sourceEnvelope,
                sourceHeader,
                preserved,
                snapshot,
                stream,
                cancellationToken));
    }

    public bool IsTileShadowReady => snapshotSource.IsTileShadowReady;

    public bool IsSaveRequested => Volatile.Read(ref saveRequested) != 0;

    /// <summary>
    /// May be called from any thread. The request is only converted into a detached snapshot by <see cref="Tick"/>
    /// on the authoritative owner.
    /// </summary>
    public void RequestSave()
    {
        if (Volatile.Read(ref acceptingRequests) == 0)
            throw new InvalidOperationException("The world save service is completing and no longer accepts requests.");

        Interlocked.Exchange(ref saveRequested, 1);
    }

    /// <summary>
    /// Advances bounded save-shadow synchronization and, once fully caught up, captures one requested save image.
    /// This method must run on the authoritative game-thread owner.
    /// </summary>
    public RuntimeWorldTileChestSaveTickResult Tick()
    {
        if (!snapshotSource.IsTileShadowReady)
        {
            snapshotSource.CaptureTileBootstrap(synchronizationSectionsPerTick);
            return IsSaveRequested
                ? RuntimeWorldTileChestSaveTickResult.SaveWaitingForSynchronization
                : RuntimeWorldTileChestSaveTickResult.Synchronizing;
        }

        snapshotSource.CaptureDirtyTiles(synchronizationSectionsPerTick);
        if (!IsSaveRequested)
            return RuntimeWorldTileChestSaveTickResult.Idle;

        // Capture readiness is based on the tracker itself rather than the number successfully applied this tick.
        // A failed section snapshot is requeued and must keep the save pending until a later owner tick captures it.
        if (snapshotSource.PendingDirtyTileSections != 0)
            return RuntimeWorldTileChestSaveTickResult.SaveWaitingForSynchronization;

        if (Interlocked.Exchange(ref saveRequested, 0) == 0)
            return RuntimeWorldTileChestSaveTickResult.Idle;

        coordinator.RequestSave();
        return RuntimeWorldTileChestSaveTickResult.SaveQueued;
    }

    /// <summary>
    /// Captures and queues the final authoritative image after the game-loop owner has stopped. With no concurrent
    /// mutations remaining, shutdown can drain the bounded shadow synchronously without violating runtime ownership.
    /// </summary>
    public void CaptureFinalSaveAfterOwnerStopped()
    {
        if (Interlocked.Exchange(ref acceptingRequests, 0) == 0)
            throw new InvalidOperationException("The world save service is already completing.");

        while (!snapshotSource.IsTileShadowReady)
            snapshotSource.CaptureTileBootstrap(synchronizationSectionsPerTick);

        while (snapshotSource.PendingDirtyTileSections != 0)
            snapshotSource.CaptureDirtyTiles(synchronizationSectionsPerTick);

        Interlocked.Exchange(ref saveRequested, 0);
        coordinator.RequestSave();
    }

    public Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref acceptingRequests, 0);
        return coordinator.CompleteAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => new(CompleteAsync());

    private RuntimeWorldTileChestSaveSnapshot CaptureSnapshotOnOwner()
    {
        if (!snapshotSource.TryCapture(out RuntimeWorldTileChestSaveSnapshot? snapshot) || snapshot is null)
            throw new InvalidOperationException("The world save shadow is not ready for an authoritative snapshot.");

        return snapshot;
    }

    private static Task SerializeAsync(
        WorldFileEnvelope sourceEnvelope,
        WorldFileHeader sourceHeader,
        WorldFilePreservedSections preserved,
        RuntimeWorldTileChestSaveSnapshot snapshot,
        Stream destination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WorldFileTileChestPatchWriteResult result = WorldFileTileChestPatchWriter.TryWrite(
            sourceEnvelope,
            sourceHeader,
            preserved,
            snapshot.Tiles,
            snapshot.Chests,
            destination,
            out _);
        if (result != WorldFileTileChestPatchWriteResult.Written)
            throw new InvalidDataException($"Authoritative tile/chest world save failed: {result}.");

        return Task.CompletedTask;
    }
}
