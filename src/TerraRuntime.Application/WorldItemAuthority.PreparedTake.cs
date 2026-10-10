using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Worlds;

namespace TerraRuntime.Application;

internal sealed partial class WorldItemAuthority
{
    internal bool TryPrepareTrustedTake(
        in WorldItemSnapshot expected,
        out RuntimeWorldItemStore.AllocationPreview? plan)
    {
        plan = worldItems.CreateAllocationPreview();
        if (plan.TryRemoveSource(expected))
            return true;
        plan.Dispose();
        plan = null;
        return false;
    }
}
