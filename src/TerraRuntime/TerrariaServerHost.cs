using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Operations;
using TerraRuntime.Protocol;
using TerraRuntime.TerminalUI;
using TerraRuntime.World;

namespace TerraRuntime;

public static class TerrariaServerHost
{
    private static readonly OutboundQueueOptions ConnectionOutboundQueueOptions = new(
        maxFrames: 4_096,
        maxQueuedBytes: 16L * 1024 * 1024,
        maxFrameBytes: TerrariaFrameDecoderOptions.AbsoluteMaximumFrameLength);

    /// <summary>
    /// Runs one Terraria world. The optional interest-management control is the only supported
    /// external switch for runtime visibility optimization; spatial policy remains owned by TerraRuntime.
    /// </summary>
    public static async Task<int> RunAsync(
        ServerHostOptions options,
        IInterestManagementControl? interestManagement = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var runtimeLogs = new RuntimeLogBuffer();

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
        WorldFileLoadProfile canonicalLoadProfile = default;
        bool runtimeCacheHit = false;

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

        PlayerBootstrapPacketSet bootstrapPackets;
        long bootstrapStart = Stopwatch.GetTimestamp();
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

        var worldItems = new RuntimeWorldItemStore();
        var runtimeConnections = new RuntimeConnectionRegistry(
            runtimeInterestManagement,
            world.Header.Dimensions);
        var vitalsReplication = new RuntimePlayerVitalsReplicator();
        var playerOperations = new RuntimePlayerOperationsTelemetry();
        var playerEvents = new RuntimePlayerEventDispatcher(
            runtimeConnections,
            vitalsReplication,
            playerOperations);
        var state = new ServerRuntimeState(playerEvents);
        using var gameLoop = new AuthoritativeGameLoop<ServerRuntimeState, RuntimeCommand>(
            state,
            static (runtime, command) => runtime.Apply(command),
            static runtime => runtime.Tick());
        var commandIngress = new AuthoritativeCommandIngress<ServerRuntimeState, RuntimeCommand>(gameLoop);
        var spawnIngress = new RuntimePlayerSpawnCommitIngress(commandIngress);
        var appearanceIngress = new RuntimePlayerAppearanceIngress(commandIngress);
        var equipmentIngress = new RuntimePlayerEquipmentIngress(commandIngress);
        var healthIngress = new RuntimePlayerHealthIngress(commandIngress);
        var manaIngress = new RuntimePlayerManaIngress(commandIngress);
        var movementIngress = new RuntimePlayerMovementIngress(commandIngress);
        var disconnectIngress = new RuntimePlayerDisconnectIngress(commandIngress);
        var slots = new PlayerSlotPool(options.MaxPlayers);
        var admission = new TerrariaConnectionAdmissionGate(options.MaxPlayers);
        var queueTelemetry = new RuntimeConnectionQueueTelemetry();
        var networkOperations = new LocalRuntimeNetworkOperations(admission, runtimeConnections, queueTelemetry);
        var connectionTasks = new ConcurrentDictionary<long, Task>();
        long nextConnectionId = 0;

        using var shutdown = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        TerminalUiHost? terminalUi = null;

        try
        {
            listener.Bind(new IPEndPoint(IPAddress.Any, options.Port));
            listener.Listen(backlog: Math.Max(32, options.MaxPlayers * 2));
            gameLoop.Start();

            TimeSpan networkReadyDuration = Stopwatch.GetElapsedTime(startupStart);
            long allocatedBytes = Math.Max(
                0L,
                GC.GetTotalAllocatedBytes(precise: false) - allocatedBytesAtStart);
            string startupProfile = FormattableString.Invariant($"startup_profile source={(runtimeCacheHit ? "runtime-cache" : "canonical-wld")} cache_result={cacheDiagnostic.Result} cache_schema={RuntimeWorldSnapshotCache.SchemaVersion} cache_parallel_reads={RuntimeWorldCacheReadOptions.Default.MaxParallelReads} source_stat_ms={sourceStatDuration.TotalMilliseconds:F3} file_read_ms={fileReadDuration.TotalMilliseconds:F3} cache_load_ms={cacheLoadDuration.TotalMilliseconds:F3} wld_total_ms={canonicalLoadProfile.Total.TotalMilliseconds:F3} wld_envelope_header_ms={canonicalLoadProfile.EnvelopeAndHeader.TotalMilliseconds:F3} wld_tile_alloc_ms={canonicalLoadProfile.TileAllocation.TotalMilliseconds:F3} wld_tile_decode_ms={canonicalLoadProfile.TileDecode.TotalMilliseconds:F3} wld_non_tile_ms={canonicalLoadProfile.NonTileSections.TotalMilliseconds:F3} cache_write_ms={cacheWriteDuration.TotalMilliseconds:F3} bootstrap_ms={bootstrapDuration.TotalMilliseconds:F3} world_ready_ms={worldReadyDuration.TotalMilliseconds:F3} network_ready_ms={networkReadyDuration.TotalMilliseconds:F3} allocated_mib={allocatedBytes / (1024d * 1024d):F3}");
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
                            CacheSchemaVersion: RuntimeWorldSnapshotCache.SchemaVersion,
                            CacheParallelReads: RuntimeWorldCacheReadOptions.Default.MaxParallelReads,
                            FileReadMilliseconds: fileReadDuration.TotalMilliseconds,
                            CacheLoadMilliseconds: cacheLoadDuration.TotalMilliseconds,
                            CanonicalWorldLoadMilliseconds: canonicalLoadProfile.Total.TotalMilliseconds,
                            CacheWriteMilliseconds: cacheWriteDuration.TotalMilliseconds,
                            BootstrapMilliseconds: bootstrapDuration.TotalMilliseconds,
                            WorldReadyMilliseconds: worldReadyDuration.TotalMilliseconds,
                            NetworkReadyMilliseconds: networkReadyDuration.TotalMilliseconds,
                            CapturedAtUtc: DateTimeOffset.UtcNow));
                    terminalUi = TerminalUiHost.Start(
                        dashboardOperations,
                        playerOperations,
                        networkOperations,
                        worldOperations,
                        runtimeLogs,
                        shutdown.Token);
                }
                catch (Exception exception)
                {
                    string message =
                        $"Terminal UI could not start; continuing in plain-console mode: {exception.Message}";
                    runtimeLogs.Publish(RuntimeLogLevel.Error, "TerminalUI", message);
                    Console.Error.WriteLine(message);
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
                    runtimeLogs.Publish(RuntimeLogLevel.Warning, "Network", $"Accept failed: {exception.SocketErrorCode}.");
                    Console.Error.WriteLine($"Accept failed: {exception.SocketErrorCode}.");
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
                    disconnectIngress,
                    runtimeConnections,
                    vitalsReplication,
                    worldItems,
                    queueTelemetry,
                    runtimeLogs,
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
            runtimeLogs.Publish(RuntimeLogLevel.Error, "Network", message);
            Console.Error.WriteLine(message);
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
                    runtimeLogs.Publish(RuntimeLogLevel.Error, "Network", message);
                    Console.Error.WriteLine(message);
                }
            }

            if (!gameLoop.Stop(TimeSpan.FromSeconds(5)))
            {
                const string message = "Authoritative game loop did not stop within the shutdown deadline.";
                runtimeLogs.Publish(RuntimeLogLevel.Error, "Runtime", message);
                Console.Error.WriteLine(message);
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
        RuntimePlayerDisconnectIngress disconnectIngress,
        RuntimeConnectionRegistry runtimeConnections,
        RuntimePlayerVitalsReplicator vitalsReplication,
        RuntimeWorldItemStore worldItems,
        RuntimeConnectionQueueTelemetry queueTelemetry,
        RuntimeLogBuffer runtimeLogs,
        CancellationToken cancellationToken)
    {
        string remote = socket.RemoteEndPoint?.ToString() ?? "unknown";
        GameCommandSourceId source = GameCommandSourceId.FromConnection(connectionId);
        runtimeLogs.Publish(RuntimeLogLevel.Information, "Network", $"Connection {connectionId} accepted from {remote}.");

        using (admissionLease)
        {
            var outbound = new TerrariaConnectionOutboundQueue(ConnectionOutboundQueueOptions);
            if (!runtimeConnections.TryRegister(source, outbound))
            {
                socket.Dispose();
                return;
            }

            if (!queueTelemetry.TryRegister(connectionId, outbound))
            {
                runtimeConnections.TryUnregister(source, out _);
                socket.Dispose();
                return;
            }

            if (!vitalsReplication.TryRegister(source, outbound))
            {
                queueTelemetry.TryUnregister(connectionId);
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
            var sink = new PlayerVitalsFrameSink(
                source,
                bootstrapSink,
                healthIngress,
                manaIngress);

            try
            {
                try
                {
                    TerrariaSocketRunResult result = await TerrariaSocketConnection.RunAsync(
                        socket,
                        sink,
                        outbound,
                        TerrariaFrameDecoderOptions.Default,
                        cancellationToken).ConfigureAwait(false);
                    string message =
                        $"Connection {connectionId} ({remote}) stopped: {result.StopReason}; " +
                        $"bootstrap={bootstrapSink.StopReason}, vitals={sink.StopReason}, state={bootstrapSink.JoinState}; " +
                        $"inbound={result.Inbound}; outbound={result.Outbound.Reason}.";
                    runtimeLogs.Publish(RuntimeLogLevel.Information, "Network", message);
                    Console.WriteLine(message);
                }
                catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        string message = $"Connection {connectionId} ({remote}) failed: {exception.Message}";
                        runtimeLogs.Publish(RuntimeLogLevel.Warning, "Network", message);
                        Console.Error.WriteLine(message);
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
                queueTelemetry.TryUnregister(connectionId);
                vitalsReplication.TryUnregister(source);
                if (runtimeConnections.TryUnregister(source, out PlayerHandle? playingPlayer) &&
                    playingPlayer is PlayerHandle player)
                {
                    bool posted = bootstrapSink.AssignedPlayerHandle == player &&
                        disconnectIngress.TryPost(new ConnectionHandle(source, player));
                    if (!posted && !cancellationToken.IsCancellationRequested)
                    {
                        string message =
                            $"Connection {connectionId} ({remote}) could not enqueue authoritative disconnect for {player}.";
                        runtimeLogs.Publish(RuntimeLogLevel.Warning, "Runtime", message);
                        Console.Error.WriteLine(message);
                    }
                }
            }
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
