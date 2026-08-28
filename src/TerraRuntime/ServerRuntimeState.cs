using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime;

internal sealed class ServerRuntimeState
{
    private const int MaxPlayerSlots = 256;
    private const float VanillaBasePlayerWidth = 20f;
    private const float VanillaBasePlayerHeight = 42f;

    private readonly Dictionary<byte, RuntimePlayerState> _players = [];
    private readonly PendingPlayerVitals?[] _pendingVitals = new PendingPlayerVitals?[MaxPlayerSlots];
    private readonly VanillaNpcTargetCandidate[] _npcTargetCandidates =
        new VanillaNpcTargetCandidate[VanillaNpcTargetingAiStepper.MaximumPlayerCandidates];
    private readonly IRuntimePlayerEventSink? _playerEvents;
    private readonly RuntimeNpcStore _npcs;
    private readonly RuntimeNpcAiStateExecutor _npcAiExecutor;
    private readonly INpcAiStateStepper _npcAiStepper;
    private readonly VanillaNpcTargetingAiStepper? _vanillaNpcTargetingAiStepper;
    private readonly VanillaNpcCheckActiveAiStepper? _vanillaNpcCheckActiveAiStepper;
    private readonly RuntimeProjectileStore _projectiles;
    private readonly RuntimeProjectileStateExecutor _projectileExecutor;
    private readonly IProjectileStateStepper? _projectileStepper;
    private readonly RuntimeProjectileReplicationRegistry? _projectileReplication;
    private readonly RuntimeWorldItemStore _worldItems;
    private readonly RuntimeWorldClock? _worldClock;
    private int lastWorkerResult;
    private int lastSpawnCommitResult = -1;

    public ServerRuntimeState(
        IRuntimePlayerEventSink? playerEvents = null,
        RuntimeNpcStore? npcs = null,
        INpcAiStateStepper? npcAiStepper = null,
        WorldTileStore? worldTiles = null,
        RuntimeWorldClock? worldClock = null,
        RuntimeProjectileStore? projectiles = null,
        IProjectileStateStepper? projectileStepper = null,
        RuntimeWorldItemStore? worldItems = null,
        RuntimeProjectileReplicationRegistry? projectileReplication = null)
    {
        _playerEvents = playerEvents;
        _worldClock = worldClock;
        _npcs = npcs ?? new RuntimeNpcStore();
        _npcAiExecutor = new RuntimeNpcAiStateExecutor(_npcs);
        _projectiles = projectiles ?? new RuntimeProjectileStore();
        _projectileExecutor = new RuntimeProjectileStateExecutor(_projectiles);
        _projectileStepper = projectileStepper;
        _projectileReplication = projectileReplication;
        _worldItems = worldItems ?? new RuntimeWorldItemStore();

        if (npcAiStepper is null)
        {
            _vanillaNpcTargetingAiStepper = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
            if (worldTiles is null)
            {
                _npcAiStepper = _vanillaNpcTargetingAiStepper;
            }
            else
            {
                var worldMotion = new VanillaNpcWorldMotionAiStepper(_vanillaNpcTargetingAiStepper, worldTiles);
                _vanillaNpcCheckActiveAiStepper = new VanillaNpcCheckActiveAiStepper(worldMotion);
                _npcAiStepper = _vanillaNpcCheckActiveAiStepper;
            }
        }
        else
        {
            _npcAiStepper = npcAiStepper;
        }
    }

    public long AppliedCommands { get; private set; }

    public long Updates { get; private set; }

    public long AppliedPlayerAppearances { get; private set; }

    public long RejectedPlayerAppearances { get; private set; }

    public long AppliedPlayerEquipmentUpdates { get; private set; }

    public long RejectedPlayerEquipmentUpdates { get; private set; }

    public long AppliedPlayerHealthUpdates { get; private set; }

    public long RejectedPlayerHealthUpdates { get; private set; }

    public long AppliedPlayerManaUpdates { get; private set; }

    public long RejectedPlayerManaUpdates { get; private set; }

    public long CommittedPlayerSpawns { get; private set; }

    public long AppliedPlayerMovements { get; private set; }

