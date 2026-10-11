using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class VanillaItemSorting1458Tests
{
    [Fact]
    public void Original_localized_main_and_bank_sort_images_have_exact_operation_journals()
    {
        using var source = Read("OriginalLocalizedItemSorting1458");
        int rows = 0;
        foreach (string platform in new[] { "Linux", "Windows" })
            foreach (var row in source.RootElement.GetProperty(platform).GetProperty("rows").EnumerateArray())
            {
                var spec = row.GetProperty("spec");
                bool main = spec.GetProperty("op").GetString() == "sortmain";
                int bank = spec.GetProperty("bank").GetInt32();
                var before = row.GetProperty("before");
                var after = row.GetProperty("afterSync");
                var input = Items(main ? before.GetProperty("inventory") : before.GetProperty("banks")[bank]);
                var expected = Items(main ? after.GetProperty("inventory") : after.GetProperty("banks")[bank]);
                var damage = row.GetProperty("beforeDamage").EnumerateArray().Select(x => x.GetSingle()).ToArray();
                var priority = Enumerable.Range(0, 4).OrderByDescending(i => damage[i]).ThenBy(i => i).ToArray();
                byte order = (byte)priority.Select((x, i) => x << (2 * i)).Sum();
                Assert.True(VanillaWorldItemOwner1458.TryPlanSort(input, main,
                    (byte)(spec.GetProperty("locale").GetInt32() - 1), order, false,
                    platform == "Windows", new(false, false, false), out var plan), spec.GetProperty("name").GetString());
                Assert.NotNull(plan);
                Assert.Equal(expected, plan.Slots);
                if (!main)
                {
                    var maxima = before.GetProperty("banks")[bank].EnumerateArray()
                        .Where(item => item[0].GetInt32() != 0)
                        .GroupBy(item => item[0].GetInt32())
                        .ToDictionary(group => group.Key, group => group.First()[4].GetInt32());
                    // Source preliminary stacking skips full donors even when the final
                    // physical image could be reproduced by a different transfer history.
                    Assert.DoesNotContain(plan.Steps, step => step.Coins is null &&
                        step.SourceBefore.Stack == maxima[step.SourceBefore.Type]);
                }
                Replay(input, main, plan);
                Assert.Equal(before.GetProperty("random").GetRawText(), after.GetProperty("random").GetRawText());
                Assert.Equal(before.GetProperty("next").GetInt32(), after.GetProperty("next").GetInt32());
                rows++;
            }
        Assert.Equal(96, rows);
        VerifyOriginalCoinOverflow();
    }

    [Fact]
    public void Original_full_catalog_locale_keys_keep_comparator_ties_and_explicit_context_fences()
    {
        using var source = Read("OriginalItemSortingMetadata1458");
        int groups = 0;
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            var root = source.RootElement.GetProperty(platform);
            var metadata = root.GetProperty("metadata").EnumerateArray().ToArray();
            Assert.Equal(6196, metadata.Length);
            foreach (var locale in root.GetProperty("rows").EnumerateArray())
            {
                var ranks = locale.GetProperty("ranks").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                for (int start = 1; start < metadata.Length; start += 40)
                {
                    var input = new WorldItemPickupSlot1458[40];
                    var indices = new List<int>();
                    for (int j = 0; j < 40 && start + j < metadata.Length; j++)
                    {
                        int type = start + j;
                        if (!VanillaItemRetainedIdentity1458.TryResolve(new ItemTypeId(type), out var retained) ||
                            retained.Value != type) continue;
                        input[j] = new(type, 1, 0, false);
                        indices.Add(j);
                    }
                    var expected = indices.OrderBy(i => metadata[input[i].Type].GetProperty("layer").GetInt32())
                        .ThenBy(i => ranks[input[i].Type]).ThenBy(i => i).Select(i => input[i])
                        .Concat(Enumerable.Repeat(default(WorldItemPickupSlot1458), 40 - indices.Count)).ToArray();
                    Assert.True(VanillaWorldItemOwner1458.TryPlanSort(input, false,
                        (byte)(locale.GetProperty("culture").GetInt32() - 1), 228, false,
                        platform == "Windows", new(false, false, false), out var plan));
                    Assert.NotNull(plan);
                    Assert.Equal(expected, plan.Slots);
                    Replay(input, false, plan);
                    groups++;
                }
            }
        }
        Assert.Equal(3720, groups);
        var image = new WorldItemPickupSlot1458[59];
        image[10] = new(3, 3, 0, false);
        image[11] = new(9, 4, 0, true);
        Assert.True(VanillaWorldItemOwner1458.TryPlanSort(image, true, 0, 228, true, false,
            new(false, false, false), out var locked));
        Assert.NotNull(locked);
        Assert.Equal(image, locked.Slots);
        Assert.Empty(locked.Steps);
        Assert.Empty(locked.Permutation.Sources);
        Assert.False(VanillaWorldItemOwner1458.TryPlanSort(image, true, 12, 228, false, false,
            new(false, false, false), out _));
        Assert.False(VanillaWorldItemOwner1458.TryPlanSort(image, true, 0, 0, false, false,
            new(false, false, false), out _));
        image[10] = new(1314, 1, 0, false);
        Assert.False(VanillaWorldItemOwner1458.TryPlanSort(image, true, 0, 228, false, false, null, out _));
    }


    private static void VerifyOriginalCoinOverflow()
    {
        using var source = Read("OriginalItemSortCoinOverflow1458");
        int comparisons = 0;
        foreach (string platform in new[] { "Linux", "Windows" })
            foreach (var row in source.RootElement.GetProperty(platform).GetProperty("rows").EnumerateArray())
            {
                string name = row.GetProperty("spec").GetProperty("name").GetString()!;
                var input = Items(row.GetProperty("before").GetProperty("inventory"));
                bool accepted = VanillaWorldItemOwner1458.TryPlanSort(input, true, 0, 228,
                    false, platform == "Windows", new(false, false, false), out var plan);
                // Positive source-retained coin stacks now include the original101 images.
                Assert.True(accepted, name);
                Assert.NotNull(plan);
                Assert.Equal(Items(row.GetProperty("afterSync").GetProperty("inventory")), plan.Slots);
                var frames = row.GetProperty("operationFrames").EnumerateArray()
                    .Select(frame => Convert.FromHexString(frame.GetString()!)).Where(frame => frame[2] == 21).ToArray();
                var offers = plan.Overflow ?? [];
                Assert.Equal(frames.Length, offers.Length);
                for (int i = 0; i < frames.Length; i++)
                {
                    Assert.Equal(BitConverter.ToInt16(frames[i], 25), offers[i].Type);
                    Assert.Equal(frames[i][23], offers[i].Prefix);
                    if (name == "canonical_ammo_platinum39996")
                    {
                        Assert.Equal(-25540, BitConverter.ToInt16(frames[i], 21));
                        Assert.Equal(39996, row.GetProperty("after").GetProperty("ground")[0].GetProperty("stack").GetInt32());
                        Assert.Equal(39996, offers[i].Stack);
                    }
                    else Assert.Equal(BitConverter.ToInt16(frames[i], 21), offers[i].Stack);
                }
                Assert.Equal(offers.Length * 2, row.GetProperty("after").GetProperty("random").GetProperty("cursor").GetInt32());
                Replay(input, true, plan);
                comparisons++;
            }
        Assert.Equal(18, comparisons);
    }

    private static void Replay(WorldItemPickupSlot1458[] input, bool main, WorldItemSortPlan1458 plan)
    {
        int offset = main ? 0 : 59;
        var image = input.ToArray();
        foreach (var step in plan.Steps)
        {
            if (step.Coins is { } coins)
            {
                Assert.Equal(Copper(coins.Before), Copper(coins.After) + Copper(coins.Overflow ?? []));
                Assert.Equal(coins.Slots.Length, coins.Before.Length);
                Assert.Equal(coins.Slots.Length, coins.After.Length);
                Assert.Equal(coins.Slots.Length, coins.Slots.Distinct().Count());
                for (int i = 0; i < coins.Slots.Length; i++)
                {
                    int slot = coins.Slots[i] - offset;
                    Assert.Equal(coins.Before[i], image[slot]);
                    image[slot] = coins.After[i];
                }
                continue;
            }
            int source = step.Source - offset;
            int target = step.Destination - offset;
            Assert.Equal(step.SourceBefore, image[source]);
            Assert.Equal(step.DestinationBefore, image[target]);
            Assert.InRange(step.Amount, 1, image[source].Stack);
            image[target] = image[target].Type == 0 ? image[source] with { Stack = step.Amount } :
                image[target] with { Stack = image[target].Stack + step.Amount };
            image[source] = image[source].Stack == step.Amount ? default : image[source] with { Stack = image[source].Stack - step.Amount };
        }
        var permutation = plan.Permutation;
        Assert.Equal(permutation.Sources.Length, permutation.Destinations.Length);
        Assert.Equal(permutation.Sources.Length, permutation.Before.Length);
        Assert.Equal(permutation.Sources.Length, permutation.Sources.Distinct().Count());
        Assert.Equal(permutation.Destinations.Length, permutation.Destinations.Distinct().Count());
        var domain = Enumerable.Range(main ? 10 : 0, main ? 40 : 40)
            .Where(i => !image[i].Favorite).Select(i => (short)(i + offset)).ToArray();
        Assert.Equal(domain, permutation.Sources.Order().ToArray());
        Assert.Equal(domain, permutation.Destinations.Order().ToArray());
        for (int i = 0; i < permutation.Sources.Length; i++)
            Assert.Equal(image[permutation.Sources[i] - offset], permutation.Before[i]);
        for (int i = 0; i < permutation.Destinations.Length; i++)
            image[permutation.Destinations[i] - offset] = permutation.Before[i];
        Assert.Equal(plan.Slots, image);
        if (main) Assert.Equal(input[58], image[58]);
    }

    private static long Copper(IEnumerable<WorldItemPickupSlot1458> items) => items.Sum(item =>
        VanillaCoinFacts.TryGetValue(new ItemTypeId(item.Type), out long unit) ? unit * item.Stack : 0);

    private static WorldItemPickupSlot1458[] Items(JsonElement items) => items.EnumerateArray().Select(item =>
        item[0].GetInt32() == 0 ? default : new WorldItemPickupSlot1458(item[0].GetInt32(), item[1].GetInt32(),
            item[2].GetInt32(), item[3].GetBoolean())).ToArray();

    private static JsonDocument Read(string name)
    {
        using var stream = typeof(VanillaItemSorting1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var zip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(zip);
    }
}