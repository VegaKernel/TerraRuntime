using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Contracts.Gameplay;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class OversizedCoinPickupStorage1458Tests
{
    [Fact]
    public void Original_positive_wire_retained_coins_follow_GetItem_and_BuyItem_with_nominal_capacity_separate()
    {
        Verify("OriginalOversizedCoinPickup1458", 16);
        Verify("OriginalRetainedCoinPickup1458", 40);
        Verify("OriginalExistingCoinPickup1458", 144);
        VerifyWallet("OriginalRetainedCoinWallet1458", 40);
        VerifyWallet("OriginalRetainedPrefixedCoinWallet1458", 8);
        VerifySortChain();
        var inventory = new WorldItemPickupSlot1458[59];
        Assert.False(VanillaWorldItemOwner1458.TryPlanStorage(new(3, 10000, 0, false), inventory, false, null, out _));
        Assert.False(VanillaWorldItemOwner1458.TryPlanStorage(new(74, 39996, 0, false), inventory, false, null, out _));
        Assert.False(VanillaWorldItemOwner1458.TryPlanStorage(new(74, 19998, VanillaItemPrefixCatalog.Count, false), inventory, false, null, out _));
        inventory[0] = new(74, 39996, 0, false);
        Assert.False(VanillaWorldItemOwner1458.TryPlanStorage(new(74, 1, 0, false), inventory, false, null, out _));
    }

    [Fact]
    public void Original_oversized_retained_cursor_transitions_preserve_source_nominal_capacity()
    {
        VerifyCursor("OriginalRetainedCoinCursor1458", 48);
        VerifyCursor("OriginalRetainedCoinSlotCursor1458", 8);
        VerifyRejectedCoinSlotExchange();
        VerifyPickupCursorChain();
    }

    private static void VerifyPickupCursorChain()
    {
        using var stream = typeof(OversizedCoinPickupStorage1458Tests).Assembly.GetManifestResourceStream("OriginalOversizedCoinPickupCursorChain1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        int rows = 0;
        foreach (var platform in document.RootElement.GetProperty("platforms").EnumerateObject())
        foreach (var row in platform.Value.GetProperty("rows").EnumerateArray())
        {
            var before = row.GetProperty("before").EnumerateArray().Select(Read).ToArray();
            Assert.True(VanillaWorldItemOwner1458.TryPlanStorage(Read(row.GetProperty("beforeIncoming")), before, false, null, out var plan));
            Assert.NotNull(plan);
            foreach (var mutation in plan.Mutations) before[mutation.Slot] = mutation.After;
            Assert.Equal(row.GetProperty("afterPickup219").EnumerateArray().Take(59).Select(Read).ToArray(), before);
            Assert.True(VanillaInventoryCursorMove1458.TryPlanLeftClick(before[53], before[58], out var slot, out var cursor, VanillaInventoryCursorMove1458.CoinContext));
            Assert.Equal(Read(row.GetProperty("afterClick219")[53]), slot);
            Assert.Equal(Read(row.GetProperty("afterClickMouse")), cursor);
            Assert.Equal(cursor, Read(row.GetProperty("afterMirror219")[58]));
            Assert.Equal(row.GetProperty("beforeRng").GetRawText(), row.GetProperty("afterSyncRng").GetRawText());
            rows++;
        }
        Assert.Equal(4, rows);
    }

    private static void VerifyRejectedCoinSlotExchange()
    {
        using var stream = typeof(OversizedCoinPickupStorage1458Tests).Assembly.GetManifestResourceStream("OriginalRetainedCoinSlotExchange1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        int rows = 0;
        foreach (var platform in document.RootElement.GetProperty("platforms").EnumerateObject())
        foreach (var row in platform.Value.GetProperty("rows").EnumerateArray())
        {
            var before = row.GetProperty("before");
            var after = row.GetProperty("after");
            var slot = Read(before.GetProperty("inventory")[53]);
            var cursor = Read(before.GetProperty("mouse"));
            Assert.False(VanillaInventoryCursorMove1458.TryPlanLeftClick(slot, cursor, out _, out _, VanillaInventoryCursorMove1458.CoinContext));
            Assert.Equal(slot, Read(after.GetProperty("inventory")[53]));
            Assert.Equal(cursor, Read(after.GetProperty("mouse")));
            Assert.Empty(row.GetProperty("syncFrames").EnumerateArray());
            Assert.Equal(before.GetProperty("random").GetRawText(), after.GetProperty("random").GetRawText());
            Assert.Equal(before.GetProperty("next").GetInt32(), after.GetProperty("next").GetInt32());
            rows++;
        }
        Assert.Equal(4, rows);
    }

    private static void VerifyCursor(string resource, int expectedRows)
    {
        using var stream = typeof(OversizedCoinPickupStorage1458Tests).Assembly.GetManifestResourceStream(resource)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        int rows = 0;
        foreach (var platform in document.RootElement.GetProperty("platforms").EnumerateObject())
        foreach (var row in platform.Value.GetProperty("rows").EnumerateArray())
        {
            var spec = row.GetProperty("spec");
            int context = spec.GetProperty("context").GetInt32();
            int bank = spec.GetProperty("bank").GetInt32();
            int slotIndex = spec.GetProperty("slot").GetInt32();
            var before = row.GetProperty("before");
            var after = row.GetProperty("after");
            var slot = Read(context is 0 or 1 ? before.GetProperty("inventory")[slotIndex] : before.GetProperty("banks")[bank][slotIndex]);
            var cursor = Read(before.GetProperty("mouse"));
            WorldItemPickupSlot1458 nextSlot, nextCursor;
            bool admitted = spec.GetProperty("op").GetString() == "left"
                ? VanillaInventoryCursorMove1458.TryPlanLeftClick(slot, cursor, out nextSlot, out nextCursor, context)
                : VanillaInventoryCursorMove1458.TryPlanSingleToCursor(slot, cursor, out nextSlot, out nextCursor, context);
            Assert.True(admitted);
            Assert.Equal(Read(context is 0 or 1 ? after.GetProperty("inventory")[slotIndex] : after.GetProperty("banks")[bank][slotIndex]), nextSlot);
            Assert.Equal(Read(after.GetProperty("mouse")), nextCursor);
            Assert.Equal(before.GetProperty("random").GetRawText(), after.GetProperty("random").GetRawText());
            Assert.Equal(before.GetProperty("next").GetInt32(), after.GetProperty("next").GetInt32());
            if (nextCursor.Stack > short.MaxValue)
                Assert.False(VanillaInventoryCursorMove1458.TryResolveReportedTransition(slot, cursor, nextSlot, nextCursor, out _, context));
            rows++;
        }
        Assert.Equal(expectedRows, rows);
        foreach (var id in new[] { VanillaCoinFacts.CopperCoin, VanillaCoinFacts.SilverCoin, VanillaCoinFacts.GoldCoin, VanillaCoinFacts.PlatinumCoin })
        {
            Assert.True(VanillaCoinFacts.IsValidRetainedStack(id, short.MaxValue));
            Assert.False(VanillaCoinFacts.IsValidRetainedStack(id, 0));
            Assert.False(VanillaCoinFacts.IsValidRetainedStack(id, -1));
            Assert.False(VanillaCoinFacts.IsValidRetainedStack(id, 32768));
            Assert.False(VanillaInventoryCursorMove1458.TryPlanLeftClick(new(id.Value, 1, 1, false), default, out _, out _));
        }
        Assert.False(VanillaCoinFacts.IsValidRetainedStack(new ItemTypeId(3), 1));
        Assert.False(VanillaCoinFacts.IsValidStack(VanillaCoinFacts.PlatinumCoin, 19998));
        Assert.True(VanillaCoinFacts.IsValidStack(VanillaCoinFacts.PlatinumCoin, 9999));
    }

    private static void VerifySortChain()
    {
        using var stream = typeof(OversizedCoinPickupStorage1458Tests).Assembly.GetManifestResourceStream("OriginalOversizedCoinPickupSort1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        int rows = 0;
        foreach (var platform in document.RootElement.GetProperty("platforms").EnumerateObject())
        foreach (var row in platform.Value.GetProperty("rows").EnumerateArray())
        {
            var before = row.GetProperty("beforeSort").EnumerateArray().Select(Read).ToArray();
            Assert.True(VanillaWorldItemOwner1458.TryPlanSort(before, true, 0, 228, false,
                platform.Name == "Windows", new(false, false, false), out var plan));
            Assert.NotNull(plan);
            Assert.Equal(row.GetProperty("afterSort").EnumerateArray().Select(Read).ToArray(), plan.Slots);
            Assert.Empty(plan.Overflow ?? []);
            Assert.Equal(row.GetProperty("beforeRng").GetRawText(), row.GetProperty("afterSortRng").GetRawText());
            rows++;
        }
        Assert.Equal(4, rows);
    }

    private static void VerifyWallet(string resource, int expectedRows)
    {
        using var stream = typeof(OversizedCoinPickupStorage1458Tests).Assembly.GetManifestResourceStream(resource)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        int rows = 0;
        foreach (var platform in document.RootElement.GetProperty("platforms").EnumerateObject())
        foreach (var row in platform.Value.GetProperty("rows").EnumerateArray())
        {
            var before = row.GetProperty("before");
            var inventory = before.GetProperty("inventory").EnumerateArray().Select(Read).ToArray();
            var banks = before.GetProperty("banks").EnumerateArray().SelectMany(bank => bank.EnumerateArray().Select(Read)).ToArray();
            long price = row.GetProperty("spec").GetProperty("price").GetInt64();
            bool accepted = VanillaShopWallet1458.TryPlanPurchase(inventory, banks, price, out var plan);
            Assert.Equal(row.GetProperty("result").GetBoolean(), accepted);
            if (accepted)
            {
                Assert.NotNull(plan);
                var after = row.GetProperty("after");
                Assert.Equal(after.GetProperty("inventory").EnumerateArray().Select(Read).ToArray(), plan.Inventory);
                Assert.Equal(after.GetProperty("banks").EnumerateArray().SelectMany(bank => bank.EnumerateArray().Select(Read)).ToArray(), plan.Banks);
                Assert.Equal(price, plan.Copper);
            }
            else Assert.Null(plan);
            Assert.Equal(row.GetProperty("beforeRng").GetRawText(), row.GetProperty("afterRng").GetRawText());
            Assert.Equal(row.GetProperty("beforeNext").GetInt32(), row.GetProperty("afterNext").GetInt32());
            rows++;
        }
        Assert.Equal(expectedRows, rows);
    }

    private static void Verify(string resource, int expectedRows)
    {
        using var stream = typeof(OversizedCoinPickupStorage1458Tests).Assembly.GetManifestResourceStream(resource)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        int rows = 0;
        foreach (var platform in document.RootElement.GetProperty("platforms").EnumerateObject())
        foreach (var row in platform.Value.GetProperty("rows").EnumerateArray())
        {
            var incoming = Read(row.GetProperty("beforeIncoming"));
            var before = row.GetProperty("before").EnumerateArray().Select(Read).ToArray();
            var expected = row.GetProperty("after").EnumerateArray().Select(Read).ToArray();
            Assert.True(VanillaWorldItemOwner1458.TryPlanStorage(in incoming, before, false, null, out var plan),
                $"{platform.Name}/{row.GetProperty("profile").GetString()}");
            Assert.NotNull(plan);
            var projected = before.ToArray();
            foreach (var mutation in plan.Mutations)
            {
                Assert.Equal(projected[mutation.Slot], mutation.Before);
                projected[mutation.Slot] = mutation.After;
            }
            Assert.Equal(expected, projected);
            Assert.Equal(Read(row.GetProperty("residual")), plan.Residual);
            Assert.Equal(incoming.Stack - plan.Residual.Stack, plan.Taken);
            if (expectedRows == 16)
            {
                Assert.Equal(row.GetProperty("beforeRng").GetRawText(), row.GetProperty("afterRng").GetRawText());
                Assert.Equal(row.GetProperty("beforeNext").GetInt32(), row.GetProperty("afterNext").GetInt32());
                Assert.Equal(219, row.GetProperty("before219").GetArrayLength());
                Assert.Equal(219, row.GetProperty("after219").GetArrayLength());
                // Source empty-slot output is intentionally oversized, not guessed 9999 chunks.
                if (plan.Residual.Type == 0) Assert.Contains(projected, item => item.Type == 74 && item.Stack == incoming.Stack);
            }
            rows++;
        }
        Assert.Equal(expectedRows, rows);
    }

    private static WorldItemPickupSlot1458 Read(JsonElement item)
    {
        int type = item.GetProperty("type").GetInt32();
        return type == 0 ? default : new(type, item.GetProperty("stack").GetInt32(),
            item.GetProperty("prefix").GetInt32(), item.GetProperty("favorite").GetBoolean());
    }
}
