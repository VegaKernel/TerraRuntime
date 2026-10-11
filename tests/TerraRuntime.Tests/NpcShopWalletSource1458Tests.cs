using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Gameplay.Items;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class NpcShopWalletSource1458Tests
{
    [Fact]
    public void Wallet_purchase_and_sale_preserve_original_full_images_and_bank_order()
    {
        // WorldItemPickupSlot1458 has an integer prefix; imported malformed
        // detached images must not become canonical through payment planning.
        var malformed = new WorldItemPickupSlot1458[59];
        malformed[0] = new(9, 1, -1, false);
        Check(!VanillaShopWallet1458.TryPlanPurchase(malformed, new WorldItemPickupSlot1458[160], 0, out _), "negative imported prefix refused");
        using var document = Read("ShopWallet1458");
        foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
        {
            string name = row.GetProperty("name").GetString()!;
            var before = row.GetProperty("before"); var inventory = before.GetProperty("inventory").EnumerateArray().Select(Slot).ToArray(); var banks = before.GetProperty("banks").EnumerateArray().SelectMany(x => x.EnumerateArray().Select(Slot)).ToArray();
            if (inventory.Concat(banks).Any(x => x.Type != 0 && x.Stack <= 0)) { Assert.Equal("empty_short", name); Assert.False(VanillaShopWallet1458.TryPlanPurchase(inventory, banks, specificationPrice(row), out _)); continue; }
            var spec = row.GetProperty("spec"); var offered = row.GetProperty("itemBefore");
            Check(VanillaShopWallet1458.TryResolvePrices(offered.GetProperty("shopCustomPrice").ValueKind == JsonValueKind.Null ? offered.GetProperty("value").GetInt32() : offered.GetProperty("shopCustomPrice").GetInt32(), spec.GetProperty("adjust").GetSingle(), spec.GetProperty("discount").GetBoolean(), spec.GetProperty("buyOnce").GetBoolean(), spec.GetProperty("discount").GetBoolean(), out long sell, out long buy), name + " price admitted");
            Check(sell == row.GetProperty("sellPrice").GetInt64() && buy == row.GetProperty("buyPrice").GetInt64(), name + " exact price");
            bool ok; ShopWalletPlan1458? plan;
            if (row.GetProperty("kind").GetString() == "buy") ok = VanillaShopWallet1458.TryPlanPurchase(inventory, banks, spec.GetProperty("price").GetInt64(), out plan);
            else ok = VanillaShopWallet1458.TryPlanSale(inventory, banks, sell, buy, offered.GetProperty("stack").GetInt32(), 0, out plan);
            Check(ok == row.GetProperty("result").GetBoolean(), name + " result");
            var after = row.GetProperty("after"); var expectedInv = after.GetProperty("inventory").EnumerateArray().Select(Slot).ToArray(); var expectedBanks = after.GetProperty("banks").EnumerateArray().SelectMany(x => x.EnumerateArray().Select(Slot)).ToArray();
            for (int i = 0; i < inventory.Length; i++) Check((ok ? plan!.Inventory[i] : inventory[i]) == expectedInv[i], name + " inventory" + i);
            for (int i = 0; i < banks.Length; i++) Check((ok ? plan!.Banks[i] : banks[i]) == expectedBanks[i], name + " bank" + i);

        }
    }

    [Fact]
    public void Store_values_and_forced_prefix_prices_match_captured_platforms()
    {
        using (var metadata = Read("ShopValueMetadata1458"))
        {
            foreach (string corpus in new[] { "nearHalfPrice", "largePrice" })
            foreach (string platform in new[] { "Linux", "Windows" })
            foreach (var row in metadata.RootElement.GetProperty(corpus).GetProperty(platform).GetProperty("records").EnumerateArray())
            {
                long sourceSelling = row.GetProperty("selling").GetInt64();
                long sourceBuying = row.GetProperty("buying").GetInt64();
                // These configured large-price rows expose source conversion overflow:
                // CoreCLR saturates at int.MaxValue; CLR4 yields int.MinValue.
                // Neither diagnostic is an admitted runtime economic price.
                bool expected = sourceSelling >= 0 && sourceBuying >= 0 &&
                    sourceSelling != int.MaxValue && sourceBuying != int.MaxValue;
                bool admitted = VanillaShopWallet1458.TryResolvePrices(row.GetProperty("value").GetInt32(),
                    row.GetProperty("adjustment").GetSingle(), row.GetProperty("discount").GetBoolean(),
                    row.GetProperty("buyOnce").GetBoolean(), true, out long selling, out long buying,
                    platform == "Windows");
                Check(admitted == expected, corpus + platform + " original price or overflow refusal " + row.GetProperty("value") + "/" + row.GetProperty("adjustment"));
                if (admitted) Check(selling == sourceSelling && buying == sourceBuying,
                    corpus + platform + " exact original rounded prices");
            }
            // Actual stock/payment composition must use the same price arithmetic
            // context, rather than correcting only the detached price query.
            foreach (bool windows in new[] { false, true })
            {
                var quoteWallet = new WorldItemPickupSlot1458[59];
                quoteWallet[50] = new(72, 10, 0, false);
                var quoteStock = new ShopItem1458[40];
                quoteStock[0] = new(new(9, 1, 0, false), 100, false, true, 150);
                Check(VanillaShopTransaction1458.TryPlanBuy(quoteWallet, new WorldItemPickupSlot1458[160],
                    quoteStock, [], default, 0, 1, new(1.01f, false, windows), out var roundedPurchase),
                    "source near-half composed payment");
                Check(roundedPurchase!.CopperDelta == (windows ? -151 : -152),
                    "source near-half composed charge follows platform context");
            }
            var variants = new HashSet<int> { 112, 157, 197, 517, 544, 556, 557, 683, 725, 1314, 1319, 1325, 2623, 3069, 4060, 5147, 5279, 5280, 5281, 5282, 5283, 5284, 5334, 5687, 5688 };
            foreach (var row in metadata.RootElement.GetProperty("normal").GetProperty("records").EnumerateArray())
            {
                int requested = row.GetProperty("requested").GetInt32(); bool expected = row.GetProperty("retainedType").GetInt32() != 0 && !variants.Contains(requested);
                bool ok = VanillaItemStoreValue1458.TryGet(new(requested), new(0), out int value);
                Check(ok == expected, "metadata admission " + requested); if (ok) Check(value == row.GetProperty("storeValue").GetInt32(), "metadata value " + requested);
                bool known = VanillaItemStoreValue1458.TryGet(new(requested), new(0), out int knownValue,
                    world: new(false, false, false));
                Check(known == (row.GetProperty("retainedType").GetInt32() != 0), "known inactive metadata " + requested);
                if (known) Check(knownValue == row.GetProperty("storeValue").GetInt32(), "known inactive value " + requested);
                if (variants.Contains(requested))
                    Check(!VanillaItemStoreValue1458.TryGet(new(requested), new(0), out _, world: new(true, true, true)), "active variant price refusal " + requested);
                if (requested != 24 && requested != 219) Check(!VanillaItemStoreValue1458.TryGet(new(requested), new(82), out _), "noncaptured prefix unsupported " + requested);
            }
            Check(!VanillaItemStoreValue1458.TryGet(new(6196), new(0), out _), "unknown positive ID");
            foreach (var row in metadata.RootElement.GetProperty("memoIdentity").GetProperty("records").EnumerateArray())
            {
                bool expected = row.GetProperty("retained").GetInt32() != 0;
                bool ok = VanillaShopTransaction1458.TryGetMemoIdentity(new(row.GetProperty("requested").GetInt32()), out int key);
                Check(ok == expected, "memo identity admission " + row.GetProperty("requested"));
                if (ok) Check(key == row.GetProperty("memoKey").GetInt32(), "memo original identity " + row.GetProperty("requested"));
            }
            Check(!VanillaShopTransaction1458.TryGetMemoIdentity(new(6196), out _), "unknown memo ID");
        }

        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var prefix = Read("ShopValueMetadata1458");
            foreach (var row in prefix.RootElement.GetProperty(platform).GetProperty("records").EnumerateArray())
            {
                int requested = row.GetProperty("requested").GetInt32(); bool expected = requested == 0 || (row.GetProperty("accepted").GetBoolean() && row.GetProperty("retainedPrefix").GetInt32() == requested);
                bool ok = VanillaItemStoreValue1458.TryGet(new(row.GetProperty("id").GetInt32()), new(requested), out int value, platform == "Windows"); Check(ok == expected, platform + " prefix price admission " + requested); if (ok) Check(value == row.GetProperty("storeValue").GetInt32(), platform + " prefix price value " + requested);
            }
        }

        // Detached policy: unrelated custom-currency offers retain their exact
        // metadata without blocking the represented ordinary cash transaction.
        var mixedInventory = new WorldItemPickupSlot1458[59];
        var mixedBanks = new WorldItemPickupSlot1458[160];
        var mixedStock = new ShopItem1458[40];
        mixedInventory[50] = new(72, 10, 0, false);
        mixedStock[0] = new(new(24, 1, 0, false), 100, false, true);
        mixedStock[12] = new(new(9, 1, 0, false), 0, false, true, 3, 1);
        var mixedContext = new ShopPriceContext1458(1f, false);
        Check(VanillaShopTransaction1458.TryPlanBuy(mixedInventory, mixedBanks, mixedStock, [], default,
            0, 1, mixedContext, out var cash), "cash purchase in mixed-currency stock");
        Check(cash!.Stock[12] == mixedStock[12], "special offer preserved after cash buy");
        Check(!VanillaShopTransaction1458.TryPlanBuy(mixedInventory, mixedBanks, mixedStock, [], default,
            12, 1, mixedContext, out _), "selected special currency is explicitly unsupported");
        Check(VanillaShopTransaction1458.TryPlanSell(cash.Wallet.Inventory, cash.Wallet.Banks,
            cash.Stock, cash.Memos, cash.Cursor, mixedContext, out var sale), "cash sale in mixed-currency stock");
        Check(sale!.Stock[12] == mixedStock[12], "special offer preserved after sale");
        using (var source = Read("ShopValueMetadata1458"))
        {
            int representedShops = 0;
            foreach (var row in source.RootElement.GetProperty("setupShop").GetProperty("rows").EnumerateArray())
            {
                var sourceStock = row.GetProperty("stock");
                var offers = sourceStock.EnumerateArray().Select(entry =>
                {
                    var item = ShopItem(entry.GetProperty("item"));
                    if (item == default) return item;
                    return item with
                    {
                        CustomPrice = entry.GetProperty("customPrice").ValueKind == JsonValueKind.Null ? null : entry.GetProperty("customPrice").GetInt32(),
                        Currency = entry.GetProperty("currency").GetInt32() < 0 ? null : entry.GetProperty("currency").GetInt32()
                    };
                }).ToArray();
                if (offers.All(item => item == default))
                {
                    Check(row.GetProperty("vendor").GetInt32() == 368, "source default travel shop is empty");
                    continue;
                }
                representedShops++;
                var wallet = new WorldItemPickupSlot1458[59];
                wallet[50] = new(74, 9999, 0, false);
                var context = new ShopPriceContext1458(1f, false, World: new(false, false, false));
                for (int slot = 0; slot < offers.Length; slot++)
                {
                    if (offers[slot] == default) continue;
                    bool ok = VanillaShopTransaction1458.TryPlanBuy(wallet, mixedBanks, offers, [], default,
                        slot, 1, context, out var bought);
                    Check(ok == (offers[slot].Currency is null), row.GetProperty("id") + " cash/special selected admission " + slot);
                    if (!ok) continue;
                    Check(bought!.CopperDelta == -sourceStock[slot].GetProperty("buying").GetInt64(), "source SetupShop exact coin quote");
                    Check(bought.Stock.SequenceEqual(offers), "all forty source offers preserved after regular purchase");
                }
            }
            Check(representedShops == 14, "source SetupShop fourteen nonempty normal/progressed profiles");
            foreach (var row in source.RootElement.GetProperty("bankMove").GetProperty("rows").EnumerateArray())
                VerifyBankMove(row);
            foreach (var row in source.RootElement.GetProperty("bankLastFavorite").GetProperty("rows").EnumerateArray())
                VerifyBankMove(row);
        }
    }

    [Fact]
    public void Shop_cursor_birth_sale_and_sellback_match_original_ui_images()
    {
        using (var ui = Read("ShopUi1458"))
        {
            foreach (var row in ui.RootElement.GetProperty("rows").EnumerateArray())
            {
                var spec = row.GetProperty("spec"); string name = spec.GetProperty("name").GetString()!; string op = spec.GetProperty("op").GetString()!; var before = row.GetProperty("before"); var after = row.GetProperty("after");
                var inventory = before.GetProperty("inventory").EnumerateArray().Select(Slot).ToArray(); var banks = before.GetProperty("banks").EnumerateArray().SelectMany(x => x.EnumerateArray().Select(Slot)).ToArray(); inventory[58] = Slot(before.GetProperty("mouse")); var stock = Slot(before.GetProperty("shop")[0]);
                var priced = op == "sell" ? before.GetProperty("mouse") : before.GetProperty("shop")[0];
                Check(VanillaItemStoreValue1458.TryGet(new(priced.GetProperty("type").GetInt32()), new(priced.GetProperty("prefix").GetInt32()), out int storeValue), name + " admitted source price metadata");
                Check(VanillaShopWallet1458.TryResolvePrices(storeValue, spec.GetProperty("adjust").GetSingle(), spec.GetProperty("discount").GetBoolean(), priced.GetProperty("buyOnce").GetBoolean(), priced.GetProperty("isAShopItem").GetBoolean(), out long resolvedSelling, out long resolvedBuying), name + " source price calculation");
                Check(resolvedSelling == row.GetProperty("sellPrice").GetInt64() && resolvedBuying == row.GetProperty("buyPrice").GetInt64(), name + " exact original UI quote");
                ShopWalletPlan1458? final = null; ShopCursorPurchasePlan1458? purchase = null;
                if (op != "sell")
                {
                    bool ok = VanillaShopWallet1458.TryPlanCursorPurchase(inventory, banks, stock, spec.GetProperty("once").GetBoolean(), row.GetProperty("buyPrice").GetInt64(), spec.GetProperty("repeat").GetInt32() * (spec.GetProperty("fast").GetInt32() + 1), out purchase, spec.GetProperty("animation").GetInt32(), spec.GetProperty("time").GetInt32(), op == "right_buy", spec.GetProperty("split").GetInt32()); if (ok) final = purchase!.Wallet;
                    if (op == "roundtrip" && ok) { Check(VanillaShopWallet1458.TryPlanCursorSale(final!.Inventory, final.Banks, row.GetProperty("sellPrice").GetInt64(), row.GetProperty("buyPrice").GetInt64(), before.GetProperty("shop")[0].GetProperty("value").GetInt32(), purchase!.SellbackAdded, out final), name + " roundtrip sale"); }
                }
                else VanillaShopWallet1458.TryPlanCursorSale(inventory, banks, row.GetProperty("sellPrice").GetInt64(), row.GetProperty("buyPrice").GetInt64(), before.GetProperty("mouse").GetProperty("value").GetInt32(), 0, out final);
                var expected = after.GetProperty("inventory").EnumerateArray().Select(Slot).ToArray(); expected[58] = Slot(after.GetProperty("mouse")); var expectedBanks = after.GetProperty("banks").EnumerateArray().SelectMany(x => x.EnumerateArray().Select(Slot)).ToArray();
                for (int slot = 0; slot < inventory.Length; slot++) Check((final?.Inventory ?? inventory)[slot] == expected[slot], name + " UI inventory" + slot);
                for (int slot = 0; slot < banks.Length; slot++) Check((final?.Banks ?? banks)[slot] == expectedBanks[slot], name + " UI bank" + slot);
                if (op != "sell" && op != "roundtrip") Check((purchase?.Stock ?? stock) == Slot(after.GetProperty("shop")[0]), name + " UI stock");
                VerifyShopTransaction(row);
            }
        }

        using (var source = Read("ShopValueMetadata1458"))
        {
            var row = source.RootElement.GetProperty("maxBatch").GetProperty("rows")[0];
            // The pinned original driver explicitly configures shopCustomPrice=1;
            // the source Dump tuple omits that input, so retain it separately here.
            VerifyShopTransaction(row, customPrice: 1);
            Check(row.GetProperty("transfers").GetProperty("count").GetInt32() == 9999,
                "original maximum batch emits 9999 per-unit transfers");
            Check(row.GetProperty("transfers").GetProperty("last").GetProperty("amount").GetInt32() == 9999,
                "original final transfer amount is resulting cursor stack");
        }
        using (var source = Read("ShopValueMetadata1458"))
            foreach (var row in source.RootElement.GetProperty("prefixWalk").GetProperty("rows").EnumerateArray())
                VerifyPrefixWalker(row);
        // Source HandleShopSlot retains earlier completed units when a later
        // BuyItem fails. This bounded source-read regression is not a new capture.
        var partialInventory = new WorldItemPickupSlot1458[59];
        partialInventory[50] = new(72, 1, 0, false);
        var partialStock = new ShopItem1458[40];
        partialStock[0] = new(new(24, 1, 0, false), 100, false, true);
        Check(VanillaShopTransaction1458.TryPlanBuy(partialInventory, new WorldItemPickupSlot1458[160],
            partialStock, [], default, 0, 2, new(1f, false), out var partial), "partial batch retains completed unit");
        Check(partial!.Units == 1 && partial.CopperDelta == -100 && partial.Cursor.Tuple.Stack == 1,
            "partial batch cannot aggregate the requested price");
        Check(VanillaShopTransaction1458.TryCreateBuySellbackPrefixWalker(partialInventory,
            new WorldItemPickupSlot1458[160], partialStock, [], default, 0, new(1f, false), 2,
            out var bounded) && bounded!.TryAdvance(), "reusable partial prefix");
        Check(bounded!.TryDetachCurrent(out var firstBuy, out var firstSell), "first prefix detached");
        var detachedBuyImage = firstBuy!.Wallet.Inventory.ToArray();
        var detachedSellImage = firstSell!.Wallet.Inventory.ToArray();
        Check(!bounded.TryAdvance() && bounded.Units == 1 &&
            bounded.BoughtInventory.SequenceEqual(detachedBuyImage), "failed next payment preserves completed prefix");
        Check(firstBuy.Wallet.Inventory.AsSpan().SequenceEqual(detachedBuyImage) &&
            firstSell.Wallet.Inventory.AsSpan().SequenceEqual(detachedSellImage), "detached refusal images stable");
        var richInventory = new WorldItemPickupSlot1458[59];
        richInventory[50] = new(74, 1, 0, false);
        partialStock[0] = new(new(110, 1, 0, false), 100, false, true);
        Check(VanillaShopTransaction1458.TryCreateBuySellbackPrefixWalker(richInventory,
            new WorldItemPickupSlot1458[160], partialStock, [], default, 0, new(1f, false), 2,
            out var advancing) && advancing!.TryAdvance() && advancing.TryDetachCurrent(out firstBuy, out firstSell),
            "detach while later prefix remains available");
        detachedBuyImage = firstBuy!.Wallet.Inventory.ToArray();
        detachedSellImage = firstSell!.Wallet.Inventory.ToArray();
        var detachedBuyStock = firstBuy.Stock.ToArray();
        var detachedSellStock = firstSell.Stock.ToArray();
        Check(advancing!.TryAdvance() && advancing.Units == 2 &&
            firstBuy.Wallet.Inventory.AsSpan().SequenceEqual(detachedBuyImage) &&
            firstSell.Wallet.Inventory.AsSpan().SequenceEqual(detachedSellImage) &&
            firstBuy.Stock.AsSpan().SequenceEqual(detachedBuyStock) &&
            firstSell.Stock.AsSpan().SequenceEqual(detachedSellStock), "detached plans survive later scratch advance");

    }

    private static void VerifyPrefixWalker(JsonElement row)
    {
        var before = row.GetProperty("before");
        var inventory = before.GetProperty("inventory").EnumerateArray().Select(SourceSlot).ToArray();
        var banks = before.GetProperty("banks").EnumerateArray().SelectMany(x => x.EnumerateArray().Select(SourceSlot)).ToArray();
        var stock = before.GetProperty("shop").EnumerateArray().Select(ShopItem).ToArray();
        stock[0] = stock[0] with { CustomPrice = 100 };
        int count = row.GetProperty("spec").GetProperty("fast").GetInt32() + 1;
        Check(VanillaShopTransaction1458.TryCreateBuySellbackPrefixWalker(inventory, banks, stock,
            [], default, 0, new(1f, false), count, out var walker), "source prefix walker prepared");
        for (int unit = 0; unit < count; unit++)
            Check(walker!.TryAdvance(), "source prefix advances through " + count);
        Check(walker!.Units == count && walker.ChargedCopper == count * 100L &&
            walker.GrantedCopper == count * 100L && walker.NetCopperDelta == 0 && walker.CanSellCurrent,
            "source cumulative charge and refund");
        Compare(row.GetProperty("afterBuy"), walker.BoughtInventory, walker.BoughtBanks,
            walker.BoughtStock, walker.BoughtMemos, walker.BoughtCursor);
        Compare(row.GetProperty("after"), walker.SoldInventory, walker.SoldBanks,
            walker.SoldStock, walker.SoldMemos, walker.SoldCursor);
        Check(walker.TryDetachCurrent(out var bought, out var sold) && bought is not null && sold is not null,
            "matching borrowed prefix detaches");
        Check(bought!.Wallet.Copper == count * 100L && sold!.Wallet.Copper == count * 100L &&
            bought.Units == count && sold.Units == count, "detached cumulative plans");
        Check(!walker.TryAdvance(), "requested prefix bound");

        foreach (bool windows in new[] { false, true })
        {
            Check(VanillaShopTransaction1458.TryCreatePurchasePrefixWalker(inventory, banks, stock,
                [], default, 0, new(1f, false, windows), count, out var purchase), "original purchase prefix prepared");
            for (int unit = 1; unit <= count; unit++)
                Check(purchase!.TryAdvance() && purchase.Units == unit, "original purchase one unit per advance");
            Compare(row.GetProperty("afterBuy"), purchase!.BoughtInventory, purchase.BoughtBanks,
                purchase.BoughtStock, purchase.BoughtMemos, purchase.BoughtCursor);
            Check(purchase.TryDetachCurrent(out var detached) && detached!.Units == count &&
                detached.Wallet.Copper == count * 100L, "original detached purchase charge");
            Check(!purchase.TryAdvance(), "original purchase canonical bound");
        }

        static WorldItemPickupSlot1458 SourceSlot(JsonElement item)
        {
            if (item.GetProperty("type").GetInt32() != 0) return Slot(item);
            int stack = item.GetProperty("stack").GetInt32();
            Check(stack is 0 or 1 && item.GetProperty("prefix").GetInt32() == 0 &&
                !item.GetProperty("favorite").GetBoolean(), "original TurnToAir raw empty shape");
            // Original DoCoins calls TurnToAir, retaining raw stack1. The
            // established runtime canonical inventory uses default Air0/0.
            return default;
        }

        static void Compare(JsonElement source, ReadOnlySpan<WorldItemPickupSlot1458> inventory,
            ReadOnlySpan<WorldItemPickupSlot1458> banks, ReadOnlySpan<ShopItem1458> stock,
            ReadOnlySpan<ShopMemo1458> memos, ShopItem1458 cursor)
        {
            var expected = source.GetProperty("inventory").EnumerateArray().Select(SourceSlot).ToArray();
            expected[58] = SourceSlot(source.GetProperty("mouse"));
            for (int slot = 0; slot < expected.Length; slot++)
                Check(inventory[slot] == expected[slot], "original prefix full inventory slot" + slot + " actual=" + inventory[slot] + " expected=" + expected[slot]);
            Check(banks.SequenceEqual(source.GetProperty("banks").EnumerateArray().SelectMany(x => x.EnumerateArray().Select(SourceSlot)).ToArray()), "original prefix all four banks");
            var offers = source.GetProperty("shop").EnumerateArray().Select(ShopItem).ToArray();
            offers[0] = offers[0] with { CustomPrice = 100 };
            Check(stock.SequenceEqual(offers), "original prefix stock40");
            Check(memos.SequenceEqual(source.GetProperty("memo").EnumerateArray().Select(Memo).ToArray()), "original prefix memo order");
            Check(cursor == ShopItem(source.GetProperty("mouse")), "original prefix economic cursor");
        }
    }

    private static long specificationPrice(JsonElement row) => row.GetProperty("spec").GetProperty("price").GetInt64();
    private static void VerifyBankMove(JsonElement row)
    {
        var spec = row.GetProperty("spec");
        string name = spec.GetProperty("name").GetString()!;
        var before = row.GetProperty("before");
        var after = row.GetProperty("after");
        var inventory = before.GetProperty("inventory").EnumerateArray().Select(Slot).ToArray();
        var banks = before.GetProperty("banks").EnumerateArray().Select(bank => bank.EnumerateArray().Select(Slot).ToArray()).ToArray();
        var cursor = Slot(before.GetProperty("mouse"));
        int bank = spec.GetProperty("bank").GetInt32(), slot = spec.GetProperty("slot").GetInt32();
        int context = spec.GetProperty("context").GetInt32();
        string op = spec.GetProperty("op").GetString()!;
        if (op == "chain")
        {
            Check(VanillaInventoryCursorMove1458.TryPlanLeftClick(inventory[0], cursor, out var next, out var nextCursor), name + " context0 first leg");
            inventory[0] = next;
            cursor = nextCursor;
        }
        for (int attempt = 0; attempt < spec.GetProperty("repeat").GetInt32(); attempt++)
        {
            bool ok = op == "right"
                ? VanillaInventoryCursorMove1458.TryPlanSingleToCursor(banks[bank][slot], cursor, out var next, out var nextCursor, context)
                : VanillaInventoryCursorMove1458.TryPlanLeftClick(banks[bank][slot], cursor, out next, out nextCursor, context);
            Check(ok, name + " source bank transition admitted");
            banks[bank][slot] = next;
            cursor = nextCursor;
        }
        Check(cursor == Slot(after.GetProperty("mouse")), name + " exact bank cursor");
        Check(inventory.SequenceEqual(after.GetProperty("inventory").EnumerateArray().Select(Slot)), name + " exact unchanged/context0 inventory");
        Check(banks.SelectMany(x => x).SequenceEqual(after.GetProperty("banks").EnumerateArray().SelectMany(x => x.EnumerateArray().Select(Slot))), name + " all four bank images");
        Check(!VanillaInventoryCursorMove1458.TryPlanLeftClick(banks[bank][slot], cursor, out _, out _, 99), "unknown bank context refused");
    }
    private static void VerifyShopTransaction(JsonElement row, int? customPrice = null)
    {
        var spec = row.GetProperty("spec");
        string name = spec.GetProperty("name").GetString()!;
        string op = spec.GetProperty("op").GetString()!;
        var before = row.GetProperty("before");
        var after = row.GetProperty("after");
        var inventory = before.GetProperty("inventory").EnumerateArray().Select(Slot).ToArray();
        var banks = before.GetProperty("banks").EnumerateArray().SelectMany(x => x.EnumerateArray().Select(Slot)).ToArray();
        var stock = before.GetProperty("shop").EnumerateArray().Select(ShopItem).ToArray();
        if (customPrice is not null) stock[0] = stock[0] with { CustomPrice = customPrice };
        var cursor = ShopItem(before.GetProperty("mouse"));
        inventory[58] = cursor.Tuple;
        var memos = before.GetProperty("memo").EnumerateArray().Select(Memo).ToArray();
        var context = new ShopPriceContext1458(spec.GetProperty("adjust").GetSingle(), spec.GetProperty("discount").GetBoolean());
        ShopTransactionPlan1458? plan;
        bool ok;
        if (op == "sell")
            ok = VanillaShopTransaction1458.TryPlanSell(inventory, banks, stock, memos, cursor, context, out plan,
                spec.GetProperty("animation").GetInt32(), spec.GetProperty("time").GetInt32());
        else
        {
            ok = VanillaShopTransaction1458.TryPlanBuy(inventory, banks, stock, memos, cursor, 0,
                spec.GetProperty("repeat").GetInt32() * (spec.GetProperty("fast").GetInt32() + 1), context, out plan,
                spec.GetProperty("animation").GetInt32(), spec.GetProperty("time").GetInt32(), op == "right_buy", spec.GetProperty("split").GetInt32());
            bool created = VanillaShopTransaction1458.TryCreatePurchasePrefixWalker(inventory, banks, stock, memos,
                cursor, 0, context, spec.GetProperty("repeat").GetInt32() * (spec.GetProperty("fast").GetInt32() + 1),
                out var purchaseWalker, spec.GetProperty("animation").GetInt32(), spec.GetProperty("time").GetInt32(),
                op == "right_buy", spec.GetProperty("split").GetInt32());
            int advanced = 0;
            if (created)
                while (purchaseWalker!.TryAdvance())
                    Check(purchaseWalker.Units == ++advanced, name + " purchase advance owns exactly one unit");
            Check((advanced > 0) == ok, name + " streamed purchase admission");
            if (ok)
            {
                Check(purchaseWalker!.TryDetachCurrent(out var streamed), name + " detached purchase");
                Check(streamed!.Units == plan!.Units && streamed.CopperDelta == plan.CopperDelta,
                    name + " exact cumulative charge/units");
                Check(streamed.Cursor == plan.Cursor && streamed.Stock.SequenceEqual(plan.Stock) &&
                    streamed.Memos.SequenceEqual(plan.Memos), name + " exact streamed economic owners");
                Check(streamed.Wallet.Inventory.SequenceEqual(plan.Wallet.Inventory) &&
                    streamed.Wallet.Banks.SequenceEqual(plan.Wallet.Banks), name + " exact streamed five wallets");
                Check(!purchaseWalker.TryAdvance(), name + " purchase bound retained");
            }
            if (op == "roundtrip" && ok)
            {
                var bought = plan!;
                Check(VanillaShopTransaction1458.TryPlanSell(bought.Wallet.Inventory, bought.Wallet.Banks,
                    bought.Stock, bought.Memos, bought.Cursor, context, out plan), name + " full shop roundtrip");
            }
        }
        for (int slot = 0; slot < 40; slot++)
        {
            var expectedStock = ShopItem(after.GetProperty("shop")[slot]);
            if (slot == 0 && customPrice is not null) expectedStock = expectedStock with { CustomPrice = customPrice };
            Check((ok ? plan!.Stock[slot] : stock[slot]) == expectedStock, name + " shop40 " + slot);
        }
        Check((ok ? plan!.Cursor : cursor) == ShopItem(after.GetProperty("mouse")), name + " full economic cursor");
        Check((ok ? plan!.Memos : memos).SequenceEqual(after.GetProperty("memo").EnumerateArray().Select(Memo)), name + " memo order and quantities");
        var expected = after.GetProperty("inventory").EnumerateArray().Select(Slot).ToArray();
        expected[58] = Slot(after.GetProperty("mouse"));
        Check((ok ? plan!.Wallet.Inventory : inventory).SequenceEqual(expected), name + " full shop inventory");
        Check((ok ? plan!.Wallet.Banks : banks).SequenceEqual(after.GetProperty("banks").EnumerateArray().SelectMany(x => x.EnumerateArray().Select(Slot))), name + " full shop banks");
    }

    private static ShopItem1458 ShopItem(JsonElement item) => item.GetProperty("type").GetInt32() == 0 ? default :
        new(Slot(item), item.GetProperty("value").GetInt32(), item.GetProperty("buyOnce").GetBoolean(), item.GetProperty("isAShopItem").GetBoolean());
    private static ShopMemo1458 Memo(JsonElement item) => new(item.GetProperty("type").GetInt32(), item.GetProperty("prefix").GetInt32(), item.GetProperty("stack").GetInt32());
    private static void Check(bool condition, string message) => Assert.True(condition, message);
    private static WorldItemPickupSlot1458 Slot(JsonElement item) => new(item.GetProperty("type").GetInt32(),
        item.GetProperty("stack").GetInt32(), item.GetProperty("prefix").GetByte(), item.GetProperty("favorite").GetBoolean());
    private static JsonDocument Read(string logicalName)
    {
        using var resource = typeof(NpcShopWalletSource1458Tests).Assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException("Missing independent source resource: " + logicalName);
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
