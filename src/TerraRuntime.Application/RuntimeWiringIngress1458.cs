using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed record ClientHitSwitchRuntimeCommand1458(ConnectionHandle Connection,
    TerrariaHitSwitchState1458 State) : RuntimeCommand;

internal interface IWiringNetworkIngress1458
{
    bool TryPostHitSwitch(ConnectionHandle connection, in TerrariaHitSwitchState1458 state);
}

internal partial class RuntimeTileNetworkIngress : IWiringNetworkIngress1458
{
    public bool TryPostHitSwitch(ConnectionHandle connection, in TerrariaHitSwitchState1458 state) =>
        connection.IsAssigned && Ingress.TryPost(connection.Source, new ClientHitSwitchRuntimeCommand1458(connection, state));
}
