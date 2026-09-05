using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.Application.Operations;
using TerraRuntime.Application.TerminalUI;
using TerraRuntime.World;
using StructuredLogCategory = TerraRuntime.Contracts.Diagnostics.RuntimeLogCategory;
using StructuredLogEventIds = TerraRuntime.Contracts.Diagnostics.RuntimeLogEventIds;

namespace TerraRuntime.Application;

/// <summary>
/// Owns one server-process session after a world has been prepared: the primary runtime, sandbox/runtime registry,
/// OS shutdown signals, trusted-host attachment, operator UI and orderly world shutdown. Public TCP connection
/// lifetime is delegated to <see cref="ServerConnectionAcceptor"/> while replaceable public bind/listen generations
/// are owned by <see cref="ListenerManager"/>.
/// </summary>
internal sealed class ServerProcessSession : IDisposable
{
    private readonly ServerHostOptions options;
    private readonly IInterestManagementControl interestManagement;
    private readonly ILifecycle? hostLifecycle;
    private readonly RuntimeLogBuffer runtimeLogs;
    private readonly RuntimeHostLog hostLog;
    private readonly PreparedWorldStartup startup;
    private readonly WorldRuntime primaryRuntime;
    private readonly WorldRegistry runtimeRegistry;
    private readonly SandboxHost sandboxHost;
    private readonly ServerConnectionAcceptor connections;
    private readonly ListenerManager listeners;
    private readonly CancellationTokenSource shutdown = new();
    private readonly ConsoleCancelEventHandler cancelHandler;
    private PosixSignalRegistration? terminateSignalRegistration;
    private Host? terminalUi;
    private bool hostRuntimeAttached;
    private bool consoleHandlerAttached;
    private int disposed;

    public ServerProcessSession(
        ServerHostOptions options,
        IInterestManagementControl interestManagement,
        ILifecycle? hostLifecycle,
        ITerraRuntimeWorldGeneratorSource? worldGenerators,
        RuntimeLogBuffer runtimeLogs,
        RuntimeHostLog hostLog,
        PreparedWorldStartup startup)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.interestManagement = interestManagement ?? throw new ArgumentNullException(nameof(interestManagement));
        this.hostLifecycle = hostLifecycle;
        this.runtimeLogs = runtimeLogs ?? throw new ArgumentNullException(nameof(runtimeLogs));
        this.hostLog = hostLog ?? throw new ArgumentNullException(nameof(hostLog));
        this.startup = startup ?? throw new ArgumentNullException(nameof(startup));

        WorldFileData world = startup.World;
        var primaryIdentity = new WorldRuntimeIdentity(
            WorldRuntimeId.CreateNew(),
            WorldSessionId.CreateNew());
        primaryRuntime = new WorldRuntime(
            primaryIdentity,
            new SandboxWorldSource.WorldFile(options.WorldPath),
            world,
            startup.BootstrapPackets,
            interestManagement,
            new WorldRuntimeOptions
            {
                MaxPlayers = options.MaxPlayers,
                CaptureOperationsTelemetry = options.TerminalUiEnabled
            },
            new WorldRuntimePersistence(options.WorldPath, startup.SaveTemplate, startup.LoadLimits));
        runtimeRegistry = new WorldRegistry(options.MaxWorldRuntimes);
        sandboxHost = new SandboxHost(
            runtimeRegistry,
            new StartupWorldGeneratorSource(worldGenerators),
            startup.LoadLimits,
            materializationConcurrency: options.SandboxMaterializationConcurrency,
            maxPlayersPerRuntime: options.MaxPlayers,
            captureOperationsTelemetry: options.TerminalUiEnabled);
        sandboxHost.JobFinished += OnSandboxJobFinished;
        connections = new ServerConnectionAcceptor(options.MaxPlayers, primaryRuntime, hostLog);
        listeners = new ListenerManager(connections, options.MaxPlayers, hostLog);

        cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };
    }

    public async Task<int> RunAsync()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        InstallShutdownSignals();

        try
        {
            listeners.Start(options.BindAddress, options.Port, shutdown.Token);
            if (!runtimeRegistry.TryAdmit(primaryRuntime, primary: true))
                throw new InvalidOperationException("Primary WorldRuntime admission failed.");

            if (!await TryAttachHostRuntimeAsync().ConfigureAwait(false))
                return 29;

            TimeSpan networkReadyDuration = Stopwatch.GetElapsedTime(startup.Metrics.StartupStartTimestamp);
            LogStartupReady(networkReadyDuration);
            StartTerminalUi(networkReadyDuration);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
                // Normal process shutdown. Listener generations and live connections drain in ShutdownAsync.
            }
        }
        catch (SocketException exception)
        {
            string message = $"Failed to start listener on {options.BindAddress}:{options.Port}: {exception.Message}";
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
            await ShutdownAsync().ConfigureAwait(false);
        }

        return 0;
    }

    private void InstallShutdownSignals()
    {
        Console.CancelKeyPress += cancelHandler;
        consoleHandlerAttached = true;
        if (!OperatingSystem.IsWindows())
        {
            terminateSignalRegistration = PosixSignalRegistration.Create(
                PosixSignal.SIGTERM,
                context =>
                {
                    context.Cancel = true;
                    shutdown.Cancel();
                });
        }
    }

    private async Task<bool> TryAttachHostRuntimeAsync()
    {
        if (hostLifecycle is null)
            return true;

        WorldFileData world = startup.World;
        var hostRuntime = new TerraRuntimeHostRuntime(
            new RuntimeInfo(
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
            interestManagement,
            primaryRuntime.PlayerStateSnapshots,
            new RuntimePlayerRouteAdministrativeOperations(connections.Directory),
            primaryRuntime.State.NpcShops,
            primaryRuntime.State.NpcArchetypes);
        try
        {
            await hostLifecycle.AttachRuntimeAsync(hostRuntime, shutdown.Token).ConfigureAwait(false);
            hostRuntimeAttached = true;
            return true;
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
            return false;
        }
    }

    private void LogStartupReady(TimeSpan networkReadyDuration)
    {
        WorldFileData world = startup.World;
        WorldStartupMetrics metrics = startup.Metrics;
        WorldFileLoadProfile canonicalLoadProfile = metrics.CanonicalLoadProfile;
        long allocatedBytes = Math.Max(
            0L,
            GC.GetTotalAllocatedBytes(precise: false) - metrics.AllocatedBytesAtStart);
        string startupProfile = FormattableString.Invariant($"startup_profile source={(metrics.RuntimeCacheHit ? "runtime-cache" : "canonical-wld")} cache_result={metrics.CacheDiagnostic.Result} cache_parallel_reads={RuntimeWorldCacheReadOptions.Default.MaxParallelReads} source_stat_ms={metrics.SourceStatDuration.TotalMilliseconds:F3} file_read_ms={metrics.FileReadDuration.TotalMilliseconds:F3} cache_load_ms={metrics.CacheLoadDuration.TotalMilliseconds:F3} wld_total_ms={canonicalLoadProfile.Total.TotalMilliseconds:F3} wld_envelope_header_ms={canonicalLoadProfile.EnvelopeAndHeader.TotalMilliseconds:F3} wld_tile_alloc_ms={canonicalLoadProfile.TileAllocation.TotalMilliseconds:F3} wld_tile_decode_ms={canonicalLoadProfile.TileDecode.TotalMilliseconds:F3} wld_non_tile_ms={canonicalLoadProfile.NonTileSections.TotalMilliseconds:F3} cache_write_ms={metrics.CacheWriteDuration.TotalMilliseconds:F3} bootstrap_cache_hit={(metrics.BootstrapCacheHit ? "true" : "false")} bootstrap_cache_result={metrics.BootstrapCacheDiagnostic.Result} bootstrap_cache_load_ms={metrics.BootstrapCacheLoadDuration.TotalMilliseconds:F3} bootstrap_cache_write_ms={metrics.BootstrapCacheWriteDuration.TotalMilliseconds:F3} bootstrap_ms={metrics.BootstrapDuration.TotalMilliseconds:F3} world_ready_ms={metrics.WorldReadyDuration.TotalMilliseconds:F3} network_ready_ms={networkReadyDuration.TotalMilliseconds:F3} allocated_mib={allocatedBytes / (1024d * 1024d):F3}");
        hostLog.Log(
            RuntimeLogLevel.Debug,
            StructuredLogEventIds.StartupProfile,
            StructuredLogCategory.Lifecycle,
            "Startup",
            startupProfile);

        string listeningMessage =
            $"TerraRuntime listening on {options.BindAddress}:{options.Port}; " +
            $"world='{world.Header.Name}' {world.Header.Dimensions.WidthTiles}x{world.Header.Dimensions.HeightTiles}; " +
            $"maxPlayers={options.MaxPlayers}; " +
            $"interestManagement={(interestManagement.IsEnabled ? "enabled" : "disabled")}; " +
            $"tui={(options.TerminalUiEnabled ? "enabled" : "disabled")}.";
        hostLog.Log(
            RuntimeLogLevel.Information,
            StructuredLogEventIds.NetworkListenerReady,
            StructuredLogCategory.Network,
            "Server",
            listeningMessage);
    }

    private void StartTerminalUi(TimeSpan networkReadyDuration)
    {
        if (!options.TerminalUiEnabled)
            return;

        try
        {
            WorldFileData world = startup.World;
            WorldStartupMetrics metrics = startup.Metrics;
            string worldAssetRoot = Path.GetDirectoryName(options.WorldPath) ?? Directory.GetCurrentDirectory();
            var transferCoordinator = new Level1PlayerTransferCoordinator(
                connections.Directory,
                runtimeRegistry,
                sandboxHost);
            var sandboxOperations = new SandboxOperations(
                sandboxHost,
                worldAssetRoot,
                primaryRuntime.World.Header.Dimensions.WidthTiles,
                primaryRuntime.World.Header.Dimensions.HeightTiles,
                transferCoordinator);
            var dashboardOperations = new LocalRuntimeDashboardOperations(
                primaryRuntime.GameLoop,
                connections.Admission,
                interestManagement,
                world.Header.Name,
                world.Header.Dimensions.WidthTiles,
                world.Header.Dimensions.HeightTiles,
                options.Port,
                options.MaxPlayers,
                GameLoopOptions.DefaultTicksPerSecond,
                listeners.CaptureSnapshot,
                listeners.TryChangeEndpoint);
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
                    RuntimeCacheHit: metrics.RuntimeCacheHit,
                    InitialCacheResult: metrics.CacheDiagnostic.Result.ToString(),
                    CacheParallelReads: RuntimeWorldCacheReadOptions.Default.MaxParallelReads,
                    InitialCacheDetailCode: metrics.CacheDiagnostic.DetailCode,
                    FileReadMilliseconds: metrics.FileReadDuration.TotalMilliseconds,
                    CacheLoadMilliseconds: metrics.CacheLoadDuration.TotalMilliseconds,
                    CanonicalWorldLoadMilliseconds: metrics.CanonicalLoadProfile.Total.TotalMilliseconds,
                    CacheWriteMilliseconds: metrics.CacheWriteDuration.TotalMilliseconds,
                    BootstrapMilliseconds: metrics.BootstrapDuration.TotalMilliseconds,
                    WorldReadyMilliseconds: metrics.WorldReadyDuration.TotalMilliseconds,
                    NetworkReadyMilliseconds: networkReadyDuration.TotalMilliseconds,
                    CapturedAtUtc: DateTimeOffset.UtcNow),
                primaryRuntime.WorldClockTelemetry,
                () => primaryRuntime.SectionCacheSnapshot,
                () => primaryRuntime.CaptureSaveStatus() ?? default,
                primaryRuntime.TryRequestSave);
            var worldInspectionOperations = new LocalRuntimeWorldInspectionOperations(
                runtimeRegistry,
                sandboxHost);
            terminalUi = Host.Start(
                dashboardOperations,
                primaryRuntime.PlayerOperations,
                primaryRuntime.NpcOperations!,
                connections.Operations,
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
                primaryRuntime.ProjectileOperations,
                primaryRuntime.WorldItemOperations,
                sandboxOperations,
                worldInspectionOperations,
                new RuntimePlayerRouteAdministrativeOperations(connections.Directory),
                connections.Sessions);
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

    private async Task ShutdownAsync()
    {
        shutdown.Cancel();
        terminalUi?.Dispose();
        terminalUi = null;
        DetachConsoleHandler();
        await listeners.CloseAsync().ConfigureAwait(false);
        await connections.DrainAsync().ConfigureAwait(false);
        await DetachHostRuntimeAsync().ConfigureAwait(false);
        await StopPrimaryRuntimeAsync().ConfigureAwait(false);
    }

    private async Task DetachHostRuntimeAsync()
    {
        if (!hostRuntimeAttached || hostLifecycle is null)
            return;

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
        finally
        {
            hostRuntimeAttached = false;
        }
    }

    private async Task StopPrimaryRuntimeAsync()
    {
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
            else if (primaryRuntime.GameLoop.Fault is null)
            {
                InvalidateRuntimeCache(startup.RuntimeBootstrapCachePath);
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

        if (primaryRuntime.GameLoop.Fault is Exception gameLoopFault)
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

    private void InvalidateRuntimeCache(string path)
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

    private void OnSandboxJobFinished(SandboxJobSnapshot job)
    {
        bool failed = job.Status is SandboxJobStatus.Failed or SandboxJobStatus.Canceled;
        hostLog.Log(
            failed ? RuntimeLogLevel.Error : RuntimeLogLevel.Information,
            failed
                ? StructuredLogEventIds.OperationsSandboxJobFailed
                : StructuredLogEventIds.OperationsSandboxJobCompleted,
            StructuredLogCategory.Operations,
            "Sandbox",
            SandboxOperations.FormatJob(job),
            useStandardError: failed);
    }

    private void DetachConsoleHandler()
    {
        if (!consoleHandlerAttached)
            return;

        Console.CancelKeyPress -= cancelHandler;
        consoleHandlerAttached = false;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        DetachConsoleHandler();
        sandboxHost.JobFinished -= OnSandboxJobFinished;
        terminalUi?.Dispose();
        listeners.Dispose();
        connections.Dispose();
        terminateSignalRegistration?.Dispose();
        shutdown.Dispose();
        sandboxHost.Dispose();
        runtimeRegistry.Dispose();
        primaryRuntime.Dispose();
    }
}
