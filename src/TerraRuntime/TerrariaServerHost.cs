using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;
using TerraRuntime.Network;
using TerraRuntime.Operations;
using TerraRuntime.Protocol;
using TerraRuntime.TerminalUI;
using TerraRuntime.World;

namespace TerraRuntime;

public static class TerrariaServerHost
{
    // Correctness-first ceiling until section rebuild throughput is measured on representative worlds.
    // One worker plus one queued item bounds live rebuild snapshots to at most two network sections.
    private const int SectionCacheWorkerCount = 1;
    private const int SectionCacheWorkCapacity = 1;
    private const int SectionCacheCompletionCapacity = 1;

    /// <summary>
    /// Runs one Terraria world. The optional interest-management control is the only supported
    /// external switch for runtime visibility optimization; spatial policy remains owned by TerraRuntime.
    /// </summary>
    public static async Task<int> RunAsync(
        ServerHostOptions options,
        IInterestManagementControl? interestManagement = null,
        ITerraRuntimeHostLifecycle? hostLifecycle = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var runtimeLogs = new RuntimeLogBuffer();
        var hostLog = new RuntimeHostLog(runtimeLogs);

        IInterestManagementControl runtimeInterestManagement =
            interestManagement ?? new InterestManagementControl(options.InterestManagementEnabled);
        if (options.InterestManagementEnabled)
            runtimeInterestManagement.SetEnabled(true);

        if (!File.Exists(options.WorldPath))
        {
            Console.Error.WriteLine($"World file not found: {options.WorldPath}");
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
            Console.Error.WriteLine($"Failed to stat world file '{options.WorldPath}'.");
            return 25;
        }
        sourceStatDuration = Stopwatch.GetElapsedTime(sourceStatStart);

        WorldFileLoadLimits worldLoadLimits = CreateServerWorldLoadLimits();
        string runtimeCachePath = RuntimeWorldSnapshotCache.GetCachePath(options.WorldPath);
        long cacheLoadStart = Stopwatch.GetTimestamp();
        RuntimeWorldSnapshotLoadDiagnostic cacheDiagnostic = RuntimeWorldSnapshotCache.TryLoad(
            runtimeCachePath,
            sourceStamp,
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
                Console.Error.WriteLine($"Failed to re-stat world file '{options.WorldPath}' after cache load.");
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
            runtimeCacheHit = true;
            worldReadyDuration = Stopwatch.GetElapsedTime(startupStart);
            Console.WriteLine($"Runtime world cache hit: '{runtimeCachePath}'.");
        }
        else
        {
            Console.WriteLine(
                $"Runtime world cache miss: result={cacheDiagnostic.Result}, code={cacheDiagnostic.DetailCode}; " +
                "falling back to canonical .wld.");

            StableWorldReadResult stableRead = await ReadStableWorldAsync(
                options.WorldPath,
                sourceStamp).ConfigureAwait(false);
            fileReadDuration = stableRead.Duration;
            if (!stableRead.Success || stableRead.Bytes is null)
            {
                Console.Error.WriteLine(stableRead.Error ?? $"Failed to read world file '{options.WorldPath}'.");
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
                Console.Error.WriteLine(
                    $"World load failed: result={diagnostic.Result}, stage={diagnostic.Stage}, code={diagnostic.StageResultCode}.");
                return 26;
            }

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
                Console.WriteLine($"Runtime world cache rebuilt: '{runtimeCachePath}'.");
            }
            else
            {
                Console.Error.WriteLine(
                    $"Runtime world cache rebuild skipped/failed: result={cacheWrite.Result}. " +
                    "The canonical .wld remains the recovery checkpoint.");
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
            Console.Error.WriteLine(
                $"World save template load failed: source={saveTemplateLoad.Source}, " +
                $"cache_result={saveTemplateLoad.CacheResult}, error={saveTemplateLoad.Error ?? "unknown"}. " +
                "Refusing to start a mutable world without a canonical persistence checkpoint.");
            return 30;
        }

        Console.WriteLine(
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
            Console.WriteLine($"Runtime bootstrap cache hit: '{runtimeBootstrapCachePath}'.");
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
                Console.Error.WriteLine($"Failed to prepare join bootstrap packets: {exception.Message}");
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
                Console.WriteLine($"Runtime bootstrap cache rebuilt: '{runtimeBootstrapCachePath}'.");
            }
            else
            {
                Console.Error.WriteLine(
                    $"Runtime bootstrap cache rebuild skipped/failed: result={bootstrapCacheWrite.Result}.");
            }
        }

