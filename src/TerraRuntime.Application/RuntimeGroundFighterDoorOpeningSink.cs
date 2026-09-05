using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// Runtime adapter between AI_003 typed opening intents, the authoritative WorldGen-shaped tile mutation service,
/// and packet-19 replication. World mutation is the commit point: a transient outbound queue rejection never rolls
/// back authoritative tiles or leaves fighter ai[1] pretending the door remained closed.
/// </summary>
internal sealed class RuntimeGroundFighterDoorOpeningSink : IVanillaGroundFighterDoorOpeningSink
{
    private readonly VanillaWorldGroundFighterDoorOpeningService openings;
    private readonly RuntimeTileManipulationReplicationRegistry? replication;

    public RuntimeGroundFighterDoorOpeningSink(
        WorldTileStore tiles,
        RuntimeTileManipulationReplicationRegistry? replication = null,
        IVanillaTallGateOccupancyProbe? tallGateOccupancy = null)
    {
        openings = new VanillaWorldGroundFighterDoorOpeningService(
            tiles ?? throw new ArgumentNullException(nameof(tiles)),
            tallGateOccupancy);
        this.replication = replication;
    }

    public bool TryOpen(in VanillaGroundFighterDoorOpeningIntent intent)
    {
        if (!openings.TryOpen(in intent, out VanillaGroundFighterDoorOpeningMutation mutation))
            return false;

        if (replication is not null &&
            mutation.PacketTileX >= short.MinValue && mutation.PacketTileX <= short.MaxValue &&
            mutation.PacketTileY >= short.MinValue && mutation.PacketTileY <= short.MaxValue)
        {
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
