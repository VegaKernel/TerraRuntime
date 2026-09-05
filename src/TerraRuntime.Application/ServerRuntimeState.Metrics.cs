using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class ServerRuntimeState
{
    public long AppliedCommands => _runtime.Commands.Current;

    internal RuntimeNpcShopCatalogRegistry NpcShops => _runtime.Npcs.Shops;

    internal bool TryGetPlayerTownShopSession(PlayerHandle player, out RuntimeTownShopSession1458? session)
        => _runtime.Players.TryGetTownShopSession(player, out session);

    internal RuntimeNpcArchetypeRegistry NpcArchetypes => _runtime.Npcs.Archetypes;

    internal IWorldItemSpawnRandom WorldItemSpawnRandom => _runtime.WorldItems.SpawnRandom;

    internal RuntimeWorldProgressionMutations WorldProgression => _runtime.WorldProgression;

    public long Updates => _runtime.Updates.Current;

    public long AppliedPlayerAppearances => _runtime.Players.AppliedAppearances;

    public long RejectedPlayerAppearances => _runtime.Players.RejectedAppearances;

    public long AppliedPlayerEquipmentUpdates => _runtime.Players.AppliedEquipmentUpdates;

    public long RejectedPlayerEquipmentUpdates => _runtime.Players.RejectedEquipmentUpdates;

    public long AppliedPlayerHealthUpdates => _runtime.Players.AppliedHealthUpdates;

    public long RejectedPlayerHealthUpdates => _runtime.Players.RejectedHealthUpdates;

    public long AppliedPlayerManaUpdates => _runtime.Players.AppliedManaUpdates;

    public long RejectedPlayerManaUpdates => _runtime.Players.RejectedManaUpdates;

    public long CommittedPlayerSpawns => _runtime.Players.CommittedSpawns;

    public long AppliedPlayerMovements => _runtime.Players.AppliedMovements;

    public long RejectedPlayerMovements => _runtime.Players.RejectedMovements;

    public long DisconnectedPlayers => _runtime.Players.DisconnectedPlayers;

    public long AppliedNpcSpawns => _runtime.Npcs.AppliedSpawns;

    public long RejectedNpcSpawns => _runtime.Npcs.RejectedSpawns;

    public long AppliedNpcUpdates => _runtime.Npcs.AppliedUpdates;

    public long RejectedNpcUpdates => _runtime.Npcs.RejectedUpdates;

    public long AppliedNpcDespawns => _runtime.Npcs.AppliedDespawns;

    public long RejectedNpcDespawns => _runtime.Npcs.RejectedDespawns;

    public long AppliedProjectileSpawns => _runtime.Projectiles.AppliedSpawns;

    public long RejectedProjectileSpawns => _runtime.Projectiles.RejectedSpawns;

    public long AppliedProjectileUpdates => _runtime.Projectiles.AppliedUpdates;

    public long RejectedProjectileUpdates => _runtime.Projectiles.RejectedUpdates;

    public long AppliedProjectileDespawns => _runtime.Projectiles.AppliedDespawns;

    public long RejectedProjectileDespawns => _runtime.Projectiles.RejectedDespawns;

    public long AppliedProjectileReflections => _runtime.Projectiles.AppliedReflections;

    public long RejectedClientProjectileUpdates => _runtime.Projectiles.RejectedClientUpdates;

    public long RejectedClientProjectileDestroys => _runtime.Projectiles.RejectedClientDestroys;

    public long RejectedTrustedClientProjectileUpdates => _runtime.Projectiles.RejectedTrustedClientUpdates;

    public long AcceptedTrustedProjectileSteeringInputs => _runtime.Projectiles.AcceptedTrustedSteeringInputs;

    public long RejectedTrustedClientProjectileDestroys => _runtime.Projectiles.RejectedTrustedClientDestroys;

    public long AppliedClientNpcDamage => _runtime.Npcs.AppliedClientDamage;

    public long RejectedClientNpcDamage => _runtime.Npcs.RejectedClientDamage;

    public long RelayedUnknownProjectileDestroys => _runtime.Projectiles.RelayedUnknownDestroys;

    public long ClientTileManipulationRequests => _runtime.WorldTileAuthority.ClientManipulationRequests;

    public long ValidatedClientTileManipulations => _runtime.WorldTileAuthority.ValidatedClientManipulations;

    public long AppliedClientTileManipulations => _runtime.WorldTileAuthority.AppliedClientManipulations;

    public long RejectedClientTileManipulations => _runtime.WorldTileAuthority.RejectedClientManipulations;

    public long UnsupportedClientTileManipulations => _runtime.WorldTileAuthority.UnsupportedClientManipulations;

    public long AppliedWorldItemAllocations =>
        _runtime.WorldItems.AppliedAllocations + _runtime.WorldTileAuthority.AppliedWorldItemAllocations;

    public long RejectedWorldItemAllocations =>
        _runtime.WorldItems.RejectedAllocations + _runtime.WorldTileAuthority.RejectedWorldItemAllocations;

    public long AppliedWorldItemDrops => _runtime.WorldItems.AppliedDrops;

    public long RejectedWorldItemDrops => _runtime.WorldItems.RejectedDrops;

    public long AppliedWorldItemRemovals => _runtime.WorldItems.AppliedRemovals;

    public long RejectedWorldItemRemovals => _runtime.WorldItems.RejectedRemovals;

    public long AppliedWorldItemOwners => _runtime.WorldItems.AppliedOwners;

    public long RejectedWorldItemOwners => _runtime.WorldItems.RejectedOwners;

    public NpcAiStateTickSummary LastNpcAiTick => _runtime.Npcs.LastAiTick;

    public ProjectileStateTickSummary LastProjectileTick => _runtime.Projectiles.LastTick;

    public PlayerSlotId? LastMovementPlayerSlot => _runtime.Players.LastMovementSlot;

    public float LastMovementPositionX => _runtime.Players.LastMovementPositionX;

    public float LastMovementPositionY => _runtime.Players.LastMovementPositionY;

    public int LastWorkerResult => Volatile.Read(ref lastWorkerResult);

    public PlayerSpawnCommitResult? LastSpawnCommitResult => _runtime.Players.LastSpawnCommitResult;
}
