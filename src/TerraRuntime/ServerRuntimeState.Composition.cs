using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

namespace TerraRuntime;

internal sealed partial class ServerRuntimeState
{
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
        RuntimeTownNpcCombatWorldFacts1458? townCombatWorldFacts = null,
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
        IWorldItemSpawnRandom? worldItemSpawnRandom = null,
        bool expertMode = false,
        bool masterMode = false)
    {
        _worldTiles = worldTiles;
        _players = new PlayerAuthority(playerEvents, worldTiles);
        _tileMutations = worldTiles is null ? null : new VanillaWorldTileMutationService(worldTiles);
        _worldClock = worldClock;
        _expertMode = expertMode;
        _masterMode = masterMode;
        _worldItemSpawnRandom = worldItemSpawnRandom ?? new SystemWorldItemSpawnRandom();
        if (masterMode && !expertMode)
            throw new ArgumentException("Master mode is a strict subset of Expert mode.", nameof(masterMode));
        _npcs = npcs ?? new RuntimeNpcStore();
        RuntimeProjectileStore projectileStore = projectiles ?? new RuntimeProjectileStore();
        _npcAiExecutor = new RuntimeNpcAiStateExecutor(_npcs, projectileStore);
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
        _npcArchetypes = npcArchetypes ?? new RuntimeNpcArchetypeRegistry();
        _npcArchetypeIdentities = npcArchetypeIdentities ?? new RuntimeNpcArchetypeIdentityStore(_npcs.Capacity);
        _npcPresentationBehaviors = new RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper>();
        _npcArchetypeBehaviors = new RuntimeArchetypeBehaviorRegistry<INpcAiStateStepper>();
        _npcBehaviorQueries = new RuntimeNpcBehaviorQueries(this, _npcs, _worldTiles);
        _npcActorCommands = new RuntimeNpcActorControlCommandService(
            _npcs,
            _npcActorControls,
            _npcPresentationBehaviors,
            _npcArchetypeBehaviors,
            _npcBehaviorQueries,
            _npcArchetypes,
            _npcArchetypeIdentities);
        _npcArchetypeSpawner = new RuntimeNpcArchetypeSpawner(_npcs, _npcArchetypes, _npcArchetypeIdentities);
        _npcShops = npcShops ?? new RuntimeNpcShopCatalogRegistry();
        IProjectileStateStepper? configuredProjectileStepper = projectileStepper ??
            (worldTiles is null ? null : new VanillaProjectileWorldStateStepper(worldTiles, this));
        _projectiles = new ProjectileAuthority(
            projectileStore,
            _players,
            _npcs,
            this,
            configuredProjectileStepper,
            projectileReplication);
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
                _npcs, projectileStore, townNpcs, _townRescue, _worldProgression, townSpawnWorldFacts?.InfectedSeed ?? false)
            : null;
        _townCommerce = worldTiles is not null && townCommerceWorldFacts is RuntimeTownCommerceWorldFacts1458 commerceFacts
            ? new RuntimeTownCommerceResolver1458(worldTiles, townNpcs, _npcs, in commerceFacts)
            : null;
        _townCombat = worldTiles is not null &&
            townNpcs is not null &&
            _worldProgression is not null &&
            townCombatWorldFacts is RuntimeTownNpcCombatWorldFacts1458 combatFacts
                ? new RuntimeTownNpcCombat1458(
                    townNpcs, _npcs, projectileStore, worldTiles, in combatFacts, _worldProgression, expertMode, masterMode)
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
            _townShimmer = new RuntimeTownNpcShimmerService1458(_npcs, townNpcs, worldTiles, npcReplication);
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
        _townCombat?.SetMeleeDamageSink(_npcCombat);

        if (npcAiStepper is null)
        {
            _vanillaNpcTargetingAiStepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
            var behaviorDispatch = new RuntimeNpcBehaviorStateStepper(
                _vanillaNpcTargetingAiStepper,
                _npcPresentationBehaviors,
                archetypeBehaviors: _npcArchetypeBehaviors,
                archetypes: _npcArchetypes,
                identities: _npcArchetypeIdentities);
            var actorIntent = new RuntimeNpcActorIntentStateStepper(
                behaviorDispatch,
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
                _vanillaNpcTargetingAiStepper.SetQueenBeeEnvironment(new VanillaQueenBeeWorldEnvironment(
                    worldTiles,
                    worldSurfaceTiles,
                    townCommerceWorldFacts?.RemixWorld ?? false));
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
            _npcAiStepper = new RuntimeNpcBehaviorStateStepper(
                npcAiStepper,
                _npcPresentationBehaviors,
                archetypeBehaviors: _npcArchetypeBehaviors,
                archetypes: _npcArchetypes,
                identities: _npcArchetypeIdentities);
        }
    }
}
