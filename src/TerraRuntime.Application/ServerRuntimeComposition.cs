using TerraRuntime.Application.Bots;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// Immutable ownership graph for one authoritative server runtime.
/// <see cref="ServerRuntimeState"/> remains the single-writer facade/orchestrator, while construction and
/// cross-subsystem wiring live here instead of being mixed with mutable tick/command state.
/// </summary>
internal sealed class ServerRuntimeComposition
{
    private ServerRuntimeComposition(
        RuntimeTickCounter updates,
        RuntimeCommandCounter commands,
        RuntimePlayerSnapshotLookup playerSnapshots,
        PlayerAuthority players,
        ServerPlayerAuthority? serverPlayers,
        RuntimeBotAuthority? bots,
        NpcAuthority npcs,
        ProjectileAuthority projectiles,
        RuntimeProjectilePlayerCombatPass projectilePlayerCombat,
        RuntimeNpcPlayerCombatPass npcPlayerCombat,
        WorldItemAuthority worldItems,
        WorldTileAuthority worldTileAuthority,
        RuntimeTallGateOccupancyProbe? tallGateOccupancy,
        WorldTileStore? worldTiles,
        RuntimeWorldClock? worldClock,
        RuntimeWorldProgressionMutations worldProgression)
    {
        Updates = updates;
        Commands = commands;
        PlayerSnapshots = playerSnapshots;
        Players = players;
        ServerPlayers = serverPlayers;
        Bots = bots;
        Npcs = npcs;
        Projectiles = projectiles;
        ProjectilePlayerCombat = projectilePlayerCombat;
        NpcPlayerCombat = npcPlayerCombat;
        WorldItems = worldItems;
        WorldTileAuthority = worldTileAuthority;
        TallGateOccupancy = tallGateOccupancy;
        WorldTiles = worldTiles;
        WorldClock = worldClock;
        WorldProgression = worldProgression;
    }

    internal RuntimeTickCounter Updates { get; }

    internal RuntimeCommandCounter Commands { get; }

    internal RuntimePlayerSnapshotLookup PlayerSnapshots { get; }

    internal PlayerAuthority Players { get; }

    internal ServerPlayerAuthority? ServerPlayers { get; }

    internal RuntimeBotAuthority? Bots { get; }

    internal NpcAuthority Npcs { get; }

    internal ProjectileAuthority Projectiles { get; }

    internal RuntimeProjectilePlayerCombatPass ProjectilePlayerCombat { get; }

    internal RuntimeNpcPlayerCombatPass NpcPlayerCombat { get; }

    internal WorldItemAuthority WorldItems { get; }

    internal WorldTileAuthority WorldTileAuthority { get; }

    internal RuntimeTallGateOccupancyProbe? TallGateOccupancy { get; }

    internal WorldTileStore? WorldTiles { get; }

    internal RuntimeWorldClock? WorldClock { get; }

    internal RuntimeWorldProgressionMutations WorldProgression { get; }