    public long RejectedPlayerMovements { get; private set; }

    public long DisconnectedPlayers { get; private set; }

    public long AppliedNpcSpawns { get; private set; }

    public long RejectedNpcSpawns { get; private set; }

    public long AppliedNpcUpdates { get; private set; }

    public long RejectedNpcUpdates { get; private set; }

    public long AppliedNpcDespawns { get; private set; }

    public long RejectedNpcDespawns { get; private set; }

    public long AppliedProjectileSpawns { get; private set; }

    public long RejectedProjectileSpawns { get; private set; }

    public long AppliedProjectileUpdates { get; private set; }

    public long RejectedProjectileUpdates { get; private set; }

    public long AppliedProjectileDespawns { get; private set; }

    public long RejectedProjectileDespawns { get; private set; }

    public long RejectedClientProjectileUpdates { get; private set; }

    public long RejectedClientProjectileDestroys { get; private set; }

    public long RelayedUnknownProjectileDestroys { get; private set; }

    public long AppliedWorldItemAllocations { get; private set; }

    public long RejectedWorldItemAllocations { get; private set; }

    public long AppliedWorldItemDrops { get; private set; }

    public long RejectedWorldItemDrops { get; private set; }

    public long AppliedWorldItemRemovals { get; private set; }

    public long RejectedWorldItemRemovals { get; private set; }

    public long AppliedWorldItemOwners { get; private set; }

    public long RejectedWorldItemOwners { get; private set; }

    public NpcAiStateTickSummary LastNpcAiTick { get; private set; }

    public ProjectileStateTickSummary LastProjectileTick { get; private set; }

    public PlayerSlotId? LastMovementPlayerSlot { get; private set; }

    public float LastMovementPositionX { get; private set; }

    public float LastMovementPositionY { get; private set; }

    public int LastWorkerResult => Volatile.Read(ref lastWorkerResult);

    public PlayerSpawnCommitResult? LastSpawnCommitResult
    {
        get
        {
            int value = Volatile.Read(ref lastSpawnCommitResult);
            return value < 0 ? null : (PlayerSpawnCommitResult)value;
        }
    }

    /// <summary>
    /// Captures an immutable projection for an exact live session. This method is authoritative-thread
    /// only; asynchronous consumers must receive the value through a command/result boundary.
    /// </summary>
    internal bool TryCapturePlayerSnapshot(PlayerHandle player, out PlayerStateSnapshot snapshot)
    {
        if (!_players.TryGetValue(player.Slot.Value, out RuntimePlayerState? state) ||
            state.Connection.Player != player)
        {
            snapshot = default;
            return false;
        }

        snapshot = state.CaptureSnapshot();
        return true;
    }

    /// <summary>
    /// Captures an exact generation-safe NPC snapshot on the authoritative thread.
    /// </summary>
    internal bool TryCaptureNpcSnapshot(NpcHandle npc, out NpcSnapshot snapshot) =>
        _npcs.TryGet(npc, out snapshot);

    /// <summary>
    /// Captures an exact generation-safe projectile snapshot on the authoritative thread.
    /// </summary>
    internal bool TryCaptureProjectileSnapshot(ProjectileHandle projectile, out ProjectileSnapshot snapshot) =>
        _projectiles.TryGet(projectile, out snapshot);

    /// <summary>
    /// Captures the currently active occupation of one vanilla world-item slot on the authoritative thread.
    /// </summary>
    internal bool TryCaptureWorldItemSnapshot(short slot, out WorldItemSnapshot snapshot) =>
        _worldItems.TryGetActive(slot, out snapshot);

