using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.Network;
using TerraRuntime.Operations;
using TerraRuntime.Protocol;
using TerraRuntime.TerminalUI;
using TerraRuntime.World;
using StructuredLogCategory = TerraRuntime.Contracts.Diagnostics.RuntimeLogCategory;
using StructuredLogContext = TerraRuntime.Contracts.Diagnostics.RuntimeLogContext;
using StructuredLogEventIds = TerraRuntime.Contracts.Diagnostics.RuntimeLogEventIds;

namespace TerraRuntime;

public static class TerrariaServerHost
{
    /// <summary>
    /// Runs one Terraria world. The optional interest-management control is the only supported
    /// external switch for runtime visibility optimization; spatial policy remains owned by TerraRuntime.
    /// </summary>
    public static async Task<int> RunAsync(
        ServerHostOptions options,
        IInterestManagementControl? interestManagement = null,
        ITerraRuntimeHostLifecycle? hostLifecycle = null,
        ITerraRuntimeWorldGeneratorSource? worldGenerators = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var runtimeLogs = new RuntimeLogBuffer();
        await using var hostLog = new RuntimeHostLog(runtimeLogs);

        IInterestManagementControl runtimeInterestManagement =
            interestManagement ?? new InterestManagementControl(options.InterestManagementEnabled);
        if (options.InterestManagementEnabled)
            runtimeInterestManagement.SetEnabled(true);

        if (!AtomicSaveFileWriter.TryCleanupAbandonedWrites(options.WorldPath))
        {
            hostLog.Log(
                RuntimeLogLevel.Warning,
                StructuredLogEventIds.PersistenceCanonicalCleanupFailed,
                StructuredLogCategory.Persistence,
                "WorldSave",
                $"Failed to clean abandoned save transactions for canonical world: {options.WorldPath}.",
                useStandardError: true);
        }

        string checkpointBackupPath = RuntimeWorldCheckpointRecovery.GetBackupPath(options.WorldPath);
        if (!AtomicSaveFileWriter.TryCleanupAbandonedWrites(checkpointBackupPath))
        {
            hostLog.Log(
                RuntimeLogLevel.Warning,
                StructuredLogEventIds.PersistenceBackupCleanupFailed,
                StructuredLogCategory.Persistence,
                "WorldSave",
                $"Failed to clean abandoned save transactions for checkpoint backup: {checkpointBackupPath}.",
                useStandardError: true);
        }

        if (!File.Exists(options.WorldPath))
        {
            hostLog.Log(
                RuntimeLogLevel.Error,
                StructuredLogEventIds.WorldFileMissing,
                StructuredLogCategory.World,
                "World",
                $"World file not found: {options.WorldPath}",
                useStandardError: true);
            return 24;
        }

        long startupStart = Stopwatch.GetTimestamp();
        long allocatedBytesAtStart = GC.GetTotalAllocatedBytes(precise: false);
        TimeSpan sourceStatDuration = TimeSpan.Zero;
        TimeSpan fileReadDuration = TimeSpan.Zero;
        TimeSpan cacheLoadDuration = TimeSpan.Zero;
        TimeSpan cacheWriteDuration = TimeSpan.Zero;
        TimeSpan bootstrapDuration = TimeSpan.Zero;
        TimeSpan bootstrapCacheLoadDuration = TimeSpan.Zero;
        TimeSpan bootstrapCacheWriteDuration = TimeSpan.Zero;
        WorldFileLoadProfile canonicalLoadProfile = default;
        bool runtimeCacheHit = false;
        bool bootstrapCacheHit = false;

        long sourceStatStart = Stopwatch.GetTimestamp();
        if (!RuntimeWorldSnapshotCache.TryCaptureSourceStamp(options.WorldPath, out RuntimeWorldSourceStamp sourceStamp))
        {
            sourceStatDuration = Stopwatch.GetElapsedTime(sourceStatStart);
            hostLog.Log(
                RuntimeLogLevel.Error,
                StructuredLogEventIds.WorldSourceStatFailed,
                StructuredLogCategory.World,
                "World",
                $"Failed to stat world file '{options.WorldPath}'.",
                useStandardError: true);
            return 25;
        }
        sourceStatDuration = Stopwatch.GetElapsedTime(sourceStatStart);

        WorldFileLoadLimits worldLoadLimits = CreateServerWorldLoadLimits();
        string runtimeCachePath = RuntimeWorldSnapshotCache.GetCachePath(options.WorldPath);
        long cacheLoadStart = Stopwatch.GetTimestamp();
        RuntimeWorldSnapshotLoadDiagnostic cacheDiagnostic = RuntimeWorldSnapshotCache.TryLoadValidatedSource(
            runtimeCachePath,
            options.WorldPath,
            worldLoadLimits,
            out WorldFileData? world);
        cacheLoadDuration = Stopwatch.GetElapsedTime(cacheLoadStart);

        if (cacheDiagnostic.IsLoaded)
        {
            long verifyStatStart = Stopwatch.GetTimestamp();
            bool statOk = RuntimeWorldSnapshotCache.TryCaptureSourceStamp(
                options.WorldPath,
                out RuntimeWorldSourceStamp postCacheStamp);
            sourceStatDuration += Stopwatch.GetElapsedTime(verifyStatStart);
            if (!statOk)
            {
                hostLog.Log(
                    RuntimeLogLevel.Error,
                    StructuredLogEventIds.WorldSourceRestatFailed,
                    StructuredLogCategory.World,
                    "World",
                    $"Failed to re-stat world file '{options.WorldPath}' after cache load.",
                    useStandardError: true);
                return 25;
            }

            if (postCacheStamp != sourceStamp)
            {
                sourceStamp = postCacheStamp;
                world = null;
                cacheDiagnostic = new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.SourceNewer);
            }
        }

