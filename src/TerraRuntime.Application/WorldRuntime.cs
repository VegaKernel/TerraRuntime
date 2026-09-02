using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Operations;
using TerraRuntime.World;

namespace TerraRuntime;

public enum WorldRuntimeLifecycle : byte
{
    Created = 0,
    Running = 1,
    Stopping = 2,
    Stopped = 3,
    Faulted = 4,
    Disposed = 5
}

public readonly record struct WorldRuntimeSnapshot(
    WorldRuntimeIdentity Identity,
    string WorldName,
    SandboxWorldSource Source,
    WorldPersistenceMode PersistenceMode,
    WorldRuntimeLifecycle Lifecycle,
    long Tick,
    int TargetTicksPerSecond,
    double ObservedTicksPerSecond,
    int Connections,
    int Npcs,
    int Projectiles,
    int WorldItems,
    Exception? Fault);

internal sealed record WorldRuntimePersistence(
    string WorldPath,
    WorldFilePreservedSections SaveTemplate,
    WorldFileLoadLimits LoadLimits);

/// <summary>
/// Owns one complete live authoritative world: mutable simulation state, registries, ingress, replication,
/// bootstrap/cache work, persistence policy and its dedicated single-writer loop. Primary is intentionally absent;
/// it is a selection made by the process-level registry.
/// </summary>
public sealed class WorldRuntime : IDisposable
{
    // Correctness-first ceiling retained from the original single-world composition. Each runtime owns its own
    // bounded rebuild worker and at most one queued/completed section snapshot.
    private const int SectionCacheWorkerCount = 1;
    private const int SectionCacheWorkCapacity = 1;
    private const int SectionCacheCompletionCapacity = 1;

    private readonly SectionCacheRebuildPipeline sectionCacheRebuild;
    private readonly RuntimeTickRateObserver tickRateObserver = new();
    private readonly RuntimeWorldTileChestSaveService? worldSave;
    private readonly VanillaWorldAutosaveScheduler? autosave;
    private int lifecycle;
    private int disposed;