    public void Apply(RuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        AppliedCommands++;

        switch (command)
        {
            case WorkerResultCommand result:
                Volatile.Write(ref lastWorkerResult, result.Value);
                break;

            case SetInterestManagementRuntimeCommand interestManagement:
                interestManagement.Control.SetEnabled(interestManagement.Enabled);
                break;

            case NpcSpawnRuntimeCommand spawn:
                ApplyNpcSpawn(spawn);
                break;

            case NpcUpdateRuntimeCommand update:
                ApplyNpcUpdate(update);
                break;

            case NpcDespawnRuntimeCommand despawn:
                ApplyNpcDespawn(despawn);
                break;

            case ProjectileSpawnRuntimeCommand spawn:
                ApplyProjectileSpawn(spawn);
                break;

            case ProjectileUpdateRuntimeCommand update:
                ApplyProjectileUpdate(update);
                break;

            case ProjectileDespawnRuntimeCommand despawn:
                ApplyProjectileDespawn(despawn);
                break;

            case ClientProjectileUpdateRuntimeCommand update:
                ApplyClientProjectileUpdate(update);
                break;

            case ClientProjectileDestroyRuntimeCommand destroy:
                ApplyClientProjectileDestroy(destroy);
                break;

            case WorldItemAllocateRuntimeCommand allocate:
                ApplyWorldItemAllocate(allocate);
                break;

            case WorldItemDropRuntimeCommand drop:
                ApplyWorldItemDrop(drop);
                break;

            case WorldItemRemoveRuntimeCommand remove:
                ApplyWorldItemRemove(remove);
                break;

            case WorldItemOwnerRuntimeCommand owner:
                ApplyWorldItemOwner(owner);
                break;

            case PlayerAppearanceRuntimeCommand appearance:
                ApplyPlayerAppearance(appearance);
                break;

            case PlayerEquipmentRuntimeCommand equipment:
                ApplyPlayerEquipment(equipment);
                break;

            case PlayerHealthRuntimeCommand health:
                ApplyPlayerHealth(health);
                break;

            case PlayerManaRuntimeCommand mana:
                ApplyPlayerMana(mana);
                break;

            case PlayerSpawnRuntimeCommand spawn:
                ApplyPlayerSpawn(spawn);
                break;

            case PlayerMovementRuntimeCommand movement:
                ApplyPlayerMovement(movement);
                break;

            case PlayerDisconnectRuntimeCommand disconnect:
                ApplyPlayerDisconnect(disconnect);
                break;

            case PlayerStateSnapshotRuntimeCommand snapshot:
                CompletePlayerSnapshot(snapshot);
                break;
        }
    }

    public void Tick()
    {
        if (_vanillaNpcTargetingAiStepper is not null)
        {
            int candidateCount = CopyVanillaNpcTargetCandidates(_npcTargetCandidates);
            ReadOnlySpan<VanillaNpcTargetCandidate> candidates = _npcTargetCandidates.AsSpan(0, candidateCount);
            _vanillaNpcTargetingAiStepper.SetCandidates(candidates);
            _vanillaNpcCheckActiveAiStepper?.SetCandidates(candidates);
            if (_worldClock is not null)
            {
                _vanillaNpcTargetingAiStepper.SetWorldConditions(
                    _worldClock.DayTime,
                    _worldClock.SlimeRainActive);
            }
        }

        LastNpcAiTick = _npcAiExecutor.Tick(_npcAiStepper);
        AppliedNpcDespawns += _npcs.DespawnExpired();
        if (_projectileStepper is not null)
            LastProjectileTick = _projectileExecutor.Tick(_projectileStepper);

        _worldClock?.Tick();
        Updates++;
    }

    private int CopyVanillaNpcTargetCandidates(Span<VanillaNpcTargetCandidate> destination)
    {
        int written = 0;
        for (int slot = 0; slot < VanillaNpcTargetingAiStepper.MaximumPlayerCandidates; slot++)
        {
            if (!_players.TryGetValue(checked((byte)slot), out RuntimePlayerState? player))
                continue;

            // Vanilla Player starts at 20x42. Mount delegates may override the hitbox, so mounted
            // players stay outside this verified baseline until mount-specific sizing is modeled.
            if (player.MountType != 0)
                continue;

            destination[written++] = new VanillaNpcTargetCandidate(
                Slot: checked((byte)slot),
                CenterX: player.PositionX + VanillaBasePlayerWidth * 0.5f,
                CenterY: player.PositionY + VanillaBasePlayerHeight * 0.5f,
                Aggro: 0,
                Active: true,
                Dead: player.IsDead,
                Ghost: false,
                NoAggro: false);
        }

        return written;
    }

