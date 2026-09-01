using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime;

internal sealed class ServerRuntimeState : IRuntimePlayerSnapshotLookup, IRuntimePlayerSlotSnapshotLookup
{
    private const int MaxPlayerSlots = 256;
    private const float VanillaBasePlayerWidth = 20f;
    private const float VanillaBasePlayerHeight = 42f;

    private readonly Dictionary<byte, RuntimePlayerState> _players = [];
    private readonly PendingPlayerVitals?[] _pendingVitals = new PendingPlayerVitals?[MaxPlayerSlots];
    private readonly short[] _playerTalkNpcSlots = new short[MaxPlayerSlots];
    private readonly RuntimeTownShopSession1458?[] _townShopSessions = new RuntimeTownShopSession1458?[MaxPlayerSlots];
    private readonly RuntimePlayerInventoryStore _playerInventory = new();
    private readonly VanillaNpcTargetCandidate[] _npcTargetCandidates =
        new VanillaNpcTargetCandidate[VanillaNpcTargetingAiStepper.MaximumPlayerCandidates];
    private readonly IRuntimePlayerEventSink? _playerEvents;
    private readonly RuntimeNpcStore _npcs;
    private readonly RuntimeNpcAiStateExecutor _npcAiExecutor;
    private readonly RuntimeNpcActorControlRegistry _npcActorControls;
    private readonly RuntimeNpcActorControlCommandService _npcActorCommands;
    private readonly RuntimeNpcArchetypeRegistry _npcArchetypes;
    private readonly RuntimeNpcArchetypeIdentityStore _npcArchetypeIdentities;
    private readonly RuntimeNpcArchetypeSpawner _npcArchetypeSpawner;
    private readonly RuntimeNpcShopCatalogRegistry _npcShops;
    private readonly RuntimeServerPlayerStateStore? _serverPlayerStates;
    private readonly RuntimeServerPlayerCommandService? _serverPlayerCommands;
    private readonly IRuntimeServerPlayerEventSink? _serverPlayerEvents;
    private readonly VanillaServerPlayerDryPhysicsStepper? _serverPlayerDryPhysics;
    private readonly PlayerStateSnapshot[] _serverPlayerSnapshots =
        new PlayerStateSnapshot[VanillaNpcTargetingAiStepper.MaximumPlayerCandidates];
    private readonly PlayerHandle[] _serverPlayerLiquidOwners =
        new PlayerHandle[VanillaNpcTargetingAiStepper.MaximumPlayerCandidates];
    private readonly VanillaLiquidContactState[] _serverPlayerLiquidContacts =
        new VanillaLiquidContactState[VanillaNpcTargetingAiStepper.MaximumPlayerCandidates];
    private readonly INpcAiStateStepper _npcAiStepper;
    private readonly VanillaNpcTargetingAiStepper? _vanillaNpcTargetingAiStepper;
    private readonly VanillaNpcCheckActiveAiStepper? _vanillaNpcCheckActiveAiStepper;
    private readonly RuntimeProjectileStore _projectiles;
    private readonly RuntimeProjectileStateExecutor _projectileExecutor;
    private readonly IProjectileStateStepper? _projectileStepper;
    private readonly RuntimeNpcProjectileReflectionPass _projectileReflections;
    private readonly RuntimeProjectileReplicationRegistry? _projectileReplication;
    private readonly RuntimeNpcReplicationRegistry? _npcReplication;
    private readonly RuntimeWorldItemReplicationRegistry? _worldItemReplication;
    private readonly RuntimeWorldItemInstancedLeaseStore _instancedItemLeases;
    private readonly RuntimeNpcNetworkCombatPipeline _npcCombat;
    private readonly short[] _expiredInstancedItemSlots = new short[RuntimeWorldItemStore.VanillaCapacity];
    private readonly RuntimeTownNpcStateStore? _townNpcs;
    private readonly RuntimeWorldProgressionMutations? _worldProgression;
    private readonly RuntimeTownNpcRescueService1458? _townRescue;
    private readonly RuntimePurificationPowderNpcInteraction1458? _purificationPowderNpcInteractions;
    private readonly RuntimeMysticFrogCatchService1458? _mysticFrogCatch;
    private readonly RuntimeTownCommerceResolver1458? _townCommerce;
    private readonly VanillaHousingValidator1458? _housingValidator;
    private readonly RuntimeTownNpcMoveInCoordinator1458? _townMoveIn;
    private readonly RuntimeTownNpcSchedule1458? _townSchedule;
    private readonly VanillaTownSpawnPlayerFacts1458[] _townSpawnPlayers = new VanillaTownSpawnPlayerFacts1458[MaxPlayerSlots];
    private readonly RuntimeTownPlayerBounds1458[] _townPlayerBounds = new RuntimeTownPlayerBounds1458[MaxPlayerSlots];
    private readonly bool _townInitialRaining;
    private readonly bool _townInitialEclipse;
    private readonly bool _townInitialInvasionActive;
    private readonly RuntimeTileManipulationReplicationRegistry? _tileManipulationReplication;
    private readonly RuntimeObjectPlacementCommandProcessor? _objectPlacementProcessor;
    private readonly RuntimeWorldItemStore _worldItems;
    private readonly IWorldItemSpawnRandom _worldItemSpawnRandom = new SystemWorldItemSpawnRandom();
    private readonly WorldTileStore? _worldTiles;
    private readonly VanillaWorldTileMutationService? _tileMutations;
    private readonly RuntimeWorldClock? _worldClock;
    private readonly bool _expertMode;
    private readonly bool _masterMode;
    private const int MaxTileEditsPerTickPerPlayer = 8;
    private readonly int[] _tileEditCounts = new int[MaxPlayerSlots];
    private long _tileEditBudgetTick;
    private bool _tileEditBudgetUsed;
    private int lastWorkerResult;
    private int lastSpawnCommitResult = -1;

