using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime;

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
    private readonly TownNpcAuthority townNpcAuthority;
    private readonly RuntimeMysticFrogCatchService1458? mysticFrogCatch;
    private readonly RuntimeWorldItemStore worldItems;
    private readonly IWorldItemSpawnRandom worldItemSpawnRandom;
    private readonly RuntimeWorldClock? worldClock;
    private readonly ServerPlayerAuthority? serverPlayers;
    private readonly bool expertMode;
    private readonly bool masterMode;
    private readonly VanillaNpcTargetCandidate[] targetCandidates =
        new VanillaNpcTargetCandidate[VanillaNpcTargetingAiStepper.MaximumPlayerCandidates];
    private readonly PlayerStateSnapshot[] serverPlayerSnapshots =
        new PlayerStateSnapshot[VanillaNpcTargetingAiStepper.MaximumPlayerCandidates];

    public NpcAuthority(
        ServerRuntimeState runtime,
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
        bool masterMode)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        ArgumentNullException.ThrowIfNull(projectiles);
        this.worldItems = worldItems ?? throw new ArgumentNullException(nameof(worldItems));
        this.worldItemSpawnRandom = worldItemSpawnRandom ?? throw new ArgumentNullException(nameof(worldItemSpawnRandom));
        ArgumentNullException.ThrowIfNull(instancedItemLeases);
        this.worldClock = worldClock;
        this.serverPlayers = serverPlayers;
        this.expertMode = expertMode;
        this.masterMode = masterMode;
        ArgumentNullException.ThrowIfNull(progression);

        aiExecutor = new RuntimeNpcAiStateExecutor(npcs, projectiles);
        var actorControls = new RuntimeNpcActorControlRegistry(npcs);
        archetypes = npcArchetypes ?? new RuntimeNpcArchetypeRegistry();
        RuntimeNpcArchetypeIdentityStore archetypeIdentities =
            npcArchetypeIdentities ?? new RuntimeNpcArchetypeIdentityStore(npcs.Capacity);
        var presentationBehaviors = new RuntimeGameplayBehaviorRegistry<NpcTypeId, INpcAiStateStepper>();
        var archetypeBehaviors = new RuntimeArchetypeBehaviorRegistry<INpcAiStateStepper>();
        var behaviorQueries = new RuntimeNpcBehaviorQueries(runtime, npcs, worldTiles);
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
            ? new RuntimeMysticFrogCatchService1458(npcs, worldTiles, runtime)
            : null;
        combat = new RuntimeNpcNetworkCombatPipeline(
            npcs,
            worldItems,
            runtime,
            npcReplication,
            instancedItemLeases,
            worldItemReplication,
            worldClock,
            progression,
            expertMode,
            masterMode,
            worldTiles,
            townCommerceWorldFacts?.Crimson ?? false);
        townNpcAuthority.SetMeleeDamageSink(combat);

        if (npcAiStepper is null)
        {
            vanillaTargeting = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
            var behaviorDispatch = new RuntimeNpcBehaviorStateStepper(
                vanillaTargeting,
                presentationBehaviors,
                archetypeBehaviors: archetypeBehaviors,
                archetypes: archetypes,
                identities: archetypeIdentities);
            var actorIntent = new RuntimeNpcActorIntentStateStepper(
                behaviorDispatch,
                actorControls,
                runtime);
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
                    townCommerceWorldFacts?.SkyblockWorld ?? false));
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

        LastAiTick = aiExecutor.Tick(aiStepper);
        townNpcAuthority.TickShimmer();
        townNpcAuthority.TickLifecycle(worldClock);
        AppliedDespawns += npcs.DespawnExpired();
    }

    public void TickProjectileInteractions() => townNpcAuthority.TickProjectileInteractions();

    public bool TryCapture(NpcHandle npc, out NpcSnapshot snapshot) => npcs.TryGet(npc, out snapshot);

    public int CopyActive(Span<NpcSnapshot> destination) => npcs.CopyActive(destination);

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

        for (int slot = 0; slot < VanillaNpcTargetingAiStepper.MaximumPlayerCandidates; slot++)
        {
            if (players.TryGet(checked((byte)slot), out RuntimePlayerMember? player))
            {
                if (player.MountType != 0)
                    continue;

                destination[written++] = new VanillaNpcTargetCandidate(
                    Slot: checked((byte)slot),
                    CenterX: player.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f,
                    CenterY: player.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f,
                    Aggro: 0,
                    Active: true,
                    Dead: player.IsDead,
                    Ghost: false,
                    NoAggro: false);
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

            destination[written++] = new VanillaNpcTargetCandidate(
                Slot: checked((byte)slot),
                CenterX: serverPlayer.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f,
                CenterY: serverPlayer.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f,
                Aggro: 0,
                Active: true,
                Dead: serverPlayer.IsDead,
                Ghost: false,
                NoAggro: false);
        }

        return written;
    }
}