    private void ApplyNpcSpawn(NpcSpawnRuntimeCommand command)
    {
        NpcStateUpdate state = command.State;
        if (_npcs.TrySpawn(command.Slot, in state, out NpcSnapshot snapshot))
        {
            AppliedNpcSpawns++;
            command.Completion?.TrySetResult(snapshot);
            return;
        }

        RejectedNpcSpawns++;
        command.Completion?.TrySetResult(null);
    }

    private void ApplyNpcUpdate(NpcUpdateRuntimeCommand command)
    {
        NpcStateUpdate state = command.State;
        if (_npcs.TryUpdate(command.Npc, in state, out _))
        {
            AppliedNpcUpdates++;
            return;
        }

        RejectedNpcUpdates++;
    }

    private void ApplyNpcDespawn(NpcDespawnRuntimeCommand command)
    {
        if (_npcs.TryDespawn(command.Npc))
        {
            AppliedNpcDespawns++;
            return;
        }

        RejectedNpcDespawns++;
    }

    private void ApplyProjectileSpawn(ProjectileSpawnRuntimeCommand command)
    {
        ProjectileStateUpdate state = command.State;
        if (_projectiles.TrySpawn(command.Slot, in state, out ProjectileSnapshot snapshot))
        {
            AppliedProjectileSpawns++;
            command.Completion?.TrySetResult(snapshot);
            return;
        }

        RejectedProjectileSpawns++;
        command.Completion?.TrySetResult(null);
    }

    private void ApplyProjectileUpdate(ProjectileUpdateRuntimeCommand command)
    {
        ProjectileStateUpdate state = command.State;
        if (_projectiles.TryUpdate(command.Projectile, in state, out _))
        {
            AppliedProjectileUpdates++;
            return;
        }

        RejectedProjectileUpdates++;
    }

    private void ApplyProjectileDespawn(ProjectileDespawnRuntimeCommand command)
    {
        if (_projectiles.TryDespawn(command.Projectile, out _))
        {
            AppliedProjectileDespawns++;
            return;
        }

        RejectedProjectileDespawns++;
    }

    private void ApplyClientProjectileUpdate(ClientProjectileUpdateRuntimeCommand command)
    {
        TerrariaProjectileUpdateState packet = command.State;
        if (_projectileReplication is null ||
            !IsCurrentPlayerConnection(command.Connection) ||
            packet.Key.Spawner != command.Connection.Player.Slot.Value ||
            !TryConvertClientProjectileUpdate(in packet, out ProjectileStateUpdate update))
        {
            RejectedClientProjectileUpdates++;
            return;
        }

        RuntimeProjectileWireIdentityRegistry identities = _projectileReplication.WireIdentities;
        RuntimeProjectileClientCommitContext clientCommits = _projectileReplication.ClientCommitContext;
        TerrariaProjectileKeyState key = packet.Key;

        if (identities.TryResolve(in key, out ProjectileHandle projectile))
        {
            using IDisposable scope = clientCommits.Enter(command.Connection.Source, in key);
            if (_projectiles.TryUpdate(projectile, in update, out _))
            {
                AppliedProjectileUpdates++;
                return;
            }

            RejectedProjectileUpdates++;
            RejectedClientProjectileUpdates++;
            return;
        }

        if (!TryFindFreeVanillaProjectileSlot(out ushort slot))
        {
            RejectedProjectileSpawns++;
            RejectedClientProjectileUpdates++;
            return;
        }

        using (clientCommits.Enter(command.Connection.Source, in key))
        {
            if (_projectiles.TrySpawn(slot, in update, out _))
            {
                AppliedProjectileSpawns++;
                return;
            }
        }

        RejectedProjectileSpawns++;
        RejectedClientProjectileUpdates++;
    }

