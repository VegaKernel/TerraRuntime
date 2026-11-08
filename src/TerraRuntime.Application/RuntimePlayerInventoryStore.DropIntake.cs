using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal readonly record struct CharacterDropAllowance1458(ItemTypeId ItemType, PrefixId Prefix, int Quantity);

internal sealed partial class RuntimePlayerInventoryStore
{
    // Classic character import is an existing trust boundary. These one-use allowances
    // authorize only conversion to a world item, never potion, reward or copper funding.
    // Keys can be added only at first spawn or trusted managed transfer, at most one per
    // physical import slot. Reports and ordinary owner operations can only spend them.
    private readonly Dictionary<(ItemTypeId, PrefixId), int>?[] characterDropAllowances = new Dictionary<(ItemTypeId, PrefixId), int>?[PlayerSlotCount];
    private readonly long[] characterDropCopper = new long[PlayerSlotCount];
    private const int MaximumCharacterDropIdentities = InventorySlotCount + ShopBankImageCount;

    internal void SealCharacterDropIntake(ConnectionHandle connection)
    {
        if (!connection.IsAssigned || connections[connection.Player.Slot.Value] != connection || Serial == ulong.MaxValue) return;
        int player = connection.Player.Slot.Value;
        if (characterDropAllowances[player] is not null) return;
        var allowances = new Dictionary<(ItemTypeId, PrefixId), int>();
        long copper = 0;
        for (int slot = 0; slot < MaximumCharacterDropIdentities; slot++)
        {
            bool main = slot < InventorySlotCount;
            int offset = main ? GetOffset(player, slot) : player * ShopBankImageCount + slot - InventorySlotCount;
            var item = main ? items[offset] : shopBankItems[offset];
            if (item.IsEmpty || !item.IsCanonical) continue;
            long value = CoinValue(item);
            if (value > 0)
                copper = checked(copper + Math.Max(0, value - (main ? coinFundedValues[offset] : shopBankFundedCopper[offset])));
            else
            {
                int quantity = Math.Max(0, item.Stack - (main ? fundedQuantities[offset] : shopBankFundedQuantities[offset]));
                if (quantity > 0)
                {
                    var key = (item.ItemType, item.Prefix);
                    allowances.TryGetValue(key, out int prior);
                    allowances[key] = checked(prior + quantity);
                }
            }
        }
        characterDropAllowances[player] = allowances;
        characterDropCopper[player] = copper;
        Changed(player);
    }

    private bool CanSpendCharacterDrop(ConnectionHandle connection, in RuntimePlayerInventoryItem item, long cost)
    {
        if (cost == 0) return true;
        if (cost < 0 || !connection.IsAssigned || connections[connection.Player.Slot.Value] != connection) return false;
        int player = connection.Player.Slot.Value;
        return CoinValue(item) > 0 ? characterDropCopper[player] >= cost :
            characterDropAllowances[player]?.TryGetValue((item.ItemType, item.Prefix), out int quantity) == true && quantity >= cost;
    }

    private bool HasCharacterDropStock(ConnectionHandle connection, in RuntimePlayerInventoryItem item)
    {
        long copper = CoinValue(item);
        return !item.IsEmpty && CanSpendCharacterDrop(connection, in item, copper > 0 ? copper : item.Stack);
    }

    private void SpendCharacterDrop(ConnectionHandle connection, in RuntimePlayerInventoryItem item, long cost)
    {
        if (cost == 0) return;
        if (!CanSpendCharacterDrop(connection, in item, cost)) throw new InvalidOperationException("Admitted character drop lost its one-use allowance.");
        int player = connection.Player.Slot.Value;
        if (CoinValue(item) > 0) characterDropCopper[player] -= cost;
        else characterDropAllowances[player]![(item.ItemType, item.Prefix)] -= checked((int)cost);
    }

