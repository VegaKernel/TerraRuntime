using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Items;
namespace TerraRuntime.Tests;
internal static class FoodQuickBuff1458Checks
{
    internal static int Run()
    {
        int checks = 0, rows = 0, modifiedMetadataDiagnostics = 0;
        void Check(bool okay, string label) { if (!okay) throw new InvalidOperationException($"Food QuickBuff {label}: case={rows}"); checks++; }
        foreach (string platform in new[] { "Linux", "Windows" })
        foreach (string family in new[] { "Behavior", "Consumers", "Seven" })
        {
            using var input = typeof(FoodQuickBuff1458Checks).Assembly.GetManifestResourceStream($"Food{family}{platform}1458")!;
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var doc = JsonDocument.Parse(gzip);
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                string kind = family == "Seven" ? "quick" : row.GetProperty("kind").GetString()!;
                if (kind != "quick" && kind != "default-duration-quick") continue;
                // This independent diagnostic mutates Item.buffTime away from SetDefaults.
                // The public slot DTO deliberately has no per-instance metadata override.
                // Genuine differing-default-duration cases are covered by the additive corpus.
                if (row.TryGetProperty("mode", out var mode) && mode.GetString() == "longer-main") { modifiedMetadataDiagnostics++; continue; }
                var before = row.GetProperty("before");
                var inventory = Items(before.GetProperty("inventory"));
                var bank = Items(before.GetProperty("bank4"));
                var types = before.GetProperty("buffType").EnumerateArray().Select(x => new BuffTypeId(x.GetInt32())).ToArray();
                var times = before.GetProperty("buffTime").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                var eligibility = new QuickBuffEligibility1458(false, before.GetProperty("cursed").GetBoolean(),
                    before.GetProperty("CCed").GetBoolean(), before.GetProperty("dead").GetBoolean(),
                    I(before, "spectating") >= 0, before.GetProperty("noItems").GetBoolean());
                Check(VanillaSelectedConsumable1458.TryPlanQuickBuff(inventory, bank, types, times,
                    in eligibility, [], out var plan), "admitted source default items");
                var after = row.GetProperty("afterQuick");
                Check(plan!.Inventory.SequenceEqual(Items(after.GetProperty("inventory"))), "complete main59");
                Check(plan.VoidItems.SequenceEqual(Items(after.GetProperty("bank4"))), "complete Void40");
                Check(plan.BuffTypes.SequenceEqual(after.GetProperty("buffType").EnumerateArray().Select(x => new BuffTypeId(x.GetInt32()))), "indexed44 types");
                Check(plan.BuffTimes.SequenceEqual(after.GetProperty("buffTime").EnumerateArray().Select(x => x.GetInt32())), "indexed44 durations");
                foreach (string scalar in new[] { "itemTime", "itemTimeMax", "itemAnimation", "itemAnimationMax", "potionDelay", "revolverCritChanceBonus", "statLife", "statMana" })
                    Check(I(before, scalar) == I(after, scalar), "source no clock/vital offer");
                Check(row.GetProperty("beforeRandom").GetRawText() == row.GetProperty("afterRandom").GetRawText(), "source no random offer");
                Check(I(row, "beforeNext") == I(row, "afterNext"), "following source RNG");
                if (row.TryGetProperty("picked", out _) && plan.Mutations.Length > 0 && plan.Mutations[0].OfferType.Value is 26 or 206 or 207)
                    Check(plan.Mutations[0].SourceSlot == I(row, "picked"), "food-first chosen source position");
                if (family == "Seven")
                {
                    int expectedCosts = row.GetProperty("definitions").GetArrayLength();
                    Check(plan.Mutations.Length == expectedCosts, "complete food-first seven-or-two costs");
                    Check(plan.Mutations[0].OfferType.Value is 26 or 206 or 207, "food precedes ordinary buffs");
                }
                rows++;
            }
        }
        Check(rows == 78 && modifiedMetadataDiagnostics == 4, "complete default-metadata QuickBuff source domain");
        Console.WriteLine($"FOOD_QUICK rows={rows} overriddenMetadataDiagnostics={modifiedMetadataDiagnostics} checks={checks}");
        return checks;
    }
    internal static WorldItemPickupSlot1458[] Items(JsonElement items) => items.EnumerateArray().Select(x =>
        new WorldItemPickupSlot1458(I(x, "type"), I(x, "stack"), I(x, "prefix"), x.GetProperty("favorite").GetBoolean())).ToArray();
    private static int I(JsonElement e, string n) => e.GetProperty(n).GetInt32();
}