    private void ApplyClientProjectileDestroy(ClientProjectileDestroyRuntimeCommand command)
    {
        TerrariaProjectileDestroyState packet = command.State;
        if (_projectileReplication is null ||
            !packet.IsValid ||
            !IsCurrentPlayerConnection(command.Connection))
        {
            RejectedClientProjectileDestroys++;
            return;
        }

        RuntimeProjectileWireIdentityRegistry identities = _projectileReplication.WireIdentities;
        TerrariaProjectileKeyState key = packet.Key;
        if (!identities.TryResolve(in key, out ProjectileHandle projectile))
        {
            if (_projectileReplication.TryRelayUnresolvedDestroy(command.Connection.Source, in packet))
            {
                RelayedUnknownProjectileDestroys++;
                return;
            }

            RejectedClientProjectileDestroys++;
            return;
        }

        if (!_projectiles.TryGet(projectile, out ProjectileSnapshot current))
        {
            identities.TryUnbind(projectile, out _);
            if (_projectileReplication.TryRelayUnresolvedDestroy(command.Connection.Source, in packet))
            {
                RelayedUnknownProjectileDestroys++;
                return;
            }

            RejectedClientProjectileDestroys++;
            return;
        }

        if (current.Spawner != command.Connection.Player.Slot.Value)
        {
            RejectedClientProjectileDestroys++;
            return;
        }

        using (_projectileReplication.ClientCommitContext.Enter(command.Connection.Source, in key))
        {
            if (_projectiles.TryDespawnAt(projectile, packet.PositionX, packet.PositionY, out _))
            {
                AppliedProjectileDespawns++;
                return;
            }
        }

        RejectedProjectileDespawns++;
        RejectedClientProjectileDestroys++;
    }

    private bool TryFindFreeVanillaProjectileSlot(out ushort slot)
    {
        int physicalCapacity = Math.Min(_projectiles.Capacity, RuntimeProjectileStore.VanillaPhysicalSlotCount);
        for (int candidate = 0; candidate < physicalCapacity; candidate++)
        {
            ushort physicalSlot = checked((ushort)candidate);
            if (_projectiles.TryGetActive(physicalSlot, out _))
                continue;

            slot = physicalSlot;
            return true;
        }

        slot = default;
        return false;
    }

    private static bool TryConvertClientProjectileUpdate(
        in TerrariaProjectileUpdateState packet,
        out ProjectileStateUpdate update)
    {
        if (!packet.IsValid ||
            !VanillaProjectileIds.TryCreate(packet.ProjectileType, out ProjectileTypeId type) ||
            !VanillaProjectileIds.IsLiveWireType(type) ||
            VanillaProjectileFacts.IsHostile(type))
        {
            update = default;
            return false;
        }

        update = new ProjectileStateUpdate(
            type,
            packet.Key.Spawner,
            packet.PositionX,
            packet.PositionY,
            packet.VelocityX,
            packet.VelocityY,
            new ProjectileAiState(packet.Ai0, packet.Ai1, packet.Ai2),
            packet.BannerIdToRespondTo,
            packet.Damage,
            packet.KnockBack,
            packet.OriginalDamage);
        return true;
    }

    private bool IsCurrentPlayerConnection(ConnectionHandle connection) =>
        connection.IsAssigned &&
        _players.TryGetValue(connection.Player.Slot.Value, out RuntimePlayerState? player) &&
        player.Connection == connection;

    private void ApplyWorldItemAllocate(WorldItemAllocateRuntimeCommand command)
    {
        WorldItemDropStateUpdate state = command.State;
        if (_worldItems.TryAllocateDrop(in state, out WorldItemSnapshot snapshot))
        {
            AppliedWorldItemAllocations++;
            command.Completion?.TrySetResult(snapshot);
            return;
        }

        RejectedWorldItemAllocations++;
        command.Completion?.TrySetResult(null);
    }

    private void ApplyWorldItemDrop(WorldItemDropRuntimeCommand command)
    {
        WorldItemDropStateUpdate state = command.State;
        if (_worldItems.TryApplyDrop(command.Slot, in state, out _))
        {
            AppliedWorldItemDrops++;
            return;
        }

        RejectedWorldItemDrops++;
    }

    private void ApplyWorldItemRemove(WorldItemRemoveRuntimeCommand command)
    {
        if (_worldItems.TryRemove(command.Slot, out _))
        {
            AppliedWorldItemRemovals++;
            return;
        }

        RejectedWorldItemRemovals++;
    }

