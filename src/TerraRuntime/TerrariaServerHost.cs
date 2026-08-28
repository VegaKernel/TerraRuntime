using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
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

        IInterestManagementControl runtimeInterestManagement =
            interestManagement ?? new InterestManagementControl(options.InterestManagementEnabled);
        if (options.InterestManagementEnabled)
            runtimeInterestManagement.SetEnabled(true);

        if (!File.Exists(options.WorldPath))
        {
            Console.Error.WriteLine($"World file not found: {options.WorldPath}");
            return 24;
        }

        byte[] file;
        try
        {
            file = await File.ReadAllBytesAsync(options.WorldPath).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Failed to read world file '{options.WorldPath}': {exception.Message}");
            return 25;
        }

        WorldFileLoadDiagnostic diagnostic = WorldFileLoader.TryLoad(
            file,
            CreateServerWorldLoadLimits(),
            out WorldFileData? world);
        if (!diagnostic.IsLoaded || world is null)
        {
            Console.Error.WriteLine(
                $"World load failed: result={diagnostic.Result}, stage={diagnostic.Stage}, code={diagnostic.StageResultCode}.");
            return 26;
        }

        PlayerBootstrapPacketSet bootstrapPackets;
        try
        {
            bootstrapPackets = PlayerBootstrapPacketSet.Create(world);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
        {
            Console.Error.WriteLine($"Failed to prepare join bootstrap packets: {exception.Message}");
            return 27;
        }

        var runtimeConnections = new RuntimeConnectionRegistry(
            runtimeInterestManagement,
            world.Header.Dimensions);
        var state = new ServerRuntimeState(runtimeConnections);
        using var gameLoop = new AuthoritativeGameLoop<ServerRuntimeState, RuntimeCommand>(
            state,
            static (runtime, command) => runtime.Apply(command),
            static runtime => runtime.Tick());
        var commandIngress = new AuthoritativeCommandIngress<ServerRuntimeState, RuntimeCommand>(gameLoop);
        var spawnIngress = new RuntimePlayerSpawnCommitIngress(commandIngress);
        var appearanceIngress = new RuntimePlayerAppearanceIngress(commandIngress);
        var movementIngress = new RuntimePlayerMovementIngress(commandIngress);
        var disconnectIngress = new RuntimePlayerDisconnectIngress(commandIngress);
        var slots = new PlayerSlotPool(options.MaxPlayers);
        var admission = new TerrariaConnectionAdmissionGate(options.MaxPlayers);
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

        try
        {
            listener.Bind(new IPEndPoint(IPAddress.Any, options.Port));
            listener.Listen(backlog: Math.Max(32, options.MaxPlayers * 2));
            gameLoop.Start();

            Console.WriteLine(
                $"TerraRuntime listening on 0.0.0.0:{options.Port}; " +
                $"world='{world.Header.Name}' {world.Header.Dimensions.WidthTiles}x{world.Header.Dimensions.HeightTiles}; " +
                $"maxPlayers={options.MaxPlayers}; " +
                $"interestManagement={(runtimeInterestManagement.IsEnabled ? "enabled" : "disabled")}.");

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
                    movementIngress,
                    disconnectIngress,
                    runtimeConnections,
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
            Console.Error.WriteLine($"Failed to start listener on port {options.Port}: {exception.Message}");
            return 28;
        }
        finally
        {
            shutdown.Cancel();
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
                    Console.Error.WriteLine($"Connection shutdown observed a fault: {exception.Message}");
                }
            }

            if (!gameLoop.Stop(TimeSpan.FromSeconds(5)))
            {
                Console.Error.WriteLine("Authoritative game loop did not stop within the shutdown deadline.");
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
        IPlayerMovementIngress movementIngress,
        RuntimePlayerDisconnectIngress disconnectIngress,
        RuntimeConnectionRegistry runtimeConnections,
        CancellationToken cancellationToken)
    {
        string remote = socket.RemoteEndPoint?.ToString() ?? "unknown";
        GameCommandSourceId source = GameCommandSourceId.FromConnection(connectionId);

        using (admissionLease)
        {
            var outbound = new TerrariaConnectionOutboundQueue(ConnectionOutboundQueueOptions);
            if (!runtimeConnections.TryRegister(source, outbound))
            {
                socket.Dispose();
                return;
            }

            using var sink = new PlayerBootstrapFrameSink(
                slots,
                outbound,
                bootstrapPackets,
                source,
                spawnIngress,
                appearanceIngress,
                movementIngress);

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
                    Console.WriteLine(
                        $"Connection {connectionId} ({remote}) stopped: {result.StopReason}; " +
                        $"bootstrap={sink.StopReason}, state={sink.JoinState}; " +
                        $"inbound={result.Inbound}; outbound={result.Outbound.Reason}.");
                }
                catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
                {
                    if (!cancellationToken.IsCancellationRequested)
                        Console.Error.WriteLine($"Connection {connectionId} ({remote}) failed: {exception.Message}");
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
            finally
            {
                if (runtimeConnections.TryUnregister(source, out PlayerSlotId? playingSlot) &&
                    playingSlot is PlayerSlotId slot &&
                    !disconnectIngress.TryPost(source, slot) &&
                    !cancellationToken.IsCancellationRequested)
                {
                    Console.Error.WriteLine(
                        $"Connection {connectionId} ({remote}) could not enqueue authoritative disconnect for slot {slot.Value}.");
                }
            }
        }
    }

    private static WorldFileLoadLimits CreateServerWorldLoadLimits() =>
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
}