        TimeSpan worldReadyDuration;
        if (cacheDiagnostic.IsLoaded && world is not null)
        {
            hostLog.SetWorldId(world.Header.WorldId.ToString());
            runtimeCacheHit = true;
            worldReadyDuration = Stopwatch.GetElapsedTime(startupStart);
            hostLog.Log(
                RuntimeLogLevel.Information,
                StructuredLogEventIds.WorldCacheHit,
                StructuredLogCategory.World,
                "World",
                $"Runtime world cache hit: '{runtimeCachePath}'.");
        }
        else
        {
            hostLog.Log(
                RuntimeLogLevel.Information,
                StructuredLogEventIds.WorldCacheMiss,
                StructuredLogCategory.World,
                "World",
                $"Runtime world cache miss: result={cacheDiagnostic.Result}, code={cacheDiagnostic.DetailCode}; falling back to canonical .wld.");

            StableWorldReadResult stableRead = await ReadStableWorldAsync(
                options.WorldPath,
                sourceStamp).ConfigureAwait(false);
            fileReadDuration = stableRead.Duration;
            if (!stableRead.Success || stableRead.Bytes is null)
            {
                hostLog.Log(
                    RuntimeLogLevel.Error,
                    StructuredLogEventIds.WorldReadFailed,
                    StructuredLogCategory.World,
                    "World",
                    stableRead.Error ?? $"Failed to read world file '{options.WorldPath}'.",
                    useStandardError: true);
                return 25;
            }

            byte[] file = stableRead.Bytes;
            sourceStamp = stableRead.Stamp;
            WorldFileLoadDiagnostic diagnostic = WorldFileLoader.TryLoad(
                file,
                worldLoadLimits,
                out world,
                out canonicalLoadProfile);
            if (!diagnostic.IsLoaded || world is null)
            {
                hostLog.Log(
                    RuntimeLogLevel.Error,
                    StructuredLogEventIds.WorldLoadFailed,
                    StructuredLogCategory.World,
                    "World",
                    $"World load failed: result={diagnostic.Result}, stage={diagnostic.Stage}, code={diagnostic.StageResultCode}.",
                    useStandardError: true);

                if (!RuntimeWorldCheckpointRecovery.CanAutomaticallyRestoreAfter(diagnostic))
                {
                    hostLog.Log(
                        RuntimeLogLevel.Error,
                        StructuredLogEventIds.WorldRecoverySuppressed,
                        StructuredLogCategory.World,
                        "World",
                        "Automatic checkpoint recovery is suppressed for an explicitly incompatible world-file version.",
                        useStandardError: true);
                    return 26;
                }

                RuntimeWorldCheckpointRestoreDiagnostic recovery =
                    await RuntimeWorldCheckpointRecovery.TryRestoreBackupAsync(
                        options.WorldPath,
                        worldLoadLimits).ConfigureAwait(false);
                if (!recovery.IsRestored)
                {
                    hostLog.Log(
                        RuntimeLogLevel.Error,
                        StructuredLogEventIds.WorldCheckpointRecoveryFailed,
                        StructuredLogCategory.World,
                        "World",
                        $"World checkpoint recovery failed: result={recovery.Result}, load_result={recovery.LoadResult}, stage={recovery.LoadStage}, code={recovery.StageResultCode}.",
                        useStandardError: true);
                    return 26;
                }

                hostLog.Log(
                    RuntimeLogLevel.Information,
                    StructuredLogEventIds.WorldCheckpointRecovered,
                    StructuredLogCategory.World,
                    "World",
                    $"Canonical world recovered from validated checkpoint backup: {RuntimeWorldCheckpointRecovery.GetBackupPath(options.WorldPath)}.");
                await hostLog.DisposeAsync().ConfigureAwait(false);
                return await RunAsync(options, runtimeInterestManagement, hostLifecycle, worldGenerators).ConfigureAwait(false);
            }

            hostLog.SetWorldId(world.Header.WorldId.ToString());
            worldReadyDuration = Stopwatch.GetElapsedTime(startupStart);
            long cacheWriteStart = Stopwatch.GetTimestamp();
            RuntimeWorldSnapshotWriteDiagnostic cacheWrite = RuntimeWorldSnapshotCache.TryWriteAtomic(
                runtimeCachePath,
                file,
                sourceStamp,
                world);
            cacheWriteDuration = Stopwatch.GetElapsedTime(cacheWriteStart);
            if (cacheWrite.IsWritten)
            {
                hostLog.Log(
                    RuntimeLogLevel.Information,
                    StructuredLogEventIds.PersistenceWorldCacheRebuilt,
                    StructuredLogCategory.Persistence,
                    "WorldCache",
                    $"Runtime world cache rebuilt: '{runtimeCachePath}'.");
            }
            else
            {
                hostLog.Log(
                    RuntimeLogLevel.Warning,
                    StructuredLogEventIds.PersistenceWorldCacheWriteFailed,
                    StructuredLogCategory.Persistence,
                    "WorldCache",
                    $"Runtime world cache rebuild skipped/failed: result={cacheWrite.Result}. The canonical .wld remains the recovery checkpoint.",
                    useStandardError: true);
            }
        }

