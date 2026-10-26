using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTileManipulationReplicationRegistry
{
    // Pure source relay. This has no join baseline and grants no progress/clock entitlement.
    internal bool TryPublishToolPicking(GameCommandSourceId excludedSource,
        in TerrariaTilePickingState1458 state) =>
        TryPublishFrame(excludedSource, TerrariaTilePickingCodec1458.Encode(in state));
}
