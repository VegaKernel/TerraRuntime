using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

namespace TerraRuntime.Application;

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
        _runtime = ServerRuntimeComposition.Create(
            playerEvents,
            npcs,
            npcAiStepper,
            worldTiles,
            worldClock,
            worldProgression,
            projectiles,
            projectileStepper,
            worldItems,
            projectileReplication,
            npcReplication,
            worldItemReplication,
            townNpcs,
            townSpawnWorldFacts,
            townCommerceWorldFacts,
            townCombatWorldFacts,
            townInitialRaining,
            townInitialEclipse,
            townInitialInvasionActive,
            tileManipulationReplication,
            serverPlayers,
            npcShops,
            npcArchetypes,
            npcArchetypeIdentities,
            worldItemSpawnRandom,
            expertMode,
            masterMode,
            skyblockLowTiles,
            isThereAWorldSurface,
            evilBossDownedBaseline);
    }
}