    private void ApplyWorldItemOwner(WorldItemOwnerRuntimeCommand command)
    {
        WorldItemOwnerStateUpdate state = command.State;
        if (_worldItems.TryApplyOwner(command.Slot, in state, out _))
        {
            AppliedWorldItemOwners++;
            return;
        }

        RejectedWorldItemOwners++;
    }

    private void ApplyPlayerAppearance(PlayerAppearanceRuntimeCommand appearance)
    {
        PlayerAppearanceCommitRequest request = appearance.Request;
        if (_players.TryGetValue(request.PlayerSlot.Value, out RuntimePlayerState? activePlayer) &&
            activePlayer.Connection != appearance.Connection)
        {
            RejectedPlayerAppearances++;
            return;
        }

        if (activePlayer is not null && !activePlayer.TryAdvanceRevision())
        {
            RejectedPlayerAppearances++;
            return;
        }

        AppliedPlayerAppearances++;
        _playerEvents?.PlayerAppearanceUpdated(appearance.Connection, in request);
    }

    private void ApplyPlayerEquipment(PlayerEquipmentRuntimeCommand equipment)
    {
        PlayerEquipmentCommitRequest request = equipment.Request;
        if (_players.TryGetValue(request.PlayerSlot.Value, out RuntimePlayerState? activePlayer) &&
            activePlayer.Connection != equipment.Connection)
        {
            RejectedPlayerEquipmentUpdates++;
            return;
        }

        if (activePlayer is not null && !activePlayer.TryAdvanceRevision())
        {
            RejectedPlayerEquipmentUpdates++;
            return;
        }

        AppliedPlayerEquipmentUpdates++;
        _playerEvents?.PlayerEquipmentUpdated(equipment.Connection, in request);
    }

    private void ApplyPlayerHealth(PlayerHealthRuntimeCommand health)
    {
        PlayerHealthCommitRequest request = VanillaPlayerHealthNormalizer.Normalize(in health.Request);
        if (!health.Connection.IsAssigned || health.Connection.Player.Slot != request.PlayerSlot)
        {
            RejectedPlayerHealthUpdates++;
            return;
        }

        if (_players.TryGetValue(request.PlayerSlot.Value, out RuntimePlayerState? activePlayer))
        {
            if (activePlayer.Connection != health.Connection || !activePlayer.TryAdvanceRevision())
            {
                RejectedPlayerHealthUpdates++;
                return;
            }

            activePlayer.HasHealth = true;
            activePlayer.Life = request.Life;
            activePlayer.MaxLife = request.MaxLife;
            activePlayer.IsDead = request.Life <= 0;
        }
        else
        {
            PendingPlayerVitals pending = GetOrReplacePending(health.Connection);
            pending.HasHealth = true;
            pending.Life = request.Life;
            pending.MaxLife = request.MaxLife;
        }

        AppliedPlayerHealthUpdates++;
        _playerEvents?.PlayerHealthUpdated(health.Connection, in request);
    }

    private void ApplyPlayerMana(PlayerManaRuntimeCommand mana)
    {
        PlayerManaCommitRequest request = mana.Request;
        if (!mana.Connection.IsAssigned || mana.Connection.Player.Slot != request.PlayerSlot)
        {
            RejectedPlayerManaUpdates++;
            return;
        }

        if (_players.TryGetValue(request.PlayerSlot.Value, out RuntimePlayerState? activePlayer))
        {
            if (activePlayer.Connection != mana.Connection || !activePlayer.TryAdvanceRevision())
            {
                RejectedPlayerManaUpdates++;
                return;
            }

            activePlayer.HasMana = true;
            activePlayer.Mana = request.Mana;
            activePlayer.MaxMana = request.MaxMana;
        }
        else
        {
            PendingPlayerVitals pending = GetOrReplacePending(mana.Connection);
            pending.HasMana = true;
            pending.Mana = request.Mana;
            pending.MaxMana = request.MaxMana;
        }

        AppliedPlayerManaUpdates++;
        _playerEvents?.PlayerManaUpdated(mana.Connection, in request);
    }

