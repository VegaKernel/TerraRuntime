using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;
namespace TerraRuntime.Tests;
internal static class FoodSelectedBehavior1458Checks
{
    internal static int Run()
    {
        int count = 0, rows = 0, phases = 0;
        void Check(bool okay, string label) { if (!okay) throw new InvalidOperationException($"Food selected {label}: row={rows}, phase={phases}"); count++; }
        foreach (string platform in new[] { "Linux", "Windows" })
        foreach (string family in new[] { "Behavior", "Input" })
        {
            using var input = typeof(FoodSelectedBehavior1458Checks).Assembly.GetManifestResourceStream($"Food{family}{platform}1458")!;
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var doc = JsonDocument.Parse(gzip);
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                bool sourceInput = row.GetProperty("kind").GetString() == "source-success-transition";
                if (!sourceInput && row.GetProperty("kind").GetString() != "selected") continue;
                var before = row.GetProperty(sourceInput ? "before" : "initial");
                bool local = row.GetProperty("local").GetBoolean();
                var state = State(before);
                int stack = I(before.GetProperty("inventory")[0], "stack");
                int item = I(before.GetProperty("inventory")[0], "type");
                bool control = before.GetProperty("controlUseItem").GetBoolean();
                // The source harness explicitly changes local control after the initial snapshot.
                // TrySync emits no packet13 in these rows; this is a configured input change,
                // not a network intent certificate.
                if (local && !sourceInput && row.GetProperty("mode").GetString() == "fresh-input") control = true;
                foreach (var frame in sourceInput ? Array.Empty<JsonElement>() : row.GetProperty("initialFrames").EnumerateArray().ToArray())
                {
                    byte[] bytes = Convert.FromHexString(frame.GetString()!);
                    if (bytes[2] == 13) control = (bytes[4] & 32) != 0;
                }
                var random = new VanillaUnifiedRandom1458(sourceInput ? 0 : I(row, "seed"));
                var initialRandom = row.GetProperty(sourceInput ? "beforeRandom" : "initialRandom");
                if (sourceInput)
                {
                    initialRandom.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()).ToArray().CopyTo(Seeds(random), 0);
                    Cursor(random) = initialRandom.GetProperty("cursor").GetUInt32();
                }
                Check(Seeds(random).SequenceEqual(initialRandom.GetProperty("state").EnumerateArray().Select(x => x.GetInt32())), "initial full56");
                Check(Cursor(random) == initialRandom.GetProperty("cursor").GetUInt32(), "initial RNG cursor");
                Check(random.Clone().Next() == I(row, sourceInput ? "beforeNext" : "initialNext"), "initial RNG Next");
                foreach (var phase in row.GetProperty("phases").EnumerateArray())
                {
                    var expected = phase.GetProperty(sourceInput ? "state" : "afterCheck");
                    var facts = new PlayerSelectedConsumableFacts1458(new(item), I(before, "statLife"), I(before, "statLifeMax2"),
                        I(before, "statMana"), I(before, "statManaMax2"), control,
                        before.GetProperty("lastItemUseAttemptSuccess").GetBoolean(), before.GetProperty("cursed").GetBoolean(),
                        before.GetProperty("CCed").GetBoolean(), true, false);
                    PlayerSelectedConsumableTransition1458 actual;
                    if (item != 0)
                        Check(VanillaSelectedConsumable1458.TryStepFood(state, in facts, stack, local,
                            before.GetProperty("noItems").GetBoolean(), random.Next, random.NextDouble, out actual), "admitted");
                    else Check(VanillaSelectedConsumable1458.TryStep(state, in facts, random.Next, random.NextDouble, out actual), "Air tail");
                    Check(actual.State == State(expected), "clocks " + actual.State + " expected " + State(expected));
                    if (local)
                        Check((actual.OwnerToolTime ?? I(before, "toolTime")) == I(expected, "toolTime"),
                            $"owner toolTime item={item}, actual={actual.OwnerToolTime}, expected={I(expected, "toolTime")}");
                    int expectedStack = I(expected.GetProperty("inventory")[0], "stack");
                    bool consumed = local && stack > expectedStack;
                    Check(actual.Consumed == consumed, "source consumption");
                    Check((item != 0 ? actual.RemainingStack : stack) == expectedStack, "remaining physical stack");
                    Check(actual.Life == I(expected, "statLife") && actual.Mana == I(expected, "statMana"), "unchanged vitals");
                    Check(actual.BuffOfferType.Value == (consumed ? I(row.GetProperty("definition"), "buffType") : 0), "food offer type");
                    Check(actual.BuffDurationOffer == (consumed ? I(row.GetProperty("definition"), "buffTime") : 0), "food duration offer");
                    var sourceRandom = phase.GetProperty("random");
                    Check(Seeds(random).SequenceEqual(sourceRandom.GetProperty("state").EnumerateArray().Select(x => x.GetInt32())), "full56");
                    Check(Cursor(random) == sourceRandom.GetProperty("cursor").GetUInt32(), "RNG cursor");
                    Check(random.Clone().Next() == I(phase, "next"), "following RNG value");
                    before = phase.GetProperty("state");
                    state = actual.State;
                    stack = I(before.GetProperty("inventory")[0], "stack");
                    item = I(before.GetProperty("inventory")[0], "type");
                    phases++;
                }
                rows++;
            }
        }
        Check(rows == 760 && phases == 15200, "complete scoped selected matrix");
        Console.WriteLine($"FOOD_SELECTED rows={rows} phases={phases} checks={count}");
        return count;
    }
    private static int I(JsonElement e, string name) => e.GetProperty(name).GetInt32();
    private static PlayerSelectedConsumableState1458 State(JsonElement e) => new(I(e, "itemTime"), I(e, "itemTimeMax"),
        I(e, "itemAnimation"), I(e, "itemAnimationMax"), e.GetProperty("releaseUseItem").GetBoolean(), I(e, "potionDelay"), I(e, "revolverCritChanceBonus"));
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")]
    private static extern ref int[] Seeds(VanillaUnifiedRandom1458 random);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inext")]
    private static extern ref uint Cursor(VanillaUnifiedRandom1458 random);
}
