using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal enum ClientTileManipulationAdmissionResult : byte
{
    Admitted = 0,
    UnknownWireAction = 1,
    AuthorityUnavailable = 2
}

/// <summary>
/// Runtime-owned admission policy for client-originated packet 17 actions. The protocol layer only resolves the
/// source-known wire identity; this layer decides whether TerraRuntime currently has enough authoritative gameplay
/// semantics to accept that action from an untrusted client.
/// </summary>
internal static class ClientTileManipulationAdmissionPolicy
{
    public static ClientTileManipulationAdmissionResult Evaluate(
        in TerrariaTileManipulationState state,
        out TerrariaTileManipulationAction action)
    {
        if (!state.TryGetWireAction(out action))
            return ClientTileManipulationAdmissionResult.UnknownWireAction;

        return action is TerrariaTileManipulationAction.KillTile or TerrariaTileManipulationAction.PlaceTile
            ? ClientTileManipulationAdmissionResult.Admitted
            : ClientTileManipulationAdmissionResult.AuthorityUnavailable;
    }
}
