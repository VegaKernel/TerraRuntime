using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolActionSteps1458
    {
        private TerrariaDollPoseState1458? expectedDollPose;
        internal TerrariaDollPoseState1458? ExpectedDollPose => expectedDollPose;
        private bool TryDollPoseAndReport(int x, int y)
        {
            if (expectedDollPose is not null || !client.TryChangeDisplayDollPose(x, y, out int id, out byte pose)) return Reject();
            if (id < 0) return true;
            if (!server.TryChangeDisplayDollPose(x, y, out int serverId, out byte serverPose) || id != serverId || pose != serverPose) return Reject();
            expectedDollPose = new(playerSlot, id, pose, pose);
            return true;
        }
    }
}