    public ServerRuntimeState(
        IRuntimePlayerEventSink? playerEvents = null,
        RuntimeNpcStore? npcs = null,
        INpcAiStateStepper? npcAiStepper = null,
        WorldTileStore? worldTiles = null,
        RuntimeWorldClock? worldClock = null,
        RuntimeProjectileStore? projectiles = null,
        IProjectileStateStepper? projectileStepper = null,
        RuntimeWorldItemStore? worldItems = null,
        RuntimeProjectileReplicationRegistry? projectileReplication = null,
        RuntimeNpcReplicationRegistry? npcReplication = null,
        RuntimeWorldItemReplicationRegistry? worldItemReplication = null,
        RuntimeTownNpcStateStore? townNpcs = null,
        VanillaTownSpawnWorldFacts1458? townSpawnWorldFacts = null,
        RuntimeTownCommerceWorldFacts1458? townCommerceWorldFacts = null,
        bool townInitialRaining = false,
        bool townInitialEclipse = false,
        bool townInitialInvasionActive = false,
        RuntimeTileManipulationReplicationRegistry? tileManipulationReplication = null,
        RuntimeServerPlayerStateStore? serverPlayerStates = null,
        RuntimeServerPlayerSlotRegistry? serverPlayerIdentities = null,
        IRuntimeServerPlayerEventSink? serverPlayerEvents = null,
        RuntimeNpcShopCatalogRegistry? npcShops = null,
        RuntimeNpcArchetypeRegistry? npcArchetypes = null,
        RuntimeNpcArchetypeIdentityStore? npcArchetypeIdentities = null,
        bool expertMode = false,
        bool masterMode = false)
    {
        Array.Fill(_playerTalkNpcSlots, TerrariaNpcTalkCodec.NoNpc);
        _playerEvents = playerEvents;
        _worldTiles = worldTiles;
        _tileMutations = worldTiles is null ? null : new VanillaWorldTileMutationService(worldTiles);
        _worldClock = worldClock;
        _expertMode = expertMode;
        _masterMode = masterMode;
        if (masterMode && !expertMode)
            throw new ArgumentException("Master mode is a strict subset of Expert mode.", nameof(masterMode));
        _npcs = npcs ?? new RuntimeNpcStore();
        _projectiles = projectiles ?? new RuntimeProjectileStore();
        _npcAiExecutor = new RuntimeNpcAiStateExecutor(_npcs, _projectiles);
        _serverPlayerStates = serverPlayerStates;
        _serverPlayerEvents = serverPlayerEvents;
        if (serverPlayerIdentities is not null && serverPlayerStates is null)
            throw new ArgumentException("Server-player identities require an authoritative state store.", nameof(serverPlayerIdentities));
        _serverPlayerCommands = serverPlayerIdentities is not null && serverPlayerStates is not null
            ? new RuntimeServerPlayerCommandService(serverPlayerIdentities, serverPlayerStates, serverPlayerEvents)
            : null;
        _serverPlayerDryPhysics = serverPlayerStates is not null && worldTiles is not null
            ? new VanillaServerPlayerDryPhysicsStepper(worldTiles)
            : null;
        _npcActorControls = new RuntimeNpcActorControlRegistry(_npcs);
        _npcActorCommands = new RuntimeNpcActorControlCommandService(_npcs, _npcActorControls);
        _npcArchetypes = npcArchetypes ?? new RuntimeNpcArchetypeRegistry();
        _npcArchetypeIdentities = npcArchetypeIdentities ?? new RuntimeNpcArchetypeIdentityStore(_npcs.Capacity);
        _npcArchetypeSpawner = new RuntimeNpcArchetypeSpawner(_npcs, _npcArchetypes, _npcArchetypeIdentities);
        _npcShops = npcShops ?? new RuntimeNpcShopCatalogRegistry();
        _projectileExecutor = new RuntimeProjectileStateExecutor(_projectiles);
        _projectileReflections = new RuntimeNpcProjectileReflectionPass(_npcs, _projectiles, this);
        _projectileStepper = projectileStepper ??
            (worldTiles is null ? null : new VanillaProjectileWorldStateStepper(worldTiles));
        _projectileReplication = projectileReplication;
        _npcReplication = npcReplication;
        _townNpcs = townNpcs;
        _worldProgression = worldTiles is null ? null : RuntimeWorldProgressionRegistry.GetOrCreate(worldTiles);
        _townRescue = townNpcs is not null && _worldProgression is not null
            ? new RuntimeTownNpcRescueService1458(_npcs, townNpcs, _worldProgression)
            : null;
        _mysticFrogCatch = worldTiles is not null
            ? new RuntimeMysticFrogCatchService1458(_npcs, worldTiles, this)
            : null;
        _purificationPowderNpcInteractions = townNpcs is not null && _worldProgression is not null && _townRescue is not null
            ? new RuntimePurificationPowderNpcInteraction1458(
                _npcs, _projectiles, townNpcs, _townRescue, _worldProgression, townSpawnWorldFacts?.InfectedSeed ?? false)
            : null;
        _townCommerce = worldTiles is not null && townCommerceWorldFacts is RuntimeTownCommerceWorldFacts1458 commerceFacts
            ? new RuntimeTownCommerceResolver1458(worldTiles, townNpcs, _npcs, in commerceFacts)
            : null;
        _housingValidator = worldTiles is not null && townNpcs is not null
            ? new VanillaHousingValidator1458(worldTiles)
            : null;
        _townInitialRaining = townInitialRaining;
        _townInitialEclipse = townInitialEclipse;
        _townInitialInvasionActive = townInitialInvasionActive;
        if (worldTiles is not null && townNpcs is not null && _housingValidator is not null)
        {
            _townSchedule = new RuntimeTownNpcSchedule1458(townNpcs, _npcs, worldTiles);
            if (townSpawnWorldFacts is VanillaTownSpawnWorldFacts1458 facts)
            {
                var houseIndex = new RuntimeTownHouseCandidateIndex1458(worldTiles, _housingValidator);
                RuntimeWorldProgressionMutations progression = _worldProgression ?? RuntimeWorldProgressionRegistry.GetOrCreate(worldTiles);
                progression.SetTruffleSpawnBaseline(facts.UnlockedTruffleSpawn);
                progression.SetSlimeYellowSpawnBaseline(facts.UnlockedSlimeYellowSpawn);
                RuntimeTownRescueFacts1458 rescuedBaseline = RuntimeTownRescueFacts1458.None;
                if (facts.SavedGoblin) rescuedBaseline |= RuntimeTownRescueFacts1458.Goblin;
                if (facts.SavedWizard) rescuedBaseline |= RuntimeTownRescueFacts1458.Wizard;
                if (facts.SavedMechanic) rescuedBaseline |= RuntimeTownRescueFacts1458.Mechanic;
                if (facts.SavedStylist) rescuedBaseline |= RuntimeTownRescueFacts1458.Stylist;
                if (facts.SavedAngler) rescuedBaseline |= RuntimeTownRescueFacts1458.Angler;
                if (facts.SavedBartender) rescuedBaseline |= RuntimeTownRescueFacts1458.Bartender;
                if (facts.SavedGolfer) rescuedBaseline |= RuntimeTownRescueFacts1458.Golfer;
                if (facts.SavedTaxCollector) rescuedBaseline |= RuntimeTownRescueFacts1458.TaxCollector;
                progression.SetTownRescueBaseline(rescuedBaseline);
                _townMoveIn = new RuntimeTownNpcMoveInCoordinator1458(
                    townNpcs, _npcs, houseIndex, in facts, npcReplication, progression: progression);
            }
        }
        _tileManipulationReplication = tileManipulationReplication;
        if (worldTiles is not null &&
            RuntimeWorldObjectMetadataRegistry.TryGet(
                worldTiles,
                out IVanillaMultiTileObjectMetadataLifecycle objectMetadata))
        {
            _objectPlacementProcessor = new RuntimeObjectPlacementCommandProcessor(
                worldTiles,
                objectMetadata,
                tileManipulationReplication);
        }
        _worldItems = worldItems ?? new RuntimeWorldItemStore();
        _worldItemReplication = worldItemReplication;
        _instancedItemLeases = new RuntimeWorldItemInstancedLeaseStore(_worldItems);
        _npcCombat = new RuntimeNpcNetworkCombatPipeline(
            _npcs,
            _worldItems,
            this,
            _npcReplication,
            _instancedItemLeases,
            _worldItemReplication,
            _worldTiles,
            _worldClock,
            expertMode,
            masterMode);

        if (npcAiStepper is null)
        {
            _vanillaNpcTargetingAiStepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
            var actorIntent = new RuntimeNpcActorIntentStateStepper(
                _vanillaNpcTargetingAiStepper,
                _npcActorControls,
                this);
            if (worldTiles is null)
            {
                _npcAiStepper = actorIntent;
            }
            else
            {
                double worldSurfaceTiles = worldTiles.WorldSurfaceTiles ??
                    Math.Max(1d, worldTiles.Dimensions.HeightTiles / 3d);
                _vanillaNpcTargetingAiStepper.EnableBlueSlimeMotion(worldSurfaceTiles);
                _vanillaNpcTargetingAiStepper.EnableZombieMotion(worldSurfaceTiles);
                _vanillaNpcTargetingAiStepper.SetFlyingEyeEnvironment(new VanillaFlyingEyeWorldEnvironment(worldTiles));
                _vanillaNpcTargetingAiStepper.SetProjectileEnvironment(new VanillaNpcProjectileWorldEnvironment(worldTiles));
                var worldMotion = new VanillaNpcWorldMotionAiStepper(
                    actorIntent,
                    worldTiles,
                    worldSurfaceTiles,
                    _worldClock);
                _vanillaNpcCheckActiveAiStepper = new VanillaNpcCheckActiveAiStepper(worldMotion);
                _npcAiStepper = _vanillaNpcCheckActiveAiStepper;
            }
        }
        else
        {
            _npcAiStepper = npcAiStepper;
        }
    }

    public long AppliedCommands { get; private set; }

    internal RuntimeNpcShopCatalogRegistry NpcShops => _npcShops;

    internal bool TryGetPlayerTownShopSession(PlayerHandle player, out RuntimeTownShopSession1458? session)
    {
        if (!player.IsAssigned ||
            !_players.TryGetValue(player.Slot.Value, out RuntimePlayerState? state) ||
            state.Connection.Player != player ||
            _townShopSessions[player.Slot.Value] is not RuntimeTownShopSession1458 current)
        {
            session = null;
            return false;
        }

        session = current;
        return true;
    }

    internal RuntimeNpcArchetypeRegistry NpcArchetypes => _npcArchetypes;

    public long Updates { get; private set; }

    public long AppliedPlayerAppearances { get; private set; }

    public long RejectedPlayerAppearances { get; private set; }

    public long AppliedPlayerEquipmentUpdates { get; private set; }

    public long RejectedPlayerEquipmentUpdates { get; private set; }

    public long AppliedPlayerHealthUpdates { get; private set; }

    public long RejectedPlayerHealthUpdates { get; private set; }

    public long AppliedPlayerManaUpdates { get; private set; }

    public long RejectedPlayerManaUpdates { get; private set; }

    public long CommittedPlayerSpawns { get; private set; }

    public long AppliedPlayerMovements { get; private set; }

    public long RejectedPlayerMovements { get; private set; }

    public long DisconnectedPlayers { get; private set; }

    public long AppliedNpcSpawns { get; private set; }

    public long RejectedNpcSpawns { get; private set; }

    public long AppliedNpcUpdates { get; private set; }

    public long RejectedNpcUpdates { get; private set; }

    public long AppliedNpcDespawns { get; private set; }

    public long RejectedNpcDespawns { get; private set; }

    public long AppliedProjectileSpawns { get; private set; }

    public long RejectedProjectileSpawns { get; private set; }

    public long AppliedProjectileUpdates { get; private set; }

    public long RejectedProjectileUpdates { get; private set; }

    public long AppliedProjectileDespawns { get; private set; }

    public long RejectedProjectileDespawns { get; private set; }

    public long AppliedProjectileReflections { get; private set; }

    public long RejectedClientProjectileUpdates { get; private set; }

    public long RejectedClientProjectileDestroys { get; private set; }

    public long AppliedClientNpcDamage { get; private set; }

    public long RejectedClientNpcDamage { get; private set; }

    public long RelayedUnknownProjectileDestroys { get; private set; }

    public long ClientTileManipulationRequests { get; private set; }

    public long ValidatedClientTileManipulations { get; private set; }

    public long AppliedClientTileManipulations { get; private set; }

    public long RejectedClientTileManipulations { get; private set; }

    public long UnsupportedClientTileManipulations { get; private set; }

    public long AppliedWorldItemAllocations { get; private set; }

    public long RejectedWorldItemAllocations { get; private set; }

    public long AppliedWorldItemDrops { get; private set; }

    public long RejectedWorldItemDrops { get; private set; }

    public long AppliedWorldItemRemovals { get; private set; }

    public long RejectedWorldItemRemovals { get; private set; }

    public long AppliedWorldItemOwners { get; private set; }

    public long RejectedWorldItemOwners { get; private set; }

    public NpcAiStateTickSummary LastNpcAiTick { get; private set; }

