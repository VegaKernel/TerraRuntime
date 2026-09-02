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
        => _players.TryGetTownShopSession(player, out session);

    internal RuntimeNpcArchetypeRegistry NpcArchetypes => _npcArchetypes;

    internal IWorldItemSpawnRandom WorldItemSpawnRandom => _worldItemSpawnRandom;

    public long Updates { get; private set; }

    public long AppliedPlayerAppearances => _players.AppliedAppearances;

    public long RejectedPlayerAppearances => _players.RejectedAppearances;

    public long AppliedPlayerEquipmentUpdates => _players.AppliedEquipmentUpdates;

    public long RejectedPlayerEquipmentUpdates => _players.RejectedEquipmentUpdates;

    public long AppliedPlayerHealthUpdates => _players.AppliedHealthUpdates;

    public long RejectedPlayerHealthUpdates => _players.RejectedHealthUpdates;

    public long AppliedPlayerManaUpdates => _players.AppliedManaUpdates;

    public long RejectedPlayerManaUpdates => _players.RejectedManaUpdates;

    public long CommittedPlayerSpawns => _players.CommittedSpawns;

    public long AppliedPlayerMovements => _players.AppliedMovements;

    public long RejectedPlayerMovements => _players.RejectedMovements;

    public long DisconnectedPlayers => _players.DisconnectedPlayers;

    public long AppliedNpcSpawns { get; private set; }

    public long RejectedNpcSpawns { get; private set; }

    public long AppliedNpcUpdates { get; private set; }

    public long RejectedNpcUpdates { get; private set; }

    public long AppliedNpcDespawns { get; private set; }

    public long RejectedNpcDespawns { get; private set; }

    public long AppliedProjectileSpawns => _projectiles.AppliedSpawns;

    public long RejectedProjectileSpawns => _projectiles.RejectedSpawns;

    public long AppliedProjectileUpdates => _projectiles.AppliedUpdates;

    public long RejectedProjectileUpdates => _projectiles.RejectedUpdates;

    public long AppliedProjectileDespawns => _projectiles.AppliedDespawns;

    public long RejectedProjectileDespawns => _projectiles.RejectedDespawns;

    public long AppliedProjectileReflections => _projectiles.AppliedReflections;

    public long RejectedClientProjectileUpdates => _projectiles.RejectedClientUpdates;

    public long RejectedClientProjectileDestroys => _projectiles.RejectedClientDestroys;

    public long AppliedClientNpcDamage { get; private set; }

    public long RejectedClientNpcDamage { get; private set; }

    public long RelayedUnknownProjectileDestroys => _projectiles.RelayedUnknownDestroys;

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

    public ProjectileStateTickSummary LastProjectileTick => _projectiles.LastTick;

    public PlayerSlotId? LastMovementPlayerSlot => _players.LastMovementSlot;

    public float LastMovementPositionX => _players.LastMovementPositionX;

    public float LastMovementPositionY => _players.LastMovementPositionY;

    public int LastWorkerResult => Volatile.Read(ref lastWorkerResult);

    public PlayerSpawnCommitResult? LastSpawnCommitResult => _players.LastSpawnCommitResult;
}