    internal WorldRuntime(
        WorldRuntimeIdentity identity,
        SandboxWorldSource source,
        WorldFileData world,
        PlayerBootstrapPacketSet bootstrapPackets,
        IInterestManagementControl interestManagement,
        WorldRuntimeOptions options,
        WorldRuntimePersistence? persistence = null)
    {
        if (!identity.IsAssigned)
            throw new ArgumentException("World runtime identity must be assigned.", nameof(identity));
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(bootstrapPackets);
        ArgumentNullException.ThrowIfNull(interestManagement);
        ArgumentNullException.ThrowIfNull(options);

        GameLoopOptions loopOptions = options.CreateLoopOptions();
        Identity = identity;
        Source = source;
        World = world;
        Options = options;
        PersistenceMode = persistence is null
            ? WorldPersistenceMode.Ephemeral
            : WorldPersistenceMode.Persistent;
        BootstrapPackets = bootstrapPackets;

        WorldItemReplication = new RuntimeWorldItemReplicationRegistry();
        WorldItems = new RuntimeWorldItemStore(WorldItemReplication);
        WorldItemOperations = options.CaptureOperationsTelemetry
            ? new LocalRuntimeWorldItemOperations(WorldItems)
            : null;
        WorldClockTelemetry = options.CaptureOperationsTelemetry
            ? new RuntimeWorldClockOperationsTelemetry()
            : null;
        WorldClock = RuntimeWorldClock.FromWorld(
            world.RuntimeMetadata,
            world.CreativePowers,
            WorldClockTelemetry);
        WorldProgression = new RuntimeWorldProgressionMutations();
        RuntimeConnections = new RuntimeConnectionRegistry(interestManagement, world.Header.Dimensions);

        NpcReplication = new RuntimeNpcReplicationRegistry();
        NpcOperations = options.CaptureOperationsTelemetry
            ? new RuntimeNpcOperationsTelemetry()
            : null;
        INpcStateCommitSink observableNpcCommitSink = NpcOperations is null
            ? NpcReplication
            : new RuntimeNpcStateCommitFanout(NpcReplication, NpcOperations);
        NpcArchetypeIdentities = new RuntimeNpcArchetypeIdentityStore(RuntimeNpcStore.MaximumAddressableCapacity);
        INpcStateCommitSink npcCommitSink = new RuntimeNpcStateCommitFanout(
            observableNpcCommitSink,
            NpcArchetypeIdentities);
        Npcs = new RuntimeNpcStore(commitSink: npcCommitSink);
        TownNpcs = new RuntimeTownNpcStateStore(world.Npcs, world.TownRooms, world.Header.Dimensions);
        if (!TownNpcs.TryReserveRuntimeSlots(Npcs))
            throw new InvalidDataException("Failed to reserve authoritative runtime slots for persisted town NPCs.");
        NpcReplication.ConfigureTownHomeBaselines(TownNpcs.CaptureHomeBaselines());
        NpcReplication.ConfigureTownIdentityBaselines(TownNpcs.CaptureIdentityBaselines());
        NpcArchetypes = new RuntimeNpcArchetypeRegistry();

        ProjectileReplication = new RuntimeProjectileReplicationRegistry();
        ProjectileOperations = options.CaptureOperationsTelemetry
            ? new RuntimeProjectileOperationsTelemetry()
            : null;
        IProjectileStateCommitSink projectileCommitSink = ProjectileOperations is null
            ? ProjectileReplication
            : new RuntimeProjectileStateCommitFanout(ProjectileReplication, ProjectileOperations);
        Projectiles = new RuntimeProjectileStore(commitSink: projectileCommitSink);

        TileManipulationReplication = new RuntimeTileManipulationReplicationRegistry();
        ChestReplication = new RuntimeChestReplicationRegistry();
        Chests = new RuntimeChestStore(world.Chests);
        ChestCommands = new RuntimeChestCommandProcessor(Chests, ChestReplication);
        SignReplication = new RuntimeSignReplicationRegistry();
        Signs = new RuntimeSignStore(world.Signs, world.Tiles);
        SignCommands = new RuntimeSignCommandProcessor(Signs, SignReplication);

        if (persistence is not null)
        {
            worldSave = new RuntimeWorldTileChestSaveService(
                persistence.WorldPath,
                world.Envelope,
                world.Header,
                persistence.SaveTemplate,
                world.Tiles,
                Chests,
                worldClock: WorldClock,
                signStore: Signs,
                townNpcStore: TownNpcs,
                progressionMutations: WorldProgression,
                checkpointValidationLimits: persistence.LoadLimits);
            autosave = new VanillaWorldAutosaveScheduler();
        }

        VitalsReplication = new RuntimePlayerVitalsReplicator();
        PlayerOperations = new RuntimePlayerOperationsTelemetry();
        var playerNetworkEvents = new RuntimePlayerEventDispatcher(
            RuntimeConnections,
            VitalsReplication,
            PlayerOperations);
        var projectileAndItemReplicationEvents = new RuntimePlayerEventFanout(
            ProjectileReplication,
            WorldItemReplication);
        var entityReplicationEvents = new RuntimePlayerEventFanout(
            NpcReplication,
            projectileAndItemReplicationEvents);
        var tileAndEntityReplicationEvents = new RuntimePlayerEventFanout(
            TileManipulationReplication,
            entityReplicationEvents);
        var chestAndEntityReplicationEvents = new RuntimePlayerEventFanout(
            ChestReplication,
            tileAndEntityReplicationEvents);
        var signAndEntityReplicationEvents = new RuntimePlayerEventFanout(
            SignReplication,
            chestAndEntityReplicationEvents);
        var playerEvents = new RuntimePlayerEventFanout(playerNetworkEvents, signAndEntityReplicationEvents);

        Slots = new PlayerSlotPool(options.MaxPlayers);
        ServerPlayerIdentities = new RuntimeServerPlayerSlotRegistry(Slots);
        ServerPlayerStates = new RuntimeServerPlayerStateStore(ServerPlayerIdentities, Slots.Capacity);
        State = new ServerRuntimeState(
            playerEvents,
            npcs: Npcs,
            worldTiles: world.Tiles,
            worldClock: WorldClock,
            worldProgression: WorldProgression,
            projectiles: Projectiles,
            worldItems: WorldItems,
            projectileReplication: ProjectileReplication,
            npcReplication: NpcReplication,
            worldItemReplication: WorldItemReplication,
            townNpcs: TownNpcs,
            townSpawnWorldFacts: RuntimeTownNpcWorldFactsProjection1458.FromMetadata(world.RuntimeMetadata),
            townCommerceWorldFacts: RuntimeTownCommerceWorldFacts1458.FromMetadata(world.RuntimeMetadata),
            townCombatWorldFacts: RuntimeTownNpcCombatWorldFacts1458.FromMetadata(world.RuntimeMetadata),
            townInitialRaining: world.RuntimeMetadata.Raining,
            townInitialEclipse: world.RuntimeMetadata.Eclipse,
            townInitialInvasionActive: world.RuntimeMetadata.InvasionType > 0,
            tileManipulationReplication: TileManipulationReplication,
            serverPlayerStates: ServerPlayerStates,
            serverPlayerIdentities: ServerPlayerIdentities,
            serverPlayerEvents: RuntimeConnections,
            npcArchetypes: NpcArchetypes,
            npcArchetypeIdentities: NpcArchetypeIdentities,
            expertMode: world.RuntimeMetadata.GameMode is
                (byte)WorldGenerationGameMode.Expert or
                (byte)WorldGenerationGameMode.Master,
            masterMode: world.RuntimeMetadata.GameMode == (byte)WorldGenerationGameMode.Master);

        sectionCacheRebuild = new SectionCacheRebuildPipeline(
            world,
            bootstrapPackets,
            workerCount: SectionCacheWorkerCount,
            workCapacity: SectionCacheWorkCapacity,
            completionCapacity: SectionCacheCompletionCapacity);
        GameLoop = new AuthoritativeGameLoop<ServerRuntimeState, RuntimeCommand>(
            State,
            (runtime, command) =>
            {
                if (!SignCommands.TryApply(command) && !ChestCommands.TryApply(command))
                    runtime.Apply(command);
            },
            runtime =>
            {
                runtime.Tick();
                sectionCacheRebuild.Tick();
                if (autosave?.Tick() == true)
                    worldSave!.RequestSave();
                worldSave?.Tick();
            },
            loopOptions);

        CommandIngress = new AuthoritativeCommandIngress<ServerRuntimeState, RuntimeCommand>(GameLoop);
        PlayerStateSnapshots = new RuntimePlayerStateSnapshotReader(CommandIngress);
        TransferIngress = new RuntimePlayerTransferIngress(CommandIngress);
        SpawnIngress = new RuntimePlayerSpawnCommitIngress(CommandIngress);
        AppearanceIngress = new RuntimePlayerAppearanceIngress(CommandIngress);
        EquipmentIngress = new RuntimePlayerEquipmentIngress(CommandIngress);
        HealthIngress = new RuntimePlayerHealthIngress(CommandIngress);
        ManaIngress = new RuntimePlayerManaIngress(CommandIngress);
        MovementIngress = new RuntimePlayerMovementIngress(CommandIngress);
        WorldItemIngress = new RuntimeWorldItemIngress(CommandIngress, WorldItems);
        ProjectileIngress = new RuntimeProjectileNetworkIngress(CommandIngress);
        ChestIngress = new RuntimeChestNetworkIngress(CommandIngress);
        SignIngress = new RuntimeSignNetworkIngress(CommandIngress);
        TownNpcHomeIngress = new RuntimeTownNpcHomeNetworkIngress(CommandIngress);
        NpcTalkIngress = new RuntimeNpcTalkNetworkIngress(CommandIngress);
        NpcCatchIngress = new RuntimeNpcCatchNetworkIngress(CommandIngress);
        DisconnectIngress = new RuntimePlayerDisconnectIngress(CommandIngress);
    }