        RuntimeWorldSaveTemplateLoadResult saveTemplateLoad = RuntimeWorldSaveTemplateLoader.TryLoad(
            options.WorldPath,
            runtimeCachePath,
            sourceStamp,
            world,
            out WorldFilePreservedSections? worldSaveTemplate);
        if (!saveTemplateLoad.Success || worldSaveTemplate is null)
        {
            hostLog.Log(
                RuntimeLogLevel.Error,
                StructuredLogEventIds.PersistenceSaveTemplateLoadFailed,
                StructuredLogCategory.Persistence,
                "WorldSave",
                $"World save template load failed: source={saveTemplateLoad.Source}, cache_result={saveTemplateLoad.CacheResult}, error={saveTemplateLoad.Error ?? "unknown"}. Refusing to start a mutable world without a canonical persistence checkpoint.",
                useStandardError: true);
            return 30;
        }

        hostLog.Log(
            RuntimeLogLevel.Information,
            StructuredLogEventIds.PersistenceSaveTemplateReady,
            StructuredLogCategory.Persistence,
            "WorldSave",
            $"World save template ready: source={saveTemplateLoad.Source}, cache_result={saveTemplateLoad.CacheResult}.");

        string runtimeBootstrapCachePath = RuntimeBootstrapSnapshotCache.GetCachePath(options.WorldPath);
        long bootstrapStart = Stopwatch.GetTimestamp();
        long bootstrapCacheLoadStart = Stopwatch.GetTimestamp();
        RuntimeBootstrapSnapshotLoadDiagnostic bootstrapCacheDiagnostic = RuntimeBootstrapSnapshotCache.TryLoad(
            runtimeBootstrapCachePath,
            sourceStamp,
            world,
            out PlayerBootstrapPacketSet? cachedBootstrapPackets);
        bootstrapCacheLoadDuration = Stopwatch.GetElapsedTime(bootstrapCacheLoadStart);

        PlayerBootstrapPacketSet bootstrapPackets;
        if (bootstrapCacheDiagnostic.IsLoaded && cachedBootstrapPackets is not null)
        {
            bootstrapPackets = cachedBootstrapPackets;
            bootstrapCacheHit = true;
            bootstrapDuration = Stopwatch.GetElapsedTime(bootstrapStart);
            hostLog.Log(
                RuntimeLogLevel.Information,
                StructuredLogEventIds.WorldBootstrapCacheHit,
                StructuredLogCategory.World,
                "Bootstrap",
                $"Runtime bootstrap cache hit: '{runtimeBootstrapCachePath}'.");
        }
        else
        {
            try
            {
                bootstrapPackets = PlayerBootstrapPacketSet.Create(world);
                bootstrapDuration = Stopwatch.GetElapsedTime(bootstrapStart);
            }
            catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
            {
                bootstrapDuration = Stopwatch.GetElapsedTime(bootstrapStart);
                hostLog.Log(
                    RuntimeLogLevel.Error,
                    StructuredLogEventIds.WorldBootstrapPreparationFailed,
                    StructuredLogCategory.World,
                    "Bootstrap",
                    $"Failed to prepare join bootstrap packets: {exception.Message}",
                    useStandardError: true);
                return 27;
            }

            long bootstrapCacheWriteStart = Stopwatch.GetTimestamp();
            RuntimeBootstrapSnapshotWriteDiagnostic bootstrapCacheWrite = RuntimeBootstrapSnapshotCache.TryWriteAtomic(
                runtimeBootstrapCachePath,
                sourceStamp,
                world,
                bootstrapPackets);
            bootstrapCacheWriteDuration = Stopwatch.GetElapsedTime(bootstrapCacheWriteStart);
            if (bootstrapCacheWrite.IsWritten)
            {
                hostLog.Log(
                    RuntimeLogLevel.Information,
                    StructuredLogEventIds.PersistenceBootstrapCacheRebuilt,
                    StructuredLogCategory.Persistence,
                    "Bootstrap",
                    $"Runtime bootstrap cache rebuilt: '{runtimeBootstrapCachePath}'.");
            }
            else
            {
                hostLog.Log(
                    RuntimeLogLevel.Warning,
                    StructuredLogEventIds.PersistenceBootstrapCacheWriteFailed,
                    StructuredLogCategory.Persistence,
                    "Bootstrap",
                    $"Runtime bootstrap cache rebuild skipped/failed: result={bootstrapCacheWrite.Result}.",
                    useStandardError: true);
            }
        }

        var primaryIdentity = new WorldRuntimeIdentity(
            WorldRuntimeId.CreateNew(),
            WorldSessionId.CreateNew());
        using var primaryRuntime = new WorldRuntime(
            primaryIdentity,
            new SandboxWorldSource.WorldFile(options.WorldPath),
            world,
            bootstrapPackets,
            runtimeInterestManagement,
            new WorldRuntimeOptions
            {
                MaxPlayers = options.MaxPlayers,
                CaptureOperationsTelemetry = options.TerminalUiEnabled
            },
            new WorldRuntimePersistence(options.WorldPath, worldSaveTemplate, worldLoadLimits));
        using var runtimeRegistry = new WorldRegistry(options.MaxWorldRuntimes);
        using var sandboxHost = new SandboxHost(
            runtimeRegistry,
            new StartupWorldGeneratorSource(worldGenerators),
            worldLoadLimits,
            materializationConcurrency: options.SandboxMaterializationConcurrency,
            maxPlayersPerRuntime: options.MaxPlayers);

