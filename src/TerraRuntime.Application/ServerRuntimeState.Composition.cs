using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
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
        ServerPlayerAuthority? serverPlayers = null,
        RuntimeNpcShopCatalogRegistry? npcShops = null,
        RuntimeNpcArchetypeRegistry? npcArchetypes = null,
        RuntimeNpcArchetypeIdentityStore? npcArchetypeIdentities = null,
        IWorldItemSpawnRandom? worldItemSpawnRandom = null,
        bool expertMode = false,
        bool masterMode = false,
        bool skyblockLowTiles = false,
        bool isThereAWorldSurface = true,
        bool evilBossDownedBaseline = false)
    {
        if (masterMode && !expertMode)
            throw new ArgumentException("Master mode is a strict subset of Expert mode.", nameof(masterMode));

        _worldTiles = worldTiles;
        _worldClock = worldClock;
        _worldProgression = worldProgression ?? new RuntimeWorldProgressionMutations();
        _players = new PlayerAuthority(playerEvents, worldTiles);

        RuntimeWorldItemStore worldItemStore = worldItems ?? new RuntimeWorldItemStore();
        IWorldItemSpawnRandom spawnRandom = worldItemSpawnRandom ?? new SystemWorldItemSpawnRandom();
        _worldItems = new WorldItemAuthority(
            _players,
            worldItemStore,
            spawnRandom,
            worldItemReplication);
        _worldTileAuthority = new WorldTileAuthority(
            _players,
            worldTiles,
            worldItemStore,
            spawnRandom,
            tileManipulationReplication);

        _serverPlayers = serverPlayers;

        RuntimeNpcStore npcStore = npcs ?? new RuntimeNpcStore();
        RuntimeProjectileStore projectileStore = projectiles ?? new RuntimeProjectileStore();
        IProjectileStateStepper? configuredProjectileStepper = projectileStepper ??
            (worldTiles is null ? null : new VanillaProjectileWorldStateStepper(worldTiles, this));
        _projectiles = new ProjectileAuthority(
            projectileStore,
            _players,
            npcStore,
            this,
            configuredProjectileStepper,
            projectileReplication,
            worldClock?.GetGoodWorld ?? townCommerceWorldFacts?.GoodWorld ?? false);
        _npcs = new NpcAuthority(
            this,
            _players,
            npcStore,
            projectileStore,
            worldItemStore,
            spawnRandom,
            _worldItems.InstancedLeases,
            worldTiles,
            worldClock,
            _worldProgression,
            npcReplication,
            worldItemReplication,
            townNpcs,
            townSpawnWorldFacts,
            townCommerceWorldFacts,
            townCombatWorldFacts,
            townInitialRaining,
            townInitialEclipse,
            townInitialInvasionActive,
            serverPlayers,
            npcShops,
            npcArchetypes,
            npcArchetypeIdentities,
            npcAiStepper,
            expertMode,
            masterMode,
            skyblockLowTiles,
            isThereAWorldSurface,
            evilBossDownedBaseline);
    }
}