    private PendingPlayerVitals GetOrReplacePending(ConnectionHandle connection)
    {
        int slot = connection.Player.Slot.Value;
        PendingPlayerVitals? pending = _pendingVitals[slot];
        if (pending is null || pending.Connection != connection)
        {
            pending = new PendingPlayerVitals(connection);
            _pendingVitals[slot] = pending;
        }

        return pending;
    }

    private void ApplyPlayerSpawn(PlayerSpawnRuntimeCommand spawn)
    {
        PlayerSpawnCommitRequest request = spawn.Request;
        if (!VanillaPlayerSpawnValidator.IsValid(in request))
        {
            Volatile.Write(ref lastSpawnCommitResult, (int)PlayerSpawnCommitResult.InvalidSpawnData);
            return;
        }

        if (!spawn.Connection.IsAssigned ||
            spawn.Connection.Player.Slot != request.ClaimedSlot)
        {
            Volatile.Write(ref lastSpawnCommitResult, (int)PlayerSpawnCommitResult.SlotMismatch);
            return;
        }

        PlayerSpawnCommitResult commit = spawn.Session.TryCommitSpawn(request.ClaimedSlot);
        Volatile.Write(ref lastSpawnCommitResult, (int)commit);
        if (commit != PlayerSpawnCommitResult.Committed)
            return;

        PendingPlayerVitals? pending = _pendingVitals[request.ClaimedSlot.Value];
        bool hasPending = pending is not null && pending.Connection == spawn.Connection;
        if (pending is not null)
            _pendingVitals[request.ClaimedSlot.Value] = null;

        CommittedPlayerSpawns++;
        _players[request.ClaimedSlot.Value] = new RuntimePlayerState
        {
            Connection = spawn.Connection,
            Revision = 1,
            Slot = request.ClaimedSlot,
            Team = request.Team,
            PositionX = request.SpawnX * 16f,
            PositionY = request.SpawnY * 16f,
            HasHealth = hasPending && pending!.HasHealth,
            Life = hasPending ? pending!.Life : (short)0,
            MaxLife = hasPending ? pending!.MaxLife : (short)0,
            IsDead = hasPending && pending!.HasHealth && pending.Life <= 0,
            HasMana = hasPending && pending!.HasMana,
            Mana = hasPending ? pending!.Mana : (short)0,
            MaxMana = hasPending ? pending!.MaxMana : (short)0
        };
        _playerEvents?.PlayerSpawned(spawn.Connection, in request);
    }

    private void ApplyPlayerMovement(PlayerMovementRuntimeCommand movement)
    {
        PlayerMovementCommitRequest submitted = movement.Request;
        if (!VanillaPlayerMovementNormalizer.TryNormalize(
                in submitted,
                out PlayerMovementCommitRequest request))
        {
            RejectedPlayerMovements++;
            return;
        }

        if (!_players.TryGetValue(request.PlayerSlot.Value, out RuntimePlayerState? player) ||
            player.Connection != movement.Connection)
        {
            RejectedPlayerMovements++;
            return;
        }

        if (!player.TryAdvanceRevision())
        {
            RejectedPlayerMovements++;
            return;
        }

        player.ControlFlags = request.ControlFlags;
        player.MovementFlags = request.MovementFlags;
        player.MiscFlags1 = request.MiscFlags1;
        player.MiscFlags2 = request.MiscFlags2;
        player.SelectedItem = request.SelectedItem;
        player.PositionX = request.PositionX;
        player.PositionY = request.PositionY;
        player.VelocityX = request.HasVelocity ? request.VelocityX : 0f;
        player.VelocityY = request.HasVelocity ? request.VelocityY : 0f;
        player.MountType = request.HasMount ? request.MountType : (ushort)0;
        player.PotionOfReturnOriginalPositionX = request.HasPotionOfReturnPositions
            ? request.PotionOfReturnOriginalPositionX
            : 0f;
        player.PotionOfReturnOriginalPositionY = request.HasPotionOfReturnPositions
            ? request.PotionOfReturnOriginalPositionY
            : 0f;
        player.PotionOfReturnHomePositionX = request.HasPotionOfReturnPositions
            ? request.PotionOfReturnHomePositionX
            : 0f;
        player.PotionOfReturnHomePositionY = request.HasPotionOfReturnPositions
            ? request.PotionOfReturnHomePositionY
            : 0f;
        player.CameraTargetX = request.HasCameraTarget ? request.CameraTargetX : 0f;
        player.CameraTargetY = request.HasCameraTarget ? request.CameraTargetY : 0f;

        AppliedPlayerMovements++;
        LastMovementPlayerSlot = request.PlayerSlot;
        LastMovementPositionX = request.PositionX;
        LastMovementPositionY = request.PositionY;
        _playerEvents?.PlayerMoved(movement.Connection, in request);
    }