        ServerRuntimeState state = primaryRuntime.State;
        AuthoritativeGameLoop<ServerRuntimeState, RuntimeCommand> gameLoop = primaryRuntime.GameLoop;
        RuntimePlayerStateSnapshotReader playerStateSnapshots = primaryRuntime.PlayerStateSnapshots;
        PlayerSlotPool slots = primaryRuntime.Slots;
        RuntimeConnectionRegistry runtimeConnections = primaryRuntime.RuntimeConnections;
        RuntimeNpcReplicationRegistry npcReplication = primaryRuntime.NpcReplication;
        RuntimeProjectileReplicationRegistry projectileReplication = primaryRuntime.ProjectileReplication;
        RuntimeWorldItemReplicationRegistry worldItemReplication = primaryRuntime.WorldItemReplication;
        RuntimeTileManipulationReplicationRegistry tileManipulationReplication = primaryRuntime.TileManipulationReplication;
        RuntimeChestReplicationRegistry chestReplication = primaryRuntime.ChestReplication;
        RuntimeSignReplicationRegistry signReplication = primaryRuntime.SignReplication;
        RuntimePlayerVitalsReplicator vitalsReplication = primaryRuntime.VitalsReplication;
        RuntimeWorldItemStore worldItems = primaryRuntime.WorldItems;
        RuntimePlayerSpawnCommitIngress spawnIngress = primaryRuntime.SpawnIngress;
        RuntimePlayerAppearanceIngress appearanceIngress = primaryRuntime.AppearanceIngress;
        RuntimePlayerEquipmentIngress equipmentIngress = primaryRuntime.EquipmentIngress;
        RuntimePlayerHealthIngress healthIngress = primaryRuntime.HealthIngress;
        RuntimePlayerManaIngress manaIngress = primaryRuntime.ManaIngress;
        RuntimePlayerMovementIngress movementIngress = primaryRuntime.MovementIngress;
        RuntimeWorldItemIngress worldItemIngress = primaryRuntime.WorldItemIngress;
        RuntimeProjectileNetworkIngress projectileIngress = primaryRuntime.ProjectileIngress;
        RuntimeChestNetworkIngress chestIngress = primaryRuntime.ChestIngress;
        RuntimeSignNetworkIngress signIngress = primaryRuntime.SignIngress;
        RuntimeTownNpcHomeNetworkIngress townNpcHomeIngress = primaryRuntime.TownNpcHomeIngress;
        RuntimeNpcTalkNetworkIngress npcTalkIngress = primaryRuntime.NpcTalkIngress;
        RuntimeNpcCatchNetworkIngress npcCatchIngress = primaryRuntime.NpcCatchIngress;
        RuntimePlayerDisconnectIngress disconnectIngress = primaryRuntime.DisconnectIngress;
        RuntimePlayerOperationsTelemetry playerOperations = primaryRuntime.PlayerOperations;
        RuntimeNpcOperationsTelemetry? npcOperations = primaryRuntime.NpcOperations;
        RuntimeProjectileOperationsTelemetry? projectileOperations = primaryRuntime.ProjectileOperations;
        LocalRuntimeWorldItemOperations? worldItemOperations = primaryRuntime.WorldItemOperations;
        RuntimeWorldClockOperationsTelemetry? worldClockTelemetry = primaryRuntime.WorldClockTelemetry;
        var admission = new TerrariaConnectionAdmissionGate(options.MaxPlayers);
        var queueTelemetry = new RuntimeConnectionQueueTelemetry();
        var rateTelemetry = new RuntimeConnectionRateTelemetry();
        var stopTelemetry = new RuntimeConnectionStopTelemetry();
        var networkOperations = new LocalRuntimeNetworkOperations(
            admission,
            runtimeConnections,
            queueTelemetry,
            rateTelemetry,
            npcReplication,
            projectileReplication,
            worldItemReplication,
            stopTelemetry);
        var connectionTasks = new ConcurrentDictionary<long, Task>();
        var connectionDirectory = new RuntimeConnectionDirectory();
        long nextConnectionId = 0;

        using var shutdown = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        using PosixSignalRegistration? terminateSignalRegistration = OperatingSystem.IsWindows()
            ? null
            : PosixSignalRegistration.Create(
                PosixSignal.SIGTERM,
                context =>
                {
                    context.Cancel = true;
                    shutdown.Cancel();
                });

        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        TerminalUiHost? terminalUi = null;
        bool hostRuntimeAttached = false;

