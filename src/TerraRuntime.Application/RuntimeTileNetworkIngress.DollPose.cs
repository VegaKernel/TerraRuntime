using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal interface IDollPoseNetworkIngress1458
{
    bool TryPostDollPose(ConnectionHandle connection, in TerrariaDollPoseState1458 state);
}
internal partial class RuntimeTileNetworkIngress : IDollPoseNetworkIngress1458
{
    public bool TryPostDollPose(ConnectionHandle connection, in TerrariaDollPoseState1458 state) =>
        connection.IsAssigned && Ingress.TryPost(connection.Source,
            new ClientToolDollPoseRuntimeCommand1458(connection, state with { PlayerSlot = connection.Player.Slot.Value }));
}
