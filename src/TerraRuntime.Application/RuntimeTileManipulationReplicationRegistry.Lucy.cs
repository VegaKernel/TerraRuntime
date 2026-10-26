using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeTileManipulationReplicationRegistry
{
    // Source case141 is a visual relay, not a join baseline or a world edit.
    internal bool TryPublishLucyAxeMessage(GameCommandSourceId excludedSource,
        in TerrariaLucyAxeMessageState1458 state)
    {
        if (TerrariaLucyAxeMessageCodec1458.TryEncode(in state, out var bytes) !=
            TerrariaLucyAxeMessageEncodeResult1458.Encoded)
        {
            Interlocked.Increment(ref encodeFailures);
            return false;
        }
        return TryPublishFrame(excludedSource, bytes);
    }
}
