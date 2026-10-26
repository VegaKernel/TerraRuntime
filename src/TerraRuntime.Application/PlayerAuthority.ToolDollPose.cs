using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal ToolReportObservationResult1458 ObserveToolDollPose(ConnectionHandle connection,
        in TerrariaDollPoseState1458 report, out RuntimePlayerToolInput1458? input)
    {
        var result = TryCaptureToolInput(connection, out input);
        if (result != ToolReportObservationResult1458.Retained || input is null) return result;
        return input.TryObserve(in report) ? ToolReportObservationResult1458.Retained : ToolReportObservationResult1458.Rejected;
    }
}
