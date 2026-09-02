using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

internal enum RuntimeWorldCheckpointTickResult : byte
{
    Synchronizing = 0,
    Idle = 1,
    SaveWaitingForSynchronization = 2,
    SaveQueued = 3
}

internal readonly record struct RuntimeWorldSaveStatus(
    bool AcceptingRequests,
    bool TileShadowReady,
    int RemainingBootstrapSections,
    int PendingDirtyTileSections,
    bool SaveRequested,
    bool WriteActive,
    bool PendingWrite,
    long AcceptedSnapshots,
    long StartedWrites,
    long CompletedWrites,
    long CoalescedSnapshots,
    long FailedWrites,
    TimeSpan LastSnapshotCaptureDuration = default,
    TimeSpan LastSerializationDuration = default,
    TimeSpan LastWriteDuration = default,
    TimeSpan TotalSnapshotCaptureDuration = default,
    TimeSpan TotalSerializationDuration = default,
    TimeSpan TotalWriteDuration = default,
    bool RuntimeCacheRebuildActive = false,
    bool RuntimeCacheRebuildPending = false,
    long RuntimeCacheRebuildRequests = 0,
    long RuntimeCacheRebuilds = 0,
    long RuntimeCacheRebuildCoalesced = 0,
    long RuntimeCacheRebuildFailures = 0,
    RuntimeWorldSnapshotRebuildResult? LastRuntimeCacheRebuildResult = null);

/// <summary>
/// Bridges thread-safe save requests into game-thread-owned snapshot capture. Tile shadow maintenance and mutable
/// world-state capture happen only from <see cref="Tick"/> (or after the authoritative owner has stopped);
/// serialization and atomic file replacement happen on the save coordinator's background worker.
/// </summary>
internal sealed class RuntimeWorldCheckpointCoordinator : IAsyncDisposable
{
    public const int DefaultSynchronizationSectionsPerTick = 4;
    private const int MaxSignTextBytes = 64 * 1024;
    private const long MaxTotalSignTextBytes = 64L * 1024 * 1024;

    private readonly RuntimeWorldCheckpointSnapshotSource snapshotSource;
    private readonly WorldSaveCoordinator<RuntimeWorldCheckpointSnapshot> coordinator;
    private readonly CoalescingSaveScheduler<long>? runtimeCacheRebuildScheduler;
    private readonly int synchronizationSectionsPerTick;
    private long runtimeCacheRebuildGeneration;
    private long runtimeCacheRebuildFailures;
    private int lastRuntimeCacheRebuildResult = -1;
    private int saveRequested;
    private int acceptingRequests = 1;
    private int tileShadowReady;
    private int remainingBootstrapSections;
    private int pendingDirtyTileSections;

    public RuntimeWorldCheckpointCoordinator(
        string destinationPath,
        WorldFileEnvelope sourceEnvelope,
        WorldFileHeader sourceHeader,
        WorldFilePreservedSections preserved,
        WorldTileStore tiles,
        RuntimeChestStore chestStore,
        RuntimeWorldClock? worldClock = null,
        int synchronizationSectionsPerTick = DefaultSynchronizationSectionsPerTick,
        RuntimeSignStore? signStore = null,
        RuntimeTownNpcStateStore? townNpcStore = null,
        RuntimeWorldProgressionMutations? progressionMutations = null,
        WorldFileLoadLimits? checkpointValidationLimits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(sourceEnvelope);
        ArgumentNullException.ThrowIfNull(sourceHeader);
        ArgumentNullException.ThrowIfNull(preserved);
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(chestStore);
        ArgumentOutOfRangeException.ThrowIfLessThan(synchronizationSectionsPerTick, 1);

        // This service is the first production composition point that owns both halves of a mutable world object:
        // canonical tiles and runtime chest metadata. Bind them before ServerRuntimeState is constructed so packet-79
        // gameplay can resolve the exact lifecycle for this WorldTileStore without process-global current-world state.
        RuntimeWorldObjectMetadataRegistry.Bind(tiles, chestStore);

        AtomicSaveFileWriterOptions? writerOptions = null;
        if (checkpointValidationLimits is WorldFileLoadLimits limits)
        {
            limits.Validate();
            writerOptions = new AtomicSaveFileWriterOptions(
                BackupPath: RuntimeWorldCheckpointRecovery.GetBackupPath(destinationPath),
                ValidateCandidateAsync: (path, cancellationToken) =>
                    RuntimeWorldCheckpointRecovery.ValidateAsync(path, limits, cancellationToken),
                ValidateBackupAsync: (path, cancellationToken) =>
                    RuntimeWorldCheckpointRecovery.ValidateAsync(path, limits, cancellationToken));

            string runtimeCachePath = RuntimeWorldSnapshotCache.GetCachePath(destinationPath);
            runtimeCacheRebuildScheduler = new CoalescingSaveScheduler<long>(async (_, cancellationToken) =>
            {
                RuntimeWorldSnapshotRebuildDiagnostic rebuild =
                    await RuntimeWorldSnapshotRebuilder.TryRebuildAsync(
                        destinationPath,
                        runtimeCachePath,
                        limits,
                        cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref lastRuntimeCacheRebuildResult, (int)rebuild.Result);
                if (!rebuild.IsRebuilt)
                    Interlocked.Increment(ref runtimeCacheRebuildFailures);
            });
        }

        this.synchronizationSectionsPerTick = synchronizationSectionsPerTick;
        snapshotSource = new RuntimeWorldCheckpointSnapshotSource(
            tiles,
            chestStore,
            synchronizationSectionsPerTick,
            worldClock,
            signStore,
            townNpcStore,
            progressionMutations);
        coordinator = new WorldSaveCoordinator<RuntimeWorldCheckpointSnapshot>(
            destinationPath,
            CaptureSnapshotOnOwner,
            (snapshot, stream, cancellationToken) => SerializeAsync(
                sourceEnvelope,
                sourceHeader,
                preserved,
                snapshot,
                stream,
                cancellationToken),
            writerOptions,
            onCommitted: _ => QueueRuntimeCacheRebuild());
        PublishOwnerStatus();
    }

