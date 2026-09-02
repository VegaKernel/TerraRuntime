using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime;

internal sealed partial class ServerRuntimeState
{
    private bool IsCurrentPlayerConnection(ConnectionHandle connection) =>
        connection.IsAssigned &&
        _players.TryGetValue(connection.Player.Slot.Value, out RuntimePlayerState? player) &&
        player.Connection == connection;

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

        if (!_playerTransferProfiles.TrySetAppearance(appearance.Connection, in request))
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
        if (!equipment.Connection.IsAssigned ||
            equipment.Connection.Player.Slot != request.PlayerSlot)
        {
            RejectedPlayerEquipmentUpdates++;
            return;
        }

        bool inventorySlot = VanillaPlayerItemSlotCatalog.IsInventorySlot(request.SlotId);
        if (inventorySlot &&
            (!RuntimePlayerInventoryItem.TryFromNormalized(in request, out _) ||
             !_playerInventory.CanAccept(equipment.Connection)))
        {
            RejectedPlayerEquipmentUpdates++;
            return;
        }

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

        if (inventorySlot && !_playerInventory.TrySet(equipment.Connection, in request))
        {
            RejectedPlayerEquipmentUpdates++;
            return;
        }

        if (!inventorySlot && VanillaPlayerItemSlotCatalog.CanRelay(request.SlotId))
            _playerTransferProfiles.TrySetEquipment(equipment.Connection, in request);

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

        if (!_playerInventory.CanAccept(spawn.Connection))
        {
            Volatile.Write(ref lastSpawnCommitResult, (int)PlayerSpawnCommitResult.InvalidJoinState);
            return;
        }

        PlayerSpawnCommitResult commit = spawn.Session.TryCommitSpawn(request.ClaimedSlot);
        Volatile.Write(ref lastSpawnCommitResult, (int)commit);
        if (commit != PlayerSpawnCommitResult.Committed)
            return;

        if (!_playerInventory.TryAttach(spawn.Connection))
            throw new InvalidOperationException("Player inventory ownership changed during authoritative spawn commit.");

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

        _playerInventory.Clear(connection);
        _playerTransferProfiles.Clear(connection);

        if (!_players.TryGetValue(connection.Player.Slot.Value, out RuntimePlayerState? player) ||
            player.Connection != disconnect.Connection)
        {
            return;
        }

