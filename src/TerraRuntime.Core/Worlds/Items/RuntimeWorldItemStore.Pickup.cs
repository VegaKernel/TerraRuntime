using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Worlds;

public sealed partial class RuntimeWorldItemStore
{
    // Canonical wire state alone cannot authorize inventory credit. Quantity custody belongs
    // to this exact live generation and is adopted with the existing allocation journal.
    internal bool TryGetFundedQuantity(in WorldItemSnapshot expected, out int quantity)
    {
        quantity = 0;
        if (!expected.Handle.IsAssigned || !IsValidSlot(expected.Handle.Slot)) return false;
        SpinWait spin = default;
        while (true)
        {
            int version = ReadStableVersion(ref spin);
            var state = _slots[expected.Handle.Slot];
            if (version != ReadVersion()) continue;
            if (!state.Active || Capture(expected.Handle.Slot, in state) != expected) return false;
            quantity = state.FundedQuantity;
            return quantity >= 0 && quantity <= state.Update.Stack;
        }
    }
}
