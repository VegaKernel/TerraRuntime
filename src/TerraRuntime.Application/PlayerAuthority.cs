using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal enum PlayerDamageCommitResult : byte
{
    Rejected = 0,
    Committed = 1,
    AvoidedByGodMode = 2
}

/// <summary>
/// Owns authoritative client-player command application and all mutable player state for one world.
/// The enclosing world loop remains the sole caller, so extraction does not introduce another writer.
/// </summary>
internal sealed partial class PlayerAuthority
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
    private readonly RuntimePlayerDamageImmunityStore damageImmunity = new(MaxPlayerSlots);
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
            case PlayerRespawnRuntimeCommand respawn:
                ApplyPlayerRespawn(respawn);
                return true;
            case PlayerTeleportRuntimeCommand teleport:
                ApplyPlayerTeleport(teleport);
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
            case SetPlayerGodModeRuntimeCommand godMode:
                ApplySetPlayerGodMode(godMode);
                return true;
            case GetPlayerGodModeRuntimeCommand godModeQuery:
                ApplyGetPlayerGodMode(godModeQuery);
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

    public bool TryConsumeMana(ConnectionHandle connection, int manaCost)
    {
        if (manaCost <= 0 || !membership.TryGet(connection, out RuntimePlayerMember? player) ||
            player.Connection != connection || !player.HasMana || player.Mana < manaCost || !player.TryAdvanceRevision())
        {
            return false;
        }

        player.Mana = checked((short)(player.Mana - manaCost));
        var request = new PlayerManaCommitRequest(player.Slot, player.Mana, player.MaxMana);
        events?.PlayerManaUpdated(connection, in request);
        return true;
    }

    public bool TryClampVanillaReachableAim(
        PlayerHandle playerHandle,
        float requestedX,
        float requestedY,
        out float targetX,
        out float targetY)
    {
        targetX = targetY = 0f;
        if (!float.IsFinite(requestedX) || !float.IsFinite(requestedY) || worldTiles is null ||
            !membership.TryGet(playerHandle, out RuntimePlayerMember? player) || player.IsDead)
        {
            return false;
        }

        const float viewWidth = 1920f;
        const float viewHeight = 1200f;
        const float border = 640f;
        float worldRight = worldTiles.Dimensions.WidthTiles * 16f - border;
        float worldBottom = worldTiles.Dimensions.HeightTiles * 16f - border;
        if (worldRight - border < viewWidth || worldBottom - border < viewHeight)
            return false;

        float playerCenterX = player.PositionX + VanillaBasePlayerWidth * 0.5f;
        float playerCenterY = player.PositionY + VanillaBasePlayerHeight * 0.5f;
        float left = Math.Clamp(playerCenterX - viewWidth * 0.5f, border, worldRight - viewWidth);
        float top = Math.Clamp(playerCenterY - viewHeight * 0.5f, border, worldBottom - viewHeight);
        float centerX = left + viewWidth * 0.5f;
        float centerY = top + viewHeight * 0.5f;
        float dx = requestedX - centerX;
        float dy = requestedY - centerY;
        float scale = 1f;
        float ax = MathF.Abs(dx);
        float ay = MathF.Abs(dy);
        if (ax > viewWidth * 0.5f)
            scale = MathF.Min(scale, viewWidth * 0.5f / ax);
        if (ay > viewHeight * 0.5f)
            scale = MathF.Min(scale, viewHeight * 0.5f / ay);
        targetX = centerX + dx * scale;
        targetY = centerY + dy * scale;
        return float.IsFinite(targetX) && float.IsFinite(targetY);
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
        PlayerHealthCommitRequest received = health.Request;
        PlayerHealthCommitRequest request = VanillaVitalsRules.NormalizeHealth(in received);
        if (!health.Connection.IsAssigned || health.Connection.Player.Slot != request.PlayerSlot)
        {
            RejectedHealthUpdates++;
            return;
        }

        if (membership.TryGet(request.PlayerSlot, out RuntimePlayerMember? activePlayer))
        {
            if (activePlayer.Connection != health.Connection)
            {
                RejectedHealthUpdates++;
                return;
            }

            // Creative-style god mode owns incoming damage too. Terraria's lava, drowning and fall damage are
            // client-local Player.Hurt sources; until those environmental systems are fully server-simulated,
            // packet 16 is the only place they can attempt to lower authoritative HP. Never accept that decrease.
            // Re-send the current authoritative value to the owner so the client is corrected immediately.
            if (activePlayer.GodMode && activePlayer.HasHealth && request.Life < activePlayer.Life)
            {
                RejectedHealthUpdates++;
                var correction = new PlayerHealthCommitRequest(
                    activePlayer.Slot,
                    activePlayer.Life,
                    activePlayer.MaxLife);
                events?.PlayerAuthoritativeHealthUpdated(activePlayer.Connection, in correction);
                return;
            }

            if (!activePlayer.TryAdvanceRevision())
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
        damageImmunity.ResetPvp(request.ClaimedSlot);
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

    private void ApplyPlayerRespawn(PlayerRespawnRuntimeCommand respawn)
    {
        PlayerSpawnCommitRequest request = respawn.Request;
        if (!VanillaPlayerSpawnValidator.IsValid(in request) ||
            !respawn.Connection.IsAssigned ||
            respawn.Connection.Player.Slot != request.ClaimedSlot ||
            !membership.TryGet(respawn.Connection, out RuntimePlayerMember? player))
        {
            Volatile.Write(ref lastSpawnCommitResult, (int)PlayerSpawnCommitResult.InvalidSpawnData);
            return;
        }

        if (!player.TryAdvanceRevision())
            return;

        player.Team = request.Team;
        player.PositionX = request.SpawnX * 16f;
        player.PositionY = request.SpawnY * 16f;
        player.VelocityX = 0f;
        player.VelocityY = 0f;
        player.IsDead = request.RespawnTimer > 0;
        damageImmunity.ResetPvp(request.ClaimedSlot);
        events?.PlayerRespawned(respawn.Connection, in request);
    }

    private void ApplyPlayerTeleport(PlayerTeleportRuntimeCommand teleport)
    {
        if (worldTiles is null || !membership.TryGet(teleport.Connection, out RuntimePlayerMember? player))
            return;

        if (!TryResolveTeleportDestination(worldTiles, player, teleport.Kind, out float positionX, out float positionY, out byte style))
        {
            // Vanilla still emits a failed teleport effect at the current position. Keep the authoritative
            // position unchanged and let the client render the item effect without inventing coordinates.
            events?.PlayerTeleported(teleport.Connection, player.PositionX, player.PositionY, style: teleport.Kind == RuntimePlayerTeleportRequestKind.MagicConch ? (byte)5 : (byte)7, failed: true);
            return;
        }

        if (!player.TryAdvanceRevision())
            return;
        player.PositionX = positionX;
        player.PositionY = positionY;
        player.VelocityX = 0f;
        player.VelocityY = 0f;
        events?.PlayerTeleported(teleport.Connection, positionX, positionY, style, failed: false);
    }

    private static bool TryResolveTeleportDestination(
        WorldTileStore tiles,
        RuntimePlayerMember player,
        RuntimePlayerTeleportRequestKind kind,
        out float positionX,
        out float positionY,
        out byte style)
    {
        int width = tiles.Dimensions.WidthTiles;
        int height = tiles.Dimensions.HeightTiles;
        style = kind == RuntimePlayerTeleportRequestKind.MagicConch ? (byte)5 : (byte)7;

        if (kind == RuntimePlayerTeleportRequestKind.MagicConch)
        {
            bool currentlyLeft = player.PositionX < width * 8f;
            if (TryFindOceanLanding(tiles, currentlyLeft, out int x, out int y) ||
                TryFindOceanLanding(tiles, !currentlyLeft, out x, out y))
            {
                ToPlayerPosition(x, y, out positionX, out positionY);
                return true;
            }
        }
        else if (kind == RuntimePlayerTeleportRequestKind.DemonConch)
        {
            int underworldStart = Math.Clamp(height - 180, 40, height - 20);
            int middle = width / 2;
            ReadOnlySpan<int> centers = stackalloc int[]
            {
                middle, middle - 75, middle + 75, 300, Math.Max(100, width - 300)
            };
            foreach (int center in centers)
            {
                int left = Math.Clamp(center - 50, 10, width - 11);
                int right = Math.Clamp(center + 50, 11, width - 10);
                for (int x = left; x <= right; x += 3)
                {
                    for (int y = underworldStart; y < Math.Min(height - 5, underworldStart + 100); y++)
                    {
                        if (!IsSafeTeleportFloor(tiles, x, y, avoidWalls: true))
                            continue;
                        ToPlayerPosition(x, y, out positionX, out positionY);
                        return true;
                    }
                }
            }
        }

        positionX = positionY = 0f;
        return false;
    }

    private static bool TryFindOceanLanding(WorldTileStore tiles, bool leftSide, out int landingX, out int landingY)
    {
        int width = tiles.Dimensions.WidthTiles;
        int height = tiles.Dimensions.HeightTiles;
        int edgePadding = 55;
        int inlandLimit = Math.Clamp(width / 12, 220, 650);
        int start = leftSide ? edgePadding : width - edgePadding - 1;
        int end = leftSide ? inlandLimit : width - inlandLimit;
        int step = leftSide ? 2 : -2;

        for (int x = start; leftSide ? x <= end : x >= end; x += step)
        {
            for (int y = 40; y < Math.Min(height - 5, height / 2); y++)
            {
                if (!IsSafeTeleportFloor(tiles, x, y, avoidWalls: false))
                    continue;
                landingX = x;
                landingY = y;
                return true;
            }
        }

        landingX = landingY = 0;
        return false;
    }

    private static bool IsSafeTeleportFloor(WorldTileStore tiles, int x, int floorY, bool avoidWalls)
    {
        if ((uint)x >= (uint)tiles.Dimensions.WidthTiles || floorY < 4 || floorY >= tiles.Dimensions.HeightTiles - 1)
            return false;

        WorldTile floor = tiles.Get(x, floorY);
        if (!floor.IsActive || floor.IsActuated ||
            (!VanillaTileCollisionCatalog.IsSolid(floor.TileType) && !VanillaTileCollisionCatalog.IsSolidTop(floor.TileType)))
            return false;

        // A vanilla player is a little over 2.5 tiles tall. Require a 2x3 clear body volume and no liquid.
        for (int dx = -1; dx <= 0; dx++)
        {
            for (int dy = 1; dy <= 3; dy++)
            {
                WorldTile body = tiles.Get(x + dx, floorY - dy);
                if ((body.IsActive && !body.IsActuated && VanillaTileCollisionCatalog.IsSolid(body.TileType)) ||
                    body.LiquidAmount != 0 ||
                    (avoidWalls && body.Wall != 0))
                    return false;
            }
        }
        return true;
    }

    private static void ToPlayerPosition(int landingTileX, int floorTileY, out float positionX, out float positionY)
    {
        positionX = landingTileX * 16f - VanillaBasePlayerWidth * 0.5f + 8f;
        positionY = floorTileY * 16f + 16f - VanillaBasePlayerHeight;
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


}