    internal static ServerRuntimeComposition Create(
        IRuntimePlayerEventSink? playerEvents,
        RuntimeNpcStore? npcs,
        INpcAiStateStepper? npcAiStepper,
        WorldTileStore? worldTiles,
        RuntimeWorldClock? worldClock,
        RuntimeWorldProgressionMutations? worldProgression,
        RuntimeProjectileStore? projectiles,
        IProjectileStateStepper? projectileStepper,
        RuntimeWorldItemStore? worldItems,
        RuntimeProjectileReplicationRegistry? projectileReplication,
        RuntimeNpcReplicationRegistry? npcReplication,
        RuntimeWorldItemReplicationRegistry? worldItemReplication,
        RuntimeTownNpcStateStore? townNpcs,
        VanillaTownSpawnWorldFacts1458? townSpawnWorldFacts,
        RuntimeTownCommerceWorldFacts1458? townCommerceWorldFacts,
        RuntimeTownNpcCombatWorldFacts1458? townCombatWorldFacts,
        bool townInitialRaining,
        bool townInitialEclipse,
        bool townInitialInvasionActive,
        RuntimeTileManipulationReplicationRegistry? tileManipulationReplication,
        ServerPlayerAuthority? serverPlayers,
        RuntimeBotTelemetry? botTelemetry,
        float botSpawnX,
        float botSpawnY,
        RuntimeNpcShopCatalogRegistry? npcShops,
        RuntimeNpcArchetypeRegistry? npcArchetypes,
        RuntimeNpcArchetypeIdentityStore? npcArchetypeIdentities,
        IWorldItemSpawnRandom? worldItemSpawnRandom,
        bool expertMode,
        bool masterMode,
        bool skyblockLowTiles,
        bool isThereAWorldSurface,
        bool evilBossDownedBaseline,
        bool skeletronDownedBaseline,
        bool golemDownedBaseline)
    {
        if (masterMode && !expertMode)
            throw new ArgumentException("Master mode is a strict subset of Expert mode.", nameof(masterMode));

        var progression = worldProgression ?? new RuntimeWorldProgressionMutations();
        var updates = new RuntimeTickCounter();
        var commands = new RuntimeCommandCounter();
        var playersAuthority = new PlayerAuthority(playerEvents, worldTiles, expertMode, masterMode);
        var playerSnapshots = new RuntimePlayerSnapshotLookup(playersAuthority, serverPlayers);

        RuntimeWorldItemStore worldItemStore = worldItems ?? new RuntimeWorldItemStore();
        RuntimeNpcStore npcStore = npcs ?? new RuntimeNpcStore();
        RuntimeTallGateOccupancyProbe? tallGateOccupancy = worldTiles is null
            ? null
            : new RuntimeTallGateOccupancyProbe(playersAuthority, serverPlayers, npcStore);
        IWorldItemSpawnRandom spawnRandom = worldItemSpawnRandom ?? new SystemWorldItemSpawnRandom();
        var worldItemAuthority = new WorldItemAuthority(
            playersAuthority,
            worldItemStore,
            spawnRandom,
            worldItemReplication);
        var worldTileAuthority = new WorldTileAuthority(
            playersAuthority,
            commands,
            worldTiles,
            worldItemStore,
            npcStore,
            spawnRandom,
            progression,
            skeletronDownedBaseline,
            golemDownedBaseline,
            townCommerceWorldFacts?.HardMode ?? false,
            worldClock?.GetGoodWorld ?? townCommerceWorldFacts?.GoodWorld ?? false,
            tileManipulationReplication);

        RuntimeProjectileStore projectileStore = projectiles ?? new RuntimeProjectileStore();
        IProjectileStateStepper? configuredProjectileStepper = projectileStepper ??
            (worldTiles is null ? null : new VanillaProjectileWorldStateStepper(worldTiles, playerSnapshots, expertMode, npcStore));
        var projectileAuthority = new ProjectileAuthority(
            projectileStore,
            playersAuthority,
            npcStore,
            playerSnapshots,
            configuredProjectileStepper,
            projectileReplication,
            () => updates.Current,
            goodWorld: worldClock?.GetGoodWorld ?? townCommerceWorldFacts?.GoodWorld ?? false,
            worldTiles: worldTiles,
            expertMode: expertMode);
        var npcAuthority = new NpcAuthority(
            playerSnapshots,
            () => updates.Current,
            playersAuthority,
            npcStore,
            projectileStore,
            worldItemStore,
            spawnRandom,
            worldItemAuthority.InstancedLeases,
            worldTiles,
            worldClock,
            progression,
            npcReplication,
            worldItemReplication,
            tileManipulationReplication,
            tallGateOccupancy,
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
        RuntimeBotAuthority? botAuthority = serverPlayers is not null && botTelemetry is not null
            ? new RuntimeBotAuthority(
                serverPlayers,
                playersAuthority,
                playerSnapshots,
                npcAuthority,
                projectileAuthority,
                worldItemAuthority,
                botTelemetry,
                () => updates.Current,
                botSpawnX,
                botSpawnY)
            : null;
        var projectilePlayerCombat = new RuntimeProjectilePlayerCombatPass(
            projectileStore,
            npcStore,
            playersAuthority,
            () => updates.Current,
            cultistLightningArcTrails: projectileAuthority.CultistLightningArcTrails,
            serverPlayers: serverPlayers);
        var npcPlayerCombat = new RuntimeNpcPlayerCombatPass(npcStore, playersAuthority);

        return new ServerRuntimeComposition(
            updates,
            commands,
            playerSnapshots,
            playersAuthority,
            serverPlayers,
            botAuthority,
            npcAuthority,
            projectileAuthority,
            projectilePlayerCombat,
            npcPlayerCombat,
            worldItemAuthority,
            worldTileAuthority,
            tallGateOccupancy,
            worldTiles,
            worldClock,
            progression);
    }
}