        var worldItemReplication = new RuntimeWorldItemReplicationRegistry();
        var worldItems = new RuntimeWorldItemStore(worldItemReplication);
        LocalRuntimeWorldItemOperations? worldItemOperations = options.TerminalUiEnabled
            ? new LocalRuntimeWorldItemOperations(worldItems)
            : null;
        RuntimeWorldClockOperationsTelemetry? worldClockTelemetry = options.TerminalUiEnabled
            ? new RuntimeWorldClockOperationsTelemetry()
            : null;
        var worldClock = RuntimeWorldClock.FromWorld(
            world.RuntimeMetadata,
            world.CreativePowers,
            worldClockTelemetry);
        var runtimeConnections = new RuntimeConnectionRegistry(
            runtimeInterestManagement,
            world.Header.Dimensions);
        var npcReplication = new RuntimeNpcReplicationRegistry();
        RuntimeNpcOperationsTelemetry? npcOperations = options.TerminalUiEnabled
            ? new RuntimeNpcOperationsTelemetry()
            : null;
        INpcStateCommitSink npcCommitSink = npcOperations is null
            ? npcReplication
            : new RuntimeNpcStateCommitFanout(npcReplication, npcOperations);
        var npcStore = new RuntimeNpcStore(commitSink: npcCommitSink);
        var projectileReplication = new RuntimeProjectileReplicationRegistry();
        RuntimeProjectileOperationsTelemetry? projectileOperations = options.TerminalUiEnabled
            ? new RuntimeProjectileOperationsTelemetry()
            : null;
        IProjectileStateCommitSink projectileCommitSink = projectileOperations is null
            ? projectileReplication
            : new RuntimeProjectileStateCommitFanout(projectileReplication, projectileOperations);
        var projectileStore = new RuntimeProjectileStore(commitSink: projectileCommitSink);
        var tileManipulationReplication = new RuntimeTileManipulationReplicationRegistry();
        var chestReplication = new RuntimeChestReplicationRegistry();
        var chestStore = new RuntimeChestStore(world.Chests);
        var chestCommands = new RuntimeChestCommandProcessor(chestStore, chestReplication);
        var signReplication = new RuntimeSignReplicationRegistry();
        var signStore = new RuntimeSignStore(world.Signs, world.Tiles);
        var signCommands = new RuntimeSignCommandProcessor(signStore, signReplication);
        var worldSaveService = new RuntimeWorldTileChestSaveService(
            options.WorldPath,
            world.Envelope,
            world.Header,
            worldSaveTemplate,
            world.Tiles,
            chestStore,
            worldClock: worldClock,
            signStore: signStore);
        var worldAutosave = new VanillaWorldAutosaveScheduler();
        var vitalsReplication = new RuntimePlayerVitalsReplicator();
        var playerOperations = new RuntimePlayerOperationsTelemetry();
        var playerNetworkEvents = new RuntimePlayerEventDispatcher(
            runtimeConnections,
            vitalsReplication,
            playerOperations);
        var projectileAndItemReplicationEvents = new RuntimePlayerEventFanout(
            projectileReplication,
            worldItemReplication);
        var entityReplicationEvents = new RuntimePlayerEventFanout(
            npcReplication,
            projectileAndItemReplicationEvents);
        var tileAndEntityReplicationEvents = new RuntimePlayerEventFanout(
            tileManipulationReplication,
            entityReplicationEvents);
        var chestAndEntityReplicationEvents = new RuntimePlayerEventFanout(
            chestReplication,
            tileAndEntityReplicationEvents);
        var signAndEntityReplicationEvents = new RuntimePlayerEventFanout(
            signReplication,
            chestAndEntityReplicationEvents);
        var playerEvents = new RuntimePlayerEventFanout(playerNetworkEvents, signAndEntityReplicationEvents);
        var slots = new PlayerSlotPool(options.MaxPlayers);
        var serverPlayerIdentities = new RuntimeServerPlayerSlotRegistry(slots);
        var serverPlayerStates = new RuntimeServerPlayerStateStore(serverPlayerIdentities, slots.Capacity);
        var state = new ServerRuntimeState(
            playerEvents,
            npcs: npcStore,
            worldTiles: world.Tiles,
            worldClock: worldClock,
            projectiles: projectileStore,
            worldItems: worldItems,
            projectileReplication: projectileReplication,
            tileManipulationReplication: tileManipulationReplication,
            serverPlayerStates: serverPlayerStates,
            serverPlayerIdentities: serverPlayerIdentities);
        using var sectionCacheRebuild = new SectionCacheRebuildPipeline(
            world,
            bootstrapPackets,
            workerCount: SectionCacheWorkerCount,
            workCapacity: SectionCacheWorkCapacity,
            completionCapacity: SectionCacheCompletionCapacity);
        using var gameLoop = new AuthoritativeGameLoop<ServerRuntimeState, RuntimeCommand>(
            state,
            (runtime, command) =>
            {
                if (!signCommands.TryApply(command) && !chestCommands.TryApply(command))
                    runtime.Apply(command);
            },
            runtime =>
            {
                runtime.Tick();
                sectionCacheRebuild.Tick();
                if (worldAutosave.Tick())
                    worldSaveService.RequestSave();
                worldSaveService.Tick();
            });
        var commandIngress = new AuthoritativeCommandIngress<ServerRuntimeState, RuntimeCommand>(gameLoop);
        var playerStateSnapshots = new RuntimePlayerStateSnapshotReader(commandIngress);
        var spawnIngress = new RuntimePlayerSpawnCommitIngress(commandIngress);
        var appearanceIngress = new RuntimePlayerAppearanceIngress(commandIngress);
        var equipmentIngress = new RuntimePlayerEquipmentIngress(commandIngress);
        var healthIngress = new RuntimePlayerHealthIngress(commandIngress);
        var manaIngress = new RuntimePlayerManaIngress(commandIngress);
        var movementIngress = new RuntimePlayerMovementIngress(commandIngress);
        var worldItemIngress = new RuntimeWorldItemIngress(commandIngress);
        var projectileIngress = new RuntimeProjectileNetworkIngress(commandIngress);
        var chestIngress = new RuntimeChestNetworkIngress(commandIngress);
        var signIngress = new RuntimeSignNetworkIngress(commandIngress);
        var disconnectIngress = new RuntimePlayerDisconnectIngress(commandIngress);
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
            sectionCacheRebuild.Start();
            gameLoop.Start();

