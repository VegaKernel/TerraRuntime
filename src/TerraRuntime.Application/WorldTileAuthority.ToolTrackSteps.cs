using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolActionSteps1458
    {
        private bool TryPoundTrackAndReport(int x, int y)
        {
            if (!client.TryPoundTrack(x, y, out bool sourceResult, out _)) return Reject();
            if (!sourceResult) return true;
            // Player emits action15 only for source FrameTrack's true return;
            // MessageBuffer runs the dedicated operation before the outer relay.
            return AppendTile(15, x, y, 1) && server.TryPoundTrack(x, y, out _, out _) &&
                RecordPublication(expectedCount) || Reject();
        }
    }
}