    public WorldRuntimeIdentity Identity { get; }
    public SandboxWorldSource Source { get; }
    public WorldPersistenceMode PersistenceMode { get; }
    public WorldRuntimeOptions Options { get; }
    internal WorldFileData World { get; }
    public WorldRuntimeLifecycle Lifecycle
    {
        get
        {
            if (Volatile.Read(ref disposed) != 0)
                return WorldRuntimeLifecycle.Disposed;
            if (GameLoop.Fault is not null)
                return WorldRuntimeLifecycle.Faulted;
            return (WorldRuntimeLifecycle)Volatile.Read(ref lifecycle);
        }
    }

    internal PlayerBootstrapPacketSet BootstrapPackets { get; }
    internal RuntimeWorldClock WorldClock { get; }
    internal RuntimeWorldProgressionMutations WorldProgression { get; }
    internal RuntimeConnectionRegistry RuntimeConnections { get; }
    internal RuntimeNpcReplicationRegistry NpcReplication { get; }
    internal RuntimeProjectileReplicationRegistry ProjectileReplication { get; }
    internal RuntimeWorldItemReplicationRegistry WorldItemReplication { get; }
    internal RuntimeTileManipulationReplicationRegistry TileManipulationReplication { get; }
    internal RuntimeChestReplicationRegistry ChestReplication { get; }
    internal RuntimeSignReplicationRegistry SignReplication { get; }
    internal RuntimePlayerVitalsReplicator VitalsReplication { get; }
    internal RuntimeNpcStore Npcs { get; }
    internal RuntimeProjectileStore Projectiles { get; }
    internal RuntimeWorldItemStore WorldItems { get; }
    internal RuntimeTownNpcStateStore TownNpcs { get; }
    internal RuntimeChestStore Chests { get; }
    internal RuntimeSignStore Signs { get; }
    internal RuntimeChestCommandProcessor ChestCommands { get; }
    internal RuntimeSignCommandProcessor SignCommands { get; }
    internal RuntimeNpcArchetypeRegistry NpcArchetypes { get; }
    internal RuntimeNpcArchetypeIdentityStore NpcArchetypeIdentities { get; }
    internal PlayerSlotPool Slots { get; }
    internal RuntimeServerPlayerSlotRegistry ServerPlayerIdentities { get; }
    internal RuntimeServerPlayerStateStore ServerPlayerStates { get; }
    internal ServerRuntimeState State { get; }
    internal AuthoritativeGameLoop<ServerRuntimeState, RuntimeCommand> GameLoop { get; }
    internal AuthoritativeCommandIngress<ServerRuntimeState, RuntimeCommand> CommandIngress { get; }
    internal RuntimePlayerStateSnapshotReader PlayerStateSnapshots { get; }
    internal RuntimePlayerTransferIngress TransferIngress { get; }
    internal RuntimePlayerSpawnCommitIngress SpawnIngress { get; }
    internal RuntimePlayerAppearanceIngress AppearanceIngress { get; }
    internal RuntimePlayerEquipmentIngress EquipmentIngress { get; }
    internal RuntimePlayerHealthIngress HealthIngress { get; }
    internal RuntimePlayerManaIngress ManaIngress { get; }
    internal RuntimePlayerMovementIngress MovementIngress { get; }
    internal RuntimeWorldItemIngress WorldItemIngress { get; }
    internal RuntimeProjectileNetworkIngress ProjectileIngress { get; }
    internal RuntimeChestNetworkIngress ChestIngress { get; }
    internal RuntimeSignNetworkIngress SignIngress { get; }
    internal RuntimeTownNpcHomeNetworkIngress TownNpcHomeIngress { get; }
    internal RuntimeNpcTalkNetworkIngress NpcTalkIngress { get; }
    internal RuntimeNpcCatchNetworkIngress NpcCatchIngress { get; }
    internal RuntimePlayerDisconnectIngress DisconnectIngress { get; }
    internal RuntimePlayerOperationsTelemetry PlayerOperations { get; }
    internal RuntimeNpcOperationsTelemetry? NpcOperations { get; }
    internal RuntimeProjectileOperationsTelemetry? ProjectileOperations { get; }
    internal LocalRuntimeWorldItemOperations? WorldItemOperations { get; }
    internal RuntimeWorldClockOperationsTelemetry? WorldClockTelemetry { get; }
    internal SectionCacheRebuildPipelineSnapshot SectionCacheSnapshot => sectionCacheRebuild.Snapshot;
    internal RuntimeWorldSaveStatus? CaptureSaveStatus() => worldSave?.CaptureStatus();
    internal bool TryRequestSave() => worldSave?.TryRequestSave() ?? false;

