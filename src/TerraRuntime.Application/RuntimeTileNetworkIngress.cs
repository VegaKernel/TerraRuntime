using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal interface ITileNetworkIngress
{
    bool TryPost(ConnectionHandle connection, in TerrariaTileManipulationState state);
    bool TryPostLiquid(ConnectionHandle connection, in TerrariaLiquidState state);
}

internal interface ITempleDoorUnlockNetworkIngress
{
    bool TryPostTempleDoorUnlock(ConnectionHandle connection, in TerrariaLockAndUnlockState state);
}

internal interface IPlayerDoorToggleNetworkIngress
{
    bool TryPostDoorOpen(ConnectionHandle connection, in TerrariaDoorToggleState state);
    bool TryPostDoorClose(ConnectionHandle connection, in TerrariaDoorToggleState state);
}

/// <summary>
/// Connection-authenticated packet-17 ingress. The socket thread only carries immutable decoded state across the
/// bounded command queue; all current-session, world-bounds and gameplay-authority decisions remain on the single
/// authoritative writer thread.
/// </summary>
internal class RuntimeTileNetworkIngress : ITileNetworkIngress, ITempleDoorUnlockNetworkIngress, IPlayerDoorToggleNetworkIngress
{
    protected IGameCommandIngress<RuntimeCommand> Ingress { get; }

    public RuntimeTileNetworkIngress(IGameCommandIngress<RuntimeCommand> ingress)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        Ingress = ingress;
    }

    public bool TryPost(ConnectionHandle connection, in TerrariaTileManipulationState state)
    {
        if (!connection.IsAssigned)
            return false;

        return Ingress.TryPost(
            connection.Source,
            new ClientTileManipulationRuntimeCommand(connection, state));
    }

    public bool TryPostLiquid(ConnectionHandle connection, in TerrariaLiquidState state)
    {
        if (!connection.IsAssigned)
            return false;

        return Ingress.TryPost(
            connection.Source,
            new ClientLiquidRuntimeCommand(connection, state));
    }

    public bool TryPostTempleDoorUnlock(ConnectionHandle connection, in TerrariaLockAndUnlockState state)
    {
        if (!connection.IsAssigned || state.Action != 2)
            return false;

        return Ingress.TryPost(
            connection.Source,
            new ClientTempleDoorUnlockRuntimeCommand(connection, state));
    }

    public bool TryPostDoorOpen(ConnectionHandle connection, in TerrariaDoorToggleState state)
    {
        if (!connection.IsAssigned || state.Action != (byte)TerrariaDoorToggleAction.OpenDoor || !state.IsValid)
            return false;

        return Ingress.TryPost(
            connection.Source,
            new ClientDoorOpenRuntimeCommand(connection, state));
    }

    public bool TryPostDoorClose(ConnectionHandle connection, in TerrariaDoorToggleState state)
    {
        if (!connection.IsAssigned || state.Action != (byte)TerrariaDoorToggleAction.CloseDoor || !state.IsValid)
            return false;

        return Ingress.TryPost(
            connection.Source,
            new ClientDoorCloseRuntimeCommand(connection, state));
    }
}
