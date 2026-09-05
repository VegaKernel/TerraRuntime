using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// Owns non-player NPC command application, AI execution, actor/archetype state, combat and town-NPC orchestration
/// for one live world. It is driven exclusively by the authoritative world loop.
/// </summary>
internal sealed class NpcAuthority
{
    private readonly PlayerAuthority players;
    private readonly RuntimeNpcStore npcs;
    private readonly RuntimeNpcAiStateExecutor aiExecutor;
    private readonly RuntimeNpcActorControlOwner actorControlOwner;
    private readonly RuntimeNpcArchetypeRegistry archetypes;
    private readonly RuntimeNpcArchetypeSpawner archetypeSpawner;
    private readonly RuntimeNpcShopCatalogRegistry shops;
    private readonly INpcAiStateStepper aiStepper;
    private readonly VanillaNpcTargetingAiStepper? vanillaTargeting;
    private readonly VanillaNpcCheckActiveAiStepper? vanillaCheckActive;
    private readonly RuntimeNpcNetworkCombatPipeline combat;
    private readonly RuntimeProjectileNpcCombatPass projectileNpcCombat;
    private readonly TownNpcAuthority townNpcAuthority;
    private readonly RuntimeMysticFrogCatchService1458? mysticFrogCatch;
    private readonly RuntimeWorldItemStore worldItems;
    private readonly IWorldItemSpawnRandom worldItemSpawnRandom;
    private readonly RuntimeWorldClock? worldClock;
    private readonly WorldTileStore? worldTiles;
    private readonly ServerPlayerAuthority? serverPlayers;
    private readonly Random naturalSpawnRandom = new();
    private readonly NpcSnapshot[] naturalSpawnNpcBuffer = new NpcSnapshot[RuntimeNpcStore.MaximumAddressableCapacity];
    private int naturalSpawnPlayerCursor;
    private readonly bool expertMode;
    private readonly bool masterMode;
    private readonly VanillaTownSceneMetricsScanner1458? npcSceneMetrics;
    private readonly RuntimeTownCommerceWorldFacts1458? naturalSpawnWorldFacts;
    private readonly VanillaNpcTargetCandidate[] targetCandidates =
        new VanillaNpcTargetCandidate[VanillaNpcTargetingAiStepper.MaximumPlayerCandidates];
    private readonly PlayerStateSnapshot[] serverPlayerSnapshots =
        new PlayerStateSnapshot[VanillaNpcTargetingAiStepper.MaximumPlayerCandidates];