            if (hostLifecycle is not null)
            {
                var hostRuntime = new TerraRuntimeHostRuntime(
                    new TerraRuntimeHostRuntimeInfo(
                        world.Header.Name,
                        options.WorldPath,
                        world.Header.Dimensions.WidthTiles,
                        world.Header.Dimensions.HeightTiles,
                        options.Port,
                        options.MaxPlayers),
                    runtimeInterestManagement,
                    playerStateSnapshots);
                try
                {
                    await hostLifecycle.AttachRuntimeAsync(hostRuntime, shutdown.Token).ConfigureAwait(false);
                    hostRuntimeAttached = true;
                }
                catch (Exception exception)
                {
                    string message = $"Trusted host runtime attachment failed: {exception.Message}";
                    hostLog.Write(RuntimeLogLevel.Error, "HostModule", message, useStandardError: true);
                    return 29;
                }
            }

            TimeSpan networkReadyDuration = Stopwatch.GetElapsedTime(startupStart);
            long allocatedBytes = Math.Max(
                0L,
                GC.GetTotalAllocatedBytes(precise: false) - allocatedBytesAtStart);
            string startupProfile = FormattableString.Invariant($"startup_profile source={(runtimeCacheHit ? "runtime-cache" : "canonical-wld")} cache_result={cacheDiagnostic.Result} cache_parallel_reads={RuntimeWorldCacheReadOptions.Default.MaxParallelReads} source_stat_ms={sourceStatDuration.TotalMilliseconds:F3} file_read_ms={fileReadDuration.TotalMilliseconds:F3} cache_load_ms={cacheLoadDuration.TotalMilliseconds:F3} wld_total_ms={canonicalLoadProfile.Total.TotalMilliseconds:F3} wld_envelope_header_ms={canonicalLoadProfile.EnvelopeAndHeader.TotalMilliseconds:F3} wld_tile_alloc_ms={canonicalLoadProfile.TileAllocation.TotalMilliseconds:F3} wld_tile_decode_ms={canonicalLoadProfile.TileDecode.TotalMilliseconds:F3} wld_non_tile_ms={canonicalLoadProfile.NonTileSections.TotalMilliseconds:F3} cache_write_ms={cacheWriteDuration.TotalMilliseconds:F3} bootstrap_cache_hit={(bootstrapCacheHit ? "true" : "false")} bootstrap_cache_result={bootstrapCacheDiagnostic.Result} bootstrap_cache_load_ms={bootstrapCacheLoadDuration.TotalMilliseconds:F3} bootstrap_cache_write_ms={bootstrapCacheWriteDuration.TotalMilliseconds:F3} bootstrap_ms={bootstrapDuration.TotalMilliseconds:F3} world_ready_ms={worldReadyDuration.TotalMilliseconds:F3} network_ready_ms={networkReadyDuration.TotalMilliseconds:F3} allocated_mib={allocatedBytes / (1024d * 1024d):F3}");
            Console.WriteLine(startupProfile);
            runtimeLogs.Publish(RuntimeLogLevel.Debug, "Startup", startupProfile);

