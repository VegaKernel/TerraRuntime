using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// Runtime adapter between AI_003 typed door intents, the authoritative WorldGen-shaped tile mutation service,
/// item drops and packet-17/19 replication. World mutation is the commit point: a transient outbound queue rejection
/// never rolls back authoritative tiles or leaves fighter ai[1] pretending the door remained closed.
/// </summary>
internal sealed class RuntimeGroundFighterDoorOpeningSink : IVanillaGroundFighterDoorOpeningSink
{
    private readonly VanillaWorldGroundFighterDoorOpeningService openings;
    private readonly RuntimeTileManipulationReplicationRegistry? replication;
    private readonly RuntimeWorldItemStore? worldItems;
    private readonly IWorldItemSpawnRandom? worldItemSpawnRandom;
    private readonly IRuntimePlayerSlotSnapshotLookup? playerSnapshots;

    public RuntimeGroundFighterDoorOpeningSink(
        WorldTileStore tiles,
        RuntimeTileManipulationReplicationRegistry? replication = null,
        IVanillaTallGateOccupancyProbe? tallGateOccupancy = null,
        RuntimeWorldItemStore? worldItems = null,
        IWorldItemSpawnRandom? worldItemSpawnRandom = null,
        IVanillaDoorCloseRandom1458? doorCloseRandom = null,
        IRuntimePlayerSlotSnapshotLookup? playerSnapshots = null)
    {
        openings = new VanillaWorldGroundFighterDoorOpeningService(
            tiles ?? throw new ArgumentNullException(nameof(tiles)),
            tallGateOccupancy, doorCloseRandom);
        this.replication = replication;
        this.worldItems = worldItems;
        this.worldItemSpawnRandom = worldItemSpawnRandom;
        this.playerSnapshots = playerSnapshots;
        if ((worldItems is null) != (worldItemSpawnRandom is null))
        {
            throw new ArgumentException(
                "Door object drops require both an authoritative item store and spawn RNG.",
                nameof(worldItems));
        }
    }

    public bool TryOpen(in VanillaGroundFighterDoorOpeningIntent intent)
    {
        RuntimeWorldItemStore.AllocationPreview? allocation = null;
        VanillaUnifiedRandom1458? preparedRandom = null;
        var original = worldItemSpawnRandom as SystemWorldItemSpawnRandom;
        if (intent.Operation == VanillaGroundFighterDoorOperation.Destroy && worldItems is not null)
        {
            if (!openings.TryPrepareDestruction(in intent, out var prepared)) return false;
            preparedRandom = original?.SourceRandom.Clone();
            IWorldItemSpawnRandom spawnRandom = preparedRandom is null ? worldItemSpawnRandom! : new SystemWorldItemSpawnRandom(preparedRandom);
            var drop = VanillaSimpleTileBreakResolver1458.MaterializeItemState(prepared.DropItem, 1,
                prepared.DropTileX, prepared.DropTileY, spawnRandom);
            Span<WorldItemAllocationPlayer1458> views = stackalloc WorldItemAllocationPlayer1458[byte.MaxValue];
            int count = 0;
            for (int slot = 0; slot < byte.MaxValue; slot++)
            {
                if (playerSnapshots?.TryGetPlayer(new((byte)slot), out var player) != true || !player.Player.IsAssigned) continue;
                var size = player.HasMount ? VanillaPlayerMountHitbox1458.Resolve(player.MountType) :
                    (VanillaPlayerHitboxFacts.BaseWidth, VanillaPlayerHitboxFacts.BaseHeight);
                views[count++] = new((byte)slot, player.PositionX, player.PositionY, (int)size.Item1, (int)size.Item2);
            }
            allocation = worldItems.CreateAllocationPreview(views[..count]);
            if (!allocation.TrySpawnSource(in drop, 0, out _) || !allocation.TryClaim())
            { allocation.Dispose(); return false; }
        }
        using var preparedAllocation = allocation;
        if (!openings.TryOpen(in intent, out VanillaGroundFighterDoorOpeningMutation mutation))
            return false;

        if (allocation is not null)
        {
            if (preparedRandom is not null) original!.SourceRandom.CopyStateFrom(preparedRandom);
            if (!allocation.TryCommitNext(out _, out _))
                throw new InvalidOperationException("A prepared door item allocation changed after authoritative destruction.");
        }
        Publish(in mutation, closing: false);
        return true;
    }

    public bool TryClose(int tileX, int tileY, bool tallGate, int direction)
    {
        bool changed = tallGate
            ? openings.TryShiftTallGate(tileX, tileY, closing: true, forced: false, out var mutation)
            : openings.TryCloseDoor(tileX, tileY, forced: false, out mutation);
        if (!changed) return false;
        mutation = mutation with { DirectionX = direction };
        Publish(in mutation, closing: true);
        return true;
    }

    private void Publish(in VanillaGroundFighterDoorOpeningMutation mutation, bool closing)
    {
        if (replication is not null &&
            mutation.PacketTileX >= short.MinValue && mutation.PacketTileX <= short.MaxValue &&
            mutation.PacketTileY >= short.MinValue && mutation.PacketTileY <= short.MaxValue)
        {
            if (mutation.Kind is VanillaGroundFighterDoorOpeningKind.DestroyedDoor or
                VanillaGroundFighterDoorOpeningKind.DestroyedTallGate)
            {
                var state = new TerrariaTileManipulationState(
                    (byte)TerrariaTileManipulationAction.KillTile,
                    checked((short)mutation.PacketTileX),
                    checked((short)mutation.PacketTileY),
                    Data: 0,
                    Style: 0);
                replication.TryPublishCommitted(GameCommandSourceId.System, in state);
                return;
            }

            byte action = mutation.Kind switch
            {
                VanillaGroundFighterDoorOpeningKind.Door => (byte)(closing ? TerrariaDoorToggleAction.CloseDoor : TerrariaDoorToggleAction.OpenDoor),
                VanillaGroundFighterDoorOpeningKind.TallGate => (byte)(closing ? TerrariaDoorToggleAction.CloseTallGate : TerrariaDoorToggleAction.OpenTallGate),
                _ => byte.MaxValue
            };

            if (action != byte.MaxValue)
            {
                var state = new TerrariaDoorToggleState(
                    action,
                    checked((short)mutation.PacketTileX),
                    checked((short)mutation.PacketTileY),
                    mutation.Kind == VanillaGroundFighterDoorOpeningKind.TallGate
                        ? -1
                        : mutation.DirectionX);
                replication.TryPublishDoorToggle(in state);
            }
        }

    }
}