    public ProjectileStateTickSummary LastProjectileTick { get; private set; }

    public PlayerSlotId? LastMovementPlayerSlot { get; private set; }

    public float LastMovementPositionX { get; private set; }

    public float LastMovementPositionY { get; private set; }

    public int LastWorkerResult => Volatile.Read(ref lastWorkerResult);

    public PlayerSpawnCommitResult? LastSpawnCommitResult
    {
        get
        {
            int value = Volatile.Read(ref lastSpawnCommitResult);
            return value < 0 ? null : (PlayerSpawnCommitResult)value;
        }
    }

    internal bool TryCapturePlayerSnapshot(PlayerHandle player, out PlayerStateSnapshot snapshot)
    {
        if (!_players.TryGetValue(player.Slot.Value, out RuntimePlayerState? state) ||
            state.Connection.Player != player)
        {
            snapshot = default;
            return false;
        }

        snapshot = state.CaptureSnapshot();
        return true;
    }

    private bool TryCaptureRuntimePlayerSnapshot(PlayerHandle player, out PlayerStateSnapshot snapshot)
    {
        if (TryCapturePlayerSnapshot(player, out snapshot))
            return true;

        if (_serverPlayerStates is not null && _serverPlayerStates.TryGet(player, out snapshot))
            return true;

        snapshot = default;
        return false;
    }

    bool IRuntimePlayerSnapshotLookup.TryGetPlayer(
        PlayerHandle player,
        out PlayerStateSnapshot snapshot) =>
        TryCaptureRuntimePlayerSnapshot(player, out snapshot);

    bool IRuntimePlayerSlotSnapshotLookup.TryGetPlayer(
        PlayerSlotId slot,
        out PlayerStateSnapshot snapshot)
    {
        if (_players.TryGetValue(slot.Value, out RuntimePlayerState? player))
            return TryCaptureRuntimePlayerSnapshot(player.Connection.Player, out snapshot);

        if (_serverPlayerStates is not null)
            return _serverPlayerStates.TryGet(slot, out snapshot);

        snapshot = default;
        return false;
    }

    internal bool TryCapturePlayerInventoryItem(
        PlayerHandle player,
        int inventorySlot,
        out RuntimePlayerInventoryItem item)
    {
        if (!_players.TryGetValue(player.Slot.Value, out RuntimePlayerState? state) ||
            state.Connection.Player != player)
        {
            item = default;
            return false;
        }

        return _playerInventory.TryGet(state.Connection, inventorySlot, out item);
    }

    internal bool TryCaptureNpcSnapshot(NpcHandle npc, out NpcSnapshot snapshot) =>
        _npcs.TryGet(npc, out snapshot);

    internal bool TryCaptureProjectileSnapshot(ProjectileHandle projectile, out ProjectileSnapshot snapshot) =>
        _projectiles.TryGet(projectile, out snapshot);

    internal bool TryCaptureWorldItemSnapshot(short slot, out WorldItemSnapshot snapshot) =>
        _worldItems.TryGetActive(slot, out snapshot);

    public void Apply(RuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        AppliedCommands++;

        if (_serverPlayerCommands?.TryApply(command) == true)
            return;
        if (_npcActorCommands.TryApply(command))
            return;
        if (_objectPlacementProcessor?.TryApply(this, command) == true)
            return;
        if (command is NpcActorSpawnRuntimeCommand actorSpawn)
        {
            ApplyNpcActorSpawn(actorSpawn);
            return;
        }

        switch (command)
        {
            case WorkerResultCommand result:
                Volatile.Write(ref lastWorkerResult, result.Value);
                break;
            case SetInterestManagementRuntimeCommand interestManagement:
                interestManagement.Control.SetEnabled(interestManagement.Enabled);
                break;
            case NpcSpawnRuntimeCommand spawn:
                ApplyNpcSpawn(spawn);
                break;
            case NpcUpdateRuntimeCommand update:
                ApplyNpcUpdate(update);
                break;
            case NpcDespawnRuntimeCommand despawn:
                ApplyNpcDespawn(despawn);
                break;
            case ProjectileSpawnRuntimeCommand spawn:
                ApplyProjectileSpawn(spawn);
                break;
            case ProjectileUpdateRuntimeCommand update:
                ApplyProjectileUpdate(update);
                break;
            case ProjectileDespawnRuntimeCommand despawn:
                ApplyProjectileDespawn(despawn);
                break;
            case ClientProjectileUpdateRuntimeCommand update:
                ApplyClientProjectileUpdate(update);
                break;
            case ClientProjectileDestroyRuntimeCommand destroy:
                ApplyClientProjectileDestroy(destroy);
                break;
            case ClientNpcDamageRuntimeCommand npcDamage:
                ApplyClientNpcDamage(npcDamage);
                break;
            case ClientTileManipulationRuntimeCommand tile:
                ApplyClientTileManipulation(tile);
                break;
            case ClientNpcHomeRuntimeCommand home:
                ApplyClientNpcHome(home);
                break;
            case ClientNpcTalkRuntimeCommand talk:
                ApplyClientNpcTalk(talk);
                break;
            case ClientNpcCatchRuntimeCommand npcCatch:
                ApplyClientNpcCatch(npcCatch);
                break;
            case WorldItemAllocateRuntimeCommand allocate:
                ApplyWorldItemAllocate(allocate);
                break;
            case WorldItemDropRuntimeCommand drop:
                ApplyWorldItemDrop(drop);
                break;
            case WorldItemRemoveRuntimeCommand remove:
                ApplyWorldItemRemove(remove);
                break;
            case WorldItemOwnerRuntimeCommand owner:
                ApplyWorldItemOwner(owner);
                break;
            case PlayerAppearanceRuntimeCommand appearance:
                ApplyPlayerAppearance(appearance);
                break;
            case PlayerEquipmentRuntimeCommand equipment:
                ApplyPlayerEquipment(equipment);
                break;
            case PlayerHealthRuntimeCommand health:
                ApplyPlayerHealth(health);
                break;
            case PlayerManaRuntimeCommand mana:
                ApplyPlayerMana(mana);
                break;
            case PlayerSpawnRuntimeCommand spawn:
                ApplyPlayerSpawn(spawn);
                break;
            case PlayerMovementRuntimeCommand movement:
                ApplyPlayerMovement(movement);
                break;
            case PlayerDisconnectRuntimeCommand disconnect:
                ApplyPlayerDisconnect(disconnect);
                break;
            case PlayerStateSnapshotRuntimeCommand snapshot:
                CompletePlayerSnapshot(snapshot);
                break;
        }
    }

    public void Tick()
    {
        if (Updates != _tileEditBudgetTick)
        {
            if (_tileEditBudgetUsed)
            {
                Array.Clear(_tileEditCounts, 0, _tileEditCounts.Length);
                _tileEditBudgetUsed = false;
            }

            _tileEditBudgetTick = Updates;
        }

        _npcArchetypes.CommitPending();
        _npcShops.CommitPending();
        _npcActorCommands.CommitPending();
        TickServerPlayerPhysics();

        if (_vanillaNpcTargetingAiStepper is not null)
        {
            int candidateCount = CopyVanillaNpcTargetCandidates(_npcTargetCandidates);
            ReadOnlySpan<VanillaNpcTargetCandidate> candidates = _npcTargetCandidates.AsSpan(0, candidateCount);
            _vanillaNpcTargetingAiStepper.SetCandidates(candidates);
            _vanillaNpcCheckActiveAiStepper?.SetCandidates(candidates);
            if (_worldClock is not null)
            {
                _vanillaNpcTargetingAiStepper.SetWorldConditions(
                    _worldClock.DayTime,
                    _worldClock.SlimeRainActive,
                    _worldClock.GetGoodWorld,
                    _expertMode,
                    _masterMode);
            }
        }

        LastNpcAiTick = _npcAiExecutor.Tick(_npcAiStepper);
        TickTownNpcLifecycle();
        AppliedNpcDespawns += _npcs.DespawnExpired();
        if (_projectileStepper is not null)
        {
            LastProjectileTick = _projectileExecutor.Tick(_projectileStepper);
            _purificationPowderNpcInteractions?.Tick();
            AppliedProjectileReflections += _projectileReflections.Tick();
        }
        TickInstancedItemLeases();

        _worldClock?.Tick();
        Updates++;
    }

