using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Worlds;

namespace TerraRuntime.Application;

internal sealed partial class WorldItemAuthority
{
    internal bool TryPrepareTrustedTake(
        in WorldItemSnapshot expected,
        out RuntimeWorldItemStore.AllocationPreview? plan)
        => TryPrepareTrustedTake(expected, expected.Stack, out plan);

    internal bool TryPrepareTrustedTake(
        in WorldItemSnapshot expected,
        int acceptedAmount,
        out RuntimeWorldItemStore.AllocationPreview? plan)
    {
        plan = worldItems.CreateAllocationPreview();
        if (plan.TryTakeSource(expected, acceptedAmount))
            return true;
        plan.Dispose();
        plan = null;
        return false;
    }
}