            string listeningMessage =
                $"TerraRuntime listening on 0.0.0.0:{options.Port}; " +
                $"world='{world.Header.Name}' {world.Header.Dimensions.WidthTiles}x{world.Header.Dimensions.HeightTiles}; " +
                $"maxPlayers={options.MaxPlayers}; " +
                $"interestManagement={(runtimeInterestManagement.IsEnabled ? "enabled" : "disabled")}; " +
                $"tui={(options.TerminalUiEnabled ? "enabled" : "disabled")}.";
            Console.WriteLine(listeningMessage);
            runtimeLogs.Publish(RuntimeLogLevel.Information, "Server", listeningMessage);

            if (options.TerminalUiEnabled)
            {
                try
                {
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
                            FileReadMilliseconds: fileReadDuration.TotalMilliseconds,
                            CacheLoadMilliseconds: cacheLoadDuration.TotalMilliseconds,
                            CanonicalWorldLoadMilliseconds: canonicalLoadProfile.Total.TotalMilliseconds,
                            CacheWriteMilliseconds: cacheWriteDuration.TotalMilliseconds,
                            BootstrapMilliseconds: bootstrapDuration.TotalMilliseconds,
                            WorldReadyMilliseconds: worldReadyDuration.TotalMilliseconds,
                            NetworkReadyMilliseconds: networkReadyDuration.TotalMilliseconds,
                            CapturedAtUtc: DateTimeOffset.UtcNow),
                        worldClockTelemetry,
                        () => sectionCacheRebuild.Snapshot,
                        worldSaveService.CaptureStatus,
                        worldSaveService.TryRequestSave);
                    terminalUi = TerminalUiHost.Start(
                        dashboardOperations,
                        playerOperations,
                        npcOperations!,
                        networkOperations,
                        worldOperations,
                        runtimeLogs,
                        hostLog.SetTerminalUiActive,
                        message => hostLog.Write(
                            RuntimeLogLevel.Error,
                            "TerminalUI",
                            message,
                            useStandardError: true),
                        shutdown.Token,
                        projectileOperations,
                        worldItemOperations);
                }
                catch (Exception exception)
                {
                    string message =
                        $"Terminal UI could not start; continuing in plain-console mode: {exception.Message}";
                    hostLog.Write(RuntimeLogLevel.Error, "TerminalUI", message, useStandardError: true);
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
                    hostLog.Write(
                        RuntimeLogLevel.Warning,
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
                    slots,
                    bootstrapPackets,
                    spawnIngress,
                    appearanceIngress,
                    equipmentIngress,
                    healthIngress,
                    manaIngress,
                    movementIngress,
                    worldItemIngress,
                    projectileIngress,
                    chestIngress,
                    signIngress,
                    disconnectIngress,
                    runtimeConnections,
                    npcReplication,
                    projectileReplication,
                    worldItemReplication,
                    tileManipulationReplication,
                    chestReplication,
                    signReplication,
                    vitalsReplication,
                    worldItems,
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
            hostLog.Write(RuntimeLogLevel.Error, "Network", message, useStandardError: true);
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
                    hostLog.Write(RuntimeLogLevel.Error, "Runtime", message, useStandardError: true);
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
                    hostLog.Write(RuntimeLogLevel.Error, "HostModule", message, useStandardError: true);
                }
            }

            bool commandsDrained = await WaitForGameLoopCommandDrainAsync(
                gameLoop,
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            if (!commandsDrained)
            {
                const string message =
                    "Accepted authoritative commands did not drain before shutdown; the canonical world checkpoint will not be replaced.";
                hostLog.Write(RuntimeLogLevel.Error, "WorldSave", message, useStandardError: true);
            }

            bool gameLoopStopped = gameLoop.Stop(TimeSpan.FromSeconds(5));
            if (!gameLoopStopped)
            {
                const string message = "Authoritative game loop did not stop within the shutdown deadline.";
                hostLog.Write(RuntimeLogLevel.Error, "Runtime", message, useStandardError: true);
            }
            else if (commandsDrained && gameLoop.Fault is null)
            {
                try
                {
                    worldSaveService.CaptureFinalSaveAfterOwnerStopped();
                    await worldSaveService.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
                    InvalidateRuntimeCache(runtimeCachePath, hostLog);
                    InvalidateRuntimeCache(runtimeBootstrapCachePath, hostLog);
                    hostLog.Write(
                        RuntimeLogLevel.Information,
                        "WorldSave",
                        $"Canonical tile/chest/clock world checkpoint committed: '{options.WorldPath}'.");
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
                {
                    hostLog.Write(
                        RuntimeLogLevel.Error,
                        "WorldSave",
                        $"Canonical world save failed; the previous checkpoint remains authoritative: {exception.Message}",
                        useStandardError: true);
                }
            }
            else if (gameLoop.Fault is Exception gameLoopFault)
            {
                hostLog.Write(
                    RuntimeLogLevel.Error,
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
        PlayerSlotPool slots,
        PlayerBootstrapPacketSet bootstrapPackets,
        IPlayerSpawnCommitIngress spawnIngress,
        IPlayerAppearanceIngress appearanceIngress,
        IPlayerEquipmentIngress equipmentIngress,
        IPlayerHealthIngress healthIngress,
        IPlayerManaIngress manaIngress,
        IPlayerMovementIngress movementIngress,
        IWorldItemIngress worldItemIngress,
        IProjectileNetworkIngress projectileIngress,
        IChestNetworkIngress chestIngress,
        ISignNetworkIngress signIngress,
        RuntimePlayerDisconnectIngress disconnectIngress,
        RuntimeConnectionRegistry runtimeConnections,
        RuntimeNpcReplicationRegistry npcReplication,
        RuntimeProjectileReplicationRegistry projectileReplication,
        RuntimeWorldItemReplicationRegistry worldItemReplication,
        RuntimeTileManipulationReplicationRegistry tileManipulationReplication,
        RuntimeChestReplicationRegistry chestReplication,
        RuntimeSignReplicationRegistry signReplication,
        RuntimePlayerVitalsReplicator vitalsReplication,
        RuntimeWorldItemStore worldItems,
        RuntimeConnectionQueueTelemetry queueTelemetry,
        RuntimeConnectionRateTelemetry rateTelemetry,
        RuntimeConnectionStopTelemetry stopTelemetry,
        RuntimeHostLog hostLog,
        CancellationToken cancellationToken)
    {
        string remote = socket.RemoteEndPoint?.ToString() ?? "unknown";
        GameCommandSourceId source = GameCommandSourceId.FromConnection(connectionId);
        hostLog.Publish(RuntimeLogLevel.Information, "Network", $"Connection {connectionId} accepted from {remote}.");

        using (admissionLease)
        {
            var outbound = new TerrariaConnectionOutboundQueue(ConnectionOutboundQueueSizing.Create(slots.Capacity));
            TerrariaConnectionPolicyOptions policyOptions = TerrariaConnectionPolicyOptions.Default;
            var rateAccountant = new TerrariaConnectionRateAccountant(policyOptions.RateBudget);
            if (!runtimeConnections.TryRegister(source, outbound))
            {
                socket.Dispose();
                return;
            }

            if (!npcReplication.TryRegister(source, outbound))
            {
                runtimeConnections.TryUnregister(source, out _);
                socket.Dispose();
                return;
            }

            if (!projectileReplication.TryRegister(source, outbound))
            {
                npcReplication.TryUnregister(source);
                runtimeConnections.TryUnregister(source, out _);
                socket.Dispose();
                return;
            }

            if (!worldItemReplication.TryRegister(source, outbound))
            {
                projectileReplication.TryUnregister(source);
                npcReplication.TryUnregister(source);
                runtimeConnections.TryUnregister(source, out _);
                socket.Dispose();
                return;
            }

            if (!queueTelemetry.TryRegister(connectionId, outbound))
            {
                worldItemReplication.TryUnregister(source);
                projectileReplication.TryUnregister(source);
                npcReplication.TryUnregister(source);
                runtimeConnections.TryUnregister(source, out _);
                socket.Dispose();
                return;
            }

            if (!rateTelemetry.TryRegister(connectionId, rateAccountant))
            {
                queueTelemetry.TryUnregister(connectionId);
                worldItemReplication.TryUnregister(source);
                projectileReplication.TryUnregister(source);
                npcReplication.TryUnregister(source);
                runtimeConnections.TryUnregister(source, out _);
                socket.Dispose();
                return;
            }

            if (!vitalsReplication.TryRegister(source, outbound))
            {
                rateTelemetry.TryUnregister(connectionId);
                queueTelemetry.TryUnregister(connectionId);
                worldItemReplication.TryUnregister(source);
                projectileReplication.TryUnregister(source);
                npcReplication.TryUnregister(source);
                runtimeConnections.TryUnregister(source, out _);
                socket.Dispose();
                return;
            }

            if (!chestReplication.TryRegister(source, outbound))
            {
                vitalsReplication.TryUnregister(source);
                rateTelemetry.TryUnregister(connectionId);
                queueTelemetry.TryUnregister(connectionId);
                worldItemReplication.TryUnregister(source);
                projectileReplication.TryUnregister(source);
                npcReplication.TryUnregister(source);
                runtimeConnections.TryUnregister(source, out _);
                socket.Dispose();
                return;
            }

            if (!tileManipulationReplication.TryRegister(source, outbound))
            {
                chestReplication.TryUnregister(source);
                vitalsReplication.TryUnregister(source);
                rateTelemetry.TryUnregister(connectionId);
                queueTelemetry.TryUnregister(connectionId);
                worldItemReplication.TryUnregister(source);
                projectileReplication.TryUnregister(source);
                npcReplication.TryUnregister(source);
                runtimeConnections.TryUnregister(source, out _);
                socket.Dispose();
                return;
            }

            if (!signReplication.TryRegister(source, outbound))
            {
                tileManipulationReplication.TryUnregister(source);
                chestReplication.TryUnregister(source);
                vitalsReplication.TryUnregister(source);
                rateTelemetry.TryUnregister(connectionId);
                queueTelemetry.TryUnregister(connectionId);
                worldItemReplication.TryUnregister(source);
                projectileReplication.TryUnregister(source);
                npcReplication.TryUnregister(source);
                runtimeConnections.TryUnregister(source, out _);
                socket.Dispose();
                return;
            }

            using var bootstrapSink = new PlayerBootstrapFrameSink(
                slots,
                outbound,
                bootstrapPackets,
                source,
                spawnIngress,
                appearanceIngress,
                equipmentIngress,
                movementIngress,
                inner: null,
                worldItems: worldItems);
            var vitalsSink = new PlayerVitalsFrameSink(
                source,
                bootstrapSink,
                healthIngress,
                manaIngress);
            var itemSink = new WorldItemFrameSink(
                source,
                bootstrapSink,
                vitalsSink,
                worldItemIngress);
            var projectileSink = new ProjectileLifecycleFrameSink(
                source,
                bootstrapSink,
                itemSink,
                projectileIngress);
            var chestSink = new ChestInteractionFrameSink(
                source,
                bootstrapSink,
                projectileSink,
                chestIngress);
            var signSink = new SignInteractionFrameSink(
                source,
                bootstrapSink,
                chestSink,
                signIngress);

            try
            {
                try
                {
                    TerrariaSocketRunResult result = await TerrariaSocketConnection.RunAsync(
                        socket,
                        signSink,
                        outbound,
                        TerrariaFrameDecoderOptions.Default,
                        policyOptions,
                        rateAccountant,
                        cancellationToken).ConfigureAwait(false);
                    stopTelemetry.Record(result.StopReason);
                    string message =
                        $"Connection {connectionId} ({remote}) stopped: {result.StopReason}; " +
                        $"bootstrap={bootstrapSink.StopReason}, vitals={vitalsSink.StopReason}, items={itemSink.StopReason}, projectiles={projectileSink.StopReason}, chests={chestSink.StopReason}, signs={signSink.StopReason}, tiles={projectileSink.TileStopReason}, state={bootstrapSink.JoinState}; " +
                        $"inbound={result.Inbound}; rate={result.Rate}; outbound={result.Outbound.Reason}.";
                    hostLog.Write(RuntimeLogLevel.Information, "Network", message);
                }
                catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        string message = $"Connection {connectionId} ({remote}) failed: {exception.Message}";
                        hostLog.Write(RuntimeLogLevel.Warning, "Network", message, useStandardError: true);
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
                signReplication.TryUnregister(source);
                tileManipulationReplication.TryUnregister(source);
                chestReplication.TryUnregister(source);
                vitalsReplication.TryUnregister(source);
                worldItemReplication.TryUnregister(source);
                projectileReplication.TryUnregister(source);
                npcReplication.TryUnregister(source);
                if (runtimeConnections.TryUnregister(source, out PlayerHandle? playingPlayer) &&
                    playingPlayer is PlayerHandle player)
                {
                    bool posted = bootstrapSink.AssignedPlayerHandle == player &&
                        disconnectIngress.TryPost(new ConnectionHandle(source, player));
                    if (!posted && !cancellationToken.IsCancellationRequested)
                    {
                        string message =
                            $"Connection {connectionId} ({remote}) could not enqueue authoritative disconnect for {player}.";
                        hostLog.Write(RuntimeLogLevel.Warning, "Runtime", message, useStandardError: true);
                    }
                }
            }
        }
    }

    private static async Task<bool> WaitForGameLoopCommandDrainAsync(
        AuthoritativeGameLoop<ServerRuntimeState, RuntimeCommand> gameLoop,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(gameLoop);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);

        long started = Stopwatch.GetTimestamp();
        while (gameLoop.Snapshot.PendingCommands != 0)
        {
            if (!gameLoop.IsRunning ||
                gameLoop.Fault is not null ||
                Stopwatch.GetElapsedTime(started) >= timeout)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
        }

        return true;
    }

    private static void InvalidateRuntimeCache(string path, RuntimeHostLog hostLog)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            hostLog.Write(
                RuntimeLogLevel.Warning,
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
