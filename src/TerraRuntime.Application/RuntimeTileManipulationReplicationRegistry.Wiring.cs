using TerraRuntime.Core;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTileManipulationReplicationRegistry
{
    public bool TryPublishHitSwitch(GameCommandSourceId excludedSource, in TerrariaHitSwitchState1458 state) =>
        TryPublishFrame(excludedSource, TerrariaHitSwitchCodec1458.Encode(in state));
}
