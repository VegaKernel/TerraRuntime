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
        RuntimeWorldProgressionMutations? worldProgression = null,
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
        _worldClock = worldClock;
        _worldProgression = worldProgression ?? new RuntimeWorldProgressionMutations();
        _expertMode = expertMode;
        _masterMode = masterMode;
        _worldItemSpawnRandom = worldItemSpawnRandom ?? new SystemWorldItemSpawnRandom();
        _worldItems = worldItems ?? new RuntimeWorldItemStore();
        _worldTileAuthority = new WorldTileAuthority(
            _players,
            worldTiles,
            _worldItems,
            _worldItemSpawnRandom,
            tileManipulationReplication);
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
        _townNpcAuthority = new TownNpcAuthority(
            _players,
            _npcs,
            projectileStore,
            worldTiles,
            _worldProgression,
            townNpcs,
            townSpawnWorldFacts,
            townCommerceWorldFacts,
            townCombatWorldFacts,
            npcReplication,
            townInitialRaining,
            townInitialEclipse,
            townInitialInvasionActive,
            expertMode,
            masterMode);
        _mysticFrogCatch = worldTiles is not null
            ? new RuntimeMysticFrogCatchService1458(_npcs, worldTiles, this)
            : null;
        _worldItemReplication = worldItemReplication;
        _instancedItemLeases = new RuntimeWorldItemInstancedLeaseStore(_worldItems);
        _npcCombat = new RuntimeNpcNetworkCombatPipeline(
            _npcs,
            _worldItems,
            this,
            _npcReplication,
            _instancedItemLeases,
            _worldItemReplication,
            _worldClock,
            _worldProgression,
            expertMode,
            masterMode);
        _townNpcAuthority.SetMeleeDamageSink(_npcCombat);

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
                    _worldClock,
                    progressionMutations: _worldProgression);
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