        try
        {
            listener.Bind(new IPEndPoint(IPAddress.Any, options.Port));
            listener.Listen(backlog: Math.Max(32, options.MaxPlayers * 2));
            if (!runtimeRegistry.TryAdmit(primaryRuntime, primary: true))
                throw new InvalidOperationException("Primary WorldRuntime admission failed.");

            if (hostLifecycle is not null)
            {
                var hostRuntime = new TerraRuntimeHostRuntime(
                    new TerraRuntimeHostRuntimeInfo(
                        world.Header.Name,
                        options.WorldPath,
                        world.Header.Dimensions.WidthTiles,
                        world.Header.Dimensions.HeightTiles,
                        options.Port,
                        options.MaxPlayers)
                    {
                        RuntimeIdentity = primaryRuntime.Identity,
                        IsolationLevel = WorldIsolationLevel.InProcess,
                        PersistenceMode = primaryRuntime.PersistenceMode
                    },
                    runtimeInterestManagement,
                    playerStateSnapshots,
                    state.NpcShops,
                    state.NpcArchetypes);
                try
                {
                    await hostLifecycle.AttachRuntimeAsync(hostRuntime, shutdown.Token).ConfigureAwait(false);
                    hostRuntimeAttached = true;
                }
                catch (Exception exception)
                {
                    string message = $"Trusted host runtime attachment failed: {exception.Message}";
                    hostLog.Log(
                        RuntimeLogLevel.Error,
                        StructuredLogEventIds.PluginHostRuntimeAttachFailed,
                        StructuredLogCategory.Plugin,
                        "HostModule",
                        message,
                        useStandardError: true);
                    return 29;
                }
            }

            TimeSpan networkReadyDuration = Stopwatch.GetElapsedTime(startupStart);
            long allocatedBytes = Math.Max(
                0L,
                GC.GetTotalAllocatedBytes(precise: false) - allocatedBytesAtStart);
            string startupProfile = FormattableString.Invariant($"startup_profile source={(runtimeCacheHit ? "runtime-cache" : "canonical-wld")} cache_result={cacheDiagnostic.Result} cache_parallel_reads={RuntimeWorldCacheReadOptions.Default.MaxParallelReads} source_stat_ms={sourceStatDuration.TotalMilliseconds:F3} file_read_ms={fileReadDuration.TotalMilliseconds:F3} cache_load_ms={cacheLoadDuration.TotalMilliseconds:F3} wld_total_ms={canonicalLoadProfile.Total.TotalMilliseconds:F3} wld_envelope_header_ms={canonicalLoadProfile.EnvelopeAndHeader.TotalMilliseconds:F3} wld_tile_alloc_ms={canonicalLoadProfile.TileAllocation.TotalMilliseconds:F3} wld_tile_decode_ms={canonicalLoadProfile.TileDecode.TotalMilliseconds:F3} wld_non_tile_ms={canonicalLoadProfile.NonTileSections.TotalMilliseconds:F3} cache_write_ms={cacheWriteDuration.TotalMilliseconds:F3} bootstrap_cache_hit={(bootstrapCacheHit ? "true" : "false")} bootstrap_cache_result={bootstrapCacheDiagnostic.Result} bootstrap_cache_load_ms={bootstrapCacheLoadDuration.TotalMilliseconds:F3} bootstrap_cache_write_ms={bootstrapCacheWriteDuration.TotalMilliseconds:F3} bootstrap_ms={bootstrapDuration.TotalMilliseconds:F3} world_ready_ms={worldReadyDuration.TotalMilliseconds:F3} network_ready_ms={networkReadyDuration.TotalMilliseconds:F3} allocated_mib={allocatedBytes / (1024d * 1024d):F3}");
            hostLog.Log(
                RuntimeLogLevel.Debug,
                StructuredLogEventIds.StartupProfile,
                StructuredLogCategory.Lifecycle,
                "Startup",
                startupProfile);

            string listeningMessage =
                $"TerraRuntime listening on 0.0.0.0:{options.Port}; " +
                $"world='{world.Header.Name}' {world.Header.Dimensions.WidthTiles}x{world.Header.Dimensions.HeightTiles}; " +
                $"maxPlayers={options.MaxPlayers}; " +
                $"interestManagement={(runtimeInterestManagement.IsEnabled ? "enabled" : "disabled")}; " +
                $"tui={(options.TerminalUiEnabled ? "enabled" : "disabled")}.";
            hostLog.Log(
                RuntimeLogLevel.Information,
                StructuredLogEventIds.NetworkListenerReady,
                StructuredLogCategory.Network,
                "Server",
                listeningMessage);

            if (options.TerminalUiEnabled)
            {
                try
                {
                    string worldAssetRoot = Path.GetDirectoryName(options.WorldPath) ?? Directory.GetCurrentDirectory();
                    var transferCoordinator = new Level1PlayerTransferCoordinator(
                        connectionDirectory,
                        runtimeRegistry,
                        sandboxHost);
                    var sandboxOperations = new SandboxOperations(
                        sandboxHost,
                        worldAssetRoot,
                        primaryRuntime.World.Header.Dimensions.WidthTiles,
                        primaryRuntime.World.Header.Dimensions.HeightTiles,
                        transferCoordinator);
                    var dashboardOperations = new LocalRuntimeDashboardOperations(
                        gameLoop,
                        admission,
                        runtimeInterestManagement,
                        world.Header.Name,
                        world.Header.Dimensions.WidthTiles,
                        world.Header.Dimensions.HeightTiles,
                        options.Port,
                        options.MaxPlayers,
                        GameLoopOptions.DefaultTicksPerSecond);
                    var worldOperations = new LocalRuntimeWorldOperations(
                        new RuntimeWorldSnapshot(
                            Ready: true,
                            Name: world.Header.Name,
                            WorldId: world.Header.WorldId,
                            UniqueId: world.Header.UniqueId,
                            FormatVersion: world.Envelope.FormatVersion,
                            WorldGeneratorVersion: world.Header.WorldGeneratorVersion,
                            WidthTiles: world.Header.Dimensions.WidthTiles,
                            HeightTiles: world.Header.Dimensions.HeightTiles,
                            TileCount: world.Tiles.Count,
                            ChestCount: world.Chests.Length,
                            SignCount: world.Signs.Length,
                            TownNpcCount: world.Npcs.TownNpcs.Length,
                            PersistentNpcCount: world.Npcs.PersistentNpcs.Length,
                            TileEntityCount: world.TileEntities.Length,
                            PressurePlateCount: world.PressurePlates.Length,
                            TownRoomCount: world.TownRooms.Length,
                            RuntimeCacheHit: runtimeCacheHit,
                            InitialCacheResult: cacheDiagnostic.Result.ToString(),
                            CacheParallelReads: RuntimeWorldCacheReadOptions.Default.MaxParallelReads,
                            InitialCacheDetailCode: cacheDiagnostic.DetailCode,
                            FileReadMilliseconds: fileReadDuration.TotalMilliseconds,
                            CacheLoadMilliseconds: cacheLoadDuration.TotalMilliseconds,
                            CanonicalWorldLoadMilliseconds: canonicalLoadProfile.Total.TotalMilliseconds,
                            CacheWriteMilliseconds: cacheWriteDuration.TotalMilliseconds,
                            BootstrapMilliseconds: bootstrapDuration.TotalMilliseconds,
                            WorldReadyMilliseconds: worldReadyDuration.TotalMilliseconds,
                            NetworkReadyMilliseconds: networkReadyDuration.TotalMilliseconds,
                            CapturedAtUtc: DateTimeOffset.UtcNow),
                        worldClockTelemetry,
                        () => primaryRuntime.SectionCacheSnapshot,
                        () => primaryRuntime.CaptureSaveStatus() ?? default,
                        primaryRuntime.TryRequestSave);
                    terminalUi = TerminalUiHost.Start(
                        dashboardOperations,
                        playerOperations,
                        npcOperations!,
                        networkOperations,
                        worldOperations,
                        runtimeLogs,
                        hostLog.SetTerminalUiActive,
                        message => hostLog.Log(
                            RuntimeLogLevel.Error,
                            StructuredLogEventIds.OperationsTerminalUiFailed,
                            StructuredLogCategory.Operations,
                            "TerminalUI",
                            message,
                            useStandardError: true),
                        shutdown.Token,
                        projectileOperations,
                        worldItemOperations,
                        sandboxOperations);
                }
                catch (Exception exception)
                {
                    string message =
                        $"Terminal UI could not start; continuing in plain-console mode: {exception.Message}";
                    hostLog.Log(
                        RuntimeLogLevel.Error,
                        StructuredLogEventIds.OperationsTerminalUiFailed,
                        StructuredLogCategory.Operations,
                        "TerminalUI",
                        message,
                        useStandardError: true);
                }
            }

            while (!shutdown.IsCancellationRequested)
            {
                Socket socket;
                try
                {
                    socket = await listener.AcceptAsync(shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException exception) when (!shutdown.IsCancellationRequested)
                {
                    hostLog.Log(
                        RuntimeLogLevel.Warning,
                        StructuredLogEventIds.NetworkAcceptFailed,
                        StructuredLogCategory.Network,
                        "Network",
                        $"Accept failed: {exception.SocketErrorCode}.",
                        useStandardError: true);
                    continue;
                }

                if (!admission.TryAcquire(out TerrariaConnectionAdmissionGate.Lease? admissionLease) || admissionLease is null)
                {
                    socket.Dispose();
                    continue;
                }

                long connectionId = Interlocked.Increment(ref nextConnectionId);
                Task connectionTask = RunConnectionAsync(
                    connectionId,
                    socket,
                    admissionLease,
                    primaryRuntime,
                    connectionDirectory,
                    queueTelemetry,
                    rateTelemetry,
                    stopTelemetry,
                    hostLog,
                    shutdown.Token);
                connectionTasks[connectionId] = connectionTask;
                _ = connectionTask.ContinueWith(
                    completed => connectionTasks.TryRemove(connectionId, out _),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        catch (SocketException exception)
        {
            string message = $"Failed to start listener on port {options.Port}: {exception.Message}";
            hostLog.Log(
                RuntimeLogLevel.Error,
                StructuredLogEventIds.NetworkListenerStartFailed,
                StructuredLogCategory.Network,
                "Network",
                message,
                useStandardError: true);
            return 28;
        }
        finally
        {
            shutdown.Cancel();
            terminalUi?.Dispose();
            Console.CancelKeyPress -= cancelHandler;

            Task[] activeConnections = connectionTasks.Values.ToArray();
            if (activeConnections.Length != 0)
            {
                try
                {
                    await Task.WhenAll(activeConnections).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    string message = $"Connection shutdown observed a fault: {exception.Message}";
                    hostLog.Log(
                        RuntimeLogLevel.Error,
                        StructuredLogEventIds.NetworkShutdownFault,
                        StructuredLogCategory.Network,
                        "Network",
                        message,
                        useStandardError: true);
                }
            }

            if (hostRuntimeAttached && hostLifecycle is not null)
            {
                try
                {
                    await hostLifecycle.DetachRuntimeAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    string message = $"Trusted host runtime detach failed: {exception.Message}";
                    hostLog.Log(
                        RuntimeLogLevel.Error,
                        StructuredLogEventIds.PluginHostRuntimeDetachFailed,
                        StructuredLogCategory.Plugin,
                        "HostModule",
                        message,
                        useStandardError: true);
                }
            }

            try
            {
                bool stopped = await primaryRuntime.StopAsync(
                    TimeSpan.FromSeconds(5),
                    captureFinalSave: true).ConfigureAwait(false);
                if (!stopped)
                {
                    const string message =
                        "Authoritative commands did not drain or the world loop did not stop within the shutdown deadline; the canonical checkpoint was not replaced.";
                    hostLog.Log(
                        RuntimeLogLevel.Error,
                        StructuredLogEventIds.ShutdownCommandDrainTimedOut,
                        StructuredLogCategory.Lifecycle,
                        "Runtime",
                        message,
                        useStandardError: true);
                }
                else if (gameLoop.Fault is null)
                {
                    InvalidateRuntimeCache(runtimeBootstrapCachePath, hostLog);
                    hostLog.Log(
                        RuntimeLogLevel.Information,
                        StructuredLogEventIds.PersistenceWorldCheckpointCommitted,
                        StructuredLogCategory.Persistence,
                        "WorldSave",
                        $"Canonical tile/chest/clock world checkpoint committed: '{options.WorldPath}'.");
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
            {
                hostLog.Log(
                    RuntimeLogLevel.Error,
                    StructuredLogEventIds.PersistenceWorldCheckpointSaveFailed,
                    StructuredLogCategory.Persistence,
                    "WorldSave",
                    $"Canonical world save failed; the previous checkpoint remains authoritative: {exception.Message}",
                    useStandardError: true);
            }

            if (gameLoop.Fault is Exception gameLoopFault)
            {
                hostLog.Log(
                    RuntimeLogLevel.Error,
                    StructuredLogEventIds.PersistenceWorldCheckpointSuppressedByLoopFault,
                    StructuredLogCategory.Persistence,
                    "WorldSave",
                    $"Authoritative loop faulted; refusing to overwrite the last canonical checkpoint: {gameLoopFault.Message}",
                    useStandardError: true);
            }
        }

        return 0;
    }

    private static async Task RunConnectionAsync(
        long connectionId,
        Socket socket,
        TerrariaConnectionAdmissionGate.Lease admissionLease,
        WorldRuntime primaryRuntime,
        RuntimeConnectionDirectory connectionDirectory,
        RuntimeConnectionQueueTelemetry queueTelemetry,
        RuntimeConnectionRateTelemetry rateTelemetry,
        RuntimeConnectionStopTelemetry stopTelemetry,
        RuntimeHostLog hostLog,
        CancellationToken cancellationToken)
    {
        string remote = socket.RemoteEndPoint?.ToString() ?? "unknown";
        GameCommandSourceId source = GameCommandSourceId.FromConnection(connectionId);
        var connectionContext = new StructuredLogContext(
            CorrelationId: $"connection-{connectionId}",
            ConnectionId: connectionId.ToString());
        hostLog.Log(
            RuntimeLogLevel.Information,
            StructuredLogEventIds.NetworkConnectionAccepted,
            StructuredLogCategory.Network,
            "Network",
            $"Connection {connectionId} accepted from {remote}.",
            connectionContext,
            bufferedOnly: !hostLog.IsPlainConsoleActive);

        using (admissionLease)
        {
            var outbound = new TerrariaConnectionOutboundQueue(
                ConnectionOutboundQueueSizing.Create(primaryRuntime.Slots.Capacity));
            TerrariaConnectionPolicyOptions policyOptions = TerrariaConnectionPolicyOptions.Default;
            var rateAccountant = new TerrariaConnectionRateAccountant(policyOptions.RateBudget);

            if (!RuntimeConnectionWorldBinding.TryCreateInitial(
                    primaryRuntime,
                    source,
                    outbound,
                    out RuntimeConnectionWorldBinding? primaryBinding) ||
                primaryBinding is null)
            {
                socket.Dispose();
                return;
            }

            using var route = new RuntimeConnectionRoute(source, outbound, primaryBinding);
            if (!connectionDirectory.TryRegister(source, route))
            {
                socket.Dispose();
                return;
            }

            if (!queueTelemetry.TryRegister(connectionId, outbound))
            {
                connectionDirectory.TryUnregister(source, out _);
                socket.Dispose();
                return;
            }
            if (!rateTelemetry.TryRegister(connectionId, rateAccountant))
            {
                queueTelemetry.TryUnregister(connectionId);
                connectionDirectory.TryUnregister(source, out _);
                socket.Dispose();
                return;
            }

            try
            {
                try
                {
                    TerrariaSocketRunResult result = await TerrariaSocketConnection.RunAsync(
                        socket,
                        route,
                        outbound,
                        TerrariaFrameDecoderOptions.Default,
                        policyOptions,
                        rateAccountant,
                        cancellationToken).ConfigureAwait(false);
                    stopTelemetry.Record(result.StopReason);
                    WorldRuntime activeRuntime = route.ActiveRuntime;
                    string message =
                        $"Connection {connectionId} ({remote}) stopped: {result.StopReason}; " +
                        $"runtime={activeRuntime.Identity}, bootstrap={route.ActiveBootstrapStopReason}, state={route.ActiveJoinState}; " +
                        $"inbound={result.Inbound}; rate={result.Rate}; outbound={result.Outbound.Reason}.";
                    hostLog.Log(
                        RuntimeLogLevel.Information,
                        StructuredLogEventIds.NetworkConnectionStopped,
                        StructuredLogCategory.Network,
                        "Network",
                        message,
                        connectionContext);
                }
                catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        hostLog.Log(
                            RuntimeLogLevel.Warning,
                            StructuredLogEventIds.NetworkConnectionFailed,
                            StructuredLogCategory.Network,
                            "Network",
                            $"Connection {connectionId} ({remote}) failed: {exception.Message}",
                            connectionContext,
                            useStandardError: true);
                    }
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
            finally
            {
                rateTelemetry.TryUnregister(connectionId);
                queueTelemetry.TryUnregister(connectionId);
                connectionDirectory.TryUnregister(source, out _);
                try
                {
                    route.DisconnectActive();
                }
                catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        hostLog.Log(
                            RuntimeLogLevel.Warning,
                            StructuredLogEventIds.NetworkDisconnectEnqueueFailed,
                            StructuredLogCategory.Network,
                            "Network",
                            $"Connection {connectionId} ({remote}) could not complete authoritative route detach: {exception.Message}",
                            connectionContext,
                            useStandardError: true);
                    }
                }
            }
        }
    }

    private static void InvalidateRuntimeCache(string path, RuntimeHostLog hostLog)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            hostLog.Log(
                RuntimeLogLevel.Warning,
                StructuredLogEventIds.PersistenceRuntimeCacheInvalidationFailed,
                StructuredLogCategory.Persistence,
                "WorldSave",
                $"Saved canonical world but could not invalidate runtime cache '{path}': {exception.Message}",
                useStandardError: true);
        }
    }

    private static async Task<StableWorldReadResult> ReadStableWorldAsync(
        string worldPath,
        RuntimeWorldSourceStamp initialStamp)
    {
        long start = Stopwatch.GetTimestamp();
        RuntimeWorldSourceStamp expectedStamp = initialStamp;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            byte[] bytes;
            try
            {
                bytes = await File.ReadAllBytesAsync(worldPath).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new StableWorldReadResult(
                    false,
                    null,
                    default,
                    Stopwatch.GetElapsedTime(start),
                    $"Failed to read world file '{worldPath}': {exception.Message}");
            }

            if (!RuntimeWorldSnapshotCache.TryCaptureSourceStamp(worldPath, out RuntimeWorldSourceStamp actualStamp))
            {
                return new StableWorldReadResult(
                    false,
                    null,
                    default,
                    Stopwatch.GetElapsedTime(start),
                    $"Failed to stat world file '{worldPath}' after reading it.");
            }

            if (actualStamp == expectedStamp && actualStamp.Length == bytes.LongLength)
            {
                return new StableWorldReadResult(
                    true,
                    bytes,
                    actualStamp,
                    Stopwatch.GetElapsedTime(start),
                    null);
            }

            expectedStamp = actualStamp;
        }

        return new StableWorldReadResult(
            false,
            null,
            default,
            Stopwatch.GetElapsedTime(start),
            $"World file '{worldPath}' changed while it was being read twice; refusing to build a mixed snapshot.");
    }

