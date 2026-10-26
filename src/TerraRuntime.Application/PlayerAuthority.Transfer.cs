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
        {
            disconnect.Completion?.TrySetResult(false);
            return;
        }

        try
        {
            ResetNpcRawSlot(connection.Player);
            DisconnectedPlayers++;
            chestCommands?.ReleasePlayer(connection);
            events?.PlayerDisconnected(connection);
        }
        finally
        {
            // The exact actor has already been removed, including when an observer
            // throws. Preserve that observer exception while completing cleanup.
            disconnect.Completion?.TrySetResult(true);
        }
    }

    private void ApplyPlayerTransferDetach(PlayerTransferDetachRuntimeCommand command)
    {
        ConnectionHandle connection = command.Connection;
        if (!membership.TryGet(connection, out RuntimePlayerMember? player))
        {
            command.Completion.TrySetResult(null);
            return;
        }

        var bankItems = new RuntimePlayerInventoryItem[160];
        var bankCopper = new long[160];
        if (!this.inventory.TryCaptureShopBanks(connection,
            bankItems,
            bankCopper) ||
            !this.inventory.TryCaptureShopBankOwnership(connection,
            out var bankQuantities,
            out var bankCaps))
        {
            command.Completion.TrySetResult(null);
            return;
        }

        var funded = this.inventory.CaptureFundedQuantities(connection);
        var coinValues = this.inventory.CaptureFundedCoinValues(connection);
        var quantityCaps = this.inventory.CaptureOwnedQuantities(connection);
        var inventory = new RuntimePlayerInventoryItem[VanillaPlayerItemSlotCatalog.InventoryCount];
        if (!this.inventory.TryCopyInventory(connection,
            inventory) ||
            !TryNormalizeTransferMouseItem(inventory,
            funded,
            coinValues,
            quantityCaps,
            receivePrefixWorld,
            out quantityCaps,
            out bool mouseItemNormalized))
        {
            command.Completion.TrySetResult(null);
            return;
        }

        transferProfiles.TryCapture(connection, out PlayerAppearanceCommitRequest? appearance, out PlayerEquipmentCommitRequest[] equipment, out BuffTypeId[]? buffTypes);
        var transfer = new RuntimePlayerTransferState(player.CaptureSnapshot(), inventory, appearance, equipment, buffTypes, player.GodMode, mouseItemNormalized)
        {
            ShopBankInventory = bankItems,
            FundedShopBankCopper = bankCopper,
            FundedShopBankQuantities = bankQuantities,
            OwnedShopBankQuantityCaps = bankCaps,
            FundedInventoryCoinValues = coinValues,
            FundedInventoryQuantities = funded,
            OwnedInventoryQuantities = quantityCaps,
            OwnsManaMaximum = player.OwnsManaMaximum,
            OwnsLifeMaximum = player.OwnsLifeMaximum,
            OwnedPermanentUpgradeMask = player.OwnedPermanentUpgradeMask,
            OwnsTorchFavor = player.OwnsTorchFavor,
            PaidTorchFavor = player.PaidTorchFavor,
            ItemPhase = player.ItemPhase,
            PhysicsPhase = player.PhysicsPhase,
            BuffState = transferProfiles.CaptureBuffState(connection)
        };
        membership.ClearPending(connection);
        this.inventory.Clear(connection);
        transferProfiles.Clear(connection);
        if (!membership.TryRemove(connection, out _))
            throw new InvalidOperationException("Player membership changed during authoritative transfer detach.");
        ResetNpcRawSlot(connection.Player);
        // Live transport shutdown uses this same detach transaction as world transfer.
        // Release world-owned interactions before completion can admit a replacement session.
        chestCommands?.ReleasePlayer(connection);
        events?.PlayerDisconnected(connection);
        command.Completion.TrySetResult(transfer);
    }

    private static bool TryNormalizeTransferMouseItem(RuntimePlayerInventoryItem[] inventory,
        short[] funded,
        long[] coinValues,
        OwnedInventoryQuantity1458[] beforeCaps,
        VanillaItemPrefixWorld1458? world,
        out OwnedInventoryQuantity1458[] afterCaps,
        out bool normalized)
    {
        normalized = false;
        afterCaps = beforeCaps;
        RuntimePlayerInventoryItem mouse = inventory[VanillaPlayerItemSlotCatalog.InventoryMouseItem];
        if (mouse.IsEmpty)
            return true;
        if (mouse.ItemType.Value is not (>= 71 and <= 74))
        {
            // Fully fitting GetItem return reuses admitted storage. Residual overflow
            // would need a source-world transaction; keep the source actor attached.
            if (!RuntimePlayerInventoryStore.TryPlanCursorReturn(inventory,
                funded,
                beforeCaps,
                world,
                false,
                out var nextItems,
                out var nextFunding,
                out var nextCaps,
                out var residual) ||
                !residual.IsEmpty)
                return false;
            nextItems.CopyTo(inventory, 0);
            nextFunding.CopyTo(funded, 0);
            afterCaps = nextCaps;
            normalized = true;
            return true;
        }

        // Coin transfer retains the existing first-empty-slot policy and exact copper
        // budget. Coin GetItem conversion during cursor return is outside this branch;
        // with no empty main slot the source player remains attached.
        for (short slot = VanillaPlayerItemSlotCatalog.MainInventoryStart; slot < VanillaPlayerItemSlotCatalog.MainInventoryEndExclusive; slot++)
        {
            if (!inventory[slot].IsEmpty)
                continue;
            // Move the source ceiling with the cursor image before validating the DTO.
            // Keep every other identity/slot obligation, including the emptied cursor lane.
            var caps = beforeCaps.ToList();
            int cursorCap = caps.FindIndex(c => c.Slot == VanillaPlayerItemSlotCatalog.InventoryMouseItem && c.ItemType == mouse.ItemType);
            int destinationCap = caps.FindIndex(c => c.Slot == slot && c.ItemType == mouse.ItemType);
            if (cursorCap >= 0 || funded[VanillaPlayerItemSlotCatalog.InventoryMouseItem] == mouse.Stack)
            {
                if (cursorCap >= 0)
                    caps[cursorCap] = caps[cursorCap] with
                    {
                        Maximum = checked((short)Math.Max(0, caps[cursorCap].Maximum - mouse.Stack))
                    };
                var nextCap = new OwnedInventoryQuantity1458(slot, mouse.ItemType, mouse.Stack);
                if (destinationCap >= 0)
                    caps[destinationCap] = nextCap;
                else if (mouse.ItemType.Value is 109 or 110 or 189 or 500 or 2209)
                    caps.Add(nextCap);
                afterCaps = caps.ToArray();
            }

            coinValues[slot] = coinValues[VanillaPlayerItemSlotCatalog.InventoryMouseItem];
            coinValues[VanillaPlayerItemSlotCatalog.InventoryMouseItem] = 0;
            funded[slot] = funded[VanillaPlayerItemSlotCatalog.InventoryMouseItem];
            funded[VanillaPlayerItemSlotCatalog.InventoryMouseItem] = 0;
            inventory[slot] = mouse;
            inventory[VanillaPlayerItemSlotCatalog.InventoryMouseItem] = default;
            normalized = true;
            return true;
        }

        return false;
    }

    private void ApplyPlayerTransferAttach(PlayerTransferAttachRuntimeCommand command)
    {
        ConnectionHandle connection = command.Connection;
        RuntimePlayerTransferState transfer = command.Transfer;
        if (transfer.PaidTorchFavor && (!transfer.OwnsTorchFavor || (transfer.Appearance?.TorchAndCartFlags & 4) != 4))
        {
            command.Completion.TrySetResult(false);
            return;
        }

        // Unknown imported clocks stay unknown; malformed represented clocks are not constructor facts.
        if (transfer.ItemPhase?.OwnedChannelExpectedType is { } expectedChannel && expectedChannel is not (16 or 34 or 79))
        {
            command.Completion.TrySetResult(false);
            return;
        }

        if (transfer.ItemPhase is { ClientUiPriceArithmeticMask: > 3 } or { ShopDiscountStates: > 15 } or { ToolTime: < 0 } or { AttackCD: < 0 } ||
            (transfer.ItemPhase?.MagicPayment is { } importedPayment &&
            (!importedPayment.IsValid ||
            !transfer.OwnsManaMaximum)) ||
            (transfer.ItemPhase?.ToolUse is { } importedTools && !importedTools.Clocks.IsValid) ||
            (transfer.ItemPhase?.DerivedCombat is { } derivedCombat &&
            !IsValidDerivedCombat(in derivedCombat)))
        {
            command.Completion.TrySetResult(false);
            return;
        }

        // Destination membership has no source town session. Retain possible open
        // latches while adding the closed alternatives before any owner writes;
        // this is a transfer ownership boundary, not a claimed UI-close packet.
        RuntimePlayerItemPhase1458? transferredPhase = transfer.ItemPhase is { } importedPhase ? importedPhase.CloseShopUiUnknownContexts() : null;
        if (transfer.Player.DerivedLifeMax is < 0 || transfer.Player.BaseLifeMax is < 0 or > short.MaxValue)
        {
            command.Completion.TrySetResult(false);
            return;
        }

        if (!float.IsFinite(transfer.Player.Luck) || (transfer.Player.LuckComponents is { } factors && !factors.IsFinite))
        {
            command.Completion.TrySetResult(false);
            return;
        }

        if ((transfer.Player.ItemAnimation is int animation &&
            animation is < 0 or > short.MaxValue) ||
            (transfer.Player.ItemRotation is float rotation &&
            !float.IsFinite(rotation)))
        {
            command.Completion.TrySetResult(false);
            return;
        }

        if (transfer.Player.Stealth is float incomingStealth && (!float.IsFinite(incomingStealth) || incomingStealth < 0f || incomingStealth > 1f))
        {
            command.Completion.TrySetResult(false);
            return;
        }

        if (!connection.IsAssigned ||
            transfer.Slot != connection.Player.Slot ||
            membership.Contains(connection.Player.Slot) ||
            transfer.Inventory.Length != VanillaPlayerItemSlotCatalog.InventoryCount ||
            !RuntimePlayerInventoryStore.AreValidFundedQuantities(transfer.FundedInventoryQuantities,
            transfer.Inventory) ||
            !RuntimePlayerInventoryStore.AreValidFundedCoinValues(transfer.FundedInventoryCoinValues,
            transfer.Inventory) ||
            !RuntimePlayerInventoryStore.AreValidShopBanks(transfer.ShopBankInventory,
            transfer.FundedShopBankCopper) ||
            !RuntimePlayerInventoryStore.AreValidShopBankOwnership(transfer.ShopBankInventory,
            transfer.FundedShopBankQuantities,
            transfer.OwnedShopBankQuantityCaps) ||
            inventory.Serial >= ulong.MaxValue - 5 ||
            transfer.OwnedInventoryQuantities is null ||
            !RuntimePlayerInventoryStore.AreValidOwnedQuantities(transfer.OwnedInventoryQuantities,
            transfer.Inventory) ||
            !inventory.CanAttachOwnedQuantities(transfer.OwnedInventoryQuantities.Length) ||
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

        inventory.AdoptOwnedQuantities(connection, transfer.OwnedInventoryQuantities);
        inventory.AdoptFundedQuantities(connection, transfer.FundedInventoryQuantities, transfer.FundedInventoryCoinValues);
        if (!inventory.TryAdoptShopBanks(connection,
            transfer.ShopBankInventory,
            transfer.FundedShopBankCopper,
            transfer.FundedShopBankQuantities,
            transfer.OwnedShopBankQuantityCaps))
            throw new InvalidOperationException("Validated bank transfer changed during adoption.");
        PlayerStateSnapshot previous = transfer.Player;
        // TerrariaServer 1.4.5.8 Player.Spawn_SetPosition treats SpawnX/SpawnY as the floor tile,
        // not as the player's top-left pixel position. Keep the authoritative snapshot aligned with the exact
        // client spawn formula so a cross-world transfer cannot place the 20x42 player body inside the floor.
        VanillaPlayerSpawnPosition1458.FromFloorTile(command.SpawnX, command.SpawnY, out float spawnPositionX, out float spawnPositionY);
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
            DerivedLifeMax = previous.DerivedLifeMax,
            BaseLifeMax = previous.BaseLifeMax,
            NpcLifeCurrent = command.ForceRespawn ? false : previous.NpcLifeCurrent,
            NpcHealth = command.ForceRespawn ? null : previous.NpcHealth,
            Debuffs = previous.Debuffs,
            IsDead = dead,
            Stealth = previous.Stealth ?? 1f,
            Zones = previous.Zones,
            Luck = previous.Luck,
            LuckComponents = previous.LuckComponents,
            ItemAnimation = preservePosition && !dead ? previous.ItemAnimation ?? 0 : 0,
            ItemRotation = previous.ItemRotation ?? 0f,
            HasMana = previous.HasMana,
            Mana = previous.Mana,
            MaxMana = previous.MaxMana,
            OwnsManaMaximum = transfer.OwnsManaMaximum,
            OwnsLifeMaximum = transfer.OwnsLifeMaximum,
            OwnedPermanentUpgradeMask = transfer.OwnedPermanentUpgradeMask,
            OwnsTorchFavor = transfer.OwnsTorchFavor,
            PaidTorchFavor = transfer.PaidTorchFavor,
            ItemPhase = transferredPhase is { } phase ? phase with
            {
                ManaCrystalIntent = null,
                SelectedPotionUse = null,
                QuickBuffExplanation = null,
                // HitTile coordinates belong to the old world. Retain known remaining
                // local clocks, but neither damage credit nor the old world's update stamp
                // may become a new-world tool entitlement on the replacement generation.
                ToolUse = phase.ToolUse is { } transferredTools ? transferredTools with
                {
                    Hits = ToolHitState1458.Constructor,
                    ReplacementHits = ToolHitState1458.Constructor,
                    PlacementAlternative = null,
                    PlacementReceipt = null,
                    PlacementReady = null,
                    PlacementHistoryEpoch = 0,
                    LastActionUpdate = long.MinValue,
                    Input = null,
                    WeaponShoot = null,
                    WeaponShootHistory = null,
                    HeldProjectile = null,
                    HeldProjectileType = 0,
                    HeldUseEpoch = 0,
                    HeldItem = default,
                    HeldPrefix = default,
                    HeldSlot = 0,
                    ShootEligible = false,
                    ToolStagePending = false,
                    LastFrameUpdate = long.MinValue,
                    SourceStagePose = null,
                    BuildingPermission = null,
                    ReleaseWitness = null,
                    DeferredHealthPublication = null,
                    Reach = null,
                    SourceDefinition = null,
                    SelectionInitialized = false,
                    DeferredSelectionSlot = null,
                    Clocks = transferredTools.Clocks with { PoundRelease = false, Channel = false },
                    RequiresFreshUse = true
                } : null,
                Selected = phase.Selected with
                {
                    Animation = preservePosition && !dead ? previous.ItemAnimation ?? 0 : 0
                },
                MagicPayment = phase.MagicPayment is { } payment ? payment with
                {
                    EligibleBirth = false,
                    BirthConsumed = true,
                    AwaitingPhase = false,
                    Refill = null,
                    ManaStage = null,
                    MidpointEligible = false,
                    MidpointConsumed = true
                }

                : null
            }

            : null,
            // Transfer attach executes a spawn boundary: source Spawn clears wet/lava contacts
            // while preserving jump/releaseJump. Missing imported provenance stays unknown.
            PhysicsPhase = transfer.PhysicsPhase is { } physics ? physics with
            {
                Contacts = physics.Contacts with
                {
                    Wet = false,
                    Lava = false
                }
            }

            : null,
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
            HasMount = preservePosition && previous.HasMount,
            PotionOfReturnOriginalPositionX = preservePosition ? previous.PotionOfReturnOriginalPositionX : 0f,
            PotionOfReturnOriginalPositionY = preservePosition ? previous.PotionOfReturnOriginalPositionY : 0f,
            PotionOfReturnHomePositionX = preservePosition ? previous.PotionOfReturnHomePositionX : 0f,
            PotionOfReturnHomePositionY = preservePosition ? previous.PotionOfReturnHomePositionY : 0f,
            CameraTargetX = preservePosition ? previous.CameraTargetX : 0f,
            CameraTargetY = preservePosition ? previous.CameraTargetY : 0f
        };
        damageImmunity.ResetPvp(connection.Player.Slot);
        membership.Commit(state);
        AttachNpcRawSlot(connection.Player);
        transferProfiles.Restore(connection, transfer.Appearance, transfer.Equipment, transfer.BuffTypes);
        // The compact import remains the trust boundary. An explicitly absent/edited compact
        // snapshot must not inherit stale full slots from a record copied with new import fields.
        if (transfer.BuffTypes is { } importedBuffs && transfer.BuffState is { } retainedBuffs && retainedBuffs.CaptureTypes().AsSpan().SequenceEqual(importedBuffs))
            transferProfiles.RestoreBuffState(connection, retainedBuffs);
        RecalculatePlayerLuck(state);
        VanillaPlayerSpawnPosition1458.ToFloorTile(positionX, positionY, out short eventSpawnX, out short eventSpawnY);
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, eventSpawnX, eventSpawnY, RespawnTimer: 0, DeathsPve: 0, DeathsPvp: 0, Team: state.Team, SpawnContext: 0);
        events?.PlayerSpawned(connection, in spawn);
        if (previous.Stealth.HasValue)
            events?.PlayerStealthUpdated(connection, state.Stealth);
        if (state.GodMode)
            events?.PlayerGodModeChanged(connection.Player, enabled: true);
        if (transfer.Appearance is PlayerAppearanceCommitRequest appearance)
        {
            PlayerAppearanceCommitRequest normalizedAppearance = appearance with
            {
                PlayerSlot = connection.Player.Slot
            };
            events?.PlayerAppearanceUpdated(connection, in normalizedAppearance);
        }

        if (transfer.BuffTypes is { } transferredBuffTypes)
        {
            var transferredBuffs = new PlayerBuffTypesCommitRequest(connection.Player.Slot, transferredBuffTypes);
            events?.PlayerBuffTypesUpdated(connection, in transferredBuffs);
        }

        if (transfer.MouseItemNormalized)
        {
            // TerrariaServer 1.4.5.8 maps packet-5 slot 58 to Main.mouseItem for the owning client. A world
            // replacement can run Player.dropItemCheck after the source snapshot was captured, so explicitly clear
            // the cursor before replaying the normalized inventory image in the destination world.
            PlayerEquipmentCommitRequest clearMouse = default(RuntimePlayerInventoryItem).ToCommitRequest(connection.Player.Slot, VanillaPlayerItemSlotCatalog.InventoryMouseItem);
            events?.PlayerEquipmentUpdated(connection, in clearMouse);
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
            PlayerEquipmentCommitRequest request = transfer.Equipment[i] with
            {
                PlayerSlot = connection.Player.Slot
            };
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

        var movement = new PlayerMovementCommitRequest(connection.Player.Slot,
            state.ControlFlags,
            state.MovementFlags,
            state.MiscFlags1,
            state.MiscFlags2,
            state.SelectedItem,
            state.PositionX,
            state.PositionY,
            HasVelocity: state.VelocityX != 0f ||
            state.VelocityY != 0f,
            state.VelocityX,
            state.VelocityY,
            HasMount: state.HasMount,
            state.MountType,
            HasPotionOfReturnPositions: preservePosition &&
            (state.PotionOfReturnOriginalPositionX != 0f ||
            state.PotionOfReturnOriginalPositionY != 0f ||
            state.PotionOfReturnHomePositionX != 0f ||
            state.PotionOfReturnHomePositionY != 0f),
            state.PotionOfReturnOriginalPositionX,
            state.PotionOfReturnOriginalPositionY,
            state.PotionOfReturnHomePositionX,
            state.PotionOfReturnHomePositionY,
            HasCameraTarget: preservePosition &&
            (state.CameraTargetX != 0f ||
            state.CameraTargetY != 0f),
            state.CameraTargetX,
            state.CameraTargetY);
        events?.PlayerMoved(connection, in movement);
        command.Completion.TrySetResult(true);
    }

    private bool IsTransferPositionValid(float positionX, float positionY)
    {
        if (worldTiles is null)
            return false;
        WorldTileDimensions dimensions = worldTiles.Dimensions;
        return VanillaPlayerWorldBounds1458.ContainsTopLeft(in dimensions, positionX, positionY);
    }
}
