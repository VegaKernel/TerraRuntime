using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Owns authoritative client-player command application and all mutable player state for one world.
/// The enclosing world loop remains the sole caller, so extraction does not introduce another writer.
/// </summary>
internal sealed class PlayerAuthority
{
    internal const float VanillaBasePlayerWidth = 20f;
    internal const float VanillaBasePlayerHeight = 42f;

    private const int MaxPlayerSlots = byte.MaxValue + 1;

    private readonly RuntimePlayerMembership membership = new(MaxPlayerSlots);
    private readonly RuntimePlayerInventoryStore inventory = new();
    private readonly RuntimePlayerTransferProfileStore transferProfiles = new();
    private readonly IRuntimePlayerEventSink? events;
    private readonly WorldTileStore? worldTiles;
    private readonly RuntimePvpCombatIntegrity pvpCombat;
    private int lastSpawnCommitResult = -1;
    private long currentCombatTick;
    private readonly long[] pvpImmuneUntil = new long[MaxPlayerSlots];
    private readonly PlayerSessionGeneration[] pvpImmuneGeneration = new PlayerSessionGeneration[MaxPlayerSlots];
    private readonly bool expertMode;
    private readonly bool masterMode;

    public PlayerAuthority(
        IRuntimePlayerEventSink? events,
        WorldTileStore? worldTiles,
        bool expertMode = false,
        bool masterMode = false)
    {
        if (masterMode && !expertMode)
            throw new ArgumentException("Master mode is a strict subset of Expert mode.", nameof(masterMode));
        this.events = events;
        this.worldTiles = worldTiles;
        this.expertMode = expertMode;
        this.masterMode = masterMode;
        pvpCombat = new RuntimePvpCombatIntegrity(this);
    }

    public bool TryApply(RuntimeCommand command)
    {
        switch (command)
        {
            case PlayerAppearanceRuntimeCommand appearance:
                ApplyPlayerAppearance(appearance);
                return true;
            case PlayerEquipmentRuntimeCommand equipment:
                ApplyPlayerEquipment(equipment);
                return true;
            case PlayerHealthRuntimeCommand health:
                ApplyPlayerHealth(health);
                return true;
            case PlayerManaRuntimeCommand mana:
                ApplyPlayerMana(mana);
                return true;
            case PlayerSpawnRuntimeCommand spawn:
                ApplyPlayerSpawn(spawn);
                return true;
            case PlayerMovementRuntimeCommand movement:
                ApplyPlayerMovement(movement);
                return true;
            case PlayerPvpToggleRuntimeCommand hostile:
                ApplyPlayerPvpToggle(hostile);
                return true;
            case PlayerTeamRuntimeCommand team:
                ApplyPlayerTeam(team);
                return true;
            case ClientPlayerPvpHitRuntimeCommand pvpHit:
                ApplyClientPvpHit(pvpHit);
                return true;
            case PlayerDisconnectRuntimeCommand disconnect:
                ApplyPlayerDisconnect(disconnect);
                return true;
            case PlayerTransferDetachRuntimeCommand detach:
                ApplyPlayerTransferDetach(detach);
                return true;
            case PlayerTransferAttachRuntimeCommand attach:
                ApplyPlayerTransferAttach(attach);
                return true;
            default:
                return false;
        }
    }

    public IEnumerable<RuntimePlayerMember> Members => membership.Members;

    public void AdvanceCombatTick(long tick) => currentCombatTick = tick;

    public bool IsCurrent(ConnectionHandle connection) => membership.IsCurrent(connection);

    public bool TryGet(ConnectionHandle connection, out RuntimePlayerMember member) =>
        membership.TryGet(connection, out member);

    public bool TryGet(PlayerHandle player, out RuntimePlayerMember member) =>
        membership.TryGet(player, out member);

    public bool TryGet(PlayerSlotId slot, out RuntimePlayerMember member) =>
        membership.TryGet(slot, out member);

    public bool TryGet(byte slot, out RuntimePlayerMember member) =>
        membership.TryGet(slot, out member);

    public bool TryCapture(PlayerHandle player, out PlayerStateSnapshot snapshot) =>
        membership.TryCapture(player, out snapshot);

