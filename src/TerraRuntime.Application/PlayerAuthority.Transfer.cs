using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
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
            equipment,
            player.GodMode);

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
            GodMode = transfer.GodMode,
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
        damageImmunity.ResetPvp(connection.Player.Slot);
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
