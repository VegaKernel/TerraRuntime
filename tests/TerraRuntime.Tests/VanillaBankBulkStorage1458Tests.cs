using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Gameplay.Items;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class VanillaBankBulkStorage1458Tests
{
    [Fact]
    public void Original_bank_bulk_and_transfer_override_images_preserve_source_order()
    {
        int rows = 0;
        foreach (string resource in new[] { "BankBulkStorageSource1458", "BankBulkStorageClocksSource1458", "BankMoveCoinsEdgesSource1458" })
        {
            using var stream = typeof(VanillaBankBulkStorage1458Tests).Assembly.GetManifestResourceStream(resource)!;
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var source = JsonDocument.Parse(gzip);
            foreach (string platform in new[] { "linux", "windows" })
                foreach (var row in source.RootElement.GetProperty(platform).GetProperty("rows").EnumerateArray())
                {
                    var spec = row.GetProperty("spec");
                    string name = spec.GetProperty("name").GetString()!;
                    int bankIndex = spec.GetProperty("bank").GetInt32();
                    var before = row.GetProperty("before");
                    var after = row.GetProperty("afterSync");
                    var main = ReadItems(before.GetProperty("inventory"));
                    var bank = ReadItems(before.GetProperty("banks")[bankIndex]);
                    BankBulkOperation1458 operation = spec.GetProperty("op").GetString() switch
                    {
                        "deposit" => BankBulkOperation1458.DepositAll,
                        "quick" => BankBulkOperation1458.QuickStack,
                        "loot" => BankBulkOperation1458.LootAll,
                        "shiftin" => BankBulkOperation1458.ShiftIntoBank,
                        "shiftout" => BankBulkOperation1458.ShiftOutOfBank,
                        _ => throw new InvalidOperationException(name)
                    };
                    short selected = operation == BankBulkOperation1458.ShiftIntoBank ? (short)10 : (short)0;
                    Assert.True(VanillaWorldItemOwner1458.TryPlanBankBulk(main, bank, bankIndex,
                        operation, selected, new(false, false, false), out var plan), name);
                    Assert.NotNull(plan);
                    Assert.Equal(ReadItems(after.GetProperty("inventory")), plan.Inventory);
                    Assert.Equal(ReadItems(after.GetProperty("banks")[bankIndex]), plan.Bank);
                    VerifySteps(main, bank, plan);
                    if (name.Contains("unchanged", StringComparison.Ordinal))
                    {
                        Assert.Equal(main[50], plan.Inventory[50]);
                        var coins = Assert.Single(plan.Steps, step => step.Coins is not null).Coins!;
                        Assert.Contains((short)50, coins.Slots);
                        int bankDestination = Array.IndexOf(coins.DestinationOrder, (short)59);
                        int mainRestoration = Array.IndexOf(coins.DestinationOrder, (short)50);
                        Assert.True(bankDestination >= 0 && mainRestoration > bankDestination,
                            "The unchanged main coin slot remains a visited pool participant restored after the bank.");
                    }
                    for (int other = 0; other < 4; other++)
                        if (other != bankIndex)
                            Assert.Equal(ReadItems(before.GetProperty("banks")[other]), ReadItems(after.GetProperty("banks")[other]));
                    // These are configured original components, not naturally driven UI or private funding.
                    Assert.Equal(before.GetProperty("random").GetRawText(), after.GetProperty("random").GetRawText());
                    Assert.Equal(before.GetProperty("next").GetInt32(), after.GetProperty("next").GetInt32());
                    rows++;
                }
        }
        Assert.Equal(168, rows);
    }

    [Fact]
    public void Bank_bulk_coin_gate_hotbar_favorites_and_detached_inputs_remain_bounded()
    {
        var main = new WorldItemPickupSlot1458[59];
        var bank = new WorldItemPickupSlot1458[40];
        main[9] = new(3, 5, 0, false);
        main[10] = new(9, 3, 0, true);
        main[50] = new(71, 99, 0, false);
        main[58] = new(71, 99, 0, false);
        var original = main.ToArray();
        Assert.True(VanillaWorldItemOwner1458.TryPlanBankBulk(main, bank, 0,
            BankBulkOperation1458.DepositAll, 0, null, out var emptyBank));
        Assert.NotNull(emptyBank);
        Assert.Equal(original, emptyBank.Inventory);
        Assert.Empty(emptyBank.Steps);
        bank[0] = new(71, 1, 0, true);
        Assert.True(VanillaWorldItemOwner1458.TryPlanBankBulk(main, bank, 0,
            BankBulkOperation1458.DepositAll, 0, null, out var coins));
        Assert.NotNull(coins);
        Assert.Equal(new WorldItemPickupSlot1458(72, 1, 0, false), coins.Bank[0]);
        Assert.Equal(default, coins.Inventory[50]);
        Assert.Equal(main[58], coins.Inventory[58]);
        Assert.Equal(main[9], coins.Inventory[9]);
        Assert.Equal(main[10], coins.Inventory[10]);
        VerifySteps(main, bank, coins);
        Assert.Equal(original, main);
        Assert.Equal(new WorldItemPickupSlot1458(71, 1, 0, true), bank[0]);
        Assert.False(VanillaWorldItemOwner1458.TryPlanBankBulk(main[..58], bank, 0,
            BankBulkOperation1458.LootAll, 0, null, out _));
        Assert.False(VanillaWorldItemOwner1458.TryPlanBankBulk(main, bank, 4,
            BankBulkOperation1458.LootAll, 0, null, out _));
        Assert.False(VanillaWorldItemOwner1458.TryPlanBankBulk(main, bank, 0,
            BankBulkOperation1458.ShiftIntoBank, 58, null, out _));
    }

    private static WorldItemPickupSlot1458[] ReadItems(JsonElement array) =>
        array.EnumerateArray().Select(ReadItem).ToArray();

    private static WorldItemPickupSlot1458 ReadItem(JsonElement item) =>
        item.GetProperty("type").GetInt32() == 0 || item.GetProperty("stack").GetInt32() <= 0
            ? default // Original raw Air fields remain in the numeric fixture; protocol/storage compares canonical Air.
            : new(item.GetProperty("type").GetInt32(), item.GetProperty("stack").GetInt32(),
                item.GetProperty("prefix").GetInt32(), item.GetProperty("favorite").GetBoolean());

    private static void VerifySteps(WorldItemPickupSlot1458[] beforeMain,
        WorldItemPickupSlot1458[] beforeBank, BankBulkStoragePlan1458 plan)
    {
        var current = beforeMain.Concat(beforeBank).ToArray();
        foreach (var step in plan.Steps)
        {
            if (step.Coins is { } coins)
            {
                Assert.Equal(coins.Slots.Length, coins.Before.Length);
                Assert.Equal(coins.Slots.Length, coins.After.Length);
                Assert.Equal(coins.Slots.Length, coins.Slots.Distinct().Count());
                for (int i = 0; i < coins.Slots.Length; i++)
                {
                    Assert.InRange(coins.Slots[i], (short)0, (short)98);
                    Assert.NotEqual((short)58, coins.Slots[i]);
                    Assert.Equal(coins.Before[i], current[coins.Slots[i]]);
                    current[coins.Slots[i]] = coins.After[i];
                }
                foreach (short destination in coins.DestinationOrder) Assert.Contains(destination, coins.Slots);
                continue;
            }
            Assert.InRange(step.Source, (short)0, (short)98);
            Assert.InRange(step.Destination, (short)0, (short)98);
            Assert.NotEqual(step.Source, step.Destination);
            Assert.Equal(step.SourceBefore, current[step.Source]);
            Assert.Equal(step.DestinationBefore, current[step.Destination]);
            Assert.InRange(step.Amount, 1, step.SourceBefore.Stack);
            current[step.Source] = step.Amount == step.SourceBefore.Stack
                ? default : step.SourceBefore with { Stack = step.SourceBefore.Stack - step.Amount };
            current[step.Destination] = step.DestinationBefore.Type == 0
                ? step.SourceBefore with { Stack = step.Amount }
                : step.DestinationBefore with { Stack = step.DestinationBefore.Stack + step.Amount };
        }
        Assert.Equal(plan.Inventory.Concat(plan.Bank).ToArray(), current);
    }
}