    public bool TryGetInventoryItem(
        PlayerHandle player,
        int inventorySlot,
        out RuntimePlayerInventoryItem item)
    {
        if (!membership.TryGet(player, out RuntimePlayerMember member))
        {
            item = default;
            return false;
        }

        return inventory.TryGet(member.Connection, inventorySlot, out item);
    }

    public bool TryGetInventoryItem(
        ConnectionHandle connection,
        int inventorySlot,
        out RuntimePlayerInventoryItem item)
    {
        if (!membership.IsCurrent(connection))
        {
            item = default;
            return false;
        }

        return inventory.TryGet(connection, inventorySlot, out item);
    }

    public bool TryCopyInventory(
        ConnectionHandle connection,
        Span<RuntimePlayerInventoryItem> destination) =>
        membership.IsCurrent(connection) && inventory.TryCopyInventory(connection, destination);

    public bool TryCommitInventoryMutation(
        ConnectionHandle connection,
        in RuntimePlayerInventoryMutation mutation)
    {
        if (!membership.IsCurrent(connection))
            return false;

        Span<RuntimePlayerInventoryMutation> mutations = stackalloc RuntimePlayerInventoryMutation[1];
        mutations[0] = mutation;
        if (!inventory.TryApplyAtomic(connection, mutations))
            return false;

        PlayerEquipmentCommitRequest request =
            mutation.Item.ToCommitRequest(connection.Player.Slot, mutation.Slot);
        events?.PlayerEquipmentUpdated(connection, in request);
        return true;
    }

    public bool TryCaptureEquipment(
        ConnectionHandle connection,
        out PlayerEquipmentCommitRequest[] equipment)
    {
        if (!membership.IsCurrent(connection) ||
            !transferProfiles.TryCapture(connection, out _, out equipment))
        {
            equipment = [];
            return false;
        }

        return true;
    }

    public bool TryCaptureEquipment(
        PlayerHandle player,
        out PlayerEquipmentCommitRequest[] equipment)
    {
        if (!membership.TryGet(player, out RuntimePlayerMember member))
        {
            equipment = [];
            return false;
        }
        return TryCaptureEquipment(member.Connection, out equipment);
    }

    public bool TryCaptureCombatSnapshot(
        ConnectionHandle connection,
        out VanillaPlayerCombatSnapshot snapshot)
    {
        if (!TryCaptureEquipment(connection, out PlayerEquipmentCommitRequest[] equipment))
        {
            snapshot = default;
            return false;
        }
        return VanillaPlayerCombatEquipmentCatalog.TryBuild(equipment, out snapshot);
    }

    public bool TryCaptureCombatSnapshot(
        PlayerHandle player,
        out VanillaPlayerCombatSnapshot snapshot)
    {
        if (!membership.TryGet(player, out RuntimePlayerMember member))
        {
            snapshot = default;
            return false;
        }
        return TryCaptureCombatSnapshot(member.Connection, out snapshot);
    }

    public bool TrySetTalkNpc(ConnectionHandle connection, short npcSlot) =>
        membership.TrySetTalkNpc(connection, npcSlot);

    public bool TryGetTalkNpc(PlayerHandle player, out short npcSlot) =>
        membership.TryGetTalkNpc(player, out npcSlot);

    public bool TrySetTownShopSession(ConnectionHandle connection, RuntimeTownShopSession1458 session) =>
        membership.TrySetTownShopSession(connection, session);

    public bool TryGetTownShopSession(PlayerHandle player, out RuntimeTownShopSession1458? session) =>
        membership.TryGetTownShopSession(player, out session);

