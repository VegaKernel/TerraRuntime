using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerToolInput1458
{
    internal TerrariaDollPoseState1458? DollPoseReport { get; private set; }
    internal bool TryObserve(in TerrariaDollPoseState1458 report)
    {
        if (!IsCurrent || Definition.Hammer <= 0 || report.EntityId < 0 ||
            report.PlayerSlot != actor!.Connection.Player.Slot.Value || report.HeaderPose != report.Pose) return false;
        if (DollPoseReport is { } before) return before == report;
        if (Revision == ulong.MaxValue) { Invalid = true; return false; }
        DollPoseReport = report; Revision++; return true;
    }
}
