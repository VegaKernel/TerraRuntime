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
internal sealed partial class NpcAuthority
{
    private readonly PlayerAuthority players;
    private readonly RuntimePlayerSnapshotLookup playerSnapshots;
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
    private readonly RuntimeNpcReplicationRegistry? npcReplication;
    private readonly RuntimeNpcLavaContactPass1458? lavaContact;
    private readonly RuntimeProjectileNpcCombatPass projectileNpcCombat;
    private readonly TownNpcAuthority townNpcAuthority;
    private readonly RuntimeMysticFrogCatchService1458? mysticFrogCatch;
    private readonly RuntimeWorldItemStore worldItems;
    private readonly IWorldItemSpawnRandom worldItemSpawnRandom;
    private readonly RuntimeWorldClock? worldClock;
    private readonly WorldTileStore? worldTiles;
    private readonly ServerPlayerAuthority? serverPlayers;
    private readonly IVanillaNpcRandom naturalSpawnRandom;
    private readonly RuntimeWorldProgressionMutations naturalSpawnProgression;
    private readonly VanillaTownSpawnWorldFacts1458? naturalTownSpawnFacts;
    private readonly NpcSnapshot[] naturalSpawnNpcBuffer = new NpcSnapshot[RuntimeNpcStore.MaximumAddressableCapacity];
    private readonly bool expertMode;
    private readonly bool masterMode;
    private readonly VanillaTownSceneMetricsScanner1458? npcSceneMetrics;
    private readonly RuntimeTownCommerceWorldFacts1458? naturalSpawnWorldFacts;
    private readonly RuntimeTownNpcStateStore? naturalSpawnTownNpcs;
    private readonly bool naturalSpawnSkyblockLowTiles;
    private readonly VanillaNpcTargetCandidate[] targetCandidates =
        new VanillaNpcTargetCandidate[VanillaNpcTargetingAiStepper.MaximumPlayerCandidates];
    private readonly PlayerStateSnapshot[] serverPlayerSnapshots =
        new PlayerStateSnapshot[VanillaNpcTargetingAiStepper.MaximumPlayerCandidates];

