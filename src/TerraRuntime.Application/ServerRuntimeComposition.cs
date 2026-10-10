using TerraRuntime.Application.Bots;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
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
        RuntimeWorldProgressionMutations worldProgression,
        RuntimeTownCommerceWorldFacts1458? environmentWorldFacts,
        bool expertMode,
        bool masterMode)
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
        EnvironmentWorldFacts = environmentWorldFacts;
        ExpertMode = expertMode;
        MasterMode = masterMode;
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
    internal RuntimeTownCommerceWorldFacts1458? EnvironmentWorldFacts { get; }
    internal bool ExpertMode { get; }
    internal bool MasterMode { get; }

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
        RuntimeWorldInvasion1458? invasion,
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
        bool golemDownedBaseline,
        Random? projectilePlayerCombatRandom,
        IVanillaNpcRandom? naturalSpawnRandom = null,
        WorldRuntimeIdentity worldIdentity = default,
        RuntimeChestCommandProcessor? chestCommands = null,
        RuntimeNpcDeathPrelude1458? deathPrelude = null,
        RuntimeTownSocialWorld1458? townSocialWorldFacts = null,
        Action<RuntimeInvasionCapture1458>? invasionProgressPublisher = null,
        Action<RuntimeInvasionCapture1458>? invasionStartPublisher = null,
        PlayerUpdateRandomSeed1458? playerUpdateRandomSeed = null,
        IncomingHumanCombatPolicy1458 incomingHumanCombatPolicy = IncomingHumanCombatPolicy1458.PhaseOwned)
    {
        if (masterMode && !expertMode)
            throw new ArgumentException("Master mode is a strict subset of Expert mode.", nameof(masterMode));

        var progression = worldProgression ?? new RuntimeWorldProgressionMutations();
        var updates = new RuntimeTickCounter();
        var commands = new RuntimeCommandCounter();
        // Human world mitigation consumes retained Player.Update fields. The explicit component
        // policy exists for trusted standalone adapters; missing phase/world facts never select it.
        var playersAuthority = new PlayerAuthority(playerEvents, worldTiles, expertMode, masterMode, serverPlayers,
            oceanTeleportSurface: townCommerceWorldFacts is { SkyblockWorld: false } oceanFacts ? oceanFacts.WorldSurface : null,
            chestCommands: chestCommands,
            lanternsUp: townCommerceWorldFacts?.LanternsUp,
            incomingHumanCombatPolicy: incomingHumanCombatPolicy);
        if (playerUpdateRandomSeed is { } playerSeed)
            playersAuthority.SetPlayerUpdateRandom(new VanillaUnifiedRandom1458(playerSeed.Value));
        if (worldTiles is not null && townCommerceWorldFacts is { } playerWorld)
        {
            var sourceWorld = new RuntimePlayerUpdateWorld1458(worldTiles.Dimensions.WidthTiles,
                worldTiles.Dimensions.HeightTiles, playerWorld.WorldSurface, playerWorld.RemixWorld, playerWorld.SkyblockWorld)
            { WindowsItemPrefixArithmetic = OperatingSystem.IsWindows() };
            if (sourceWorld.IsValid) playersAuthority.SetPlayerUpdateWorldFacts(in sourceWorld);
        }
        if (townCommerceWorldFacts is { } healthWorld)
            playersAuthority.SetNpcHealthWorldFacts(() => new PlayerNpcHealthWorld1458(
                expertMode || (worldClock?.GetGoodWorld ?? healthWorld.GoodWorld), healthWorld.VampireSeed));
        var playerSnapshots = new RuntimePlayerSnapshotLookup(playersAuthority, serverPlayers);

        RuntimeWorldItemStore worldItemStore = worldItems ?? new RuntimeWorldItemStore();
        RuntimeNpcStore npcStore = npcs ?? new RuntimeNpcStore();
        playersAuthority.SetZoneNpcOwner(npcStore);
        RuntimeTallGateOccupancyProbe? tallGateOccupancy = worldTiles is null
            ? null
            : new RuntimeTallGateOccupancyProbe(playersAuthority, serverPlayers, npcStore);
        // Main.rand is shared by these admitted callbacks. Explicit host adapters retain their own policy
        // and are outside the default coupled-stream contract.
        var gameplayRandom = (naturalSpawnRandom as TerraRuntime.Core.Npcs.SystemVanillaNpcRandom)?.SourceRandom
            ?? (worldItemSpawnRandom as SystemWorldItemSpawnRandom)?.SourceRandom
            ?? new VanillaUnifiedRandom1458(Environment.TickCount);
        // Admitted MessageBuffer.GetData(5)/Item.Prefix proposals use this existing Main.rand
        // owner. This does not recover unowned callers or the separate UpdatePlayers schedule.
        playersAuthority.BindReceiveEquipmentRandom(gameplayRandom, OperatingSystem.IsWindows());
        IVanillaNpcRandom npcRandom = naturalSpawnRandom ?? new TerraRuntime.Core.Npcs.SystemVanillaNpcRandom(gameplayRandom);
        IWorldItemSpawnRandom spawnRandom = worldItemSpawnRandom ?? new SystemWorldItemSpawnRandom(gameplayRandom);
        // Player.UpdateEquips uses Main.expertMode/masterMode, derived from Difficulty including Good World.
        float pickupDifficulty = (masterMode ? 3f : expertMode ? 2f : 1f) +
            ((worldClock?.GetGoodWorld ?? townCommerceWorldFacts?.GoodWorld ?? false) ? 1f : 0f);
        playersAuthority.SetCombatEquipmentWorldModes(pickupDifficulty >= 2f, pickupDifficulty >= 3f);
        serverPlayers?.SetCombatEquipmentWorldModes(pickupDifficulty >= 2f, pickupDifficulty >= 3f);
        // Standard dedicated Main.LocalPlayer is reserved slot255 with constructor-empty armor.
        // The shared PlayerSlotPool leases only0..254; client/server-player equipment cannot write it.
        playersAuthority.SetCombatEquipmentLocalVanityArmor(VanillaPlayerLocalVanityArmor1458.Empty);
        serverPlayers?.SetCombatEquipmentLocalVanityArmor(VanillaPlayerLocalVanityArmor1458.Empty);
        var worldItemAuthority = new WorldItemAuthority(
            playersAuthority,
            worldItemStore,
            spawnRandom,
            worldItemReplication,
            worldTiles,
            pickupDifficulty >= 2f,
            pickupDifficulty >= 3f,
            townCommerceWorldFacts is { } ownedCalendar ? () => new VanillaSeasonalItemDropContext1458(
                ownedCalendar.Halloween, ownedCalendar.XMas, ownedCalendar.TenthAnniversaryWorld) : null);
        worldItemStore.AttachOwnerFactsProvider(worldItemAuthority.SourceOwnerFacts);
        RuntimeProjectileStore projectileStore = projectiles ?? new RuntimeProjectileStore();
        playersAuthority.SetNpcHealthGrapplingFacts(player => PlayerAuthority.CaptureNpcHealthGrappling(projectileStore, player));
        if (townCommerceWorldFacts is { } ownedPlayerEnvironment)
            playersAuthority.SetRemotePlayerEnvironment(new PlayerNpcHealthWorld1458(
                expertMode || (worldClock?.GetGoodWorld ?? ownedPlayerEnvironment.GoodWorld),
                ownedPlayerEnvironment.VampireSeed), projectileStore);
        var projectileNpcLocalImmunity = new RuntimeProjectileNpcLocalImmunityRegistry(
            projectileStore.Capacity,
            npcStore.Capacity);
        IProjectileStateStepper? configuredProjectileStepper = projectileStepper ??
            (worldTiles is null
                ? null
                : new VanillaProjectileWorldStateStepper(
                    worldTiles,
                    playerSnapshots,
                    expertMode,
                    npcStore,
                    projectileNpcLocalImmunity,
                    () => updates.Current));
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
            expertMode: expertMode,
            projectileRandom: gameplayRandom,
            npcReplication: npcReplication);
        // Official 1.4.5.8 Program selects GameCulture.DefaultCulture (English).
        // Loot-name predicates follow that fixed server profile, independently of OS culture.
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
            invasion,
            serverPlayers,
            npcShops,
            npcArchetypes,
            npcArchetypeIdentities,
            npcAiStepper,
            expertMode,
            masterMode,
            skyblockLowTiles,
            isThereAWorldSurface,
            evilBossDownedBaseline,
            projectileNpcLocalImmunity,
            npcRandom,
            projectileReplication,
            lootRandom: gameplayRandom, deathPrelude: deathPrelude, townSocialWorldFacts: townSocialWorldFacts,
            townLootLanguage: VanillaTownNpcLootLanguage1458.English,
            invasionProgressPublisher: invasionProgressPublisher, invasionStartPublisher: invasionStartPublisher);
        var worldTileAuthority = new WorldTileAuthority(
            playersAuthority,
            commands,
            worldTiles,
            worldItemStore,
            npcStore,
            npcAuthority,
            spawnRandom,
            progression,
            skeletronDownedBaseline,
            golemDownedBaseline,
            townCommerceWorldFacts?.HardMode ?? false,
            worldClock?.GetGoodWorld ?? townCommerceWorldFacts?.GoodWorld ?? false,
            tileManipulationReplication);
        RuntimeBotAuthority? botAuthority = serverPlayers is not null && botTelemetry is not null && worldTiles is not null
            ? new RuntimeBotAuthority(
                serverPlayers,
                playersAuthority,
                playerSnapshots,
                npcAuthority,
                projectileAuthority,
                worldItemAuthority,
                worldTiles,
                worldTileAuthority,
                botTelemetry,
                () => updates.Current,
                botSpawnX,
                botSpawnY,
                world: worldIdentity)
            : null;
        var projectilePlayerCombat = new RuntimeProjectilePlayerCombatPass(
            projectileStore,
            npcStore,
            playersAuthority,
            () => updates.Current,
            random: projectilePlayerCombatRandom,
            cultistLightningArcTrails: projectileAuthority.CultistLightningArcTrails,
            serverPlayers: serverPlayers,
            expertMode: expertMode,
            masterMode: masterMode);
        var npcPlayerCombat = new RuntimeNpcPlayerCombatPass(
            npcStore,
            playersAuthority,
            serverPlayers: serverPlayers,
            expertMode: expertMode,
            masterMode: masterMode);

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
            progression,
            townCommerceWorldFacts,
            expertMode,
            masterMode);
    }

}
