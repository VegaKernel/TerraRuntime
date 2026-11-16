using TerraRuntime.Core;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTileManipulationReplicationRegistry
{
    internal bool TryPublishLogicSmoke(int x, int y) =>
        TerrariaPoofOfSmokeCodec1458.TryEncode(x, y, out var frame) && TryPublishFrameToAll(frame);

    public bool TryPublishHitSwitch(GameCommandSourceId excludedSource, in TerrariaHitSwitchState1458 state) =>
        TryPublishFrame(excludedSource, TerrariaHitSwitchCodec1458.Encode(in state));
}