    public long AppliedAppearances { get; private set; }
    public long RejectedAppearances { get; private set; }
    public long AppliedEquipmentUpdates { get; private set; }
    public long RejectedEquipmentUpdates { get; private set; }
    public long AppliedHealthUpdates { get; private set; }
    public long RejectedHealthUpdates { get; private set; }
    public long AppliedManaUpdates { get; private set; }
    public long RejectedManaUpdates { get; private set; }
    public long CommittedSpawns { get; private set; }
    public long AppliedMovements { get; private set; }
    public long RejectedMovements { get; private set; }
    public long DisconnectedPlayers { get; private set; }
    public long AppliedPvpToggles { get; private set; }
    public long RejectedPvpToggles { get; private set; }
    public long AppliedTeamChanges { get; private set; }
    public long RejectedTeamChanges { get; private set; }
    public long AppliedAuthoritativePvpHits { get; private set; }
    public long RejectedAuthoritativePvpHits { get; private set; }
    public long LegacyPvpFallbackHits { get; private set; }
    public PlayerSlotId? LastMovementSlot { get; private set; }
    public float LastMovementPositionX { get; private set; }
    public float LastMovementPositionY { get; private set; }

    public PlayerSpawnCommitResult? LastSpawnCommitResult
    {
        get
        {
            int value = Volatile.Read(ref lastSpawnCommitResult);
            return value < 0 ? null : (PlayerSpawnCommitResult)value;
        }
    }

    private void ApplyPlayerAppearance(PlayerAppearanceRuntimeCommand appearance)
    {
        PlayerAppearanceCommitRequest request = appearance.Request;
        if (membership.TryGet(request.PlayerSlot, out RuntimePlayerMember? activePlayer) &&
            activePlayer.Connection != appearance.Connection)
        {
            RejectedAppearances++;
            return;
        }

        if (activePlayer is not null && !activePlayer.TryAdvanceRevision())
        {
            RejectedAppearances++;
            return;
        }

        if (!transferProfiles.TrySetAppearance(appearance.Connection, in request))
        {
            RejectedAppearances++;
            return;
        }

        AppliedAppearances++;
        events?.PlayerAppearanceUpdated(appearance.Connection, in request);
    }

    private void ApplyPlayerEquipment(PlayerEquipmentRuntimeCommand equipment)
    {
        PlayerEquipmentCommitRequest request = equipment.Request;
        if (!equipment.Connection.IsAssigned ||
            equipment.Connection.Player.Slot != request.PlayerSlot)
        {
            RejectedEquipmentUpdates++;
            return;
        }

        bool inventorySlot = VanillaPlayerItemSlotCatalog.IsInventorySlot(request.SlotId);
        if (inventorySlot &&
            (!RuntimePlayerInventoryItem.TryFromNormalized(in request, out _) ||
             !inventory.CanAccept(equipment.Connection)))
        {
            RejectedEquipmentUpdates++;
            return;
        }

        if (membership.TryGet(request.PlayerSlot, out RuntimePlayerMember? activePlayer) &&
            activePlayer.Connection != equipment.Connection)
        {
            RejectedEquipmentUpdates++;
            return;
        }

        if (activePlayer is not null && !activePlayer.TryAdvanceRevision())
        {
            RejectedEquipmentUpdates++;
            return;
        }

        if (inventorySlot && !inventory.TrySet(equipment.Connection, in request))
        {
            RejectedEquipmentUpdates++;
            return;
        }

        if (!inventorySlot && VanillaPlayerItemSlotCatalog.CanRelay(request.SlotId))
            transferProfiles.TrySetEquipment(equipment.Connection, in request);

        AppliedEquipmentUpdates++;
        events?.PlayerEquipmentUpdated(equipment.Connection, in request);
    }

    private void ApplyPlayerHealth(PlayerHealthRuntimeCommand health)
    {
        PlayerHealthCommitRequest request = VanillaPlayerVitalsRules.NormalizeHealth(in health.Request);
        if (!health.Connection.IsAssigned || health.Connection.Player.Slot != request.PlayerSlot)
        {
            RejectedHealthUpdates++;
            return;
        }

        if (membership.TryGet(request.PlayerSlot, out RuntimePlayerMember? activePlayer))
        {
            if (activePlayer.Connection != health.Connection || !activePlayer.TryAdvanceRevision())
            {
                RejectedHealthUpdates++;
                return;
            }

            activePlayer.HasHealth = true;
            activePlayer.Life = request.Life;
            activePlayer.MaxLife = request.MaxLife;
            activePlayer.IsDead = request.Life <= 0;
        }
        else
        {
            RuntimePendingPlayerVitals pending = membership.GetOrReplacePending(health.Connection);
            pending.HasHealth = true;
            pending.Life = request.Life;
            pending.MaxLife = request.MaxLife;
        }

        AppliedHealthUpdates++;
        events?.PlayerHealthUpdated(health.Connection, in request);
    }

