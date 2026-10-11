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
        out RuntimeWorldItemStore.AllocationPreview? plan, bool requireFundedQuantity = false, long? expectedCoinValue = null)
    {
        plan = worldItems.CreateAllocationPreview();
        if (expectedCoinValue is { } coinValue
            ? plan.TryTakeFundedCoinSource(expected, acceptedAmount, out long transferred) && transferred == coinValue
            : plan.TryTakeSource(expected, acceptedAmount, requireFundedQuantity))
            return true;
        plan.Dispose();
        plan = null;
        return false;
    }
}