    public bool IsTileShadowReady => Volatile.Read(ref tileShadowReady) != 0;

    public bool IsSaveRequested => Volatile.Read(ref saveRequested) != 0;

    public RuntimeWorldSaveStatus CaptureStatus()
    {
        CoalescingSaveSchedulerSnapshot scheduler = coordinator.CaptureSnapshot();
        WorldSaveCoordinatorTimingSnapshot timing = coordinator.CaptureTimingSnapshot();
        CoalescingSaveSchedulerSnapshot cacheScheduler = runtimeCacheRebuildScheduler?.CaptureSnapshot() ?? default;
        int cacheResult = Volatile.Read(ref lastRuntimeCacheRebuildResult);
        return new RuntimeWorldSaveStatus(
            AcceptingRequests: Volatile.Read(ref acceptingRequests) != 0,
            TileShadowReady: Volatile.Read(ref tileShadowReady) != 0,
            RemainingBootstrapSections: Volatile.Read(ref remainingBootstrapSections),
            PendingDirtyTileSections: Volatile.Read(ref pendingDirtyTileSections),
            SaveRequested: IsSaveRequested,
            WriteActive: scheduler.WriteActive,
            PendingWrite: scheduler.HasPendingSnapshot,
            AcceptedSnapshots: scheduler.RequestedSaves,
            StartedWrites: scheduler.StartedWrites,
            CompletedWrites: scheduler.CompletedWrites,
            CoalescedSnapshots: scheduler.CoalescedRequests,
            FailedWrites: scheduler.FailedWrites,
            LastSnapshotCaptureDuration: timing.LastSnapshotCaptureDuration,
            LastSerializationDuration: timing.LastSerializationDuration,
            LastWriteDuration: timing.LastWriteDuration,
            TotalSnapshotCaptureDuration: timing.TotalSnapshotCaptureDuration,
            TotalSerializationDuration: timing.TotalSerializationDuration,
            TotalWriteDuration: timing.TotalWriteDuration,
            RuntimeCacheRebuildActive: cacheScheduler.WriteActive,
            RuntimeCacheRebuildPending: cacheScheduler.HasPendingSnapshot,
            RuntimeCacheRebuildRequests: cacheScheduler.RequestedSaves,
            RuntimeCacheRebuilds: cacheScheduler.CompletedWrites,
            RuntimeCacheRebuildCoalesced: cacheScheduler.CoalescedRequests,
            RuntimeCacheRebuildFailures: Volatile.Read(ref runtimeCacheRebuildFailures),
            LastRuntimeCacheRebuildResult: cacheResult < 0
                ? null
                : (RuntimeWorldSnapshotRebuildResult)cacheResult);
    }

    /// <summary>
    /// May be called from any thread. The request is only converted into a detached snapshot by <see cref="Tick"/>
    /// on the authoritative owner. Returns false after persistence has stopped accepting requests.
    /// </summary>
    public bool TryRequestSave()
    {
        if (Volatile.Read(ref acceptingRequests) == 0)
            return false;

        Interlocked.Exchange(ref saveRequested, 1);
        return true;
    }

