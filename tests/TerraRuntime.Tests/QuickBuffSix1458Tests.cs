using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class QuickBuffSix1458Tests
{
    [Fact]
    public void Original_quick_buff_ordered_inventory_void_and_indexed_buff_images_match()
    {
        int rows = 0;
        foreach (string family in new[] { "QuickBuffSix", "QuickBuffSparse" })
            foreach (string capture in new[] { "LinuxFirst", "LinuxRepeat", "WindowsFirst", "WindowsRepeat" })
            {
                using JsonDocument document = Read(family + capture + "1458");
                foreach (JsonElement row in document.RootElement.EnumerateArray())
                {
                    JsonElement before = row.GetProperty("before");
                    JsonElement expected = row.GetProperty("afterQuick");
                    WorldItemPickupSlot1458[] inventory = Items(before.GetProperty("inventory"));
                    WorldItemPickupSlot1458[] bank = Items(row.GetProperty("beforeBank4"));
                    BuffTypeId[] types = Types(before.GetProperty("buffType"));
                    int[] times = Integers(before.GetProperty("buffTime"));
                    string mode = row.GetProperty("mode").GetString()!;
                    var eligibility = new QuickBuffEligibility1458(false, B(before, "cursed"),
                        mode == "cced", mode == "dead", mode == "spectating", B(before, "noItems"));
                    bool[] immunity = new bool[118];
                    if (mode == "immune-duplicate") immunity[5] = true;
                    Assert.True(VanillaSelectedConsumable1458.TryPlanQuickBuff(inventory, bank,
                        types, times, in eligibility, immunity, out var plan));
                    Assert.NotNull(plan);
                    Assert.Equal(Items(expected.GetProperty("inventory")), plan.Inventory);
                    Assert.Equal(Items(row.GetProperty("afterBank4")), plan.VoidItems);
                    Assert.Equal(Types(expected.GetProperty("buffType")), plan.BuffTypes);
                    Assert.Equal(Integers(expected.GetProperty("buffTime")), plan.BuffTimes);
                    var expectedCosts = new List<QuickBuffMutation1458>();
                    for (int slot = 0; slot < 58; slot++)
                        AddExpectedCost(expectedCosts, slot, inventory[slot], plan.Inventory[slot]);
                    for (int slot = 0; slot < 40; slot++)
                        AddExpectedCost(expectedCosts, 59 + slot, bank[slot], plan.VoidItems[slot]);
                    Assert.Equal(expectedCosts, plan.Mutations);
                    Assert.Equal(inventory[58], plan.Inventory[58]);
                    // This detached operation has no random or clock inputs. The unchanged original
                    // calls independently prove those states do not advance, rather than predicting UI intent.
                    Assert.Equal(row.GetProperty("beforeRandom").GetRawText(), row.GetProperty("afterRandom").GetRawText());
                    Assert.Equal(I(row, "beforeNext"), I(row, "afterNext"));
                    foreach (string clock in new[] { "itemTime", "itemTimeMax", "itemAnimation", "itemAnimationMax" })
                        Assert.Equal(I(before, clock), I(expected, clock));
                    Assert.Equal(B(before, "releaseUseItem"), B(expected, "releaseUseItem"));
                    Assert.Empty(row.GetProperty("quickFrames").EnumerateArray());
                    // Detached plan must not mutate its caller's retained before image.
                    Assert.Equal(Items(before.GetProperty("inventory")), inventory);
                    Assert.Equal(Items(row.GetProperty("beforeBank4")), bank);
                    rows++;
                }
            }
        Assert.Equal(152, rows);

        foreach (string capture in new[] { "LinuxFirst", "LinuxRepeat", "WindowsFirst", "WindowsRepeat" })
        {
            using JsonDocument document = Read("QuickBuffMetadata" + capture + "1458");
            int metadataRows = 0;
            foreach (JsonElement row in document.RootElement.EnumerateArray())
            {
                bool potential = I(row, "buffType") > 0 && !B(row, "summon");
                Assert.Equal(potential, VanillaSelectedConsumableCatalog1458.MayParticipateInQuickBuff(new(I(row, "id"))));
                metadataRows++;
            }
            Assert.Equal(6396, metadataRows);
        }
        int closedRows = 0;
        foreach (string capture in new[] { "LinuxFirst", "LinuxRepeat", "WindowsFirst", "WindowsRepeat" })
        {
            using JsonDocument document = Read("QuickBuffClosed" + capture + "1458");
            foreach (JsonElement row in document.RootElement.EnumerateArray())
            {
                JsonElement before = row.GetProperty("afterCycle");
                Assert.Equal(Items(row.GetProperty("before").GetProperty("inventory")), Items(before.GetProperty("inventory")));
                var eligibility = default(QuickBuffEligibility1458);
                Assert.True(VanillaSelectedConsumable1458.TryPlanQuickBuff(Items(before.GetProperty("inventory")),
                    Items(row.GetProperty("beforeBank4")), Types(before.GetProperty("buffType")),
                    Integers(before.GetProperty("buffTime")), in eligibility, [], out var plan));
                Assert.Equal(Items(row.GetProperty("afterQuick").GetProperty("inventory")), plan!.Inventory);
                Assert.Equal(Types(row.GetProperty("afterQuick").GetProperty("buffType")), plan.BuffTypes);
                Assert.Equal(Integers(row.GetProperty("afterQuick").GetProperty("buffTime")), plan.BuffTimes);
                Assert.Single(plan.Mutations);
                Assert.Equal(0, plan.Mutations[0].SourceSlot);
                // Original object identity swaps without changing any final inventory tuple.
                // It is evidence for the runtime's separate private-custody ambiguity policy, not a Game funding field.
                if (row.GetProperty("cycle").GetBoolean())
                    Assert.Equal(1, row.GetProperty("afterCycleOrigins").GetProperty("main0").GetInt32());
                else Assert.Equal(0, row.GetProperty("afterCycleOrigins").GetProperty("main0").GetInt32());
                closedRows++;
            }
        }
        Assert.Equal(16, closedRows);
        VerifyUnsupportedAndEligibilityBoundaries();
    }

    private static void VerifyUnsupportedAndEligibilityBoundaries()
    {
        var inventory = new WorldItemPickupSlot1458[59];
        var bank = new WorldItemPickupSlot1458[40];
        var types = new BuffTypeId[44];
        var times = new int[44];
        var allowed = new QuickBuffEligibility1458(false, false, false, false, false, false);
        inventory[0] = new(292, 2, 0, false);
        // Food and other unsupported buff candidates are explicit refusal, not silently skipped.
        inventory[1] = new(289, 2, 0, false);
        Assert.False(VanillaSelectedConsumable1458.TryPlanQuickBuff(inventory, bank, types, times,
            in allowed, [], out _));
        var pending = allowed with { PendingInventoryActions = true };
        Assert.True(VanillaSelectedConsumable1458.TryPlanQuickBuff(inventory, bank, types, times,
            in pending, [], out var noOp));
        Assert.Equal(inventory, noOp!.Inventory);
        Assert.Empty(noOp.Mutations);
        inventory[1] = new(357, 2, 0, false);
        Assert.False(VanillaSelectedConsumable1458.TryPlanQuickBuff(inventory, bank, types, times,
            in allowed, [], out _));
        inventory[1] = new(9, 9999, 0, false);
        Assert.True(VanillaSelectedConsumable1458.TryPlanQuickBuff(inventory, bank, types, times,
            in allowed, [], out var ordinary));
        Assert.Equal(1, ordinary!.Inventory[0].Stack);
        Assert.Equal(inventory[1], ordinary.Inventory[1]);
        bank[0] = new(289, 2, 0, false);
        Assert.True(VanillaSelectedConsumable1458.TryPlanQuickBuff(inventory, bank, types, times,
            in allowed, [], out var inactiveVoid));
        Assert.Equal(bank, inactiveVoid!.VoidItems);
        inventory[2] = new(4131, 1, 0, false);
        Assert.False(VanillaSelectedConsumable1458.TryPlanQuickBuff(inventory, bank, types, times,
            in allowed, [], out _));
        inventory[2] = default;
        // Canonical detached-image validation applies even to an inactive bank. It is an
        // explicit input contract, not a claim that original QuickBuff reads that bank.
        bank[0] = new(6196, 1, 0, false);
        Assert.False(VanillaSelectedConsumable1458.TryPlanQuickBuff(inventory, bank, types, times,
            in allowed, [], out _));
    }

    private static void AddExpectedCost(List<QuickBuffMutation1458> costs, int slot,
        WorldItemPickupSlot1458 before, WorldItemPickupSlot1458 after)
    {
        if (before == after) return;
        Assert.True(VanillaSelectedConsumableCatalog1458.TryGet(new(before.Type), out var item));
        Assert.Equal(before.Stack - 1, after.Stack);
        costs.Add(new(slot, before, after, new((short)item.BuffType), item.BuffTime));
    }

    private static WorldItemPickupSlot1458[] Items(JsonElement array) => array.EnumerateArray()
        .Select(item => new WorldItemPickupSlot1458(I(item, "type"), I(item, "stack"), I(item, "prefix"), B(item, "favorite"))).ToArray();
    private static BuffTypeId[] Types(JsonElement array) => array.EnumerateArray().Select(item => new BuffTypeId(item.GetInt16())).ToArray();
    private static int[] Integers(JsonElement array) => array.EnumerateArray().Select(item => item.GetInt32()).ToArray();
    private static int I(JsonElement item, string field) => item.GetProperty(field).GetInt32();
    private static bool B(JsonElement item, string field) => item.GetProperty(field).GetBoolean();
    private static JsonDocument Read(string resource)
    {
        using Stream compressed = typeof(QuickBuffSix1458Tests).Assembly.GetManifestResourceStream(resource)!;
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
