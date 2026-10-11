using TerraRuntime.Contracts.Gameplay;
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
            long unit = CoinPhysicalValue(state.Update.ItemNetId, 1);
            quantity = unit > 0 ? checked((int)(state.FundedCoinValue / unit)) : state.FundedQuantity;
            return quantity >= 0 && quantity <= state.Update.Stack;
        }
    }
    private static long CoinPhysicalValue(short itemNetId, int stack) =>
        stack > 0 && VanillaCoinFacts.TryGetValue(new ItemTypeId(itemNetId), out long unit)
            ? checked(unit * stack) : 0;

    internal bool TryGetFundedCoinValue(in WorldItemSnapshot expected, out long value)
    {
        value = 0;
        if (!expected.Handle.IsAssigned || !IsValidSlot(expected.Handle.Slot)) return false;
        SpinWait spin = default;
        while (true)
        {
            int version = ReadStableVersion(ref spin);
            var state = _slots[expected.Handle.Slot];
            if (version != ReadVersion()) continue;
            if (!state.Active || state.Reserved || Capture(expected.Handle.Slot, in state) != expected) return false;
            long physical = CoinPhysicalValue(state.Update.ItemNetId, state.Update.Stack);
            value = state.FundedCoinValue;
            return physical > 0 && value >= 0 && value <= physical;
        }
    }

}