    /// <summary>
    /// May be called from any thread. Throws when persistence is already completing.
    /// </summary>
    public void RequestSave()
    {
        if (!TryRequestSave())
            throw new InvalidOperationException("The world save service is completing and no longer accepts requests.");
    }

    /// <summary>
    /// Advances bounded save-shadow synchronization and, once fully caught up, captures one requested save image.
    /// This method must run on the authoritative game-thread owner.
    /// </summary>
    public RuntimeWorldCheckpointTickResult Tick()
    {
        try
        {
            if (!snapshotSource.IsTileShadowReady)
            {
                snapshotSource.CaptureTileBootstrap(synchronizationSectionsPerTick);
                return IsSaveRequested
                    ? RuntimeWorldCheckpointTickResult.SaveWaitingForSynchronization
                    : RuntimeWorldCheckpointTickResult.Synchronizing;
            }

            snapshotSource.CaptureDirtyTiles(synchronizationSectionsPerTick);
            if (!IsSaveRequested)
                return RuntimeWorldCheckpointTickResult.Idle;

            // Capture readiness is based on the persistence tracker itself rather than the number successfully applied
            // this tick. A failed section snapshot is requeued and must keep the save pending until a later owner tick.
            if (snapshotSource.PendingDirtyTileSections != 0)
                return RuntimeWorldCheckpointTickResult.SaveWaitingForSynchronization;

            if (Interlocked.Exchange(ref saveRequested, 0) == 0)
                return RuntimeWorldCheckpointTickResult.Idle;

            coordinator.RequestSave();
            return RuntimeWorldCheckpointTickResult.SaveQueued;
        }
        finally
        {
            PublishOwnerStatus();
        }
    }

    /// <summary>
    /// Captures and queues the final authoritative image after the game-loop owner has stopped. With no concurrent
    /// mutations remaining, shutdown can drain the bounded shadow synchronously without violating runtime ownership.
    /// </summary>
    public void CaptureFinalSaveAfterOwnerStopped()
    {
        if (Interlocked.Exchange(ref acceptingRequests, 0) == 0)
            throw new InvalidOperationException("The world save service is already completing.");

        try
        {
            while (!snapshotSource.IsTileShadowReady)
                snapshotSource.CaptureTileBootstrap(synchronizationSectionsPerTick);

            while (snapshotSource.PendingDirtyTileSections != 0)
                snapshotSource.CaptureDirtyTiles(synchronizationSectionsPerTick);

            Interlocked.Exchange(ref saveRequested, 0);
            coordinator.RequestSave();
        }
        finally
        {
            PublishOwnerStatus();
        }
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref acceptingRequests, 0);
        await coordinator.CompleteAsync(cancellationToken).ConfigureAwait(false);
        if (runtimeCacheRebuildScheduler is not null)
            await runtimeCacheRebuildScheduler.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => new(CompleteAsync());

    private void QueueRuntimeCacheRebuild()
    {
        if (runtimeCacheRebuildScheduler is null)
            return;

        try
        {
            runtimeCacheRebuildScheduler.RequestSave(Interlocked.Increment(ref runtimeCacheRebuildGeneration));
        }
        catch (InvalidOperationException)
        {
            // Canonical publication has already succeeded. A derived-cache scheduling failure must never
            // retroactively turn a valid .wld commit into a failed save result.
            Interlocked.Increment(ref runtimeCacheRebuildFailures);
        }
    }

    private void PublishOwnerStatus()
    {
        Volatile.Write(ref remainingBootstrapSections, snapshotSource.RemainingBootstrapSections);
        Volatile.Write(ref pendingDirtyTileSections, snapshotSource.PendingDirtyTileSections);
        Volatile.Write(ref tileShadowReady, snapshotSource.IsTileShadowReady ? 1 : 0);
    }

    private RuntimeWorldCheckpointSnapshot CaptureSnapshotOnOwner()
    {
        if (!snapshotSource.TryCapture(out RuntimeWorldCheckpointSnapshot? snapshot) || snapshot is null)
            throw new InvalidOperationException("The world save shadow is not ready for an authoritative snapshot.");

        return snapshot;
    }