    public NpcAuthority(
        RuntimePlayerSnapshotLookup playerSnapshots,
        Func<long> tickProvider,
        PlayerAuthority players,
        RuntimeNpcStore npcs,
        RuntimeProjectileStore projectiles,
        RuntimeWorldItemStore worldItems,
        IWorldItemSpawnRandom worldItemSpawnRandom,
        RuntimeWorldItemInstancedLeaseStore instancedItemLeases,
        WorldTileStore? worldTiles,
        RuntimeWorldClock? worldClock,
        RuntimeWorldProgressionMutations progression,
        RuntimeNpcReplicationRegistry? npcReplication,
        RuntimeWorldItemReplicationRegistry? worldItemReplication,
        RuntimeTownNpcStateStore? townNpcs,
        VanillaTownSpawnWorldFacts1458? townSpawnWorldFacts,
        RuntimeTownCommerceWorldFacts1458? townCommerceWorldFacts,
        RuntimeTownNpcCombatWorldFacts1458? townCombatWorldFacts,
        bool townInitialRaining,
        bool townInitialEclipse,
        bool townInitialInvasionActive,
        ServerPlayerAuthority? serverPlayers,
        RuntimeNpcShopCatalogRegistry? npcShops,
        RuntimeNpcArchetypeRegistry? npcArchetypes,
        RuntimeNpcArchetypeIdentityStore? npcArchetypeIdentities,
        INpcAiStateStepper? npcAiStepper,
        bool expertMode,
        bool masterMode,
        bool skyblockLowTiles,
        bool isThereAWorldSurface,
        bool evilBossDownedBaseline)
    {
        ArgumentNullException.ThrowIfNull(playerSnapshots);
        ArgumentNullException.ThrowIfNull(tickProvider);
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        ArgumentNullException.ThrowIfNull(projectiles);
        this.worldItems = worldItems ?? throw new ArgumentNullException(nameof(worldItems));
        this.worldItemSpawnRandom = worldItemSpawnRandom ?? throw new ArgumentNullException(nameof(worldItemSpawnRandom));
        ArgumentNullException.ThrowIfNull(instancedItemLeases);
        this.worldClock = worldClock;
        this.worldTiles = worldTiles;
        this.serverPlayers = serverPlayers;
        this.expertMode = expertMode;
        this.masterMode = masterMode;
        if (worldTiles is not null && townCommerceWorldFacts is RuntimeTownCommerceWorldFacts1458 sceneWorldFacts)
        {
            naturalSpawnWorldFacts = sceneWorldFacts;
            npcSceneMetrics = new VanillaTownSceneMetricsScanner1458(worldTiles, in sceneWorldFacts);
        }
        ArgumentNullException.ThrowIfNull(progression);

        aiExecutor = new RuntimeNpcAiStateExecutor(npcs, projectiles);
        var actorControls = new RuntimeNpcActorControlRegistry(npcs);
        archetypes = npcArchetypes ?? new RuntimeNpcArchetypeRegistry();
        RuntimeNpcArchetypeIdentityStore archetypeIdentities =
            npcArchetypeIdentities ?? new RuntimeNpcArchetypeIdentityStore(npcs.Capacity);
        var presentationBehaviors = new RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper>();
        var archetypeBehaviors = new RuntimeArchetypeBehaviorRegistry<INpcAiStateStepper>();
        var behaviorQueries = new RuntimeNpcBehaviorQueries(tickProvider, playerSnapshots, npcs, worldTiles);
        actorControlOwner = new RuntimeNpcActorControlOwner(
            npcs,
            actorControls,
            presentationBehaviors,
            archetypeBehaviors,
            behaviorQueries,
            archetypes,
            archetypeIdentities);
        archetypeSpawner = new RuntimeNpcArchetypeSpawner(npcs, archetypes, archetypeIdentities);
        shops = npcShops ?? new RuntimeNpcShopCatalogRegistry();

        townNpcAuthority = new TownNpcAuthority(
            players,
            npcs,
            projectiles,
            worldTiles,
            progression,
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
        mysticFrogCatch = worldTiles is not null
            ? new RuntimeMysticFrogCatchService1458(npcs, worldTiles, playerSnapshots)
            : null;
        combat = new RuntimeNpcNetworkCombatPipeline(
            npcs,
            worldItems,
            playerSnapshots,
            players,
            tickProvider,
            npcReplication,
            instancedItemLeases,
            worldItemReplication,
            worldClock,
            progression,
            expertMode,
            masterMode,
            worldTiles,
            townCommerceWorldFacts?.Crimson ?? false,
            skyblockLowTiles,
            isThereAWorldSurface,
            evilBossDownedBaseline,
            projectiles);
        projectileNpcCombat = new RuntimeProjectileNpcCombatPass(
            projectiles, npcs, combat, players, tickProvider);
        townNpcAuthority.SetMeleeDamageSink(combat);

        if (npcAiStepper is null)
        {
            vanillaTargeting = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
            vanillaTargeting.SetPlayerInteractions(combat.Interactions);
            var behaviorDispatch = new RuntimeNpcBehaviorStateStepper(
                vanillaTargeting,
                presentationBehaviors,
                archetypeBehaviors: archetypeBehaviors,
                archetypes: archetypes,
                identities: archetypeIdentities);
            var actorIntent = new RuntimeNpcActorIntentStateStepper(
                behaviorDispatch,
                actorControls,
                playerSnapshots);
            if (worldTiles is null)
            {
                aiStepper = actorIntent;
            }
            else
            {
                double worldSurfaceTiles = worldTiles.WorldSurfaceTiles ??
                    Math.Max(1d, worldTiles.Dimensions.HeightTiles / 3d);
                vanillaTargeting.EnableBlueSlimeMotion(worldSurfaceTiles);
                vanillaTargeting.EnableZombieMotion(worldSurfaceTiles);
                vanillaTargeting.SetFlyingEyeEnvironment(new VanillaFlyingEyeWorldEnvironment(worldTiles));
                vanillaTargeting.SetQueenBeeEnvironment(new VanillaQueenBeeWorldEnvironment(
                    worldTiles,
                    worldSurfaceTiles,
                    townCommerceWorldFacts?.RemixWorld ?? false));
                vanillaTargeting.SetDeerclopsEnvironment(new VanillaDeerclopsWorldEnvironment(
                    worldTiles,
                    skyblockLowTiles));
                vanillaTargeting.SetWallOfFleshEnvironment(new VanillaWallOfFleshWorldEnvironment(worldTiles));
                vanillaTargeting.SetProjectileEnvironment(new VanillaNpcProjectileWorldEnvironment(worldTiles));
                var worldMotion = new VanillaNpcWorldMotionAiStepper(
                    actorIntent,
                    worldTiles,
                    worldSurfaceTiles,
                    worldClock,
                    progressionMutations: progression);
                vanillaCheckActive = new VanillaNpcCheckActiveAiStepper(worldMotion);
                aiStepper = vanillaCheckActive;
            }
        }
        else
        {
            aiStepper = new RuntimeNpcBehaviorStateStepper(
                npcAiStepper,
                presentationBehaviors,
                archetypeBehaviors: archetypeBehaviors,
                archetypes: archetypes,
                identities: archetypeIdentities);
        }
    }

    public RuntimeNpcShopCatalogRegistry Shops => shops;
    public RuntimeNpcArchetypeRegistry Archetypes => archetypes;
    public int Capacity => npcs.Capacity;

    public long AppliedSpawns { get; private set; }
    public long RejectedSpawns { get; private set; }
    public long AppliedUpdates { get; private set; }
    public long RejectedUpdates { get; private set; }
    public long AppliedDespawns { get; private set; }
    public long RejectedDespawns { get; private set; }
    public long AppliedClientDamage { get; private set; }
    public long RejectedClientDamage { get; private set; }
    public NpcAiStateTickSummary LastAiTick { get; private set; }

    public bool TryApply(RuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (actorControlOwner.TryApply(command))
            return true;
        if (command is NpcActorSpawnRuntimeCommand actorSpawn)
        {
            ApplyActorSpawn(actorSpawn);
            return true;
        }

        switch (command)
        {
            case NpcSpawnRuntimeCommand spawn:
                ApplySpawn(spawn);
                return true;
            case NpcUpdateRuntimeCommand update:
                ApplyUpdate(update);
                return true;
            case NpcDespawnRuntimeCommand despawn:
                ApplyDespawn(despawn);
                return true;
            case ClientNpcDamageRuntimeCommand damage:
                ApplyClientDamage(damage);
                return true;
            case ClientNpcHomeRuntimeCommand home:
                TerrariaNpcHomeState homeState = home.State;
                townNpcAuthority.ApplyHome(home.Connection, in homeState);
                return true;
            case ClientNpcTalkRuntimeCommand talk:
                townNpcAuthority.ApplyTalk(talk.Connection, talk.State.NpcSlot, worldClock);
                return true;
            case ClientNpcCatchRuntimeCommand npcCatch:
                ApplyClientCatch(npcCatch);
                return true;
            case ClientBossSummonRuntimeCommand bossSummon:
                ApplyClientBossSummon(bossSummon);
                return true;
            default:
                return false;
        }
    }

    public void CommitPending()
    {
        archetypes.CommitPending();
        shops.CommitPending();
        actorControlOwner.CommitPending();
    }

    public void TickSimulation()
    {
        if (vanillaTargeting is not null)
        {
            int candidateCount = CopyTargetCandidates(targetCandidates);
            ReadOnlySpan<VanillaNpcTargetCandidate> candidates = targetCandidates.AsSpan(0, candidateCount);
            vanillaTargeting.SetCandidates(candidates);
            vanillaCheckActive?.SetCandidates(candidates);
            if (worldClock is not null)
            {
                vanillaTargeting.SetWorldConditions(
                    worldClock.DayTime,
                    worldClock.SlimeRainActive,
                    worldClock.GetGoodWorld,
                    expertMode,
                    masterMode);
            }
        }

        TickNaturalHostileSpawning();
        LastAiTick = aiExecutor.Tick(aiStepper, combat);
        townNpcAuthority.TickShimmer();
        townNpcAuthority.TickLifecycle(worldClock);
        AppliedDespawns += npcs.DespawnExpired();
    }

    public void TickProjectileInteractions(ReadOnlySpan<RuntimeProjectileExplosionEvent> explosions)
    {
        projectileNpcCombat.Tick();
        projectileNpcCombat.TickExplosions(explosions);
        townNpcAuthority.TickProjectileInteractions();
    }

    public bool TryCapture(NpcHandle npc, out NpcSnapshot snapshot) => npcs.TryGet(npc, out snapshot);

    public int CopyActive(Span<NpcSnapshot> destination) => npcs.CopyActive(destination);

    internal int CopyCombatIntegrityDiagnostics(Span<CombatIntegrityDiagnostic> destination) =>
        combat.CopyCombatIntegrityDiagnostics(destination);

    private void ApplyClientBossSummon(ClientBossSummonRuntimeCommand command)
    {
        if (!command.Connection.IsAssigned || !players.IsCurrent(command.Connection) ||
            !IsVanillaMultiplayerAllowedSummon(command.NpcType))
            return;

        var type = new NpcTypeId(command.NpcType);
        // Packet 61 is gated by NPCID.Sets.MPAllowedEnemies in vanilla, not by the IsBoss flag.
        // The allow-list also contains Skeletron Prime limbs, so rejecting non-boss definitions here
        // diverges from the source and can break otherwise valid summon flows.
        if (!VanillaNpcDefinitionCatalog.TryGet(type, out VanillaNpcDefinition definition))
            return;

        int activeCount = npcs.CopyActive(naturalSpawnNpcBuffer);
        for (int i = 0; i < activeCount; i++)
        {
            if (naturalSpawnNpcBuffer[i].TypeIdentity == type)
                return;
        }

        if (!TryGetPlayerTarget(command.Connection.Player.Slot.Value, out VanillaNpcTargetCandidate player) ||
            !TryFindBossSpawnPosition(player, definition, out float x, out float y))
            return;

        var update = new NpcStateUpdate(
            Type: type.Value,
            NetId: checked((short)type.Value),
            PositionX: x,
            PositionY: y,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: command.Connection.Player.Slot.Value,
            Ai: default,
            Simulation: NpcSimulationState.Initial with { TimeLeft = VanillaNpcDefinitionCatalog.NewNpcTimeLeft });
        if (npcs.TrySpawnVanilla(in update, out _))
            AppliedSpawns++;
        else
            RejectedSpawns++;
    }

    private static bool IsVanillaMultiplayerAllowedSummon(short npcType) => npcType is
        4 or 13 or 50 or 125 or 126 or 127 or 128 or 129 or 130 or 131 or 134 or 222 or 245 or 266 or 370 or 657 or 668;

    private bool TryGetPlayerTarget(byte slot, out VanillaNpcTargetCandidate target)
    {
        int count = CopyTargetCandidates(targetCandidates);
        for (int i = 0; i < count; i++)
        {
            VanillaNpcTargetCandidate candidate = targetCandidates[i];
            if (candidate.Slot == slot && candidate.Active && !candidate.Dead && !candidate.Ghost)
            {
                target = candidate;
                return true;
            }
        }
        target = default;
        return false;
    }

    private bool TryFindBossSpawnPosition(
        in VanillaNpcTargetCandidate player,
        in VanillaNpcDefinition definition,
        out float x,
        out float y)
    {
        // Vanilla SpawnOnPlayer searches outside the player's safe rectangle. Bosses with no-tile-collide
        // can safely materialize above/aside the player; grounded bosses use the same bounded floor scan as
        // natural hostile spawns.
        if (definition.NoTileCollideAtSpawn || worldTiles is null)
        {
            float direction = naturalSpawnRandom.Next(2) == 0 ? -1f : 1f;
            x = player.CenterX + direction * 720f - definition.Width * 0.5f;
            y = player.CenterY - 360f - definition.Height * 0.5f;
            return float.IsFinite(x) && float.IsFinite(y);
        }

        if (TryFindNaturalSpawnFloor(in player, minimumHorizontalTiles: 36, maximumHorizontalTiles: 62, out int tileX, out int floorY))
        {
            x = tileX * 16f + 8f - definition.Width * 0.5f;
            y = floorY * 16f - definition.Height;
            return true;
        }

        x = y = 0f;
        return false;
    }

    private void TickNaturalHostileSpawning()
    {
        if (worldTiles is null || worldClock is null || npcs.ActiveCount >= Math.Min(npcs.Capacity - 8, 180))
            return;

        int count = CopyTargetCandidates(targetCandidates);
        if (count == 0)
            return;

        // One candidate maximum per authoritative tick keeps spawn work bounded regardless of player count.
        int start = naturalSpawnPlayerCursor++ % count;
        VanillaNpcTargetCandidate player = targetCandidates[start];
        if (!player.Active || player.Dead || player.Ghost)
            return;

        GetNaturalSpawnBudget(in player, out int spawnRate, out int maxSpawns);
        if (naturalSpawnRandom.Next(spawnRate) != 0 ||
            CountNearbyOrdinaryNpcs(player.CenterX, player.CenterY, 1600f) >= maxSpawns)
        {
            return;
        }

        if (!TryFindVanillaNaturalSpawnFloor(in player, out int tileX, out int floorY))
            return;

        NpcTypeId type = SelectNaturalHostileType(in player, tileX, floorY);
        if (!VanillaNpcDefinitionCatalog.TryGet(type, out VanillaNpcDefinition definition) || definition.IsBoss ||
            !VanillaNpcAiCoverageCatalog.TryGet(type, out _))
        {
            return;
        }

        float spawnX = tileX * 16f + 8f - definition.Width * 0.5f;
        float spawnY = floorY * 16f - definition.Height;
        var update = new NpcStateUpdate(
            Type: type.Value,
            NetId: checked((short)type.Value),
            PositionX: spawnX,
            PositionY: spawnY,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: player.Slot,
            Ai: default,
            Simulation: NpcSimulationState.Initial with { TimeLeft = VanillaNpcDefinitionCatalog.NewNpcTimeLeft });
        if (npcs.TrySpawnVanilla(in update, out _))
            AppliedSpawns++;
    }

    private void GetNaturalSpawnBudget(
        in VanillaNpcTargetCandidate player,
        out int spawnRate,
        out int maxSpawns)
    {
        // TerrariaServer 1.4.5.8 NPC.Spawner defaults are 600 ticks / 5 NPC slots.  The runtime
        // intentionally imports the vertical/day-night modifiers that can be evaluated from server-owned
        // world state without pretending that unsupported player buffs/candles/town suppression are known.
        const int defaultSpawnRate = 600;
        const int defaultMaxSpawns = 5;
        spawnRate = defaultSpawnRate;
        maxSpawns = defaultMaxSpawns;

        bool hardMode = naturalSpawnWorldFacts?.HardMode ?? false;
        if (hardMode)
        {
            spawnRate = (int)(defaultSpawnRate * 0.9d);
            maxSpawns = defaultMaxSpawns + 1;
        }

        WorldTileStore tiles = worldTiles!;
        double playerTileY = player.CenterY / 16d;
        double surface = tiles.WorldSurfaceTiles ?? naturalSpawnWorldFacts?.WorldSurface ?? tiles.Dimensions.HeightTiles / 3d;
        double rockLayer = naturalSpawnWorldFacts?.RockLayer ?? Math.Max(surface + 1d, tiles.Dimensions.HeightTiles * 0.45d);
        // NPC.sHeight is pinned to 1200 in 1.4.5.8; GetSpawnRate compares player.position.Y to
        // worldSurface/rockLayer plus one screen height. 75 tiles is the exact 1200 / 16 projection.
        const double sourceScreenHeightTiles = 75d;

        if (playerTileY > tiles.Dimensions.HeightTiles - 200d)
        {
            maxSpawns = (int)(maxSpawns * 2f);
        }
        else if (playerTileY > rockLayer + sourceScreenHeightTiles)
        {
            spawnRate = (int)(spawnRate * 0.4d);
            maxSpawns = (int)(maxSpawns * 1.9f);
        }
        else if (playerTileY > surface + sourceScreenHeightTiles)
        {
            spawnRate = (int)(spawnRate * (hardMode ? 0.45d : 0.5d));
            maxSpawns = (int)(maxSpawns * (hardMode ? 1.8f : 1.7f));
        }
        else if (!worldClock!.DayTime)
        {
            spawnRate = (int)(spawnRate * 0.6d);
            maxSpawns = (int)(maxSpawns * 1.3f);
            if (worldClock.BloodMoonActive)
            {
                spawnRate = (int)(spawnRate * 0.3d);
                maxSpawns = (int)(maxSpawns * 1.8f);
            }
        }

        spawnRate = Math.Max(defaultSpawnRate / 10, spawnRate);
        maxSpawns = Math.Clamp(maxSpawns, 1, defaultMaxSpawns * 3);
    }

    private bool TryFindVanillaNaturalSpawnFloor(
        in VanillaNpcTargetCandidate player,
        out int tileX,
        out int floorY)
    {
        WorldTileStore tiles = worldTiles!;
        int playerTileX = (int)(player.CenterX / 16f);
        int playerTileY = (int)(player.CenterY / 16f);
        int width = tiles.Dimensions.WidthTiles;
        int height = tiles.Dimensions.HeightTiles;

        // TerrariaServer 1.4.5.8 NPC.GetSpawnArea uses the fixed 1920x1200 NPC spawn screen:
        // spawn range = 70% (84x52 tiles), safe range = 52% (62x39 tiles), then tries 50 samples.
        const int spawnRangeX = 84;
        const int spawnRangeY = 52;
        const int safeRangeX = 62;
        const int safeRangeY = 39;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            int x = playerTileX + naturalSpawnRandom.Next(-spawnRangeX, spawnRangeX + 1);
            int y = playerTileY + naturalSpawnRandom.Next(-spawnRangeY, spawnRangeY + 1);
            if (x < 10 || x >= width - 10 || y < 10 || y >= height - 12)
                continue;

            WorldTile start = tiles.Get(x, y);
            if ((start.IsActive && !start.IsActuated && VanillaTileCollisionCatalog.IsSolid(start.TileType)) ||
                IsHouseWall(start.Wall))
            {
                continue;
            }

            int bottom = Math.Min(height - 6, playerTileY + spawnRangeY);
            for (; y <= bottom; y++)
            {
                WorldTile floor = tiles.Get(x, y);
                if (!floor.IsActive || floor.IsActuated ||
                    (!VanillaTileCollisionCatalog.IsSolid(floor.TileType) &&
                     !VanillaTileCollisionCatalog.IsSolidTop(floor.TileType)))
                {
                    continue;
                }

                if (Math.Abs(x - playerTileX) < safeRangeX && Math.Abs(y - playerTileY) < safeRangeY)
                    break;
                if (!HasNpcSpawnClearance(tiles, x, y))
                    break;

                tileX = x;
                floorY = y;
                return true;
            }
        }

        tileX = floorY = 0;
        return false;
    }