    internal static WorldFileLoadLimits CreateServerWorldLoadLimits() =>
        new(
            MaxTileCount: 32_000_000,
            MaxItemsPerChest: 100,
            MaxTotalChestItems: 1_000_000,
            MaxTextBytesPerSign: 64 * 1024,
            MaxTotalSignTextBytes: 64L * 1024 * 1024,
            Npcs: new WorldFileNpcDecodeOptions(
                MaxShimmeredTownNpcIndices: 1_024,
                MaxShimmerIndexExclusive: 1_024,
                MaxTownNpcs: 1_024,
                MaxPersistentNpcs: 4_096,
                MaxNameBytesPerTownNpc: 4 * 1024,
                MaxTotalNameBytes: 4L * 1024 * 1024),
            MaxTileEntities: 100_000,
            MaxPressurePlates: 1_000_000,
            MaxTownRooms: VanillaWorldFormat326.NpcTypeCount,
            Bestiary: new WorldFileBestiaryLimits(
                MaxKillEntries: 100_000,
                MaxSightEntries: 100_000,
                MaxChatEntries: 100_000,
                MaxPersistentIdBytes: 4 * 1024,
                MaxTotalPersistentIdBytes: 64L * 1024 * 1024),
            RuntimeMetadata: new WorldFileRuntimeMetadataLimits(
                MaxStringBytes: 64 * 1024,
                MaxTotalStringBytes: 64L * 1024 * 1024,
                MaxAnglerNames: 4_096,
                MaxBannerEntries: 8_192,
                MaxPartyNpcEntries: 4_096,
                MaxManifestBytes: 4 * 1024 * 1024));

    private readonly record struct StableWorldReadResult(
        bool Success,
        byte[]? Bytes,
        RuntimeWorldSourceStamp Stamp,
        TimeSpan Duration,
        string? Error);
}