    // Call only for a complete accepted owner operation, using its original before
    // image and final funding. Raw packet-5 fragments are not consumption witnesses.
    private void BurnCharacterDropConsumption(ConnectionHandle connection,
        ReadOnlySpan<RuntimePlayerInventoryItem> before, ReadOnlySpan<short> beforeFunding, ReadOnlySpan<long> beforeCopper,
        ReadOnlySpan<RuntimePlayerInventoryItem> after, ReadOnlySpan<short> afterFunding, ReadOnlySpan<long> afterCopper)
    {
        if (!connection.IsAssigned || connections[connection.Player.Slot.Value] != connection ||
            characterDropAllowances[connection.Player.Slot.Value] is not { } allowances) return;
        if (before.Length > MaximumCharacterDropIdentities || after.Length > MaximumCharacterDropIdentities ||
            beforeFunding.Length != before.Length || afterFunding.Length != after.Length ||
            beforeCopper.Length != before.Length || afterCopper.Length != after.Length)
            throw new InvalidOperationException("Invalid character-drop consumption image.");
        var decreases = new Dictionary<(ItemTypeId, PrefixId), int>();
        long copperDecrease = 0;
        for (int phase = 0; phase < 2; phase++)
        {
            var image = phase == 0 ? before : after;
            var funding = phase == 0 ? beforeFunding : afterFunding;
            var coins = phase == 0 ? beforeCopper : afterCopper;
            int sign = phase == 0 ? 1 : -1;
            for (int slot = 0; slot < image.Length; slot++)
            {
                var item = image[slot];
                if (item.IsEmpty) continue;
                long value = CoinValue(item);
                if (value > 0) copperDecrease = checked(copperDecrease + sign * Math.Max(0, value - coins[slot]));
                else if (allowances.ContainsKey((item.ItemType, item.Prefix)))
                {
                    var key = (item.ItemType, item.Prefix);
                    decreases.TryGetValue(key, out int prior);
                    decreases[key] = checked(prior + sign * Math.Max(0, item.Stack - funding[slot]));
                }
            }
        }
        foreach (var (key, decrease) in decreases)
            if (decrease > 0) allowances[key] = Math.Max(0, allowances[key] - decrease);
        if (copperDecrease > 0)
        {
            int player = connection.Player.Slot.Value;
            characterDropCopper[player] = Math.Max(0, characterDropCopper[player] - copperDecrease);
        }
    }

    private void BurnCharacterDropConsumption(ConnectionHandle connection, in RuntimePlayerInventoryItem before,
        short beforeFunding, long beforeCopper, in RuntimePlayerInventoryItem after, short afterFunding, long afterCopper)
    {
        BurnCharacterDropConsumption(connection, [before], [beforeFunding], [beforeCopper], [after], [afterFunding], [afterCopper]);
    }

    internal CharacterDropAllowance1458[] CaptureCharacterDropAllowances(ConnectionHandle connection, out long copper, out bool sealedIntake)
    {
        copper = 0; sealedIntake = false;
        if (!connection.IsAssigned || connections[connection.Player.Slot.Value] != connection ||
            characterDropAllowances[connection.Player.Slot.Value] is not { } values) return [];
        sealedIntake = true; copper = characterDropCopper[connection.Player.Slot.Value];
        return values.Select(pair => new CharacterDropAllowance1458(pair.Key.Item1, pair.Key.Item2, pair.Value)).ToArray();
    }

    internal static bool AreValidCharacterDropAllowances(CharacterDropAllowance1458[]? values, long copper, bool sealedIntake)
    {
        const long maximumCopper = (long)MaximumCharacterDropIdentities * short.MaxValue * 1_000_000;
        if (values is null || values.Length > MaximumCharacterDropIdentities || copper < 0 || copper > maximumCopper ||
            (!sealedIntake && (values.Length != 0 || copper != 0))) return false;
        var seen = new HashSet<(ItemTypeId, PrefixId)>();
        long total = 0;
        foreach (var value in values)
        {
            if (value.Quantity < 0 || value.Quantity > MaximumCharacterDropIdentities * short.MaxValue ||
                value.ItemType.IsNone || !VanillaItemIds.TryCreate(value.ItemType.Value, out _) ||
                !VanillaPrefixIds.TryCreate(value.Prefix.Value, out _) || !seen.Add((value.ItemType, value.Prefix))) return false;
            total += value.Quantity;
        }
        if (total > MaximumCharacterDropIdentities * short.MaxValue) return false;
        return true;
    }

    internal void AdoptCharacterDropAllowances(ConnectionHandle connection, CharacterDropAllowance1458[] values, long copper, bool sealedIntake)
    {
        if (!AreValidCharacterDropAllowances(values, copper, sealedIntake) || !connection.IsAssigned ||
            connections[connection.Player.Slot.Value] != connection) throw new InvalidOperationException("Invalid character-drop transfer.");
        int player = connection.Player.Slot.Value;
        characterDropAllowances[player] = sealedIntake ? values.ToDictionary(value => (value.ItemType, value.Prefix), value => value.Quantity) : null;
        characterDropCopper[player] = copper;
        Changed(player);
    }
}
