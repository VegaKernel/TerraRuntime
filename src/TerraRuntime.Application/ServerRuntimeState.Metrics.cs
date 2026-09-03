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

    internal RuntimeNpcShopCatalogRegistry NpcShops => _npcs.Shops;

    internal bool TryGetPlayerTownShopSession(PlayerHandle player, out RuntimeTownShopSession1458? session)
        => _players.TryGetTownShopSession(player, out session);

    internal RuntimeNpcArchetypeRegistry NpcArchetypes => _npcs.Archetypes;

    internal IWorldItemSpawnRandom WorldItemSpawnRandom => _worldItems.SpawnRandom;

    internal RuntimeWorldProgressionMutations WorldProgression => _worldProgression;

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

    public long AppliedNpcSpawns => _npcs.AppliedSpawns;

    public long RejectedNpcSpawns => _npcs.RejectedSpawns;

    public long AppliedNpcUpdates => _npcs.AppliedUpdates;

    public long RejectedNpcUpdates => _npcs.RejectedUpdates;

    public long AppliedNpcDespawns => _npcs.AppliedDespawns;

    public long RejectedNpcDespawns => _npcs.RejectedDespawns;

    public long AppliedProjectileSpawns => _projectiles.AppliedSpawns;

    public long RejectedProjectileSpawns => _projectiles.RejectedSpawns;

    public long AppliedProjectileUpdates => _projectiles.AppliedUpdates;

    public long RejectedProjectileUpdates => _projectiles.RejectedUpdates;

    public long AppliedProjectileDespawns => _projectiles.AppliedDespawns;

    public long RejectedProjectileDespawns => _projectiles.RejectedDespawns;

    public long AppliedProjectileReflections => _projectiles.AppliedReflections;

    public long RejectedClientProjectileUpdates => _projectiles.RejectedClientUpdates;

    public long RejectedClientProjectileDestroys => _projectiles.RejectedClientDestroys;

    public long RejectedTrustedClientProjectileUpdates => _projectiles.RejectedTrustedClientUpdates;

    public long RejectedTrustedClientProjectileDestroys => _projectiles.RejectedTrustedClientDestroys;

    public long AppliedClientNpcDamage => _npcs.AppliedClientDamage;

    public long RejectedClientNpcDamage => _npcs.RejectedClientDamage;

    public long RelayedUnknownProjectileDestroys => _projectiles.RelayedUnknownDestroys;

    public long ClientTileManipulationRequests => _worldTileAuthority.ClientManipulationRequests;

    public long ValidatedClientTileManipulations => _worldTileAuthority.ValidatedClientManipulations;

    public long AppliedClientTileManipulations => _worldTileAuthority.AppliedClientManipulations;

    public long RejectedClientTileManipulations => _worldTileAuthority.RejectedClientManipulations;

    public long UnsupportedClientTileManipulations => _worldTileAuthority.UnsupportedClientManipulations;

    public long AppliedWorldItemAllocations =>
        _worldItems.AppliedAllocations + _worldTileAuthority.AppliedWorldItemAllocations;

    public long RejectedWorldItemAllocations =>
        _worldItems.RejectedAllocations + _worldTileAuthority.RejectedWorldItemAllocations;

    public long AppliedWorldItemDrops => _worldItems.AppliedDrops;

    public long RejectedWorldItemDrops => _worldItems.RejectedDrops;

    public long AppliedWorldItemRemovals => _worldItems.AppliedRemovals;

    public long RejectedWorldItemRemovals => _worldItems.RejectedRemovals;

    public long AppliedWorldItemOwners => _worldItems.AppliedOwners;

    public long RejectedWorldItemOwners => _worldItems.RejectedOwners;

    public NpcAiStateTickSummary LastNpcAiTick => _npcs.LastAiTick;

    public ProjectileStateTickSummary LastProjectileTick => _projectiles.LastTick;

    public PlayerSlotId? LastMovementPlayerSlot => _players.LastMovementSlot;

    public float LastMovementPositionX => _players.LastMovementPositionX;

    public float LastMovementPositionY => _players.LastMovementPositionY;

    public int LastWorkerResult => Volatile.Read(ref lastWorkerResult);

    public PlayerSpawnCommitResult? LastSpawnCommitResult => _players.LastSpawnCommitResult;
}