    private void ApplyPlayerMana(PlayerManaRuntimeCommand mana)
    {
        PlayerManaCommitRequest request = mana.Request;
        if (!mana.Connection.IsAssigned || mana.Connection.Player.Slot != request.PlayerSlot)
        {
            RejectedManaUpdates++;
            return;
        }

        if (membership.TryGet(request.PlayerSlot, out RuntimePlayerMember? activePlayer))
        {
            if (activePlayer.Connection != mana.Connection || !activePlayer.TryAdvanceRevision())
            {
                RejectedManaUpdates++;
                return;
            }

            activePlayer.HasMana = true;
            activePlayer.Mana = request.Mana;
            activePlayer.MaxMana = request.MaxMana;
        }
        else
        {
            RuntimePendingPlayerVitals pending = membership.GetOrReplacePending(mana.Connection);
            pending.HasMana = true;
            pending.Mana = request.Mana;
            pending.MaxMana = request.MaxMana;
        }

        AppliedManaUpdates++;
        events?.PlayerManaUpdated(mana.Connection, in request);
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

        if (!inventory.CanAccept(spawn.Connection))
        {
            Volatile.Write(ref lastSpawnCommitResult, (int)PlayerSpawnCommitResult.InvalidJoinState);
            return;
        }

        PlayerSpawnCommitResult commit = spawn.Session.TryCommitSpawn(request.ClaimedSlot);
        Volatile.Write(ref lastSpawnCommitResult, (int)commit);
        if (commit != PlayerSpawnCommitResult.Committed)
            return;

        if (!inventory.TryAttach(spawn.Connection))
            throw new InvalidOperationException("Player inventory ownership changed during authoritative spawn commit.");

        RuntimePendingPlayerVitals? pending = membership.TakePending(request.ClaimedSlot);
        bool hasPending = pending is not null && pending.Connection == spawn.Connection;

        CommittedSpawns++;
        pvpImmuneUntil[request.ClaimedSlot.Value] = 0;
        pvpImmuneGeneration[request.ClaimedSlot.Value] = default;
        membership.Commit(new RuntimePlayerMember
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
        });
        events?.PlayerSpawned(spawn.Connection, in request);
    }

    private void ApplyPlayerMovement(PlayerMovementRuntimeCommand movement)
    {
        PlayerMovementCommitRequest submitted = movement.Request;
        if (!VanillaPlayerMovementNormalizer.TryNormalize(
                in submitted,
                out PlayerMovementCommitRequest request))
        {
            RejectedMovements++;
            return;
        }

        if (!membership.TryGet(movement.Connection, out RuntimePlayerMember? player))
        {
            RejectedMovements++;
            return;
        }

        if (!player.TryAdvanceRevision())
        {
            RejectedMovements++;
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

        AppliedMovements++;
        LastMovementSlot = request.PlayerSlot;
        LastMovementPositionX = request.PositionX;
        LastMovementPositionY = request.PositionY;
        events?.PlayerMoved(movement.Connection, in request);
    }


    private void ApplyPlayerPvpToggle(PlayerPvpToggleRuntimeCommand command)
    {
        if (!membership.TryGet(command.Connection, out RuntimePlayerMember? player) || !player.TryAdvanceRevision())
        {
            RejectedPvpToggles++;
            return;
        }

        player.Hostile = command.Hostile;
        AppliedPvpToggles++;
    }

    private void ApplyPlayerTeam(PlayerTeamRuntimeCommand command)
    {
        if (command.Team > 5 || !membership.TryGet(command.Connection, out RuntimePlayerMember? player) || !player.TryAdvanceRevision())
        {
            RejectedTeamChanges++;
            return;
        }

        player.Team = command.Team;
        AppliedTeamChanges++;
    }

    private void ApplyClientPvpHit(ClientPlayerPvpHitRuntimeCommand command)
    {
        PvpCombatResolveResult resolve = pvpCombat.ResolveClientItemHit(
            currentCombatTick,
            command.Connection,
            command.State,
            out AuthoritativePvpHit hit);
        if (resolve == PvpCombatResolveResult.LegacyFallback)
        {
            LegacyPvpFallbackHits++;
            return;
        }
        if (resolve != PvpCombatResolveResult.Accepted)
        {
            RejectedAuthoritativePvpHits++;
            return;
        }

        if (!TryCommitAuthoritativePvpDamage(
                currentCombatTick,
                hit.Attacker,
                hit.Target,
                hit.Context.Source,
                hit.Damage,
                hit.Critical,
                hit.HitDirection,
                out _))
        {
            RejectedAuthoritativePvpHits++;
            return;
        }

        AppliedAuthoritativePvpHits++;
    }

    internal bool TryCommitAuthoritativePvpDamage(
        long tick,
        PlayerHandle attacker,
        PlayerHandle targetHandle,
        DamageSource sourceDamage,
        int damage,
        bool critical,
        int hitDirection,
        out PlayerStateSnapshot committed)
    {
        committed = default;
        if (!attacker.IsAssigned || !targetHandle.IsAssigned || attacker == targetHandle ||
            !sourceDamage.IsValid || sourceDamage.Player != attacker || damage <= 0 ||
            hitDirection is < -1 or > 1 ||
            !membership.TryGet(attacker, out RuntimePlayerMember? source) ||
            !membership.TryGet(targetHandle, out RuntimePlayerMember? target) ||
            !source.Hostile || !target.Hostile || source.IsDead || target.IsDead || !target.HasHealth || target.Life <= 0 ||
            (source.Team != 0 && source.Team == target.Team))
        {
            return false;
        }

        if (!TryCaptureCombatSnapshot(targetHandle, out VanillaPlayerCombatSnapshot targetCombat))
            return false;

        int targetSlot = target.Slot.Value;
        bool immune = pvpImmuneGeneration[targetSlot] == targetHandle.Generation && tick < pvpImmuneUntil[targetSlot];
        var attack = new AuthoritativeAttackDamage(
            sourceDamage,
            damage,
            ArmorPenetration: 0,
            critical,
            KnockBack: 4.5f,
            hitDirection);
        if (!VanillaCombatDamagePipeline.TryResolvePvp(
                in attack,
                in targetCombat,
                immune,
                out FinalDamageToHp final,
                expertMode,
                masterMode) ||
            final.Damage <= 0 ||
            !target.TryAdvanceRevision())
        {
            return false;
        }

        target.Life = checked((short)Math.Max(0, target.Life - final.Damage));
        target.IsDead = target.Life <= 0;
        if (!final.Mitigation.NoKnockback && hitDirection != 0)
        {
            // Player.Hurt(pvp:true) uses this fixed vanilla impulse; weapon knockback is not an input here.
            target.VelocityX = 4.5f * hitDirection;
            target.VelocityY = -3.5f;
        }
        pvpImmuneGeneration[targetSlot] = targetHandle.Generation;
        pvpImmuneUntil[targetSlot] = tick + 8;
        committed = target.CaptureSnapshot();
        var health = new PlayerHealthCommitRequest(target.Slot, target.Life, target.MaxLife);
        events?.PlayerHealthUpdated(target.Connection, in health);
        return true;
    }

    private void ApplyPlayerDisconnect(PlayerDisconnectRuntimeCommand disconnect)
    {
        ConnectionHandle connection = disconnect.Connection;
        membership.ClearPending(connection);

        inventory.Clear(connection);
        transferProfiles.Clear(connection);

        if (!membership.TryRemove(connection, out _))
            return;

        DisconnectedPlayers++;
        events?.PlayerDisconnected(connection);
    }

    private void ApplyPlayerTransferDetach(PlayerTransferDetachRuntimeCommand command)
    {
        ConnectionHandle connection = command.Connection;
        if (!membership.TryGet(connection, out RuntimePlayerMember? player))
        {
            command.Completion.TrySetResult(null);
            return;
        }

        var inventory = new RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
        if (!this.inventory.TryCopyInventory(connection, inventory))
        {
            command.Completion.TrySetResult(null);
            return;
        }

        transferProfiles.TryCapture(
            connection,
            out PlayerAppearanceCommitRequest? appearance,
            out PlayerEquipmentCommitRequest[] equipment);
        var transfer = new RuntimePlayerTransferState(
            player.CaptureSnapshot(),
            inventory,
            appearance,
            equipment);

        membership.ClearPending(connection);
        this.inventory.Clear(connection);
        transferProfiles.Clear(connection);
        if (!membership.TryRemove(connection, out _))
            throw new InvalidOperationException("Player membership changed during authoritative transfer detach.");
        events?.PlayerDisconnected(connection);
        command.Completion.TrySetResult(transfer);
    }

    private void ApplyPlayerTransferAttach(PlayerTransferAttachRuntimeCommand command)
    {
        ConnectionHandle connection = command.Connection;
        RuntimePlayerTransferState transfer = command.Transfer;
        int slot = connection.Player.Slot.Value;
        if (!connection.IsAssigned ||
            transfer.Slot != connection.Player.Slot ||
            membership.Contains(connection.Player.Slot) ||
            transfer.Inventory.Length != VanillaPlayerItemSlotCatalog.InventoryCount ||
            !inventory.TryAttach(connection))
        {
            command.Completion.TrySetResult(false);
            return;
        }

        var inventoryMutations = new RuntimePlayerInventoryMutation[VanillaPlayerItemSlotCatalog.InventoryCount];
        for (short inventorySlot = 0; inventorySlot < inventoryMutations.Length; inventorySlot++)
            inventoryMutations[inventorySlot] = new RuntimePlayerInventoryMutation(inventorySlot, transfer.Inventory[inventorySlot]);
        if (!inventory.TryApplyAtomic(connection, inventoryMutations))
        {
            inventory.Clear(connection);
            command.Completion.TrySetResult(false);
            return;
        }

        PlayerStateSnapshot previous = transfer.Player;
        float spawnPositionX = command.SpawnX * 16f;
        float spawnPositionY = command.SpawnY * 16f;
        bool preservePosition = command.PreserveWorldPosition && IsTransferPositionValid(previous.PositionX, previous.PositionY);
        float positionX = preservePosition ? previous.PositionX : spawnPositionX;
        float positionY = preservePosition ? previous.PositionY : spawnPositionY;
        short life = previous.Life;
        bool dead = previous.IsDead;
        if (command.ForceRespawn)
        {
            dead = false;
            if (previous.HasHealth && previous.MaxLife > 0)
                life = previous.MaxLife;
        }

        var state = new RuntimePlayerMember
        {
            Connection = connection,
            Revision = 1,
            Slot = connection.Player.Slot,
            Team = previous.Team,
            Hostile = previous.Hostile,
            HasHealth = previous.HasHealth,
            Life = life,
            MaxLife = previous.MaxLife,
            IsDead = dead,
            HasMana = previous.HasMana,
            Mana = previous.Mana,
            MaxMana = previous.MaxMana,
            ControlFlags = preservePosition ? previous.ControlFlags : (byte)0,
            MovementFlags = preservePosition ? previous.MovementFlags : (byte)0,
            MiscFlags1 = preservePosition ? previous.MiscFlags1 : (byte)0,
            MiscFlags2 = preservePosition ? previous.MiscFlags2 : (byte)0,
            SelectedItem = previous.SelectedItem,
            PositionX = positionX,
            PositionY = positionY,
            VelocityX = preservePosition ? previous.VelocityX : 0f,
            VelocityY = preservePosition ? previous.VelocityY : 0f,
            MountType = preservePosition ? previous.MountType : (ushort)0,
            PotionOfReturnOriginalPositionX = preservePosition ? previous.PotionOfReturnOriginalPositionX : 0f,
            PotionOfReturnOriginalPositionY = preservePosition ? previous.PotionOfReturnOriginalPositionY : 0f,
            PotionOfReturnHomePositionX = preservePosition ? previous.PotionOfReturnHomePositionX : 0f,
            PotionOfReturnHomePositionY = preservePosition ? previous.PotionOfReturnHomePositionY : 0f,
            CameraTargetX = preservePosition ? previous.CameraTargetX : 0f,
            CameraTargetY = preservePosition ? previous.CameraTargetY : 0f
        };
        pvpImmuneUntil[slot] = 0;
        pvpImmuneGeneration[slot] = default;
        membership.Commit(state);
        transferProfiles.Restore(connection, transfer.Appearance, transfer.Equipment);

        short eventSpawnX = checked((short)Math.Clamp((int)(positionX / 16f), short.MinValue, short.MaxValue));
        short eventSpawnY = checked((short)Math.Clamp((int)(positionY / 16f), short.MinValue, short.MaxValue));
        var spawn = new PlayerSpawnCommitRequest(
            connection.Player.Slot,
            eventSpawnX,
            eventSpawnY,
            RespawnTimer: 0,
            DeathsPve: 0,
            DeathsPvp: 0,
            Team: state.Team,
            SpawnContext: 0);
        events?.PlayerSpawned(connection, in spawn);

        if (transfer.Appearance is PlayerAppearanceCommitRequest appearance)
        {
            PlayerAppearanceCommitRequest normalizedAppearance = appearance with { PlayerSlot = connection.Player.Slot };
            events?.PlayerAppearanceUpdated(connection, in normalizedAppearance);
        }

        for (short inventorySlot = 0; inventorySlot < transfer.Inventory.Length; inventorySlot++)
        {
            RuntimePlayerInventoryItem item = transfer.Inventory[inventorySlot];
            if (item.IsEmpty)
                continue;
            PlayerEquipmentCommitRequest request = item.ToCommitRequest(connection.Player.Slot, inventorySlot);
            events?.PlayerEquipmentUpdated(connection, in request);
        }
        for (int i = 0; i < transfer.Equipment.Length; i++)
        {
            PlayerEquipmentCommitRequest request = transfer.Equipment[i] with { PlayerSlot = connection.Player.Slot };
            events?.PlayerEquipmentUpdated(connection, in request);
        }

        if (state.HasHealth)
        {
            var health = new PlayerHealthCommitRequest(connection.Player.Slot, state.Life, state.MaxLife);
            events?.PlayerHealthUpdated(connection, in health);
        }
        if (state.HasMana)
        {
            var mana = new PlayerManaCommitRequest(connection.Player.Slot, state.Mana, state.MaxMana);
            events?.PlayerManaUpdated(connection, in mana);
        }

        var movement = new PlayerMovementCommitRequest(
            connection.Player.Slot,
            state.ControlFlags,
            state.MovementFlags,
            state.MiscFlags1,
            state.MiscFlags2,
            state.SelectedItem,
            state.PositionX,
            state.PositionY,
            HasVelocity: state.VelocityX != 0f || state.VelocityY != 0f,
            state.VelocityX,
            state.VelocityY,
            HasMount: state.MountType != 0,
            state.MountType,
            HasPotionOfReturnPositions: preservePosition &&
                (state.PotionOfReturnOriginalPositionX != 0f || state.PotionOfReturnOriginalPositionY != 0f ||
                 state.PotionOfReturnHomePositionX != 0f || state.PotionOfReturnHomePositionY != 0f),
            state.PotionOfReturnOriginalPositionX,
            state.PotionOfReturnOriginalPositionY,
            state.PotionOfReturnHomePositionX,
            state.PotionOfReturnHomePositionY,
            HasCameraTarget: preservePosition && (state.CameraTargetX != 0f || state.CameraTargetY != 0f),
            state.CameraTargetX,
            state.CameraTargetY);
        events?.PlayerMoved(connection, in movement);
        command.Completion.TrySetResult(true);
    }

    private bool IsTransferPositionValid(float positionX, float positionY)
    {
        if (worldTiles is null || !float.IsFinite(positionX) || !float.IsFinite(positionY))
            return false;

        float maximumX = worldTiles.Dimensions.WidthTiles * 16f - VanillaBasePlayerWidth;
        float maximumY = worldTiles.Dimensions.HeightTiles * 16f - VanillaBasePlayerHeight;
        return positionX >= 0f && positionY >= 0f && positionX <= maximumX && positionY <= maximumY;
    }

}