    private void ApplyPlayerDisconnect(PlayerDisconnectRuntimeCommand disconnect)
    {
        ConnectionHandle connection = disconnect.Connection;
        PendingPlayerVitals? pending = _pendingVitals[connection.Player.Slot.Value];
        if (pending is not null && pending.Connection == connection)
            _pendingVitals[connection.Player.Slot.Value] = null;

        if (!_players.TryGetValue(connection.Player.Slot.Value, out RuntimePlayerState? player) ||
            player.Connection != disconnect.Connection)
        {
            return;
        }

        _players.Remove(connection.Player.Slot.Value);
        DisconnectedPlayers++;
        _playerEvents?.PlayerDisconnected(connection);
    }

    private void CompletePlayerSnapshot(PlayerStateSnapshotRuntimeCommand command)
    {
        PlayerStateSnapshot? result = TryCapturePlayerSnapshot(command.Player, out PlayerStateSnapshot snapshot)
            ? snapshot
            : null;
        command.Completion.TrySetResult(result);
    }

    private sealed class PendingPlayerVitals(ConnectionHandle connection)
    {
        public ConnectionHandle Connection { get; } = connection;
        public bool HasHealth { get; set; }
        public short Life { get; set; }
        public short MaxLife { get; set; }
        public bool HasMana { get; set; }
        public short Mana { get; set; }
        public short MaxMana { get; set; }
    }

    private sealed class RuntimePlayerState
    {
        public ConnectionHandle Connection { get; init; }
        public ulong Revision { get; set; }
        public PlayerSlotId Slot { get; init; }
        public byte Team { get; init; }
        public bool HasHealth { get; set; }
        public short Life { get; set; }
        public short MaxLife { get; set; }
        public bool IsDead { get; set; }
        public bool HasMana { get; set; }
        public short Mana { get; set; }
        public short MaxMana { get; set; }
        public byte ControlFlags { get; set; }
        public byte MovementFlags { get; set; }
        public byte MiscFlags1 { get; set; }
        public byte MiscFlags2 { get; set; }
        public byte SelectedItem { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float VelocityX { get; set; }
        public float VelocityY { get; set; }
        public ushort MountType { get; set; }
        public float PotionOfReturnOriginalPositionX { get; set; }
        public float PotionOfReturnOriginalPositionY { get; set; }
        public float PotionOfReturnHomePositionX { get; set; }
        public float PotionOfReturnHomePositionY { get; set; }
        public float CameraTargetX { get; set; }
        public float CameraTargetY { get; set; }

        public bool TryAdvanceRevision()
        {
            if (Revision == ulong.MaxValue)
                return false;

            Revision++;
            return true;
        }

        public PlayerStateSnapshot CaptureSnapshot() =>
            new(
                Connection.Player,
                new PlayerStateRevision(Revision),
                Team,
                ControlFlags,
                MovementFlags,
                MiscFlags1,
                MiscFlags2,
                SelectedItem,
                PositionX,
                PositionY,
                VelocityX,
                VelocityY,
                MountType,
                PotionOfReturnOriginalPositionX,
                PotionOfReturnOriginalPositionY,
                PotionOfReturnHomePositionX,
                PotionOfReturnHomePositionY,
                CameraTargetX,
                CameraTargetY)
            {
                HasHealth = HasHealth,
                Life = Life,
                MaxLife = MaxLife,
                IsDead = IsDead,
                HasMana = HasMana,
                Mana = Mana,
                MaxMana = MaxMana
            };
    }
}