    /// <summary>
    /// Advances the NPC transport projection from the sole authoritative world-loop owner.
    /// </summary>
    public void AdvanceWorldTick()
    {
        npcs.UpdateProtectedSpawnSlots();
        npcReplication?.AdvanceAuthoritativeTick();
    }

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
        RuntimeTileManipulationReplicationRegistry? tileManipulationReplication,
        IVanillaTallGateOccupancyProbe? tallGateOccupancy,
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
        bool evilBossDownedBaseline,
        RuntimeProjectileNpcLocalImmunityRegistry? projectileNpcLocalImmunity = null,
        IVanillaNpcRandom? naturalSpawnRandom = null,
        RuntimeProjectileReplicationRegistry? projectileReplication = null)
    {
        ArgumentNullException.ThrowIfNull(playerSnapshots);
        this.playerSnapshots = playerSnapshots;
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
        npcs.SetVanillaSpawnContextSource(CaptureSpawnContext);
        this.naturalSpawnRandom = naturalSpawnRandom ?? new TerraRuntime.Core.Npcs.SystemVanillaNpcRandom();
        npcs.SetVanillaSpawnRandomSource(this.naturalSpawnRandom);
        naturalSpawnWorldFacts = townCommerceWorldFacts;
        naturalSpawnTownNpcs = townNpcs;
        naturalSpawnSkyblockLowTiles = skyblockLowTiles;
        if (worldTiles is not null && townCommerceWorldFacts is RuntimeTownCommerceWorldFacts1458 sceneWorldFacts)
        {
            npcSceneMetrics = new VanillaTownSceneMetricsScanner1458(worldTiles, in sceneWorldFacts);
        }
        ArgumentNullException.ThrowIfNull(progression);
        naturalSpawnProgression = progression;
        naturalTownSpawnFacts = townSpawnWorldFacts;
        this.npcReplication = npcReplication;

        aiExecutor = new RuntimeNpcAiStateExecutor(npcs, projectiles, npcReplication, npcReplication);
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
            projectiles,
            townCommerceWorldFacts?.DownedPlantera,
            projectileReplication,
            townCommerceWorldFacts?.ZenithWorld ?? false);
        projectileNpcCombat = new RuntimeProjectileNpcCombatPass(
            projectiles,
            npcs,
            combat,
            players,
            tickProvider,
            serverPlayers: serverPlayers,
            localNpcImmunity: projectileNpcLocalImmunity);
        townNpcAuthority.SetMeleeDamageSink(combat);
        if (worldTiles is not null && townCommerceWorldFacts is { RemixWorld: false, GoodWorld: false })
            lavaContact = new RuntimeNpcLavaContactPass1458(npcs, worldTiles, combat, tickProvider);

        if (npcAiStepper is null)
        {
            vanillaTargeting = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper(), random: this.naturalSpawnRandom);
            vanillaTargeting.SetPlayerInteractions(combat.Interactions);
            if (projectileReplication is not null)
                vanillaTargeting.SetProjectileAnchors(new RuntimeNpcProjectileAnchors(projectiles, projectileReplication.WireIdentities, players));
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
                if (worldTiles.WorldSurfaceTiles is double verifiedSurface)
                {
                    double rockLayer = townCommerceWorldFacts is RuntimeTownCommerceWorldFacts1458 facts &&
                        facts.RockLayer > verifiedSurface ? facts.RockLayer : double.PositiveInfinity;
                    vanillaTargeting.SetWorldBounds(worldTiles.Dimensions.WidthTiles, verifiedSurface, rockLayer);
                }
                vanillaTargeting.SetFlyingEyeEnvironment(new VanillaFlyingEyeWorldEnvironment(worldTiles));
                vanillaTargeting.SetQueenBeeEnvironment(new VanillaQueenBeeWorldEnvironment(
                    worldTiles,
                    worldSurfaceTiles,
                    townCommerceWorldFacts?.RemixWorld ?? false));
                vanillaTargeting.SetDeerclopsEnvironment(new VanillaDeerclopsWorldEnvironment(
                    worldTiles,
                    skyblockLowTiles));
                vanillaTargeting.SetWallOfFleshEnvironment(new VanillaWallOfFleshWorldEnvironment(worldTiles));
                vanillaTargeting.SetSkeletronEnvironment(new VanillaSkeletronWorldEnvironment(worldTiles));
                vanillaTargeting.SetCasterEnvironment(new VanillaCasterWorldEnvironment(worldTiles));
                vanillaTargeting.SetProjectileEnvironment(new VanillaNpcProjectileWorldEnvironment(worldTiles));
                var worldMotion = new VanillaNpcWorldMotionAiStepper(
                    actorIntent,
                    worldTiles,
                    worldSurfaceTiles,
                    worldClock,
                    doorOpeningSink: new RuntimeGroundFighterDoorOpeningSink(
                        worldTiles,
                        tileManipulationReplication,
                        tallGateOccupancy,
                        worldItems,
                        worldItemSpawnRandom),
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
                    CaptureDifficulty() >= 2f,
                    CaptureDifficulty() >= 3f,
                    worldClock.WindSpeedCurrent,
                    naturalSpawnWorldFacts?.RemixWorld ?? false,
                    worldClock.Time);
            }
        }

        TickNaturalHostileSpawning();
        LastAiTick = aiExecutor.Tick(aiStepper, combat);
        lavaContact?.Tick();
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

    public bool TryStrikeBotPlayerMelee(
        PlayerHandle attacker,
        NpcHandle target,
        int authoritativeDamage,
        int armorPenetration,
        bool critical,
        float knockBack,
        int hitDirection) =>
        combat.TryStrikeServerPlayerMelee(
            attacker,
            target,
            authoritativeDamage,
            armorPenetration,
            critical,
            knockBack,
            hitDirection) != RuntimeProjectileNpcDamageResult.Rejected;

    public int CopyActive(Span<NpcSnapshot> destination) => npcs.CopyActive(destination);

    /// <summary>
    /// Creates one runtime-owned hostile-NPC presentation for bot control. The supported surface is deliberately
    /// narrower than the vanilla NPC ID space: a definition and authoritative actor-control coverage must both exist,
    /// bosses and town NPCs are excluded, contact damage is disabled, and the presentation actor is invulnerable.
    /// Until bot-specific death/drop semantics exist, invulnerability prevents operator actors from becoming a loot or
    /// progression farm through the ordinary vanilla NPC death pipeline.
    /// </summary>
    internal bool TrySpawnBotNpc(
        NpcTypeId type,
        ActorControllerId controllerId,
        float positionX,
        float positionY,
        out NpcHandle npc)
    {
        npc = default;
        if (!type.IsAssigned || !controllerId.IsAssigned ||
            !float.IsFinite(positionX) || !float.IsFinite(positionY) ||
            !VanillaNpcDefinitionCatalog.TryGet(type, out VanillaNpcDefinition definition) ||
            definition.Role == NpcArchetypeRole.Town || definition.IsBoss || definition.Damage <= 0 ||
            !VanillaNpcActorControlSupport1458.IsSupported(type))
        {
            return false;
        }

        var state = new NpcStateUpdate(
            Type: type.Value,
            NetId: checked((short)type.Value),
            PositionX: positionX,
            PositionY: positionY,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: byte.MaxValue,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                TimeLeft = VanillaNpcDefinitionCatalog.NewNpcTimeLeft,
                DamageOverride = 0,
                DontTakeDamage = true
            });
        if (!npcs.TrySpawnVanilla(in state, out NpcSnapshot spawned))
            return false;

        if (actorControlOwner.Acquire(spawned.Handle, controllerId) != NpcActorAcquireStatus.Acquired)
        {
            _ = npcs.TryDespawn(spawned.Handle);
            return false;
        }

        AppliedSpawns++;
        npc = spawned.Handle;
        return true;
    }

    internal bool TrySetBotNpcIntent(NpcHandle npc, ActorControllerId controllerId, in NpcActorIntent intent) =>
        actorControlOwner.SetIntent(npc, controllerId, in intent);

    internal bool TryTeleportBotNpc(
        NpcHandle npc,
        ActorControllerId controllerId,
        float positionX,
        float positionY)
    {
        NpcActorIntent stop = NpcActorIntent.Stop();
        if (!float.IsFinite(positionX) || !float.IsFinite(positionY) ||
            !actorControlOwner.SetIntent(npc, controllerId, in stop) ||
            !npcs.TryGet(npc, out NpcSnapshot current))
        {
            return false;
        }

        var update = new NpcStateUpdate(
            current.Type,
            current.NetId,
            positionX,
            positionY,
            0f,
            0f,
            current.Target,
            current.Ai,
            current.Simulation with
            {
                OldPositionX = positionX,
                OldPositionY = positionY,
                OldVelocityX = 0f,
                OldVelocityY = 0f,
                DamageOverride = 0
            });
        if (!npcs.TryUpdate(npc, in update, out _))
            return false;

        AppliedUpdates++;
        return true;
    }

    internal bool TryDespawnBotNpc(NpcHandle npc, ActorControllerId controllerId)
    {
        if (!npc.IsAssigned || !controllerId.IsAssigned)
            return false;

        _ = actorControlOwner.Release(npc, controllerId);
        if (!npcs.TryDespawn(npc))
            return false;

        AppliedDespawns++;
        return true;
    }

    internal int CopyCombatIntegrityDiagnostics(Span<CombatIntegrityDiagnostic> destination) =>
        combat.CopyCombatIntegrityDiagnostics(destination);

    /// <summary>
    /// Applies the server-side <c>NPC.SpawnOnPlayer</c> boundary used by world-owned boss triggers such as Larva.
    /// Player selection remains slot ordered and uses the source's Manhattan distance from player position.
    /// </summary>
    internal bool TrySpawnBossFromTile(NpcTypeId type, int tileX, int tileY, float maximumDistance)
    {
        if (!VanillaNpcDefinitionCatalog.TryGet(type, out VanillaNpcDefinition definition) ||
            !definition.IsBoss ||
            !float.IsFinite(maximumDistance) ||
            maximumDistance <= 0f)
        {
            return false;
        }

        float sourceX = tileX * 16f;
        float sourceY = tileY * 16f;
        bool found = false;
        float closestDistance = 0f;
        VanillaNpcTargetCandidate closest = default;
        for (int slot = 0; slot < byte.MaxValue; slot++)
        {
            float positionX;
            float positionY;
            bool dead;
            if (players.TryGet(checked((byte)slot), out RuntimePlayerMember? player))
            {
                positionX = player.PositionX;
                positionY = player.PositionY;
                dead = player.IsDead;
            }
            else if (serverPlayers?.TryGet(new PlayerSlotId(checked((byte)slot)), out PlayerStateSnapshot serverPlayer) == true)
            {
                positionX = serverPlayer.PositionX;
                positionY = serverPlayer.PositionY;
                dead = serverPlayer.IsDead;
            }
            else
            {
                continue;
            }

            if (dead || !float.IsFinite(positionX) || !float.IsFinite(positionY))
                continue;

            float distance = MathF.Abs(positionX - sourceX) + MathF.Abs(positionY - sourceY);
            if (found && distance >= closestDistance)
                continue;

            found = true;
            closestDistance = distance;
            closest = new VanillaNpcTargetCandidate(
                Slot: checked((byte)slot),
                CenterX: positionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f,
                CenterY: positionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f,
                Aggro: 0,
                Active: true,
                Dead: false,
                Ghost: false,
                NoAggro: false);
        }

        if (!found || closestDistance >= maximumDistance ||
            !TryFindBossSpawnPosition(in closest, definition, out float x, out float y))
        {
            return false;
        }

        var update = new NpcStateUpdate(
            Type: type.Value,
            NetId: checked((short)type.Value),
            PositionX: x,
            PositionY: y,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: closest.Slot,
            Ai: default,
            Simulation: NpcSimulationState.Initial with { TimeLeft = VanillaNpcDefinitionCatalog.NewNpcTimeLeft });
        if (!npcs.TrySpawnVanilla(in update, out _))
        {
            RejectedSpawns++;
            return false;
        }

        AppliedSpawns++;
        return true;
    }

    private void ApplyClientBossSummon(ClientBossSummonRuntimeCommand command)
    {
        if (!command.Connection.IsAssigned || !players.IsCurrent(command.Connection))
            return;

        // MessageBuffer case 61 routes -16 to NPC.SpawnMechQueen before the ordinary MPAllowedEnemies
        // gate. The special-seed predicate lives with the loaded world facts, not with a client packet.
        if (command.NpcType == -16)
        {
            TrySpawnMechdusa(command.Connection.Player.Slot.Value);
            return;
        }

        if (!IsVanillaMultiplayerAllowedSummon(command.NpcType))
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

    /// <summary>
    /// The server half of NPC.SpawnMechQueen for packet-61's -16 action in TerrariaServer 1.4.5.8. It creates
    /// the Prime anchor through the usual SpawnOnPlayer placement, then creates the two Twins, Destroyer and
    /// two Probes at that anchor's center in source order. Mechdusa's coupled combat AI is intentionally owned
    /// by the individual boss steppers and remains outside this spawn boundary.
    /// </summary>
    private void TrySpawnMechdusa(byte playerSlot)
    {
        if (naturalSpawnWorldFacts is not { ZenithWorld: true } ||
            HasActiveMechanicalBossRoot() ||
            !TryGetPlayerTarget(playerSlot, out VanillaNpcTargetCandidate player) ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.SkeletronPrime, out VanillaNpcDefinition primeDefinition) ||
            !TryFindBossSpawnPosition(in player, primeDefinition, out float primeX, out float primeY))
        {
            return;
        }

        var primeState = new NpcStateUpdate(
            Type: VanillaNpcIds.SkeletronPrime.Value,
            NetId: checked((short)VanillaNpcIds.SkeletronPrime.Value),
            PositionX: primeX,
            PositionY: primeY,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: player.Slot,
            Ai: default,
            Simulation: NpcSimulationState.Initial with { TimeLeft = VanillaNpcDefinitionCatalog.NewNpcTimeLeft });
        // SpawnMechQueen assigns mechQueen = -2 before SpawnOnPlayer. SpawnBoss therefore uses Start=100,
        // stores the allocated Prime slot in ai[3], and multiplies only that SpawnBoss anchor's lifetime by 20.
        if (!npcs.TrySpawnVanilla(in primeState, out NpcSnapshot prime, startSlot: 100))
        {
            RejectedSpawns++;
            return;
        }

        AppliedSpawns++;
        var linkedPrime = new NpcStateUpdate(
            prime.Type,
            prime.NetId,
            prime.PositionX,
            prime.PositionY,
            prime.VelocityX,
            prime.VelocityY,
            prime.Target,
            prime.Ai with { Ai3 = prime.Handle.Slot },
            prime.Simulation with { TimeLeft = checked(prime.Simulation.TimeLeft * 20) });
        if (!npcs.TryUpdate(prime.Handle, in linkedPrime, out prime))
            return;

        if (!VanillaNpcDefinitionCatalog.TryGet(prime.TypeIdentity, prime.NetIdentity, out primeDefinition) ||
            !primeDefinition.TryResolveHitbox(prime.Simulation, out VanillaNpcHitboxSize hitbox))
        {
            return;
        }

        // NewNPC receives these as top-left coordinates. Center uses float halves, then source truncates.
        int x = (int)(prime.PositionX + hitbox.Width * 0.5f);
        int y = (int)(prime.PositionY + hitbox.Height * 0.5f);
        TrySpawnMechdusaPart(VanillaNpcIds.Retinazer, x, y, default, out _);
        TrySpawnMechdusaPart(VanillaNpcIds.Spazmatism, x, y, default, out _);
        if (!TrySpawnMechdusaPart(VanillaNpcIds.Destroyer, x, y, default, out NpcSnapshot destroyer))
            return;

        TrySpawnMechdusaPart(VanillaNpcIds.Probe, x, y,
            new NpcAiState(0f, 0f, destroyer.Handle.Slot, -1f), out _);
        TrySpawnMechdusaPart(VanillaNpcIds.Probe, x, y,
            new NpcAiState(0f, 0f, destroyer.Handle.Slot, 1f), out _);
    }

    private bool HasActiveMechanicalBossRoot()
    {
        int count = npcs.CopyActive(naturalSpawnNpcBuffer);
        for (int index = 0; index < count; index++)
        {
            NpcTypeId type = naturalSpawnNpcBuffer[index].TypeIdentity;
            if (type == VanillaNpcIds.SkeletronPrime || type == VanillaNpcIds.Destroyer ||
                type == VanillaNpcIds.Retinazer || type == VanillaNpcIds.Spazmatism)
            {
                return true;
            }
        }
        return false;
    }

    private bool TrySpawnMechdusaPart(NpcTypeId type, int x, int y, NpcAiState ai, out NpcSnapshot snapshot)
    {
        var state = new NpcStateUpdate(
            Type: type.Value,
            NetId: checked((short)type.Value),
            PositionX: x,
            PositionY: y,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: ai,
            Simulation: NpcSimulationState.Initial with { TimeLeft = VanillaNpcDefinitionCatalog.NewNpcTimeLeft });
        if (!npcs.TrySpawnVanilla(in state, out snapshot, startSlot: 1))
        {
            RejectedSpawns++;
            return false;
        }

        AppliedSpawns++;
        return true;
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
            float direction = naturalSpawnRandom.NextInt32(0, 2) == 0 ? -1f : 1f;
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

        // NPC.Spawner walks player slots in ascending order and stops after its first complete spawn
        // attempt. A rejected rate/floor check therefore permits the next player during the same tick;
        // a selected unsupported runtime type still consumes the source attempt and ends this pass.
        for (int candidateIndex = 0; candidateIndex < count; candidateIndex++)
        {
            VanillaNpcTargetCandidate player = targetCandidates[candidateIndex];
            if (!player.Active || player.Dead)
                continue;

            // The server refreshes Player.nearbyActiveNPCs before NPC.Spawner asks for its rate. Retain one
            // authoritative snapshot for both source checks so a spawn attempt cannot observe two different caps.
            float nearbyNpcCount = CountNearbyOrdinaryNpcs(in player);
            GetNaturalSpawnBudget(in player, nearbyNpcCount, out int spawnRate, out int maxSpawns);
            if (nearbyNpcCount >= maxSpawns || naturalSpawnRandom.NextInt32(0, spawnRate) != 0)
                continue;

            if (!TryFindVanillaNaturalSpawnFloor(in player, out int tileX, out int floorY))
                continue;

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
            return;
        }
    }

    private void GetNaturalSpawnBudget(
        in VanillaNpcTargetCandidate player,
        float nearbyNpcCount,
        out int spawnRate,
        out int maxSpawns)
    {
        // TerrariaServer 1.4.5.8 NPC.Spawner.GetSpawnRate defaults are 600 ticks / 5 NPC slots.  This
        // follows every branch whose world facts are server-owned in this runtime; per-player buffs,
        // candles and Journey slider input remain outside this authority until they have an authoritative
        // state projection.
        const int defaultSpawnRate = 600;
        const int defaultMaxSpawns = 5;
        spawnRate = defaultSpawnRate;
        maxSpawns = defaultMaxSpawns;

        bool hardMode = (naturalSpawnWorldFacts?.HardMode ?? false) ||
            naturalSpawnProgression.IsCompleted(VanillaWorldProgressionId.Hardmode);
        if (hardMode)
        {
            spawnRate = (int)(defaultSpawnRate * 0.9d);
            maxSpawns = defaultMaxSpawns + 1;
        }

        WorldTileStore tiles = worldTiles!;
        // Source compares Player.position rather than its centre.  Target candidates retain mount-aware
        // physical dimensions, so recover the same top-left coordinate here.
        double playerTileY = (player.CenterY - player.HitboxHeight * .5f) / 16d;
        double surface = tiles.WorldSurfaceTiles ?? naturalSpawnWorldFacts?.WorldSurface ?? tiles.Dimensions.HeightTiles / 3d;
        double rockLayer = naturalSpawnWorldFacts?.RockLayer ?? Math.Max(surface + 1d, tiles.Dimensions.HeightTiles * 0.45d);
        // NPC.sHeight is pinned to 1200 in 1.4.5.8; GetSpawnRate compares player.position.Y to
        // worldSurface/rockLayer plus one screen height. 75 tiles is the exact 1200 / 16 projection.
        const double sourceScreenHeightTiles = 75d;

        bool remixWorld = naturalSpawnWorldFacts?.RemixWorld == true;
        if (playerTileY > tiles.Dimensions.HeightTiles - 200d)
        {
            maxSpawns = (int)(maxSpawns * 2f);
        }
        else if (playerTileY > rockLayer + sourceScreenHeightTiles)
        {
            if (remixWorld)
            {
                spawnRate = (int)(spawnRate * (hardMode ? .45d : .5d));
                maxSpawns = (int)(maxSpawns * (hardMode ? 1.8f : 1.7f));
            }
            else
            {
                spawnRate = (int)(spawnRate * .4d);
                maxSpawns = (int)(maxSpawns * 1.9f);
            }
        }
        else if (playerTileY > surface + sourceScreenHeightTiles)
        {
            if (remixWorld)
            {
                spawnRate = (int)(spawnRate * .4d);
                maxSpawns = (int)(maxSpawns * 1.9f);
            }
            else
            {
                spawnRate = (int)(spawnRate * (hardMode ? .45d : .5d));
                maxSpawns = (int)(maxSpawns * (hardMode ? 1.8f : 1.7f));
            }
        }
        else if (remixWorld)
        {
            if (!worldClock!.DayTime)
            {
                spawnRate = (int)(spawnRate * .6d);
                maxSpawns = (int)(maxSpawns * 1.3f);
            }
        }
        else if (!worldClock!.DayTime)
        {
            spawnRate = (int)(spawnRate * .6d);
            maxSpawns = (int)(maxSpawns * 1.3f);
            if (worldClock.BloodMoonActive)
            {
                spawnRate = (int)(spawnRate * .3d);
                maxSpawns = (int)(maxSpawns * 1.8f);
            }
        }
        else if (naturalSpawnWorldFacts?.Eclipse == true)
        {
            spawnRate = (int)(spawnRate * .2d);
            maxSpawns = (int)(maxSpawns * 1.9f);
        }

        // The remix blood-moon adjustment runs after the depth branch.  Pumpkin/Snow Moon state is not
        // yet authoritative in WorldRuntime and therefore cannot be folded into this branch.
        if (remixWorld && !worldClock!.DayTime && worldClock.BloodMoonActive)
        {
            spawnRate = (int)(spawnRate * .3d);
            maxSpawns = (int)(maxSpawns * 1.8f);
            if (playerTileY > rockLayer + sourceScreenHeightTiles)
                spawnRate = (int)(spawnRate * .6d);
        }

        int playerTileX = Math.Clamp((int)(player.CenterX / 16f), 0, tiles.Dimensions.WidthTiles - 1);
        // NPC.Spawner.SetSpawnFlags derives pX/pY and all SceneMetrics zones from Player.Center.
        // GetSpawnRate's vertical depth checks below intentionally retain Player.position.
        int playerSceneTileY = Math.Clamp((int)(player.CenterY / 16f), 0, tiles.Dimensions.HeightTiles - 1);
        VanillaTownSceneMetrics1458? scene = npcSceneMetrics?.Scan(playerTileX, playerSceneTileY);
        // На выделенном сервере Main.Update присваивает cloudAlpha = maxRaining. GetSpawnRate
        // применяет этот Snow-поверхностный множитель до стен и остальных biome-модификаторов.
        if (scene is { ZoneSnow: true } && playerTileY < surface)
        {
            float cloudAlpha = worldClock!.MaxRain;
            maxSpawns = (int)(maxSpawns + maxSpawns * cloudAlpha);
            spawnRate = (int)(spawnRate * (1f - cloudAlpha + 1f) / 2f);
        }

        if (naturalSpawnWorldFacts?.DrunkWorld == true && tiles.Get(playerTileX, playerSceneTileY).Wall == 86)
        {
            spawnRate = (int)(spawnRate * .3d);
            maxSpawns = (int)(maxSpawns * 1.8f);
        }

        if (scene is VanillaTownSceneMetrics1458 biome)
        {
            if (biome.ZoneDungeon)
            {
                spawnRate = (int)(spawnRate * .3d);
                maxSpawns = (int)(maxSpawns * 1.8f);
            }
            else if (biome.ZoneDesert && naturalSpawnWorldFacts?.SandstormHappening == true &&
                (remixWorld
                    ? playerSceneTileY > rockLayer && playerSceneTileY < tiles.Dimensions.HeightTiles - 350
                    : playerSceneTileY <= surface))
            {
                // SceneMetrics.ZoneSandstorm = ZoneDesert && SurfaceAtmospherics && Sandstorm.Happening.
                // Preserve the source float multipliers before the later occupancy transforms.
                spawnRate = (int)(spawnRate * (hardMode ? .4f : .9f));
                maxSpawns = (int)(maxSpawns * (hardMode ? 1.5f : 1.2f));
            }
            else if (biome.ZoneDesert && playerSceneTileY > surface &&
                IsUndergroundDesertWall(tiles.Get(playerTileX, playerSceneTileY).Wall))
            {
                // SceneMetrics.ZoneUndergroundDesert also excludes Main.wallHouse; none of these source
                // conversion walls are housing walls in the supported wall catalog.
                spawnRate = (int)(spawnRate * .2f);
                maxSpawns = (int)(maxSpawns * 3f);
            }
            else if (biome.ZoneJungle)
            {
                // SceneMetrics.ScanNPCPositions counts active town NPC centers in the 3840x2400-pixel
                // TownNPCRectSize around Player.Center. The persisted town roster owns those runtime slots.
                switch (CountNearbyTownNpcs(player.CenterX, player.CenterY))
                {
                    case 0:
                        spawnRate = (int)(spawnRate * .4d);
                        maxSpawns = (int)(maxSpawns * 1.5f);
                        break;
                    case 1:
                        spawnRate = (int)(spawnRate * .55d);
                        maxSpawns = (int)(maxSpawns * 1.4d);
                        break;
                    case 2:
                        spawnRate = (int)(spawnRate * .7d);
                        maxSpawns = (int)(maxSpawns * 1.3f);
                        break;
                    default:
                        spawnRate = (int)(spawnRate * .85d);
                        maxSpawns = (int)(maxSpawns * 1.2f);
                        break;
                }
            }
            else if (biome.ZoneCorrupt || biome.ZoneCrimson)
            {
                spawnRate = (int)(spawnRate * .65d);
                maxSpawns = (int)(maxSpawns * 1.3f);
            }
            else if (biome.ZoneMeteor)
            {
                spawnRate = (int)(spawnRate * .4d);
                maxSpawns = (int)(maxSpawns * 1.1f);
            }

            if (biome.ZoneLihzhardTemple)
            {
                spawnRate = (int)(spawnRate * .8f);
                maxSpawns = (int)(maxSpawns * 1.2f);
                if (remixWorld)
                {
                    spawnRate = (int)(spawnRate * .4d);
                    maxSpawns = (int)(maxSpawns * 1.5f);
                }
            }

            if (remixWorld && (biome.ZoneCorrupt || biome.ZoneCrimson) && playerTileY < surface)
            {
                spawnRate = (int)(spawnRate * .5d);
                maxSpawns *= 2;
            }

            if (biome.ZoneHallow && playerTileY > rockLayer + sourceScreenHeightTiles)
            {
                spawnRate = (int)(spawnRate * .65d);
                maxSpawns = (int)(maxSpawns * 1.3f);
            }

            if (playerTileY > tiles.Dimensions.HeightTiles - 200d && IsWallOfFleshActive())
            {
                maxSpawns = (int)(maxSpawns * .3f);
                spawnRate *= 3;
            }
        }

        // GetSpawnRate applies these two occupancy bands after the biome/event rate transforms.
        if (nearbyNpcCount < maxSpawns * .2f)
            spawnRate = (int)(spawnRate * .6f);
        else if (nearbyNpcCount < maxSpawns * .4f)
            spawnRate = (int)(spawnRate * .7f);
        else if (nearbyNpcCount < maxSpawns * .6f)
            spawnRate = (int)(spawnRate * .8f);
        else if (nearbyNpcCount < maxSpawns * .8f)
            spawnRate = (int)(spawnRate * .9f);

        if (playerTileY > (surface + rockLayer) * .5d || scene is { ZoneCorrupt: true } or { ZoneCrimson: true })
        {
            if (nearbyNpcCount < maxSpawns * .2f)
                spawnRate = (int)(spawnRate * .7f);
            else if (nearbyNpcCount < maxSpawns * .4f)
                spawnRate = (int)(spawnRate * .9f);
        }

        if (remixWorld && playerTileY < surface && scene is { ZoneCorrupt: true } or { ZoneCrimson: true })
        {
            spawnRate = (int)(spawnRate * .8d);
            maxSpawns *= 2;
        }

        if (scene is { ZoneWaterCandle: true } candleScene)
        {
            if (!candleScene.ZonePeaceCandle)
            {
                spawnRate = (int)(spawnRate * .75d);
                maxSpawns = (int)(maxSpawns * 1.5f);
            }
            if (playerTileY < surface * .3499999940395355d)
                spawnRate = (int)(spawnRate * .5d);
        }
        else if (scene is { ZonePeaceCandle: true })
        {
            spawnRate = (int)(spawnRate * 1.3d);
            maxSpawns = (int)(maxSpawns * .7f);
        }

        if (IsNearFairy(player.CenterX, player.CenterY))
        {
            spawnRate = (int)(spawnRate * 1.2f);
            maxSpawns = (int)(maxSpawns * .8f);
        }

        spawnRate = Math.Max(defaultSpawnRate / 10, spawnRate);
        maxSpawns = Math.Min(defaultMaxSpawns * 3, maxSpawns);
        if (worldClock!.GetGoodWorld)
        {
            spawnRate = (int)(spawnRate * .8f);
            maxSpawns = (int)(maxSpawns * 1.2f);
        }

        if (naturalSpawnWorldFacts?.InvasionActive == true)
        {
            spawnRate = 20;
            maxSpawns = (int)(defaultMaxSpawns * (2d + .3d * CountActiveNaturalSpawnPlayers()));
        }

        if (scene is { ZoneDungeon: true } && naturalSpawnWorldFacts?.DownedBoss3 == false)
            spawnRate = 10;

        if (naturalSpawnSkyblockLowTiles)
            spawnRate /= 2;
    }

    private bool IsWallOfFleshActive()
    {
        int active = npcs.CopyActive(naturalSpawnNpcBuffer);
        for (int index = 0; index < active; index++)
            if (naturalSpawnNpcBuffer[index].TypeIdentity == VanillaNpcIds.WallOfFlesh)
                return true;
        return false;
    }

    private bool IsNearFairy(float playerCenterX, float playerCenterY)
    {
        // TerrariaServer 1.4.5.8 Player.isNearFairy uses NPC.sWidth (1920 px), independent of
        // the spawn-rate occupancy count. Fairies have fixed SetDefaults dimensions 18 by 20.
        const float fairyRange = 1920f;
        const float fairyRangeSquared = fairyRange * fairyRange;
        int active = npcs.CopyActive(naturalSpawnNpcBuffer);
        for (int index = 0; index < active; index++)
        {
            NpcSnapshot npc = naturalSpawnNpcBuffer[index];
            NpcTypeId type = npc.TypeIdentity;
            if (type != VanillaNpcIds.BlueFairy && type != VanillaNpcIds.GreenFairy && type != VanillaNpcIds.PinkFairy)
            {
                continue;
            }

            float dx = npc.PositionX + 9f - playerCenterX;
            float dy = npc.PositionY + 10f - playerCenterY;
            if (dx * dx + dy * dy < fairyRangeSquared)
                return true;
        }
        return false;
    }

    private int CountActiveNaturalSpawnPlayers()
    {
        int count = 0;
        for (int slot = 0; slot < byte.MaxValue; slot++)
            if (playerSnapshots.TryGetPlayer(new PlayerSlotId((byte)slot), out _)) count++;
        return count;
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
            int x = playerTileX + naturalSpawnRandom.NextInt32(-spawnRangeX, spawnRangeX + 1);
            int y = playerTileY + naturalSpawnRandom.NextInt32(-spawnRangeY, spawnRangeY + 1);
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

    private static bool IsUndergroundDesertWall(ushort wallType) => wallType is
        187 or 220 or 222 or 221 or 275 or 308 or 310 or 309 or
        216 or 217 or 219 or 218 or 304 or 305 or 307 or 306 or 223;

    private float CountNearbyOrdinaryNpcs(in VanillaNpcTargetCandidate player)
    {
        // NPC.CheckActive in TerrariaServer 1.4.5.8 adds an NPC to Player.nearbyActiveNPCs when the
        // player's physical body intersects the NPC-centered active rectangle. It is 2.1 NPC screens in
        // each direction (4032 by 2520 px for the fixed 1920 by 1200 spawn screen), not a radial probe.
        const int activeRangeX = 4032;
        const int activeRangeY = 2520;
        int count = npcs.CopyActive(naturalSpawnNpcBuffer);
        float nearby = 0f;
        int playerLeft = (int)(player.CenterX - player.HitboxWidth * .5f);
        int playerTop = (int)(player.CenterY - player.HitboxHeight * .5f);
        int playerRight = playerLeft + (int)player.HitboxWidth;
        int playerBottom = playerTop + (int)player.HitboxHeight;
        for (int i = 0; i < count; i++)
        {
            NpcSnapshot npc = naturalSpawnNpcBuffer[i];
            if (npc.Type is 25 or 30 or 33 ||
                !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, out VanillaNpcDefinition definition) ||
                definition.IsBoss ||
                !definition.TryResolveHitbox(npc.Simulation, out VanillaNpcHitboxSize hitbox) ||
                definition.LifeMax <= 0)
            {
                continue;
            }

            int npcLeft = (int)(npc.PositionX + hitbox.Width / 2f - activeRangeX);
            int npcTop = (int)(npc.PositionY + hitbox.Height / 2f - activeRangeY);
            int npcRight = npcLeft + activeRangeX * 2;
            int npcBottom = npcTop + activeRangeY * 2;
            if (npcLeft < playerRight && playerLeft < npcRight && npcTop < playerBottom && playerTop < npcBottom)
                nearby += GetNaturalSpawnSlots(npc.TypeIdentity);
        }
        return nearby;
    }

    private static float GetNaturalSpawnSlots(NpcTypeId type) => type switch
    {
        // TerrariaServer 1.4.5.8 NPC.SetDefaults source weights for the currently admitted natural types.
        // Other admitted base definitions retain the SetDefaults default of one slot.
        var value when value == VanillaNpcIds.FireImp => 3f,
        var value when value == VanillaNpcIds.BoneSerpentHead => 6f,
        var value when value == VanillaNpcIds.CaveBat || value == VanillaNpcIds.Hellbat || value == VanillaNpcIds.LavaBat => .5f,
        var value when value == VanillaNpcIds.Demon || value == VanillaNpcIds.VoodooDemon => 2f,
        _ => 1f
    };

    private int CountNearbyTownNpcs(float centerX, float centerY)
    {
        if (naturalSpawnTownNpcs is null)
            return 0;

        const float halfWidth = 1920f;
        const float halfHeight = 1200f;
        int count = 0;
        for (short slot = 0; slot < RuntimeTownNpcStateStore.MaximumTownNpcs; slot++)
        {
            if (!naturalSpawnTownNpcs.TryGet(slot, out _) ||
                !npcs.TryGetActive(checked((byte)slot), out NpcSnapshot npc) ||
                !VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, out VanillaNpcDefinition definition) ||
                !definition.TryResolveHitbox(npc.Simulation, out VanillaNpcHitboxSize hitbox))
            {
                continue;
            }

            float npcCenterX = npc.PositionX + hitbox.Width * .5f;
            float npcCenterY = npc.PositionY + hitbox.Height * .5f;
            if (npcCenterX >= centerX - halfWidth && npcCenterX < centerX + halfWidth &&
                npcCenterY >= centerY - halfHeight && npcCenterY < centerY + halfHeight)
            {
                count++;
            }
        }
        return count;
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
            int horizontal = naturalSpawnRandom.NextInt32(minimumHorizontalTiles, maximumHorizontalTiles + 1);
            if (naturalSpawnRandom.NextInt32(0, 2) == 0)
                horizontal = -horizontal;
            int x = playerTileX + horizontal;
            if (x < 10 || x >= width - 10)
                continue;
            int y = Math.Clamp(playerTileY + naturalSpawnRandom.NextInt32(-28, 29), 10, height - 12);
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
                    (tile.LiquidAmount != 0 && tile.LiquidKind == WorldLiquidKind.Lava))
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
        // NPC.Spawner.SetSpawnFlagsForChosenTile defines surfaceSpawn inclusively.
        bool surface = floorY <= surfaceThreshold;

        VanillaTownSceneMetrics1458? scene = npcSceneMetrics?.Scan(
            Math.Clamp((int)(player.CenterX / 16f), 0, tiles.Dimensions.WidthTiles - 1),
            Math.Clamp((int)(player.CenterY / 16f), 0, tiles.Dimensions.HeightTiles - 1));

        // Базовая ветка waterTile из SpawnAnNPC: две заполненные обычной водой клетки над
        // твёрдым spawnTileY. Биомные, океанские, событийные и Hardmode-цепочки остаются
        // закрытыми до появления всех необходимых source-фактов.
        if (IsOrdinaryPreHardmodeWaterSpawn(tiles, tileX, floorY, scene))
            return naturalSpawnRandom.NextInt32(0, 400) == 0 ? VanillaNpcIds.GoldGoldfish : VanillaNpcIds.Goldfish;

        if (VanillaUnderworldSpawn1458.IsUnderworld(floorY, tiles.Dimensions.HeightTiles))
        {
            // This is the ordinary dry underworld branch, not a substitute for the source's
            // earlier event/biome/secret-seed branches. Missing facts never become cave slimes.
            if (naturalSpawnWorldFacts is not RuntimeTownCommerceWorldFacts1458 facts ||
                naturalTownSpawnFacts is not VanillaTownSpawnWorldFacts1458 townFacts ||
                facts.RemixWorld || facts.InfectedSeed || facts.SkyblockWorld ||
                scene is not VanillaTownSceneMetrics1458 hellScene ||
                hellScene.ZoneJungle || hellScene.ZoneCorrupt || hellScene.ZoneCrimson || hellScene.ZoneDungeon ||
                hellScene.ZoneSnow || hellScene.ZoneHallow || hellScene.ZoneGlowshroom || hellScene.ZoneDesert)
                return default;

            int npcCount = npcs.CopyActive(naturalSpawnNpcBuffer);
            bool soulPresent = false;
            bool serpentPresent = false;
            for (int i = 0; i < npcCount; i++)
            {
                soulPresent |= naturalSpawnNpcBuffer[i].Type == 534;
                serpentPresent |= naturalSpawnNpcBuffer[i].Type == 39;
            }
            var spawnFacts = new VanillaUnderworldSpawnFacts1458(
                facts.HardMode || naturalSpawnProgression.IsCompleted(VanillaWorldProgressionId.Hardmode),
                facts.DownedMechBossAny || naturalSpawnProgression.IsCompleted(VanillaWorldProgressionId.AnyMechanicalBoss),
                townFacts.SavedTaxCollector || (naturalSpawnProgression.CaptureSnapshot().RescuedTownNpcs & RuntimeTownRescueFacts1458.TaxCollector) != 0,
                soulPresent, serpentPresent);
            return VanillaUnderworldSpawn1458.TrySelect(in spawnFacts, naturalSpawnRandom, out NpcTypeId underworldType)
                ? underworldType : default;
        }

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
            return naturalSpawnRandom.NextInt32(0, 3) == 0 ? VanillaNpcIds.DemonEye : VanillaNpcIds.Zombie;
        if (surface)
            return VanillaNpcIds.BlueSlime;

        // The current ordinary underground AI catalog is intentionally conservative. Skeleton has the
        // server-owned ground-fighter motion slice; unsupported cave families stay out instead of spawning
        // entities that cannot simulate authoritatively.
        return naturalSpawnRandom.NextInt32(0, 3) == 0 ? VanillaNpcIds.Skeleton : VanillaNpcIds.BlueSlime;
    }

    private bool IsOrdinaryPreHardmodeWaterSpawn(
        WorldTileStore tiles,
        int tileX,
        int spawnTileY,
        VanillaTownSceneMetrics1458? scene)
    {
        if (spawnTileY < 2 ||
            naturalSpawnWorldFacts is not RuntimeTownCommerceWorldFacts1458 facts ||
            facts.HardMode || facts.Eclipse || facts.RemixWorld || facts.InfectedSeed ||
            worldClock!.BloodMoonActive ||
            scene is not VanillaTownSceneMetrics1458 biome ||
            biome.ZoneJungle || biome.ZoneCorrupt || biome.ZoneCrimson || biome.ZoneDungeon ||
            biome.ZoneHallow || biome.ZoneSnow || biome.ZoneGlowshroom || biome.ZoneDesert)
            return false;

        WorldTile above = tiles.Get(tileX, spawnTileY - 1);
        WorldTile twoAbove = tiles.Get(tileX, spawnTileY - 2);
        return above.LiquidAmount != 0 && twoAbove.LiquidAmount != 0 &&
            above.LiquidKind == WorldLiquidKind.Water;
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

    private float CaptureDifficulty() => (masterMode ? 3f : expertMode ? 2f : 1f) +
        (worldClock?.GetGoodWorld == true ? 1f : 0f);

    private VanillaNpcSpawnContext CaptureSpawnContext()
    {
        int activePlayers = 0;
        // Match the physical player range, including dead actors. Snapshot ownership excludes disconnected actors.
        // Hardcore ghost state and Journey difficulty control still need their own authoritative projection.
        for (int slot = 0; slot < byte.MaxValue; slot++)
            if (playerSnapshots.TryGetPlayer(new PlayerSlotId((byte)slot), out _)) activePlayers++;
        bool skeletronActive = false;
        for (int slot = 0; slot < npcs.Capacity && slot < VanillaNpcSpawnRules.PhysicalSlotCount; slot++)
            if (npcs.TryGetActive((byte)slot, out var npc) && npc.TypeIdentity == VanillaNpcIds.SkeletronHead)
            {
                skeletronActive = true;
                break;
            }
        return new(CaptureDifficulty(), activePlayers, worldClock?.GetGoodWorld == true)
        {
            HardMode = (naturalSpawnWorldFacts?.HardMode ?? false) || naturalSpawnProgression.IsCompleted(VanillaWorldProgressionId.Hardmode),
            DownedPlantera = (naturalSpawnWorldFacts?.DownedPlantera ?? false) || naturalSpawnProgression.IsCompleted(VanillaWorldProgressionId.Plantera),
            SkeletronActive = skeletronActive,
            TenthAnniversaryWorld = naturalSpawnWorldFacts?.TenthAnniversaryWorld ?? false
        };
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
                (float memberWidth, float memberHeight) = VanillaPlayerMountHitbox1458.Resolve(player.MountType);
                destination[written++] = WithPlayerWorldFacts(new VanillaNpcTargetCandidate(
                    Slot: checked((byte)slot),
                    CenterX: player.PositionX + memberWidth * 0.5f,
                    CenterY: player.PositionY + memberHeight * 0.5f,
                    Aggro: 0,
                    Active: true,
                    Dead: player.IsDead,
                    Ghost: false,
                    NoAggro: false)
                { HitboxWidth = memberWidth, HitboxHeight = memberHeight }, includeBiomeZoneFacts);
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
            (float mountWidth, float mountHeight) = VanillaPlayerMountHitbox1458.Resolve(serverPlayer.MountType);
            destination[written++] = WithPlayerWorldFacts(new VanillaNpcTargetCandidate(
                Slot: checked((byte)slot),
                CenterX: serverPlayer.PositionX + mountWidth * 0.5f,
                CenterY: serverPlayer.PositionY + mountHeight * 0.5f,
                Aggro: 0,
                Active: true,
                Dead: serverPlayer.IsDead,
                Ghost: false,
                NoAggro: false)
            { HitboxWidth = mountWidth, HitboxHeight = mountHeight }, includeBiomeZoneFacts);
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

    private VanillaNpcTargetCandidate WithPlayerWorldFacts(
        VanillaNpcTargetCandidate candidate,
        bool includeBiomeZoneFacts)
    {
        candidate = WithBiomeZoneFacts(candidate, includeBiomeZoneFacts);
        if (worldTiles is null)
            return candidate;

        float playerX = candidate.CenterX - candidate.Width * 0.5f;
        float playerY = candidate.CenterY - candidate.Height * 0.5f;
        return candidate with
        {
            Wet = VanillaWorldCollision.TryGetWetContact(
                worldTiles,
                playerX,
                playerY,
                (int)candidate.Width,
                (int)candidate.Height,
                out _)
        };
    }
}
