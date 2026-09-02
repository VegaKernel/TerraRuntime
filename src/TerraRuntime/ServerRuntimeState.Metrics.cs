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
    public long AppliedCommands { get; private set; }

    internal RuntimeNpcShopCatalogRegistry NpcShops => _npcShops;

    internal bool TryGetPlayerTownShopSession(PlayerHandle player, out RuntimeTownShopSession1458? session)
    {
        if (!player.IsAssigned ||
            !_players.TryGetValue(player.Slot.Value, out RuntimePlayerState? state) ||
            state.Connection.Player != player ||
            _townShopSessions[player.Slot.Value] is not RuntimeTownShopSession1458 current)
        {
            session = null;
            return false;
        }

        session = current;
        return true;
    }

    internal RuntimeNpcArchetypeRegistry NpcArchetypes => _npcArchetypes;

    internal IWorldItemSpawnRandom WorldItemSpawnRandom => _worldItemSpawnRandom;

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

    public long AppliedProjectileReflections { get; private set; }

    public long RejectedClientProjectileUpdates { get; private set; }

    public long RejectedClientProjectileDestroys { get; private set; }

    public long AppliedClientNpcDamage { get; private set; }

    public long RejectedClientNpcDamage { get; private set; }

    public long RelayedUnknownProjectileDestroys { get; private set; }

    public long ClientTileManipulationRequests { get; private set; }

    public long ValidatedClientTileManipulations { get; private set; }

    public long AppliedClientTileManipulations { get; private set; }

    public long RejectedClientTileManipulations { get; private set; }

    public long UnsupportedClientTileManipulations { get; private set; }

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
}