    private static bool IsHouseWall(ushort wallType)
    {
        // This is deliberately conservative for the current natural-spawn slice: the common player-placed
        // housing walls suppress hostile spawn selection. Full WallID.Sets.HousingWalls parity remains a
        // separate source-backed catalog rather than trusting arbitrary client wall state.
        return wallType is 1 or 4 or 5 or 6 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 17 or 18 or 19 or 20 or
            21 or 22 or 23 or 24 or 27 or 29 or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37 or 38 or 39;
    }

    private int CountNearbyOrdinaryNpcs(float x, float y, float radius)
    {
        int count = npcs.CopyActive(naturalSpawnNpcBuffer);
        float radiusSq = radius * radius;
        int nearby = 0;
        for (int i = 0; i < count; i++)
        {
            NpcSnapshot npc = naturalSpawnNpcBuffer[i];
            if (!VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, out VanillaNpcDefinition definition) || definition.IsBoss)
                continue;
            float dx = npc.PositionX - x;
            float dy = npc.PositionY - y;
            if (dx * dx + dy * dy <= radiusSq)
                nearby++;
        }
        return nearby;
    }

    private bool TryFindNaturalSpawnFloor(
        in VanillaNpcTargetCandidate player,
        int minimumHorizontalTiles,
        int maximumHorizontalTiles,
        out int tileX,
        out int floorY)
    {
        WorldTileStore tiles = worldTiles!;
        int playerTileX = (int)(player.CenterX / 16f);
        int playerTileY = (int)(player.CenterY / 16f);
        int width = tiles.Dimensions.WidthTiles;
        int height = tiles.Dimensions.HeightTiles;

        for (int attempt = 0; attempt < 24; attempt++)
        {
            int horizontal = naturalSpawnRandom.Next(minimumHorizontalTiles, maximumHorizontalTiles + 1);
            if (naturalSpawnRandom.Next(2) == 0)
                horizontal = -horizontal;
            int x = playerTileX + horizontal;
            if (x < 10 || x >= width - 10)
                continue;
            int y = Math.Clamp(playerTileY + naturalSpawnRandom.Next(-28, 29), 10, height - 12);
            int bottom = Math.Min(height - 6, y + 48);
            for (; y <= bottom; y++)
            {
                WorldTile floor = tiles.Get(x, y);
                if (!floor.IsActive || floor.IsActuated ||
                    (!VanillaTileCollisionCatalog.IsSolid(floor.TileType) && !VanillaTileCollisionCatalog.IsSolidTop(floor.TileType)))
                    continue;
                if (!HasNpcSpawnClearance(tiles, x, y))
                    break;
                tileX = x;
                floorY = y;
                return true;
            }
        }
        tileX = floorY = 0;
        return false;
    }

    private static bool HasNpcSpawnClearance(WorldTileStore tiles, int x, int floorY)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = 1; dy <= 4; dy++)
            {
                WorldTile tile = tiles.Get(x + dx, floorY - dy);
                if ((tile.IsActive && !tile.IsActuated && VanillaTileCollisionCatalog.IsSolid(tile.TileType)) ||
                    tile.LiquidAmount > 160)
                    return false;
            }
        }
        return true;
    }

    private NpcTypeId SelectNaturalHostileType(
        in VanillaNpcTargetCandidate player,
        int tileX,
        int floorY)
    {
        WorldTileStore tiles = worldTiles!;
        double surfaceThreshold = Math.Clamp(
            tiles.WorldSurfaceTiles ?? naturalSpawnWorldFacts?.WorldSurface ?? tiles.Dimensions.HeightTiles / 3d,
            1d,
            tiles.Dimensions.HeightTiles - 1d);
        bool surface = floorY < surfaceThreshold;

        VanillaTownSceneMetrics1458? scene = npcSceneMetrics?.Scan(
            Math.Clamp((int)(player.CenterX / 16f), 0, tiles.Dimensions.WidthTiles - 1),
            Math.Clamp((int)(player.CenterY / 16f), 0, tiles.Dimensions.HeightTiles - 1));

        if (scene is VanillaTownSceneMetrics1458 biome)
        {
            if (biome.ZoneJungle && !surface)
                return VanillaNpcIds.Hornet;
            if (biome.ZoneCorrupt)
                return VanillaNpcIds.EaterOfSouls;
            if (biome.ZoneCrimson)
                return VanillaNpcIds.Crimera;
            if (biome.ZoneDesert && surface && worldClock!.DayTime)
                return VanillaNpcIds.Vulture;
            if (biome.ZoneDungeon && !surface)
                return VanillaNpcIds.Skeleton;
        }

        if (surface && !worldClock!.DayTime)
            return naturalSpawnRandom.Next(3) == 0 ? VanillaNpcIds.DemonEye : VanillaNpcIds.Zombie;
        if (surface)
            return VanillaNpcIds.BlueSlime;

        // The current ordinary underground AI catalog is intentionally conservative. Skeleton has the
        // server-owned ground-fighter motion slice; unsupported cave families stay out instead of spawning
        // entities that cannot simulate authoritatively.
        return naturalSpawnRandom.Next(3) == 0 ? VanillaNpcIds.Skeleton : VanillaNpcIds.BlueSlime;
    }

    private void ApplySpawn(NpcSpawnRuntimeCommand command)
    {
        NpcStateUpdate state = command.State;
        if (npcs.TrySpawn(command.Slot, in state, out NpcSnapshot snapshot))
        {
            AppliedSpawns++;
            command.Completion?.TrySetResult(snapshot);
            return;
        }

        RejectedSpawns++;
        command.Completion?.TrySetResult(null);
    }

    private void ApplyActorSpawn(NpcActorSpawnRuntimeCommand command)
    {
        NpcActorSpawnRequest request = command.Request;
        if (!request.IsValid)
        {
            command.Completion.TrySetResult(new NpcActorSpawnResult(NpcActorSpawnStatus.InvalidRequest, default));
            return;
        }

        archetypes.CommitPending();
        if (!archetypes.Snapshot.TryGet(request.ArchetypeId, out _))
        {
            command.Completion.TrySetResult(new NpcActorSpawnResult(NpcActorSpawnStatus.ArchetypeNotFound, default));
            return;
        }

        var spawn = new NpcArchetypeAllocateRequest(request.ArchetypeId, request.PositionX, request.PositionY);
        if (!archetypeSpawner.TrySpawnAllocated(in spawn, out NpcSnapshot snapshot))
        {
            command.Completion.TrySetResult(new NpcActorSpawnResult(NpcActorSpawnStatus.NoAvailableSlot, default));
            return;
        }

        AppliedSpawns++;
        command.Completion.TrySetResult(new NpcActorSpawnResult(NpcActorSpawnStatus.Spawned, snapshot.Handle));
    }

    private void ApplyUpdate(NpcUpdateRuntimeCommand command)
    {
        NpcStateUpdate state = command.State;
        if (npcs.TryUpdate(command.Npc, in state, out _))
        {
            AppliedUpdates++;
            return;
        }

        RejectedUpdates++;
    }

    private void ApplyDespawn(NpcDespawnRuntimeCommand command)
    {
        if (npcs.TryDespawn(command.Npc))
        {
            AppliedDespawns++;
            command.Completion?.TrySetResult(true);
            return;
        }

        RejectedDespawns++;
        command.Completion?.TrySetResult(false);
    }

    private void ApplyClientDamage(ClientNpcDamageRuntimeCommand command)
    {
        TerrariaNpcDamageState damageState = command.State;
        if (!players.IsCurrent(command.Connection))
        {
            RejectedClientDamage++;
            return;
        }

        RuntimeNpcNetworkDamageResult result = combat.TryApply(command.Connection, in damageState);
        if (result == RuntimeNpcNetworkDamageResult.Rejected)
            RejectedClientDamage++;
        else
            AppliedClientDamage++;
    }

    private void ApplyClientCatch(ClientNpcCatchRuntimeCommand command)
    {
        if (!players.IsCurrent(command.Connection) ||
            !TerrariaNpcCatchCodec.IsValidNpcSlot(command.State.NpcSlot) ||
            !players.TryGet(command.Connection, out RuntimePlayerMember? player) ||
            !npcs.TryGetActive(checked((byte)command.State.NpcSlot), out NpcSnapshot npc) ||
            !NpcTypeId.TryCreate(npc.Type, out NpcTypeId npcType) ||
            !VanillaNpcCatchCatalog1458.TryGetCatchItem(npcType, out ItemTypeId catchItem))
        {
            return;
        }

        if (VanillaNpcCatchCatalog1458.IsMysticFrog(npcType))
        {
            mysticFrogCatch?.TryApply(npc.Handle, out _);
            return;
        }

        if (npc.Simulation.SpawnedFromStatue)
        {
            npcs.TryDespawn(npc.Handle);
            return;
        }

        float playerCenterX = player.PositionX + PlayerAuthority.VanillaBasePlayerWidth / 2f;
        float playerCenterY = player.PositionY + PlayerAuthority.VanillaBasePlayerHeight / 2f;
        WorldItemDropStateUpdate drop = VanillaNpcCatchWorldItem1458.Create(
            playerCenterX,
            playerCenterY,
            catchItem,
            worldItemSpawnRandom);
        if (!worldItems.TryReserveDrop(in drop, out WorldItemDropReservation reservation))
            return;
        if (!npcs.TryDespawn(npc.Handle))
        {
            worldItems.TryReleaseDropReservation(in reservation);
            return;
        }
        if (!worldItems.TryCommitReservedDrop(in reservation, out WorldItemSnapshot item))
            throw new InvalidOperationException("Reserved NPC catch item failed after authoritative NPC despawn.");

        var owner = new WorldItemOwnerStateUpdate(
            OwnerPlayerId: command.Connection.Player.Slot.Value,
            TimeToKeepReservation: VanillaNpcCatchWorldItem1458.ReservationTicks,
            GrabDelayPlayer: byte.MaxValue,
            GrabDelayTime: 0,
            PositionX: item.PositionX,
            PositionY: item.PositionY);
        if (!worldItems.TryApplyOwner(item.Handle.Slot, in owner, out _))
            throw new InvalidOperationException("Caught NPC item could not be reserved for the authenticated player.");
    }

    private int CopyTargetCandidates(Span<VanillaNpcTargetCandidate> destination)
    {
        int serverPlayerCount = serverPlayers?.CopySnapshots(serverPlayerSnapshots) ?? 0;
        int serverPlayerIndex = 0;
        int written = 0;
        bool includeBiomeZoneFacts = npcSceneMetrics is not null && HasActiveBrainOfCthulhu();

        for (int slot = 0; slot < VanillaNpcTargetingAiStepper.MaximumPlayerCandidates; slot++)
        {
            if (players.TryGet(checked((byte)slot), out RuntimePlayerMember? player))
            {
                if (player.MountType != 0)
                    continue;

                destination[written++] = WithBiomeZoneFacts(new VanillaNpcTargetCandidate(
                    Slot: checked((byte)slot),
                    CenterX: player.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f,
                    CenterY: player.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f,
                    Aggro: 0,
                    Active: true,
                    Dead: player.IsDead,
                    Ghost: false,
                    NoAggro: false), includeBiomeZoneFacts);
                continue;
            }

            while (serverPlayerIndex < serverPlayerCount &&
                   serverPlayerSnapshots[serverPlayerIndex].Player.Slot.Value < slot)
            {
                serverPlayerIndex++;
            }

            if (serverPlayerIndex >= serverPlayerCount ||
                serverPlayerSnapshots[serverPlayerIndex].Player.Slot.Value != slot)
            {
                continue;
            }

            PlayerStateSnapshot serverPlayer = serverPlayerSnapshots[serverPlayerIndex++];
            if (serverPlayer.MountType != 0)
                continue;

            destination[written++] = WithBiomeZoneFacts(new VanillaNpcTargetCandidate(
                Slot: checked((byte)slot),
                CenterX: serverPlayer.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f,
                CenterY: serverPlayer.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f,
                Aggro: 0,
                Active: true,
                Dead: serverPlayer.IsDead,
                Ghost: false,
                NoAggro: false), includeBiomeZoneFacts);
        }

        return written;
    }

    private bool HasActiveBrainOfCthulhu()
    {
        for (int slot = 0; slot < npcs.Capacity && slot <= byte.MaxValue; slot++)
        {
            if (npcs.TryGetActive(checked((byte)slot), out NpcSnapshot npc) &&
                npc.TypeIdentity == VanillaNpcIds.BrainOfCthulhu)
            {
                return true;
            }
        }

        return false;
    }

    private VanillaNpcTargetCandidate WithBiomeZoneFacts(
        VanillaNpcTargetCandidate candidate,
        bool includeBiomeZoneFacts)
    {
        if (!includeBiomeZoneFacts || npcSceneMetrics is null ||
            !float.IsFinite(candidate.CenterX) || !float.IsFinite(candidate.CenterY))
        {
            return candidate;
        }

        VanillaTownSceneMetrics1458 scene = npcSceneMetrics.Scan(
            (int)(candidate.CenterX / 16f),
            (int)(candidate.CenterY / 16f));
        return candidate with
        {
            HasBiomeZoneFacts = true,
            ZoneCrimson = scene.ZoneCrimson
        };
    }
}
