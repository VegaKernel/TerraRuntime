using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
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

    public RuntimeGroundFighterDoorOpeningSink(
        WorldTileStore tiles,
        RuntimeTileManipulationReplicationRegistry? replication = null,
        IVanillaTallGateOccupancyProbe? tallGateOccupancy = null,
        RuntimeWorldItemStore? worldItems = null,
        IWorldItemSpawnRandom? worldItemSpawnRandom = null)
    {
        openings = new VanillaWorldGroundFighterDoorOpeningService(
            tiles ?? throw new ArgumentNullException(nameof(tiles)),
            tallGateOccupancy);
        this.replication = replication;
        this.worldItems = worldItems;
        this.worldItemSpawnRandom = worldItemSpawnRandom;
        if ((worldItems is null) != (worldItemSpawnRandom is null))
        {
            throw new ArgumentException(
                "Door object drops require both an authoritative item store and spawn RNG.",
                nameof(worldItems));
        }
    }

    public bool TryOpen(in VanillaGroundFighterDoorOpeningIntent intent)
    {
        if (!openings.TryOpen(in intent, out VanillaGroundFighterDoorOpeningMutation mutation))
            return false;

        if (!mutation.DropItem.IsNone && worldItems is not null && worldItemSpawnRandom is not null)
        {
            WorldItemDropStateUpdate drop = VanillaSimpleTileBreakResolver1458.MaterializeItemState(
                mutation.DropItem,
                stack: 1,
                mutation.DropTileX,
                mutation.DropTileY,
                worldItemSpawnRandom);
            _ = worldItems.TryAllocateDrop(in drop, out _);
        }

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
                return true;
            }

            byte action = mutation.Kind switch
            {
                VanillaGroundFighterDoorOpeningKind.Door => (byte)TerrariaDoorToggleAction.OpenDoor,
                VanillaGroundFighterDoorOpeningKind.TallGate => (byte)TerrariaDoorToggleAction.OpenTallGate,
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

        return true;
    }
}