    private void TickTownNpcLifecycle()
    {
        if (_townMoveIn is null && _townSchedule is null)
            return;

        int spawnPlayerCount = 0;
        int boundsCount = 0;
        Span<RuntimePlayerInventoryItem> inventory = stackalloc RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
        foreach (RuntimePlayerState player in _players.Values)
        {
            long coinValue = 0;
            bool bullet = false;
            bool bomb = false;
            bool dye = false;
            inventory.Clear();
            if (_playerInventory.TryCopyInventory(player.Connection, inventory))
            {
                foreach (RuntimePlayerInventoryItem item in inventory)
                {
                    if (item.IsEmpty)
                        continue;
                    coinValue = Math.Min(5_000L, coinValue + VanillaTownNpcSpawnItemFacts1458.GetCoinValue(item.ItemType, item.Stack));
                    bullet |= VanillaTownNpcSpawnItemFacts1458.CountsForArmsDealer(item.ItemType);
                    bomb |= VanillaTownNpcSpawnItemFacts1458.CountsForDemolitionist(item.ItemType);
                    dye |= VanillaTownNpcSpawnItemFacts1458.CountsForDyeTrader(item.ItemType);
                }
            }

            _townSpawnPlayers[spawnPlayerCount++] = new VanillaTownSpawnPlayerFacts1458(
                Active: true,
                MaxLife: player.HasHealth ? player.MaxLife : (short)100,
                CoinValue: coinValue,
                HasBulletAmmoOrWeapon: bullet,
                HasDemolitionistBomb: bomb,
                HasDyeTraderItem: dye);
            _townPlayerBounds[boundsCount++] = new RuntimeTownPlayerBounds1458(
                player.PositionX, player.PositionY, VanillaBasePlayerWidth, VanillaBasePlayerHeight);
        }

        if (_townMoveIn is not null)
        {
            var moveInConditions = new RuntimeTownNpcMoveInConditions1458(
                DayTime: _worldClock?.DayTime ?? true,
                Eclipse: _townInitialEclipse,
                InvasionActive: _townInitialInvasionActive,
                WorldUpdateRate: 1);
            _townMoveIn.Tick(in moveInConditions, _townSpawnPlayers.AsSpan(0, spawnPlayerCount));
        }

        if (_townSchedule is not null)
        {
            var scheduleConditions = new RuntimeTownNpcScheduleConditions1458(
                DayTime: _worldClock?.DayTime ?? true,
                Raining: _townInitialRaining,
                Eclipse: _townInitialEclipse,
                SlimeRain: _worldClock?.SlimeRainActive ?? false,
                StormingAboveSurface: false);
            _townSchedule.Tick(in scheduleConditions, _townPlayerBounds.AsSpan(0, boundsCount));
        }
    }

    private void TickServerPlayerPhysics()
    {
        if (_serverPlayerStates is null || _serverPlayerDryPhysics is null)
            return;

        int count = _serverPlayerStates.CopySnapshots(_serverPlayerSnapshots);
        for (int index = 0; index < count; index++)
        {
            PlayerStateSnapshot player = _serverPlayerSnapshots[index];
            ServerPlayerMovementIntent movementIntent =
                _serverPlayerCommands?.GetMovementIntent(player.Player) ?? ServerPlayerMovementIntent.Stop();
            ServerPlayerHorizontalIntent horizontalIntent;
            ServerPlayerJumpIntent jumpIntent;
            if (movementIntent.Kind != ServerPlayerMovementIntentKind.Stop)
            {
                RuntimeServerPlayerMovementIntentController.TryResolve(
                    in player,
                    in movementIntent,
                    this,
                    out horizontalIntent,
                    out jumpIntent);
            }
            else
            {
                horizontalIntent =
                    _serverPlayerCommands?.GetHorizontalIntent(player.Player) ?? ServerPlayerHorizontalIntent.Stop;
                jumpIntent =
                    _serverPlayerCommands?.GetJumpIntent(player.Player) ?? ServerPlayerJumpIntent.Released;
            }
            VanillaServerPlayerJumpState jumpState =
                _serverPlayerCommands?.GetJumpState(player.Player) ?? VanillaServerPlayerJumpState.Initial;
            int slot = player.Player.Slot.Value;
            VanillaLiquidContactState liquidContacts = _serverPlayerLiquidOwners[slot] == player.Player
                ? _serverPlayerLiquidContacts[slot]
                : default;
            if (!_serverPlayerDryPhysics.TryStep(
                    in player,
                    horizontalIntent,
                    jumpIntent,
                    in jumpState,
                    in liquidContacts,
                    out ServerPlayerDryPhysicsStepResult next,
                    out VanillaServerPlayerJumpState nextJumpState))
            {
                continue;
            }

            _serverPlayerCommands?.CommitJumpState(player.Player, in nextJumpState);
            VanillaLiquidContactState nextLiquidContacts = next.LiquidContacts;
            _serverPlayerLiquidOwners[slot] = player.Player;
            _serverPlayerLiquidContacts[slot] = nextLiquidContacts;

            if (next.PositionX == player.PositionX &&
                next.PositionY == player.PositionY &&
                next.VelocityX == player.VelocityX &&
                next.VelocityY == player.VelocityY)
            {
                continue;
            }

            if (_serverPlayerStates.TrySetMotion(
                player.Player,
                next.PositionX,
                next.PositionY,
                next.VelocityX,
                next.VelocityY,
                out PlayerStateSnapshot committed))
            {
                _serverPlayerEvents?.ServerPlayerMoved(in committed);
            }
        }
    }

    private int CopyVanillaNpcTargetCandidates(Span<VanillaNpcTargetCandidate> destination)
    {
        int serverPlayerCount = _serverPlayerStates?.CopySnapshots(_serverPlayerSnapshots) ?? 0;
        int serverPlayerIndex = 0;
        int written = 0;

        for (int slot = 0; slot < VanillaNpcTargetingAiStepper.MaximumPlayerCandidates; slot++)
        {
            if (_players.TryGetValue(checked((byte)slot), out RuntimePlayerState? player))
            {
                if (player.MountType != 0)
                    continue;

                destination[written++] = new VanillaNpcTargetCandidate(
                    Slot: checked((byte)slot),
                    CenterX: player.PositionX + VanillaBasePlayerWidth * 0.5f,
                    CenterY: player.PositionY + VanillaBasePlayerHeight * 0.5f,
                    Aggro: 0,
                    Active: true,
                    Dead: player.IsDead,
                    Ghost: false,
                    NoAggro: false);
                continue;
            }

            while (serverPlayerIndex < serverPlayerCount &&
                   _serverPlayerSnapshots[serverPlayerIndex].Player.Slot.Value < slot)
            {
                serverPlayerIndex++;
            }

            if (serverPlayerIndex >= serverPlayerCount ||
                _serverPlayerSnapshots[serverPlayerIndex].Player.Slot.Value != slot)
            {
                continue;
            }

            PlayerStateSnapshot serverPlayer = _serverPlayerSnapshots[serverPlayerIndex++];
            if (serverPlayer.MountType != 0)
                continue;

            destination[written++] = new VanillaNpcTargetCandidate(
                Slot: checked((byte)slot),
                CenterX: serverPlayer.PositionX + VanillaBasePlayerWidth * 0.5f,
                CenterY: serverPlayer.PositionY + VanillaBasePlayerHeight * 0.5f,
                Aggro: 0,
                Active: true,
                Dead: serverPlayer.IsDead,
                Ghost: false,
                NoAggro: false);
        }

        return written;
    }

    private bool IsTileActorFree(int tileX, int tileY)
    {
        if (_worldTiles is null)
            return false;
        if ((uint)tileX >= (uint)_worldTiles.Dimensions.WidthTiles || (uint)tileY >= (uint)_worldTiles.Dimensions.HeightTiles)
            return false;
        int tileLeft = tileX * 16;
        int tileTop = tileY * 16;
        int tileRight = tileLeft + 16;
        int tileBottom = tileTop + 16;
        foreach (var kvp in _players)
        {
            RuntimePlayerState player = kvp.Value;
            if (player.IsDead)
                continue;
            if (Intersects(player.PositionX, player.PositionY, VanillaBasePlayerWidth, VanillaBasePlayerHeight, tileLeft, tileTop, tileRight, tileBottom))
                return false;
        }
        if (_serverPlayerStates is not null)
        {
            int count = _serverPlayerStates.CopySnapshots(_serverPlayerSnapshots);
            for (int i = 0; i < count; i++)
            {
                PlayerStateSnapshot snapshot = _serverPlayerSnapshots[i];
                if (snapshot.IsDead)
                    continue;
                if (Intersects(snapshot.PositionX, snapshot.PositionY, VanillaBasePlayerWidth, VanillaBasePlayerHeight, tileLeft, tileTop, tileRight, tileBottom))
                    return false;
            }
        }
        var npcBuffer = new NpcSnapshot[_npcs.Capacity];
        int npcCount = _npcs.CopyActive(npcBuffer);
        for (int i = 0; i < npcCount; i++)
        {
            if (!IsNpcFree(npcBuffer[i], tileLeft, tileTop, tileRight, tileBottom))
                return false;
        }
        return true;
    }

