using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal sealed partial class RuntimePlayerInventoryStore
{
    // Copper-unit provenance is separate from physical count and reported wallet value.
    private readonly long[] coinFundedValues = new long[PlayerSlotCount * InventorySlotCount];

    internal bool TryGetFundedCoinValue(ConnectionHandle connection, int slot, out long value)
    {
        value = 0;
        if (!TryGet(connection, slot, out var item)) return false;
        value = coinFundedValues[GetOffset(connection.Player.Slot.Value, slot)];
        return value >= 0 && value <= CoinValue(item);
    }

    internal long[] CaptureFundedCoinValues(ConnectionHandle connection)
    {
        var result = new long[InventorySlotCount];
        if (connection.IsAssigned && connections[connection.Player.Slot.Value] == connection)
            coinFundedValues.AsSpan(connection.Player.Slot.Value * InventorySlotCount, InventorySlotCount).CopyTo(result);
        return result;
    }

    internal static bool AreValidFundedCoinValues(long[]? values, ReadOnlySpan<RuntimePlayerInventoryItem> image)
    {
        if (values is null || values.Length != InventorySlotCount || image.Length != InventorySlotCount) return false;
        for (int slot = 0; slot < values.Length; slot++)
            if (values[slot] < 0 || values[slot] > CoinValue(image[slot])) return false;
        return true;
    }

    private bool TryPlanCoinFunding(ConnectionHandle connection, RuntimePlayerInventoryItem[] before,
        RemotePhaseInventoryMutation[] mutations, long credit, out long[] afterFunding)
    {
        afterFunding = CaptureFundedCoinValues(connection);
        if (!AreValidFundedCoinValues(afterFunding, before) || credit < 0) return false;
        long beforeBudget = 0, pool = credit;
        foreach (long value in afterFunding) beforeBudget = checked(beforeBudget + value);
        foreach (var mutation in mutations)
        {
            int slot = mutation.Slot;
            long old = afterFunding[slot];
            long kept = mutation.Before.ItemType == mutation.After.ItemType && mutation.Before.Prefix == mutation.After.Prefix
                ? Math.Min(old, CoinValue(mutation.After)) : 0;
            afterFunding[slot] = kept;
            pool = checked(pool + old - kept);
        }
        // This deterministic assignment is server custody policy over the exact source journal.
        // Original Terraria tracks physical money; it has no trusted/unknown value metadata.
        foreach (var mutation in mutations)
        {
            int slot = mutation.Slot;
            long assigned = Math.Min(pool, CoinValue(mutation.After) - afterFunding[slot]);
            afterFunding[slot] += assigned;
            pool -= assigned;
        }
        long afterBudget = 0;
        foreach (long value in afterFunding) afterBudget = checked(afterBudget + value);
        return pool == 0 && afterBudget == checked(beforeBudget + credit);
    }
}