    public WorldRuntimeSnapshot CaptureSnapshot()
    {
        GameLoopSnapshot loop = GameLoop.Snapshot;
        return new WorldRuntimeSnapshot(
            Identity,
            World.Header.Name,
            Source,
            PersistenceMode,
            Lifecycle,
            loop.Tick,
            Options.TargetTicksPerSecond,
            tickRateObserver.Observe(loop.Tick),
            RuntimeConnections.Count,
            Npcs.ActiveCount,
            Projectiles.ActiveCount,
            WorldItems.ActiveCount,
            GameLoop.Fault);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.CompareExchange(
                ref lifecycle,
                (int)WorldRuntimeLifecycle.Running,
                (int)WorldRuntimeLifecycle.Created) != (int)WorldRuntimeLifecycle.Created)
        {
            throw new InvalidOperationException("World runtime has already been started or stopped.");
        }

        sectionCacheRebuild.Start();
        GameLoop.Start();
    }

    internal async Task<bool> StopAsync(TimeSpan timeout, bool captureFinalSave)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        WorldRuntimeLifecycle current = (WorldRuntimeLifecycle)Interlocked.CompareExchange(
            ref lifecycle,
            (int)WorldRuntimeLifecycle.Stopping,
            (int)WorldRuntimeLifecycle.Running);
        if (current == WorldRuntimeLifecycle.Created)
        {
            Volatile.Write(ref lifecycle, (int)WorldRuntimeLifecycle.Stopped);
            return true;
        }
        if (current is WorldRuntimeLifecycle.Stopped or WorldRuntimeLifecycle.Disposed)
            return true;
        if (current != WorldRuntimeLifecycle.Running)
            return false;

        bool drained = await WaitForCommandDrainAsync(timeout).ConfigureAwait(false);
        bool stopped = GameLoop.Stop(timeout);
        if (stopped && drained && captureFinalSave && worldSave is not null && GameLoop.Fault is null)
        {
            worldSave.CaptureFinalSaveAfterOwnerStopped();
            await worldSave.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
        }

        Volatile.Write(ref lifecycle, (int)WorldRuntimeLifecycle.Stopped);
        return stopped && drained;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        if ((WorldRuntimeLifecycle)Volatile.Read(ref lifecycle) == WorldRuntimeLifecycle.Running)
            _ = GameLoop.Stop(TimeSpan.FromSeconds(5));
        Volatile.Write(ref lifecycle, (int)WorldRuntimeLifecycle.Disposed);
        GameLoop.Dispose();
        sectionCacheRebuild.Dispose();
    }

    private async Task<bool> WaitForCommandDrainAsync(TimeSpan timeout)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (GameLoop.Snapshot.PendingCommands != 0)
        {
            if (!GameLoop.IsRunning ||
                GameLoop.Fault is not null ||
                System.Diagnostics.Stopwatch.GetElapsedTime(started) >= timeout)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
        }

        return true;
    }
}