    private static bool IsNpcFree(in NpcSnapshot npc, int tileLeft, int tileTop, int tileRight, int tileBottom)
    {
        if (!npc.IsActive)
            return true;
        if (!NpcTypeId.TryCreate(npc.Type, out NpcTypeId type) ||
            !VanillaNpcDefinitionCatalog.TryGet(type, npc.NetIdentity, out VanillaNpcDefinition definition))
        {
            return !Intersects(npc.PositionX, npc.PositionY, 16f, 16f, tileLeft, tileTop, tileRight, tileBottom);
        }
        if (!definition.TryResolveHitbox(npc.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
            return true;
        return !Intersects(npc.PositionX, npc.PositionY, hitbox.Width, hitbox.Height, tileLeft, tileTop, tileRight, tileBottom);
    }

    private static bool Intersects(float rx, float ry, float rw, float rh, int tx0, int ty0, int tx1, int ty1)
    {
        float rx1 = rx + rw;
        float ry1 = ry + rh;
        return rx < tx1 && rx1 > tx0 && ry < ty1 && ry1 > ty0;
    }

    internal bool IsTileActorFreeForTesting(int tileX, int tileY) => IsTileActorFree(tileX, tileY);

    private void ApplyNpcSpawn(NpcSpawnRuntimeCommand command)
    {
        NpcStateUpdate state = command.State;
        if (_npcs.TrySpawn(command.Slot, in state, out NpcSnapshot snapshot))
        {
            AppliedNpcSpawns++;
            command.Completion?.TrySetResult(snapshot);
            return;
        }

        RejectedNpcSpawns++;
        command.Completion?.TrySetResult(null);
    }

    private void ApplyNpcActorSpawn(NpcActorSpawnRuntimeCommand command)
    {
        NpcActorSpawnRequest request = command.Request;
        if (!request.IsValid)
        {
            command.Completion.TrySetResult(new NpcActorSpawnResult(NpcActorSpawnStatus.InvalidRequest, default));
            return;
        }

        _npcArchetypes.CommitPending();
        if (!_npcArchetypes.Snapshot.TryGet(request.ArchetypeId, out _))
        {
            command.Completion.TrySetResult(new NpcActorSpawnResult(NpcActorSpawnStatus.ArchetypeNotFound, default));
            return;
        }

        var spawn = new NpcArchetypeAllocateRequest(request.ArchetypeId, request.PositionX, request.PositionY);
        if (!_npcArchetypeSpawner.TrySpawnAllocated(in spawn, out NpcSnapshot snapshot))
        {
            command.Completion.TrySetResult(new NpcActorSpawnResult(NpcActorSpawnStatus.NoAvailableSlot, default));
            return;
        }

        AppliedNpcSpawns++;
        command.Completion.TrySetResult(new NpcActorSpawnResult(NpcActorSpawnStatus.Spawned, snapshot.Handle));
    }

    private void ApplyNpcUpdate(NpcUpdateRuntimeCommand command)
    {
        NpcStateUpdate state = command.State;
        if (_npcs.TryUpdate(command.Npc, in state, out _))
        {
            AppliedNpcUpdates++;
            return;
        }

        RejectedNpcUpdates++;
    }

    private void ApplyNpcDespawn(NpcDespawnRuntimeCommand command)
    {
        if (_npcs.TryDespawn(command.Npc))
        {
            AppliedNpcDespawns++;
            command.Completion?.TrySetResult(true);
            return;
        }

        RejectedNpcDespawns++;
        command.Completion?.TrySetResult(false);
    }

    private void ApplyProjectileSpawn(ProjectileSpawnRuntimeCommand command)
    {
        ProjectileStateUpdate state = command.State;
        if (_projectiles.TrySpawn(command.Slot, in state, out ProjectileSnapshot snapshot))
        {
            AppliedProjectileSpawns++;
            command.Completion?.TrySetResult(snapshot);
            return;
        }

        RejectedProjectileSpawns++;
        command.Completion?.TrySetResult(null);
    }

    private void ApplyProjectileUpdate(ProjectileUpdateRuntimeCommand command)
    {
        ProjectileStateUpdate state = command.State;
        if (_projectiles.TryUpdate(command.Projectile, in state, out _))
        {
            AppliedProjectileUpdates++;
            return;
        }

        RejectedProjectileUpdates++;
    }

    private void ApplyProjectileDespawn(ProjectileDespawnRuntimeCommand command)
    {
        if (_projectiles.TryDespawn(command.Projectile, out _))
        {
            AppliedProjectileDespawns++;
            return;
        }

        RejectedProjectileDespawns++;
    }

    private void ApplyClientNpcDamage(ClientNpcDamageRuntimeCommand command)
    {
        TerrariaNpcDamageState damageState = command.State;
        if (!IsCurrentPlayerConnection(command.Connection))
        {
            RejectedClientNpcDamage++;
            return;
        }

        RuntimeNpcNetworkDamageResult result = _npcCombat.TryApply(command.Connection, in damageState);
        if (result == RuntimeNpcNetworkDamageResult.Rejected)
            RejectedClientNpcDamage++;
        else
            AppliedClientNpcDamage++;
    }

    private void TickInstancedItemLeases()
    {
        int expired = _instancedItemLeases.Tick(_expiredInstancedItemSlots);
        if (_worldItemReplication is null)
            return;
        for (int index = 0; index < expired; index++)
            _worldItemReplication.TryBroadcastInstancedSlotRelease(_expiredInstancedItemSlots[index]);
    }

    private void ApplyClientProjectileUpdate(ClientProjectileUpdateRuntimeCommand command)
    {
        TerrariaProjectileUpdateState packet = command.State;
        if (_projectileReplication is null ||
            !IsCurrentPlayerConnection(command.Connection) ||
            packet.Key.Spawner != command.Connection.Player.Slot.Value ||
            !TryConvertClientProjectileUpdate(in packet, out ProjectileStateUpdate update))
        {
            RejectedClientProjectileUpdates++;
            return;
        }

        RuntimeProjectileWireIdentityRegistry identities = _projectileReplication.WireIdentities;
        RuntimeProjectileClientCommitContext clientCommits = _projectileReplication.ClientCommitContext;
        TerrariaProjectileKeyState key = packet.Key;

        if (identities.TryResolve(in key, out ProjectileHandle projectile))
        {
            using IDisposable scope = clientCommits.Enter(command.Connection.Source, in key);
            if (_projectiles.TryUpdate(projectile, in update, out _))
            {
                AppliedProjectileUpdates++;
                return;
            }

            RejectedProjectileUpdates++;
            RejectedClientProjectileUpdates++;
            return;
        }

        using (clientCommits.Enter(command.Connection.Source, in key))
        {
            if (_projectiles.TrySpawnVanilla(in update, out _))
            {
                AppliedProjectileSpawns++;
                return;
            }
        }

        RejectedProjectileSpawns++;
        RejectedClientProjectileUpdates++;
    }

    private void ApplyClientProjectileDestroy(ClientProjectileDestroyRuntimeCommand command)
    {
        TerrariaProjectileDestroyState packet = command.State;
        if (_projectileReplication is null ||
            !packet.IsValid ||
            !IsCurrentPlayerConnection(command.Connection))
        {
            RejectedClientProjectileDestroys++;
            return;
        }

        RuntimeProjectileWireIdentityRegistry identities = _projectileReplication.WireIdentities;
        TerrariaProjectileKeyState key = packet.Key;
        if (!identities.TryResolve(in key, out ProjectileHandle projectile))
        {
            if (_projectileReplication.TryRelayUnresolvedDestroy(command.Connection.Source, in packet))
            {
                RelayedUnknownProjectileDestroys++;
                return;
            }

            RejectedClientProjectileDestroys++;
            return;
        }

        if (!_projectiles.TryGet(projectile, out ProjectileSnapshot current))
        {
            identities.TryUnbind(projectile, out _);
            if (_projectileReplication.TryRelayUnresolvedDestroy(command.Connection.Source, in packet))
            {
                RelayedUnknownProjectileDestroys++;
                return;
            }

            RejectedClientProjectileDestroys++;
            return;
        }

        if (current.Spawner != command.Connection.Player.Slot.Value)
        {
            RejectedClientProjectileDestroys++;
            return;
        }

        using (_projectileReplication.ClientCommitContext.Enter(command.Connection.Source, in key))
        {
            if (_projectiles.TryDespawnAt(projectile, packet.PositionX, packet.PositionY, out _))
            {
                AppliedProjectileDespawns++;
                return;
            }
        }

        RejectedProjectileDespawns++;
        RejectedClientProjectileDestroys++;
    }

    private void ApplyClientTileManipulation(ClientTileManipulationRuntimeCommand command)
    {
        ClientTileManipulationRequests++;
        VanillaWorldTileMutationService? tileMutations = _tileMutations;
        if (_worldTiles is null ||
            tileMutations is null ||
            !command.Connection.IsAssigned ||
            !_players.TryGetValue(command.Connection.Player.Slot.Value, out RuntimePlayerState? player) ||
            player.Connection != command.Connection ||
            !VanillaTileManipulationWorldRules.IsInPacket17WorldBounds(
                _worldTiles.Dimensions.WidthTiles,
                _worldTiles.Dimensions.HeightTiles,
                command.State.TileX,
                command.State.TileY))
        {
            RejectedClientTileManipulations++;
            return;
        }

        if (!command.State.TryGetKnownAction(out var action))
        {
            UnsupportedClientTileManipulations++;
            return;
        }

        byte slot = command.Connection.Player.Slot.Value;
        if (_tileEditCounts[slot] >= MaxTileEditsPerTickPerPlayer)
        {
            RejectedClientTileManipulations++;
            return;
        }

        _tileEditCounts[slot]++;
        _tileEditBudgetUsed = true;
        ValidatedClientTileManipulations++;
        var tileState = command.State;

        if (action == TerraRuntime.Protocol.Multiplicity.TerrariaTileManipulationAction.KillWall)
        {
            if (!ApplyTileMutation(
                    tileMutations,
                    WorldTileMutationKind.KillWall,
                    tileState.TileX,
                    tileState.TileY))
            {
                RejectedClientTileManipulations++;
                return;
            }

            AppliedClientTileManipulations++;
            _tileManipulationReplication?.TryPublishCommitted(command.Connection.Source, in tileState);
            return;
        }

        if (action == TerraRuntime.Protocol.Multiplicity.TerrariaTileManipulationAction.PlaceWall)
        {
            if (!VanillaWallIds.TryCreate(tileState.Data, out WallTypeId wallType) ||
                wallType == VanillaWallIds.None ||
                !VanillaWallDefinitionCatalog.TryGet(wallType, out VanillaWallDefinition wallDefinition) ||
                !wallDefinition.IsPresent)
            {
                RejectedClientTileManipulations++;
                return;
            }

            if (!_playerInventory.TryGet(
                    command.Connection,
                    player.SelectedItem,
                    out RuntimePlayerInventoryItem wallItem) ||
                wallItem.IsEmpty)
            {
                RejectedClientTileManipulations++;
                return;
            }

            if (!ApplyTileMutation(
                    tileMutations,
                    WorldTileMutationKind.PlaceWall,
                    tileState.TileX,
                    tileState.TileY,
                    wallType: wallType))
            {
                RejectedClientTileManipulations++;
                return;
            }

            AppliedClientTileManipulations++;
            _tileManipulationReplication?.TryPublishCommitted(command.Connection.Source, in tileState);
            return;
        }

        if (action == TerraRuntime.Protocol.Multiplicity.TerrariaTileManipulationAction.KillTileNoItem)
        {
            WorldTile before = _worldTiles.Get(tileState.TileX, tileState.TileY);
            bool isDirt = before.TileType == VanillaTileIds.Dirt;
            if (isDirt && !VanillaDirtPlacement.CanKillIsolated(_worldTiles, tileState.TileX, tileState.TileY))
            {
                RejectedClientTileManipulations++;
                return;
            }

            if (!ApplyTileMutation(
                    tileMutations,
                    WorldTileMutationKind.KillTile,
                    tileState.TileX,
                    tileState.TileY))
            {
                RejectedClientTileManipulations++;
                return;
            }

            AppliedClientTileManipulations++;
            _tileManipulationReplication?.TryPublishCommitted(command.Connection.Source, in tileState);
            return;
        }

        if (action == TerraRuntime.Protocol.Multiplicity.TerrariaTileManipulationAction.KillTile)
        {
            if (tileState.Data != 0 && tileState.Data != 1)
            {
                UnsupportedClientTileManipulations++;
                return;
            }

            if (!_playerInventory.TryGet(
                    command.Connection,
                    player.SelectedItem,
                    out RuntimePlayerInventoryItem toolItem) ||
                toolItem.IsEmpty ||
                toolItem.ItemType != VanillaItemIds.CopperPickaxe ||
                !VanillaTileInteractionItemFacts.TryGetPickPower(toolItem.ItemType, out _, out _))
            {
                RejectedClientTileManipulations++;
                return;
            }

            if (tileState.Data == 1)
            {
                AppliedClientTileManipulations++;
                _tileManipulationReplication?.TryPublishAccepted(command.Connection.Source, in tileState);
                return;
            }

            WorldTile beforeKill = _worldTiles.Get(tileState.TileX, tileState.TileY);
            TileTypeId beforeType = beforeKill.TileType;
            bool isDirtKill = beforeType == VanillaTileIds.Dirt;
            if (isDirtKill && !VanillaDirtPlacement.CanKillIsolated(_worldTiles, tileState.TileX, tileState.TileY))
            {
                RejectedClientTileManipulations++;
                return;
            }

            bool hasDrop = VanillaTileWorldItemDrop.TryCreate(beforeType, tileState.TileX, tileState.TileY, _worldItemSpawnRandom, out WorldItemDropStateUpdate dropState);
            if (!hasDrop && isDirtKill)
            {
                hasDrop = true;
                dropState = VanillaDirtWorldItemDrop.Create(tileState.TileX, tileState.TileY, _worldItemSpawnRandom);
            }

            WorldItemDropReservation reservation = default;
            bool reserved = false;
            if (hasDrop)
            {
                if (!_worldItems.TryReserveDropSlot(out reservation))
                {
                    RejectedClientTileManipulations++;
                    RejectedWorldItemAllocations++;
                    return;
                }

                reserved = true;
            }

            if (!ApplyTileMutation(
                    tileMutations,
                    WorldTileMutationKind.KillTile,
                    tileState.TileX,
                    tileState.TileY))
            {
                if (reserved)
                    _ = _worldItems.TryReleaseDropReservation(in reservation);
                RejectedClientTileManipulations++;
                return;
            }

            if (reserved)
            {
                if (!_worldItems.TryCommitReservedDrop(in reservation, in dropState, out _))
                {
                    throw new InvalidOperationException(
                        "Reserved tile drop could not commit after authoritative tile mutation.");
                }

                AppliedWorldItemAllocations++;
            }

            AppliedClientTileManipulations++;
            _tileManipulationReplication?.TryPublishCommitted(command.Connection.Source, in tileState);
            return;
        }

        if (action != TerraRuntime.Protocol.Multiplicity.TerrariaTileManipulationAction.PlaceTile)
        {
            UnsupportedClientTileManipulations++;
            return;
        }

        if (!_playerInventory.TryGet(
                command.Connection,
                player.SelectedItem,
                out RuntimePlayerInventoryItem selectedItem))
        {
            RejectedClientTileManipulations++;
            return;
        }

        ClientTileManipulationConsistencyResult consistency =
            ClientTileManipulationConsistency.Evaluate(in tileState, in selectedItem);
        switch (consistency)
        {
            case ClientTileManipulationConsistencyResult.Mismatch:
                RejectedClientTileManipulations++;
                return;

            case ClientTileManipulationConsistencyResult.Unsupported:
                UnsupportedClientTileManipulations++;
                return;

            case ClientTileManipulationConsistencyResult.Consistent:
                if (!VanillaTileIds.TryCreate(tileState.Data, out TileTypeId requestedTile))
                {
                    RejectedClientTileManipulations++;
                    return;
                }

                if (!VanillaTileDefinitionCatalog.TryGet(requestedTile, out VanillaTileDefinition definition) ||
                    definition.IsFrameImportant ||
                    VanillaMultiTileObjectCatalog.TryGet(requestedTile, out _))
                {
                    RejectedClientTileManipulations++;
                    return;
                }

                if (requestedTile == VanillaTileIds.Dirt &&
                    !VanillaDirtPlacement.CanPlaceOnEmpty(_worldTiles, tileState.TileX, tileState.TileY))
                {
                    RejectedClientTileManipulations++;
                    return;
                }

                if (!ApplyTileMutation(
                        tileMutations,
                        WorldTileMutationKind.PlaceTile,
                        tileState.TileX,
                        tileState.TileY,
                        requestedTile))
                {
                    RejectedClientTileManipulations++;
                    return;
                }

                AppliedClientTileManipulations++;
                _tileManipulationReplication?.TryPublishCommitted(command.Connection.Source, in tileState);
                return;

            default:
                throw new InvalidOperationException("Unknown client tile-manipulation consistency result.");
        }
    }

    private static bool ApplyTileMutation(
        VanillaWorldTileMutationService tileMutations,
        WorldTileMutationKind kind,
        int x,
        int y,
        TileTypeId tileType = default,
        WallTypeId wallType = default)
    {
        var request = new WorldTileMutationRequest(kind, x, y, TileType: tileType, WallType: wallType);
        return tileMutations.Apply(in request).Applied;
    }

    private static bool TryConvertClientProjectileUpdate(
        in TerrariaProjectileUpdateState packet,
        out ProjectileStateUpdate update)
    {
        if (!packet.IsValid ||
            !VanillaProjectileIds.TryCreate(packet.ProjectileType, out ProjectileTypeId type) ||
            !VanillaProjectileIds.IsLiveWireType(type) ||
            VanillaProjectileFacts.IsHostile(type))
        {
            update = default;
            return false;
        }

        update = new ProjectileStateUpdate(
            type,
            packet.Key.Spawner,
            packet.PositionX,
            packet.PositionY,
            packet.VelocityX,
            packet.VelocityY,
            new ProjectileAiState(packet.Ai0, packet.Ai1, packet.Ai2),
            packet.BannerIdToRespondTo,
            packet.Damage,
            packet.KnockBack,
            packet.OriginalDamage);
        return true;
    }

    private void ApplyClientNpcTalk(ClientNpcTalkRuntimeCommand command)
    {
        if (!IsCurrentPlayerConnection(command.Connection) ||
            !TerrariaNpcTalkCodec.IsValidNpcSlot(command.State.NpcSlot))
        {
            return;
        }

        byte playerSlot = command.Connection.Player.Slot.Value;
        if (command.State.NpcSlot != TerrariaNpcTalkCodec.NoNpc)
            _townRescue?.TryRescueTalk(command.State.NpcSlot, out _);
        _playerTalkNpcSlots[playerSlot] = command.State.NpcSlot;
        _townShopSessions[playerSlot] = null;
        if (command.State.NpcSlot != TerrariaNpcTalkCodec.NoNpc &&
            _townCommerce is not null &&
            _players.TryGetValue(playerSlot, out RuntimePlayerState? playerState))
        {
            var commercePlayer = new RuntimeTownCommercePlayer1458(
                playerState.PositionX,
                playerState.PositionY,
                playerState.HasHealth ? playerState.MaxLife : 100,
                playerState.HasMana ? playerState.MaxMana : 20,
                playerState.Team);
            if (_townCommerce.TryResolve(
                    command.Connection,
                    _playerInventory,
                    in commercePlayer,
                    command.State.NpcSlot,
                    _worldClock,
                    out RuntimeTownShopSession1458 session))
            {
                _townShopSessions[playerSlot] = session;
            }
        }

        _npcReplication?.TryPublishNpcTalk(command.Connection, command.State.NpcSlot);
    }

    private void ApplyClientNpcCatch(ClientNpcCatchRuntimeCommand command)
    {
        if (!IsCurrentPlayerConnection(command.Connection) ||
            !TerrariaNpcCatchCodec.IsValidNpcSlot(command.State.NpcSlot) ||
            !_players.TryGetValue(command.Connection.Player.Slot.Value, out RuntimePlayerState? player) ||
            !_npcs.TryGetActive(checked((byte)command.State.NpcSlot), out NpcSnapshot npc) ||
            !NpcTypeId.TryCreate(npc.Type, out NpcTypeId npcType) ||
            !VanillaNpcCatchCatalog1458.TryGetCatchItem(npcType, out ItemTypeId catchItem))
        {
            return;
        }

        if (VanillaNpcCatchCatalog1458.IsMysticFrog(npcType))
        {
            _mysticFrogCatch?.TryApply(npc.Handle, out _);
            return;
        }

        if (npc.Simulation.SpawnedFromStatue)
        {
            _npcs.TryDespawn(npc.Handle);
            return;
        }

        float playerCenterX = player.PositionX + VanillaBasePlayerWidth / 2f;
        float playerCenterY = player.PositionY + VanillaBasePlayerHeight / 2f;
        WorldItemDropStateUpdate drop = VanillaNpcCatchWorldItem1458.Create(
            playerCenterX,
            playerCenterY,
            catchItem,
            _worldItemSpawnRandom);
        if (!_worldItems.TryReserveDrop(in drop, out WorldItemDropReservation reservation))
            return;
        if (!_npcs.TryDespawn(npc.Handle))
        {
            _worldItems.TryReleaseDropReservation(in reservation);
            return;
        }
        if (!_worldItems.TryCommitReservedDrop(in reservation, out WorldItemSnapshot item))
            throw new InvalidOperationException("Reserved NPC catch item failed after authoritative NPC despawn.");

        var owner = new WorldItemOwnerStateUpdate(
            OwnerPlayerId: command.Connection.Player.Slot.Value,
            TimeToKeepReservation: VanillaNpcCatchWorldItem1458.ReservationTicks,
            GrabDelayPlayer: byte.MaxValue,
            GrabDelayTime: 0,
            PositionX: item.PositionX,
            PositionY: item.PositionY);
        if (!_worldItems.TryApplyOwner(item.Handle.Slot, in owner, out _))
            throw new InvalidOperationException("Caught NPC item could not be reserved for the authenticated player.");
    }

    internal bool TryGetPlayerTalkNpc(PlayerHandle player, out short npcSlot)
    {
        if (!player.IsAssigned ||
            !_players.TryGetValue(player.Slot.Value, out RuntimePlayerState? state) ||
            state.Connection.Player != player)
        {
            npcSlot = TerrariaNpcTalkCodec.NoNpc;
            return false;
        }

        npcSlot = _playerTalkNpcSlots[player.Slot.Value];
        return true;
    }

    private void ApplyClientNpcHome(ClientNpcHomeRuntimeCommand command)
    {
        if (!IsCurrentPlayerConnection(command.Connection) ||
            _townNpcs is null ||
            _housingValidator is null ||
            !command.State.TryGetStatus(out TerrariaNpcHomeStatus status))
        {
            return;
        }

        RuntimeTownNpcHomeCommit commit = default;
        bool applied = status switch
        {
            TerrariaNpcHomeStatus.Homeless => _townNpcs.TryKickOut(command.State.NpcSlot, out commit),
            TerrariaNpcHomeStatus.None => _townNpcs.TryAssignRoom(
                command.State.NpcSlot,
                command.State.HomeTileX,
                command.State.HomeTileY,
                _housingValidator,
                out commit,
                out _),
            // Status 2 is server-authored GetHouseholdStatus state, not a client room-move request.
            TerrariaNpcHomeStatus.HasRoom => false,
            _ => false
        };

        if (applied)
            _npcReplication?.TryPublishTownHome(in commit);
    }

    private bool IsCurrentPlayerConnection(ConnectionHandle connection) =>
        connection.IsAssigned &&
        _players.TryGetValue(connection.Player.Slot.Value, out RuntimePlayerState? player) &&
        player.Connection == connection;

    private bool IsCurrentWorldItemTarget(WorldItemHandle target) =>
        target.IsAssigned &&
        _worldItems.TryGetActive(target.Slot, out WorldItemSnapshot snapshot) &&
        snapshot.Handle == target;

    private void ApplyWorldItemAllocate(WorldItemAllocateRuntimeCommand command)
    {
        if (!IsCurrentPlayerConnection(command.Connection))
        {
            RejectedWorldItemAllocations++;
            command.Completion?.TrySetResult(null);
            return;
        }

        WorldItemDropStateUpdate state = command.State;
        if (_worldItems.TryAllocateDrop(in state, out WorldItemSnapshot snapshot))
        {
            AppliedWorldItemAllocations++;
            command.Completion?.TrySetResult(snapshot);
            return;
        }

        RejectedWorldItemAllocations++;
        command.Completion?.TrySetResult(null);
    }

    private void ApplyWorldItemDrop(WorldItemDropRuntimeCommand command)
    {
        if (!IsCurrentPlayerConnection(command.Connection) ||
            !IsCurrentWorldItemTarget(command.Target))
        {
            RejectedWorldItemDrops++;
            return;
        }

        WorldItemDropStateUpdate state = command.State;
        if (_worldItems.TryApplyDrop(command.Target.Slot, in state, out _))
        {
            AppliedWorldItemDrops++;
            return;
        }

        RejectedWorldItemDrops++;
    }

    private void ApplyWorldItemRemove(WorldItemRemoveRuntimeCommand command)
    {
        if (!IsCurrentPlayerConnection(command.Connection) ||
            !IsCurrentWorldItemTarget(command.Target))
        {
            RejectedWorldItemRemovals++;
            return;
        }

        if (_worldItems.TryRemove(command.Target.Slot, out _))
        {
            AppliedWorldItemRemovals++;
            return;
        }

        RejectedWorldItemRemovals++;
    }

    private void ApplyWorldItemOwner(WorldItemOwnerRuntimeCommand command)
    {
        if (!IsCurrentPlayerConnection(command.Connection) ||
            !IsCurrentWorldItemTarget(command.Target))
        {
            RejectedWorldItemOwners++;
            return;
        }

        WorldItemOwnerStateUpdate state = command.State;
        if (_worldItems.TryApplyOwner(command.Target.Slot, in state, out _))
        {
            AppliedWorldItemOwners++;
            return;
        }

        RejectedWorldItemOwners++;
    }

    private void ApplyPlayerAppearance(PlayerAppearanceRuntimeCommand appearance)
    {
        PlayerAppearanceCommitRequest request = appearance.Request;
        if (_players.TryGetValue(request.PlayerSlot.Value, out RuntimePlayerState? activePlayer) &&
            activePlayer.Connection != appearance.Connection)
        {
            RejectedPlayerAppearances++;
            return;
        }

        if (activePlayer is not null && !activePlayer.TryAdvanceRevision())
        {
            RejectedPlayerAppearances++;
            return;
        }

        AppliedPlayerAppearances++;
        _playerEvents?.PlayerAppearanceUpdated(appearance.Connection, in request);
    }

    private void ApplyPlayerEquipment(PlayerEquipmentRuntimeCommand equipment)
    {
        PlayerEquipmentCommitRequest request = equipment.Request;
        if (!equipment.Connection.IsAssigned ||
            equipment.Connection.Player.Slot != request.PlayerSlot)
        {
            RejectedPlayerEquipmentUpdates++;
            return;
        }

        bool inventorySlot = VanillaPlayerItemSlotCatalog.IsInventorySlot(request.SlotId);
        if (inventorySlot &&
            (!RuntimePlayerInventoryItem.TryFromNormalized(in request, out _) ||
             !_playerInventory.CanAccept(equipment.Connection)))
        {
            RejectedPlayerEquipmentUpdates++;
            return;
        }

        if (_players.TryGetValue(request.PlayerSlot.Value, out RuntimePlayerState? activePlayer) &&
            activePlayer.Connection != equipment.Connection)
        {
            RejectedPlayerEquipmentUpdates++;
            return;
        }

        if (activePlayer is not null && !activePlayer.TryAdvanceRevision())
        {
            RejectedPlayerEquipmentUpdates++;
            return;
        }

        if (inventorySlot && !_playerInventory.TrySet(equipment.Connection, in request))
        {
            RejectedPlayerEquipmentUpdates++;
            return;
        }

        AppliedPlayerEquipmentUpdates++;
        _playerEvents?.PlayerEquipmentUpdated(equipment.Connection, in request);
    }

    private void ApplyPlayerHealth(PlayerHealthRuntimeCommand health)
    {
        PlayerHealthCommitRequest request = VanillaPlayerHealthNormalizer.Normalize(in health.Request);
        if (!health.Connection.IsAssigned || health.Connection.Player.Slot != request.PlayerSlot)
        {
            RejectedPlayerHealthUpdates++;
            return;
        }

        if (_players.TryGetValue(request.PlayerSlot.Value, out RuntimePlayerState? activePlayer))
        {
            if (activePlayer.Connection != health.Connection || !activePlayer.TryAdvanceRevision())
            {
                RejectedPlayerHealthUpdates++;
                return;
            }

            activePlayer.HasHealth = true;
            activePlayer.Life = request.Life;
            activePlayer.MaxLife = request.MaxLife;
            activePlayer.IsDead = request.Life <= 0;
        }
        else
        {
            PendingPlayerVitals pending = GetOrReplacePending(health.Connection);
            pending.HasHealth = true;
            pending.Life = request.Life;
            pending.MaxLife = request.MaxLife;
        }

        AppliedPlayerHealthUpdates++;
        _playerEvents?.PlayerHealthUpdated(health.Connection, in request);
    }

    private void ApplyPlayerMana(PlayerManaRuntimeCommand mana)
    {
        PlayerManaCommitRequest request = mana.Request;
        if (!mana.Connection.IsAssigned || mana.Connection.Player.Slot != request.PlayerSlot)
        {
            RejectedPlayerManaUpdates++;
            return;
        }

        if (_players.TryGetValue(request.PlayerSlot.Value, out RuntimePlayerState? activePlayer))
        {
            if (activePlayer.Connection != mana.Connection || !activePlayer.TryAdvanceRevision())
            {
                RejectedPlayerManaUpdates++;
                return;
            }

            activePlayer.HasMana = true;
            activePlayer.Mana = request.Mana;
            activePlayer.MaxMana = request.MaxMana;
        }
        else
        {
            PendingPlayerVitals pending = GetOrReplacePending(mana.Connection);
            pending.HasMana = true;
            pending.Mana = request.Mana;
            pending.MaxMana = request.MaxMana;
        }

        AppliedPlayerManaUpdates++;
        _playerEvents?.PlayerManaUpdated(mana.Connection, in request);
    }

    private PendingPlayerVitals GetOrReplacePending(ConnectionHandle connection)
    {
        int slot = connection.Player.Slot.Value;
        PendingPlayerVitals? pending = _pendingVitals[slot];
        if (pending is null || pending.Connection != connection)
        {
            pending = new PendingPlayerVitals(connection);
            _pendingVitals[slot] = pending;
        }

        return pending;
    }

    private void ApplyPlayerSpawn(PlayerSpawnRuntimeCommand spawn)
    {
        PlayerSpawnCommitRequest request = spawn.Request;
        if (!VanillaPlayerSpawnValidator.IsValid(in request))
        {
            Volatile.Write(ref lastSpawnCommitResult, (int)PlayerSpawnCommitResult.InvalidSpawnData);
            return;
        }

        if (!spawn.Connection.IsAssigned ||
            spawn.Connection.Player.Slot != request.ClaimedSlot)
        {
            Volatile.Write(ref lastSpawnCommitResult, (int)PlayerSpawnCommitResult.SlotMismatch);
            return;
        }

        if (!_playerInventory.CanAccept(spawn.Connection))
        {
            Volatile.Write(ref lastSpawnCommitResult, (int)PlayerSpawnCommitResult.InvalidJoinState);
            return;
        }

        PlayerSpawnCommitResult commit = spawn.Session.TryCommitSpawn(request.ClaimedSlot);
        Volatile.Write(ref lastSpawnCommitResult, (int)commit);
        if (commit != PlayerSpawnCommitResult.Committed)
            return;

        if (!_playerInventory.TryAttach(spawn.Connection))
            throw new InvalidOperationException("Player inventory ownership changed during authoritative spawn commit.");

        PendingPlayerVitals? pending = _pendingVitals[request.ClaimedSlot.Value];
        bool hasPending = pending is not null && pending.Connection == spawn.Connection;
        if (pending is not null)
            _pendingVitals[request.ClaimedSlot.Value] = null;

        CommittedPlayerSpawns++;
        _players[request.ClaimedSlot.Value] = new RuntimePlayerState
        {
            Connection = spawn.Connection,
            Revision = 1,
            Slot = request.ClaimedSlot,
            Team = request.Team,
            PositionX = request.SpawnX * 16f,
            PositionY = request.SpawnY * 16f,
            HasHealth = hasPending && pending!.HasHealth,
            Life = hasPending ? pending!.Life : (short)0,
            MaxLife = hasPending ? pending!.MaxLife : (short)0,
            IsDead = hasPending && pending!.HasHealth && pending.Life <= 0,
            HasMana = hasPending && pending!.HasMana,
            Mana = hasPending ? pending!.Mana : (short)0,
            MaxMana = hasPending ? pending!.MaxMana : (short)0
        };
        _playerEvents?.PlayerSpawned(spawn.Connection, in request);
    }

    private void ApplyPlayerMovement(PlayerMovementRuntimeCommand movement)
    {
        PlayerMovementCommitRequest submitted = movement.Request;
        if (!VanillaPlayerMovementNormalizer.TryNormalize(
                in submitted,
                out PlayerMovementCommitRequest request))
        {
            RejectedPlayerMovements++;
            return;
        }

        if (!_players.TryGetValue(request.PlayerSlot.Value, out RuntimePlayerState? player) ||
            player.Connection != movement.Connection)
        {
            RejectedPlayerMovements++;
            return;
        }

        if (!player.TryAdvanceRevision())
        {
            RejectedPlayerMovements++;
            return;
        }

        player.ControlFlags = request.ControlFlags;
        player.MovementFlags = request.MovementFlags;
        player.MiscFlags1 = request.MiscFlags1;
        player.MiscFlags2 = request.MiscFlags2;
        player.SelectedItem = request.SelectedItem;
        player.PositionX = request.PositionX;
        player.PositionY = request.PositionY;
        player.VelocityX = request.HasVelocity ? request.VelocityX : 0f;
        player.VelocityY = request.HasVelocity ? request.VelocityY : 0f;
        player.MountType = request.HasMount ? request.MountType : (ushort)0;
        player.PotionOfReturnOriginalPositionX = request.HasPotionOfReturnPositions
            ? request.PotionOfReturnOriginalPositionX
            : 0f;
        player.PotionOfReturnOriginalPositionY = request.HasPotionOfReturnPositions
            ? request.PotionOfReturnOriginalPositionY
            : 0f;
        player.PotionOfReturnHomePositionX = request.HasPotionOfReturnPositions
            ? request.PotionOfReturnHomePositionX
            : 0f;
        player.PotionOfReturnHomePositionY = request.HasPotionOfReturnPositions
            ? request.PotionOfReturnHomePositionY
            : 0f;
        player.CameraTargetX = request.HasCameraTarget ? request.CameraTargetX : 0f;
        player.CameraTargetY = request.HasCameraTarget ? request.CameraTargetY : 0f;

        AppliedPlayerMovements++;
        LastMovementPlayerSlot = request.PlayerSlot;
        LastMovementPositionX = request.PositionX;
        LastMovementPositionY = request.PositionY;
        _playerEvents?.PlayerMoved(movement.Connection, in request);
    }

    private void ApplyPlayerDisconnect(PlayerDisconnectRuntimeCommand disconnect)
    {
        ConnectionHandle connection = disconnect.Connection;
        PendingPlayerVitals? pending = _pendingVitals[connection.Player.Slot.Value];
        if (pending is not null && pending.Connection == connection)
            _pendingVitals[connection.Player.Slot.Value] = null;

        _playerInventory.Clear(connection);

        if (!_players.TryGetValue(connection.Player.Slot.Value, out RuntimePlayerState? player) ||
            player.Connection != disconnect.Connection)
        {
            return;
        }

        _playerTalkNpcSlots[connection.Player.Slot.Value] = TerrariaNpcTalkCodec.NoNpc;
        _townShopSessions[connection.Player.Slot.Value] = null;
        _players.Remove(connection.Player.Slot.Value);
        DisconnectedPlayers++;
        _playerEvents?.PlayerDisconnected(connection);
    }

    private void CompletePlayerSnapshot(PlayerStateSnapshotRuntimeCommand command)
    {
        PlayerStateSnapshot? result = TryCaptureRuntimePlayerSnapshot(command.Player, out PlayerStateSnapshot snapshot)
            ? snapshot
            : null;
        command.Completion.TrySetResult(result);
    }

    private sealed class PendingPlayerVitals(ConnectionHandle connection)
    {
        public ConnectionHandle Connection { get; } = connection;
        public bool HasHealth { get; set; }
        public short Life { get; set; }
        public short MaxLife { get; set; }
        public bool HasMana { get; set; }
        public short Mana { get; set; }
        public short MaxMana { get; set; }
    }

    private sealed class RuntimePlayerState
    {
        public ConnectionHandle Connection { get; init; }
        public ulong Revision { get; set; }
        public PlayerSlotId Slot { get; init; }
        public byte Team { get; init; }
        public bool HasHealth { get; set; }
        public short Life { get; set; }
        public short MaxLife { get; set; }
        public bool IsDead { get; set; }
        public bool HasMana { get; set; }
        public short Mana { get; set; }
        public short MaxMana { get; set; }
        public byte ControlFlags { get; set; }
        public byte MovementFlags { get; set; }
        public byte MiscFlags1 { get; set; }
        public byte MiscFlags2 { get; set; }
        public byte SelectedItem { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float VelocityX { get; set; }
        public float VelocityY { get; set; }
        public ushort MountType { get; set; }
        public float PotionOfReturnOriginalPositionX { get; set; }
        public float PotionOfReturnOriginalPositionY { get; set; }
        public float PotionOfReturnHomePositionX { get; set; }
        public float PotionOfReturnHomePositionY { get; set; }
        public float CameraTargetX { get; set; }
        public float CameraTargetY { get; set; }

        public bool TryAdvanceRevision()
        {
            if (Revision == ulong.MaxValue)
                return false;

            Revision++;
            return true;
        }

        public PlayerStateSnapshot CaptureSnapshot() =>
            new(
                Connection.Player,
                new PlayerStateRevision(Revision),
                Team,
                ControlFlags,
                MovementFlags,
                MiscFlags1,
                MiscFlags2,
                SelectedItem,
                PositionX,
                PositionY,
                VelocityX,
                VelocityY,
                MountType,
                PotionOfReturnOriginalPositionX,
                PotionOfReturnOriginalPositionY,
                PotionOfReturnHomePositionX,
                PotionOfReturnHomePositionY,
                CameraTargetX,
                CameraTargetY)
            {
                HasHealth = HasHealth,
                Life = Life,
                MaxLife = MaxLife,
                IsDead = IsDead,
                HasMana = HasMana,
                Mana = Mana,
                MaxMana = MaxMana
            };
    }
}


