using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class FishingCrateOutcome1458Tests
{
    private static readonly int[] Crates = [2334, 2335, 2336, 3979, 3980, 3981, 3203, 3982, 3204, 3983,
        3205, 3984, 3206, 3985, 3207, 3986, 3208, 3987, 4405, 4406, 4407, 4408, 4877, 4878, 5002, 5003];

    [Fact]
    public void Independent_original_416_outcomes_are_possible_without_the_original_random_cursor()
    {
        using var resource = typeof(FishingCrateOutcome1458Tests).Assembly.GetManifestResourceStream("OriginalFishingCrate1458");
        Assert.NotNull(resource);
        using var zip = new GZipStream(resource, CompressionMode.Decompress);
        using var original = JsonDocument.Parse(zip);
        Assert.Equal("4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034",
            original.RootElement.GetProperty("sourceSha256").GetString()!.ToUpperInvariant());
        int rows = 0;
        foreach (var row in original.RootElement.GetProperty("rows").EnumerateArray())
        {
            int crate = row.GetProperty("id").GetInt32();
            bool remix = row.GetProperty("remix").GetBoolean();
            // Independent original QuickSpawn/GetItem into an empty image. Each
            // source identity occurs once and these coin grants never carry100.
            var grants = row.GetProperty("official").GetProperty("inventory").EnumerateArray()
                .Where(item => item.GetProperty("type").GetInt32() != 0)
                .Select(item => new FishingReward1458(item.GetProperty("type").GetInt32(), item.GetProperty("stack").GetInt32()))
                .Reverse().ToArray();
            Assert.True(VanillaFishingCrate1458.TryValidateOutcome(crate, remix, false, grants, out var witness, out int steps),
                $"originalcrate={crate},seed={row.GetProperty("seed")},remix={remix},steps={steps}");
            Assert.Equal(grants.OrderBy(item => item.Type), witness.OrderBy(item => item.Type));
            Assert.True(VanillaFishingCrate1458.IsPossibleOrderedOutcome(crate, remix, false, witness));
            Assert.InRange(steps, 1, VanillaFishingCrate1458.MaximumValidationSteps);
            rows++;
        }
        Assert.Equal(416, rows);
    }

    [Fact]
    public void Every_crate_world_context_and_independent_random_seed_yields_a_complete_witness()
    {
        foreach (int crate in Crates)
        foreach (bool remix in new[] { false, true })
        foreach (bool lowTiles in new[] { false, true })
        for (int seed = 1000; seed < 1032; seed++)
        {
            var random = new Random(seed);
            var grants = new List<FishingReward1458>();
            Assert.True(VanillaFishingCrate1458.TryOpen(crate, remix, lowTiles, random.Next,
                (type, stack) => { grants.Add(new FishingReward1458(type, stack)); return true; }));
            Assert.InRange(grants.Count, 1, VanillaFishingCrate1458.MaximumGrants);
            Assert.Equal(grants.Count, grants.Select(item => item.Type).Distinct().Count());
            Assert.True(VanillaFishingCrate1458.IsPossibleOrderedOutcome(crate, remix, lowTiles, grants));
            Assert.True(VanillaFishingCrate1458.TryValidateOutcome(crate, remix, lowTiles,
                grants.AsEnumerable().Reverse().ToArray(), out var witness, out int steps));
            Assert.True(VanillaFishingCrate1458.IsPossibleOrderedOutcome(crate, remix, lowTiles, witness));
            Assert.InRange(steps, 1, VanillaFishingCrate1458.MaximumValidationSteps);
        }
    }

    [Fact]
    public void Required_retry_groups_and_exclusive_rares_pools_and_stack_ranges_are_enforced_together()
    {
        bool Valid(int crate, params FishingReward1458[] grants) =>
            VanillaFishingCrate1458.TryValidateOutcome(crate, false, false, grants, out _);
        foreach (int crate in Crates) Assert.False(Valid(crate));
        Assert.False(Valid(2334, new FishingReward1458(2674, 1))); // Post-retry bait is not a successful body.
        Assert.False(Valid(2335, new FishingReward1458(188, 5)));
        Assert.False(Valid(2336, new FishingReward1458(2676, 3)));
        Assert.False(Valid(3207, new FishingReward1458(2675, 2)));
        Assert.False(Valid(3986, new FishingReward1458(520, 2), new FishingReward1458(502, 4)));
        Assert.False(Valid(2334, new FishingReward1458(3200), new FishingReward1458(3201)));
        Assert.False(Valid(3979, new FishingReward1458(3064), new FishingReward1458(2424)));
        Assert.False(Valid(2334, new FishingReward1458(285), new FishingReward1458(997)));
        Assert.False(Valid(2334, new FishingReward1458(3200), new FishingReward1458(997)));
        Assert.True(Valid(2334, new FishingReward1458(3200), new FishingReward1458(285))); // Independent weapon pool still runs.
        Assert.False(Valid(2334, new FishingReward1458(12, 4), new FishingReward1458(20, 2)));
        Assert.False(Valid(2334, new FishingReward1458(288), new FishingReward1458(290)));
        Assert.False(Valid(2334, new FishingReward1458(72, 20), new FishingReward1458(73, 1)));
        Assert.True(Valid(2334, new FishingReward1458(72, 20))); Assert.True(Valid(2334, new FishingReward1458(72, 90)));
        Assert.False(Valid(2334, new FishingReward1458(72, 19))); Assert.False(Valid(2334, new FishingReward1458(72, 91)));
        Assert.False(Valid(2334, new FishingReward1458(73, 6)));
        Assert.False(Valid(2334, new FishingReward1458(364, 4))); Assert.True(Valid(3979, new FishingReward1458(364, 4)));
        Assert.False(Valid(3979, new FishingReward1458(364, 16))); Assert.False(Valid(3979, new FishingReward1458(381, 4)));
        Assert.False(Valid(2335, new FishingReward1458(2501), new FishingReward1458(2587)));
        Assert.False(Valid(3980, new FishingReward1458(3064), new FishingReward1458(2501)));
        Assert.False(Valid(2335, new FishingReward1458(12, 12), new FishingReward1458(20, 4)));
        Assert.True(Valid(3980, new FishingReward1458(381, 3))); Assert.False(Valid(3980, new FishingReward1458(20, 3)));
        Assert.False(Valid(2336, new FishingReward1458(29), new FishingReward1458(2491)));
        Assert.True(Valid(2336, new FishingReward1458(29), new FishingReward1458(73, 8), new FishingReward1458(21, 8)));
        Assert.False(Valid(3203, new FishingReward1458(162), new FishingReward1458(111)));
        Assert.False(Valid(3205, new FishingReward1458(149, 5))); Assert.False(Valid(3205, new FishingReward1458(3085), new FishingReward1458(134, 25), new FishingReward1458(137, 25)));
        Assert.True(Valid(3205, new FishingReward1458(3085), new FishingReward1458(149, 15), new FishingReward1458(134, 50)));
        Assert.False(Valid(3205, new FishingReward1458(3085), new FishingReward1458(149, 16)));
        Assert.True(Valid(3203, new FishingReward1458(162), new FishingReward1458(12, 20), new FishingReward1458(22, 6))); // Biome ore/bar are independent.
        Assert.False(Valid(3982, new FishingReward1458(162), new FishingReward1458(12, 20), new FishingReward1458(364, 20))); // One ore group.
        Assert.False(Valid(4877, new FishingReward1458(4879))); Assert.False(Valid(4877, new FishingReward1458(4822)));
        Assert.True(Valid(4877, new FishingReward1458(4822), new FishingReward1458(4879), new FishingReward1458(221), new FishingReward1458(4443), new FishingReward1458(4737), new FishingReward1458(4551)));
        Assert.False(Valid(4407, new FishingReward1458(4056), new FishingReward1458(4442)));
        Assert.True(Valid(4405, new FishingReward1458(1319)));
        Assert.False(VanillaFishingCrate1458.TryValidateOutcome(4405, true, false, [new FishingReward1458(1319)], out _));
        Assert.True(VanillaFishingCrate1458.TryValidateOutcome(4405, true, false, [new FishingReward1458(725)], out _));
        Assert.False(VanillaFishingCrate1458.TryValidateOutcome(3206, false, false, [new FishingReward1458(158), new FishingReward1458(2197)], out _));
        Assert.True(VanillaFishingCrate1458.TryValidateOutcome(3206, false, true, [new FishingReward1458(158), new FishingReward1458(2197)], out _));
    }

    [Fact]
    public void Full_lava_outcome_order_and_hostile_inputs_are_bounded_without_seed_enumeration()
    {
        FishingReward1458[] maximal = [new FishingReward1458(4822), new FishingReward1458(4858, 2), new FishingReward1458(4879), new FishingReward1458(4824, 7), new FishingReward1458(4902), new FishingReward1458(73, 5),
            new FishingReward1458(364, 20), new FishingReward1458(381, 5), new FishingReward1458(288, 2), new FishingReward1458(188, 5), new FishingReward1458(2676, 2), new FishingReward1458(1497), new FishingReward1458(221), new FishingReward1458(4443), new FishingReward1458(4737), new FishingReward1458(4551)];
        Assert.Equal(VanillaFishingCrate1458.MaximumGrants, maximal.Length);
        Assert.True(VanillaFishingCrate1458.IsPossibleOrderedOutcome(4878, false, false, maximal));
        Assert.False(VanillaFishingCrate1458.IsPossibleOrderedOutcome(4878, false, false, maximal.Reverse().ToArray()));
        Assert.True(VanillaFishingCrate1458.TryValidateOutcome(4878, false, false, maximal.Reverse().ToArray(), out var witness, out int steps));
        Assert.Equal(maximal, witness); Assert.InRange(steps, 1, VanillaFishingCrate1458.MaximumValidationSteps);
        Assert.False(VanillaFishingCrate1458.TryValidateOutcome(0, false, false, maximal, out _));
        Assert.False(VanillaFishingCrate1458.TryValidateOutcome(2334, false, false, new Oversized(), out _));
        Assert.True(VanillaFishingCrate1458.TryValidateOutcome(2334, false, false, new NonEnumerated(), out _));
        Assert.False(VanillaFishingCrate1458.TryValidateOutcome(2334, false, false, [new FishingReward1458(73, 1), new FishingReward1458(73, 1)], out _));
        Assert.False(VanillaFishingCrate1458.TryValidateOutcome(2334, false, false, [new FishingReward1458(73, 0)], out _));
        Assert.False(VanillaFishingCrate1458.TryValidateOutcome(2334, false, false, [new FishingReward1458(73, int.MaxValue)], out _));
        foreach (int crate in Crates)
            Assert.False(VanillaFishingCrate1458.TryValidateOutcome(crate, false, false,
                maximal.Select(item => item with { Stack = 9999 }).ToArray(), out _, out steps));
        Assert.InRange(steps, 0, VanillaFishingCrate1458.MaximumValidationSteps);
    }

    private sealed class Oversized : IReadOnlyList<FishingReward1458>
    {
        public int Count => int.MaxValue;
        public FishingReward1458 this[int index] => throw new InvalidOperationException("Oversized input was inspected.");
        public IEnumerator<FishingReward1458> GetEnumerator() => throw new InvalidOperationException("Oversized input was enumerated.");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private sealed class NonEnumerated : IReadOnlyList<FishingReward1458>
    {
        private bool countRead;
        public int Count
        {
            get
            {
                if (countRead) throw new InvalidOperationException("Input Count changed after admission.");
                countRead = true;
                return 1;
            }
        }
        public FishingReward1458 this[int index] => index == 0 ? new(73, 1) : throw new IndexOutOfRangeException();
        public IEnumerator<FishingReward1458> GetEnumerator() => throw new InvalidOperationException("Input enumeration is not bounded by Count.");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