    private static Task SerializeAsync(
        WorldFileEnvelope sourceEnvelope,
        WorldFileHeader sourceHeader,
        WorldFilePreservedSections preserved,
        RuntimeWorldCheckpointSnapshot snapshot,
        Stream destination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ReadOnlySpan<byte> headerSection = preserved.Header.Span;
        byte[]? patchedHeader = null;
        if (snapshot.Clock is RuntimeWorldClockSaveState clock)
        {
            WorldFileClockHeaderPatchResult patchResult = WorldFileClockHeaderPatcher.TryPatch(
                headerSection,
                sourceHeader,
                clock.Time,
                clock.DayTime,
                (byte)clock.MoonPhase,
                clock.SlimeRainTime,
                out patchedHeader);
            if (patchResult != WorldFileClockHeaderPatchResult.Patched)
                throw new InvalidDataException($"Authoritative world clock header patch failed: {patchResult}.");

            headerSection = patchedHeader;
        }

        if (snapshot.ProgressionMutations is RuntimeWorldProgressionMutationSnapshot progression && progression.HasAny)
        {
            WorldFileProgressionHeaderPatchResult progressionResult = WorldFileProgressionHeaderPatcher.TryPatch(
                headerSection,
                sourceHeader,
                in progression,
                out byte[] progressionHeader);
            if (progressionResult != WorldFileProgressionHeaderPatchResult.Patched)
            {
                throw new InvalidDataException(
                    $"Authoritative world progression header patch failed: {progressionResult}.");
            }

            headerSection = progressionHeader;
        }

        ReadOnlySpan<byte> signSection = preserved.Signs.Span;
        byte[]? encodedSigns = null;
        if (snapshot.Signs is WorldSign[] signs)
        {
            using var signStream = new MemoryStream();
            WorldFileSignEncodeResult signResult = WorldFileSignEncoder.TryEncode(
                signs,
                sourceHeader.Dimensions,
                MaxSignTextBytes,
                MaxTotalSignTextBytes,
                signStream,
                out _);
            if (signResult != WorldFileSignEncodeResult.Encoded)
                throw new InvalidDataException($"Authoritative world sign encoding failed: {signResult}.");

            encodedSigns = signStream.ToArray();
            signSection = encodedSigns;
        }

        ReadOnlySpan<byte> npcSection = preserved.Npcs.Span;
        byte[]? encodedNpcs = null;
        if (snapshot.Npcs is WorldNpcPersistence npcs)
        {
            using var npcStream = new MemoryStream();
            WorldFileNpcDecodeOptions npcLimits = new(
                MaxShimmeredTownNpcIndices: VanillaWorldFormat326.NpcTypeCount,
                MaxShimmerIndexExclusive: VanillaWorldFormat326.NpcTypeCount,
                MaxTownNpcs: RuntimeTownNpcStateStore.MaximumTownNpcs,
                MaxPersistentNpcs: RuntimeTownNpcStateStore.MaximumTownNpcs,
                MaxNameBytesPerTownNpc: 16 * 1024,
                MaxTotalNameBytes: 4L * 1024 * 1024);
            WorldFileNpcEncodeResult npcResult = WorldFileNpcEncoder.TryEncode(
                npcs,
                npcLimits,
                npcStream,
                out _);
            if (npcResult != WorldFileNpcEncodeResult.Encoded)
                throw new InvalidDataException($"Authoritative town-NPC persistence encoding failed: {npcResult}.");

            encodedNpcs = npcStream.ToArray();
            npcSection = encodedNpcs;
        }

        ReadOnlySpan<byte> townRoomSection = preserved.TownRooms.Span;
        byte[]? encodedTownRooms = null;
        if (snapshot.TownRooms is WorldTownRoom[] townRooms)
        {
            using var roomStream = new MemoryStream();
            WorldFileTownRoomEncodeResult roomResult = WorldFileTownRoomEncoder.TryEncode(
                townRooms,
                sourceHeader.Dimensions,
                VanillaWorldFormat326.NpcTypeCount,
                roomStream,
                out _);
            if (roomResult != WorldFileTownRoomEncodeResult.Encoded)
                throw new InvalidDataException($"Authoritative town-room persistence encoding failed: {roomResult}.");

            encodedTownRooms = roomStream.ToArray();
            townRoomSection = encodedTownRooms;
        }

        WorldFileTileChestRewriteResult result = WorldFileTileChestRewriter.TryRewrite(
            sourceEnvelope,
            sourceHeader,
            headerSection,
            preserved,
            snapshot.Tiles,
            snapshot.Chests,
            signSection,
            npcSection,
            townRoomSection,
            destination,
            out _);
        if (result != WorldFileTileChestRewriteResult.Rewritten)
            throw new InvalidDataException($"Authoritative tile/chest/sign/town world save failed: {result}.");

        return Task.CompletedTask;
    }
}
