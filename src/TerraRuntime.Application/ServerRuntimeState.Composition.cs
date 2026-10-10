using TerraRuntime.Application.Bots;
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
    internal WorldRuntimeIdentity WorldIdentity { get; }

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
        RuntimeWorldInvasion1458? invasion = null,
        RuntimeTileManipulationReplicationRegistry? tileManipulationReplication = null,
        ServerPlayerAuthority? serverPlayers = null,
        RuntimeBotTelemetry? botTelemetry = null,
        float botSpawnX = 0f,
        float botSpawnY = 0f,
        RuntimeNpcShopCatalogRegistry? npcShops = null,
        RuntimeNpcArchetypeRegistry? npcArchetypes = null,
        RuntimeNpcArchetypeIdentityStore? npcArchetypeIdentities = null,
        IWorldItemSpawnRandom? worldItemSpawnRandom = null,
        bool expertMode = false,
        bool masterMode = false,
        bool skyblockLowTiles = false,
        bool isThereAWorldSurface = true,
        bool evilBossDownedBaseline = false,
        bool skeletronDownedBaseline = false,
        bool golemDownedBaseline = false,
        Random? projectilePlayerCombatRandom = null,
        IVanillaNpcRandom? naturalSpawnRandom = null,
        WorldRuntimeIdentity worldIdentity = default,
        RuntimeChestCommandProcessor? chestCommands = null,
        RuntimeNpcDeathPrelude1458? deathPrelude = null,
        RuntimeTownSocialWorld1458? townSocialWorldFacts = null,
        Action<RuntimeInvasionCapture1458>? invasionProgressPublisher = null,
        Action<RuntimeInvasionCapture1458>? invasionStartPublisher = null,
        PlayerUpdateRandomSeed1458? playerUpdateRandomSeed = null,
        IncomingHumanCombatPolicy1458 incomingHumanCombatPolicy = IncomingHumanCombatPolicy1458.PhaseOwned,
        VanillaItemPrefixWorld1458? itemPrefixWorld = null)
    {
        WorldIdentity = worldIdentity.IsAssigned ? worldIdentity : new(WorldRuntimeId.CreateNew(), WorldSessionId.CreateNew());
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
            invasion,
            tileManipulationReplication,
            serverPlayers,
            botTelemetry,
            botSpawnX,
            botSpawnY,
            npcShops,
            npcArchetypes,
            npcArchetypeIdentities,
            worldItemSpawnRandom,
            expertMode,
            masterMode,
            skyblockLowTiles,
            isThereAWorldSurface,
            evilBossDownedBaseline,
            skeletronDownedBaseline,
            golemDownedBaseline,
            projectilePlayerCombatRandom,
            naturalSpawnRandom,
            WorldIdentity,
            chestCommands, deathPrelude, townSocialWorldFacts, invasionProgressPublisher, invasionStartPublisher,
            playerUpdateRandomSeed, incomingHumanCombatPolicy, itemPrefixWorld);
    }
}