        _playerTalkNpcSlots[connection.Player.Slot.Value] = TerrariaNpcTalkCodec.NoNpc;
        _townShopSessions[connection.Player.Slot.Value] = null;
        _players.Remove(connection.Player.Slot.Value);
        DisconnectedPlayers++;
        _playerEvents?.PlayerDisconnected(connection);
    }

    private void ApplyPlayerTransferDetach(PlayerTransferDetachRuntimeCommand command)
    {
        ConnectionHandle connection = command.Connection;
        if (!_players.TryGetValue(connection.Player.Slot.Value, out RuntimePlayerState? player) ||
            player.Connection != connection)
        {
            command.Completion.TrySetResult(null);
            return;
        }

        var inventory = new RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
        if (!_playerInventory.TryCopyInventory(connection, inventory))
        {
            command.Completion.TrySetResult(null);
            return;
        }

        _playerTransferProfiles.TryCapture(
            connection,
            out PlayerAppearanceCommitRequest? appearance,
            out PlayerEquipmentCommitRequest[] equipment);
        var transfer = new RuntimePlayerTransferState(
            player.CaptureSnapshot(),
            inventory,
            appearance,
            equipment);

        _pendingVitals[connection.Player.Slot.Value] = null;
        _playerInventory.Clear(connection);
        _playerTransferProfiles.Clear(connection);
        _playerTalkNpcSlots[connection.Player.Slot.Value] = TerrariaNpcTalkCodec.NoNpc;
        _townShopSessions[connection.Player.Slot.Value] = null;
        _players.Remove(connection.Player.Slot.Value);
        _playerEvents?.PlayerDisconnected(connection);
        command.Completion.TrySetResult(transfer);
    }

    private void ApplyPlayerTransferAttach(PlayerTransferAttachRuntimeCommand command)
    {
        ConnectionHandle connection = command.Connection;
        RuntimePlayerTransferState transfer = command.Transfer;
        int slot = connection.Player.Slot.Value;
        if (!connection.IsAssigned ||
            transfer.Slot != connection.Player.Slot ||
            _players.ContainsKey(checked((byte)slot)) ||
            transfer.Inventory.Length != VanillaPlayerItemSlotCatalog.InventoryCount ||
            !_playerInventory.TryAttach(connection))
        {
            command.Completion.TrySetResult(false);
            return;
        }

        var inventoryMutations = new RuntimePlayerInventoryMutation[VanillaPlayerItemSlotCatalog.InventoryCount];
        for (short inventorySlot = 0; inventorySlot < inventoryMutations.Length; inventorySlot++)
            inventoryMutations[inventorySlot] = new RuntimePlayerInventoryMutation(inventorySlot, transfer.Inventory[inventorySlot]);
        if (!_playerInventory.TryApplyAtomic(connection, inventoryMutations))
        {
            _playerInventory.Clear(connection);
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

        var state = new RuntimePlayerState
        {
            Connection = connection,
            Revision = 1,
            Slot = connection.Player.Slot,
            Team = previous.Team,
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
        _players[checked((byte)slot)] = state;
        _playerTransferProfiles.Restore(connection, transfer.Appearance, transfer.Equipment);

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
        _playerEvents?.PlayerSpawned(connection, in spawn);

        if (transfer.Appearance is PlayerAppearanceCommitRequest appearance)
        {
            PlayerAppearanceCommitRequest normalizedAppearance = appearance with { PlayerSlot = connection.Player.Slot };
            _playerEvents?.PlayerAppearanceUpdated(connection, in normalizedAppearance);
        }

        for (short inventorySlot = 0; inventorySlot < transfer.Inventory.Length; inventorySlot++)
        {
            RuntimePlayerInventoryItem item = transfer.Inventory[inventorySlot];
            if (item.IsEmpty)
                continue;
            PlayerEquipmentCommitRequest request = item.ToCommitRequest(connection.Player.Slot, inventorySlot);
            _playerEvents?.PlayerEquipmentUpdated(connection, in request);
        }
        for (int i = 0; i < transfer.Equipment.Length; i++)
        {
            PlayerEquipmentCommitRequest request = transfer.Equipment[i] with { PlayerSlot = connection.Player.Slot };
            _playerEvents?.PlayerEquipmentUpdated(connection, in request);
        }

        if (state.HasHealth)
        {
            var health = new PlayerHealthCommitRequest(connection.Player.Slot, state.Life, state.MaxLife);
            _playerEvents?.PlayerHealthUpdated(connection, in health);
        }
        if (state.HasMana)
        {
            var mana = new PlayerManaCommitRequest(connection.Player.Slot, state.Mana, state.MaxMana);
            _playerEvents?.PlayerManaUpdated(connection, in mana);
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
        _playerEvents?.PlayerMoved(connection, in movement);
        command.Completion.TrySetResult(true);
    }

    private bool IsTransferPositionValid(float positionX, float positionY)
    {
        if (_worldTiles is null || !float.IsFinite(positionX) || !float.IsFinite(positionY))
            return false;

        float maximumX = _worldTiles.Dimensions.WidthTiles * 16f - VanillaBasePlayerWidth;
        float maximumY = _worldTiles.Dimensions.HeightTiles * 16f - VanillaBasePlayerHeight;
        return positionX >= 0f && positionY >= 0f && positionX <= maximumX && positionY <= maximumY;
    }

    private void CompletePlayerSnapshot(PlayerStateSnapshotRuntimeCommand command)
    {
        PlayerStateSnapshot? result = TryCaptureRuntimePlayerSnapshot(command.Player, out PlayerStateSnapshot snapshot)
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
