using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal interface ITileNetworkIngress
{
    bool TryPost(ConnectionHandle connection, in TerrariaTileManipulationState state);
    bool TryPostLiquid(ConnectionHandle connection, in TerrariaLiquidState state);
}

internal interface ITilePickingNetworkIngress1458
{
    bool TryPostPicking(ConnectionHandle connection, in TerrariaTilePickingState1458 state);
}

internal interface IToolTileSquareIngress1458
{
    bool TryPostToolSquare(ConnectionHandle connection, in TerrariaToolTileSquareState1458 state);
}

internal interface ILucyAxeMessageIngress1458
{
    bool TryPostLucy(ConnectionHandle connection, in TerrariaLucyAxeMessageState1458 state);
}

internal interface ITempleDoorUnlockNetworkIngress
{
    bool TryPostTempleDoorUnlock(ConnectionHandle connection, in TerrariaLockAndUnlockState state);
}

internal interface IPlayerDoorToggleNetworkIngress
{
    bool TryPostDoorOpen(ConnectionHandle connection, in TerrariaDoorToggleState state);
    bool TryPostDoorClose(ConnectionHandle connection, in TerrariaDoorToggleState state);
    bool TryPostTallGateToggle(ConnectionHandle connection, in TerrariaDoorToggleState state);
    bool TryPostTrapdoorToggle(ConnectionHandle connection, in TerrariaDoorToggleState state);
}

/// <summary>
/// Connection-authenticated packet-17 ingress. The socket thread only carries immutable decoded state across the
/// bounded command queue; all current-session, world-bounds and gameplay-authority decisions remain on the single
/// authoritative writer thread.
/// </summary>
internal class RuntimeTileNetworkIngress : ITileNetworkIngress, ITilePickingNetworkIngress1458, ILucyAxeMessageIngress1458, IToolTileSquareIngress1458, ITempleDoorUnlockNetworkIngress, IPlayerDoorToggleNetworkIngress
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

    public bool TryPostPicking(ConnectionHandle connection, in TerrariaTilePickingState1458 state)
    {
        if (!connection.IsAssigned) return false;
        // Source case125 replaces the client player byte with whoAmI. No tile,
        // clock, item or private progress state is changed by this relay command.
        var authenticated = state with { PlayerSlot = connection.Player.Slot.Value };
        return Ingress.TryPost(connection.Source, new ClientTilePickingRuntimeCommand1458(connection, authenticated));
    }

    public bool TryPostToolSquare(ConnectionHandle connection, in TerrariaToolTileSquareState1458 state)
    {
        if (!connection.IsAssigned || !TerrariaTileSquareCodec.TryEncodeToolFrame(in state, out _)) return false;
        return Ingress.TryPost(connection.Source, new ClientToolTileSquareRuntimeCommand1458(connection, state));
    }

    public bool TryPostLucy(ConnectionHandle connection, in TerrariaLucyAxeMessageState1458 state)
    {
        if (!connection.IsAssigned || !state.IsValid) return false;
        return Ingress.TryPost(connection.Source, new ClientLucyAxeMessageRuntimeCommand1458(connection, state));
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

    public bool TryPostTallGateToggle(ConnectionHandle connection, in TerrariaDoorToggleState state)
    {
        if (!connection.IsAssigned ||
            state.Action is not (byte)TerrariaDoorToggleAction.OpenTallGate and not (byte)TerrariaDoorToggleAction.CloseTallGate ||
            !state.IsValid)
        {
            return false;
        }

        return Ingress.TryPost(
            connection.Source,
            new ClientTallGateToggleRuntimeCommand(connection, state));
    }

    public bool TryPostTrapdoorToggle(ConnectionHandle connection, in TerrariaDoorToggleState state)
    {
        if (!connection.IsAssigned ||
            state.Action is not (byte)TerrariaDoorToggleAction.OpenTrapdoor and not (byte)TerrariaDoorToggleAction.CloseTrapdoor ||
            !state.IsValid)
        {
            return false;
        }

        return Ingress.TryPost(connection.Source, new ClientTrapdoorToggleRuntimeCommand(connection, state));
    }
}
