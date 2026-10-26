using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Tests;

public sealed class AnglerOutcome1458Tests
{
    [Fact]
    public void All_original_whole_rewards_are_legal_and_the_shared_generator_keeps_exact_inventory_and_rng()
    {
        using var resource = typeof(AnglerOutcome1458Tests).Assembly.GetManifestResourceStream("AnglerOriginal1458");
        Assert.NotNull(resource);
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(gzip);
        int compared = 0;
        foreach (var row in source.RootElement.GetProperty("rewards").EnumerateArray())
        {
            int done = row.GetProperty("done").GetInt32();
            var context = new AnglerOutcomeContext1458(row.GetProperty("fish").GetInt32(),
                row.GetProperty("hard").GetBoolean(), row.GetProperty("expert").GetBoolean(),
                row.GetProperty("price").GetSingle(),
                row.GetProperty("owned").EnumerateArray().Select(static value => value.GetInt32()).ToArray());
            var observed = row.GetProperty("items").EnumerateArray()
                .Select(static item => new AnglerReward1458(item[1].GetInt32(), item[2].GetInt32())).ToArray();
            var sourceRandom = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
            var generated = VanillaAnglerReward1458.Roll(new SourceRandom(sourceRandom),
                new(done, context.QuestFish, context.HardMode, context.ExpertMode, context.PriceAdjustment, context.OwnedItems)).ToArray();
            Assert.Equal(Image(observed), Image(generated));
            Assert.Equal(row.GetProperty("next").GetInt32(), sourceRandom.Next());
            Assert.True(VanillaAnglerReward1458.ValidateOutcome(context, AnglerQuestCountEvidence1458.FromKnown(done - 1),
                observed.Reverse().ToArray(), out var witness, out var next, out int work));
            Assert.Equal(Image(observed), Image(witness));
            Assert.True(next.TryGetKnown(out int count)); Assert.Equal(done, count);
            Assert.InRange(work, 1, VanillaAnglerReward1458.MaximumOutcomeWork);
            Assert.True(VanillaAnglerReward1458.ValidateOutcome(context, AnglerQuestCountEvidence1458.Unknown,
                observed, out witness, out next, out work));
            Assert.Equal(Image(observed), Image(witness)); Assert.True(next.Allows(done));
            Assert.InRange(work, 1, VanillaAnglerReward1458.MaximumOutcomeWork);
            compared++;
        }
        Assert.Equal(480, compared);
    }

    [Fact]
    public void Costume_companions_main_reward_money_and_single_main_are_required_together()
    {
        var context = Context();
        AnglerReward1458[] costume = [new(2417), new(2418), new(2419), new(72, 27)];
        Assert.True(Validate(context, 0, costume));
        Assert.False(Validate(context, 0, [new(2417), new(2419), new(72, 27)]));
        Assert.False(Validate(context, 0, [new(2417), new(2418), new(2500), new(72, 27)]));
        Assert.False(Validate(context, 0, [new(2417), new(2418), new(2419)]));
        Assert.False(Validate(context, 0, [new(72, 27)]));
        Assert.False(Validate(context, 0, [new(2417, 2), new(2418), new(2419), new(72, 27)]));
        Assert.False(Validate(context, 0, [new(2417), new(2418), new(2419), new(2498), new(2499), new(2500), new(72, 27)]));
        Assert.False(VanillaAnglerReward1458.ValidateOutcome(context, AnglerQuestCountEvidence1458.Unknown,
            [new(2428), new(2367), new(72, 60)], out _, out _));
    }

    [Fact]
    public void Accessory_composites_and_Zombie_Fish_preserve_correlated_source_main_branches()
    {
        AnglerReward1458[] accessory = [new(2373), new(72, 27)];
        Assert.True(Validate(Context(), 0, accessory));
        Assert.False(Validate(Context(owned: [3721]), 0, accessory));
        // When every source accessory category is excluded, the source returns to its full pool.
        Assert.True(Validate(Context(owned: [3721, 3036, 5140]), 0, accessory));
        Assert.True(Validate(Context(fish: 2451, hard: true), 0, [new(5302), new(72, 54)]));
        Assert.True(Validate(Context(fish: 2451, hard: true), 0, [new(5303), new(72, 54)]));
        Assert.False(Validate(Context(fish: 2451, hard: true), 0, [new(2354, 2), new(72, 54)]));
        Assert.True(Validate(Context(fish: 2451, hard: true), 4, [new(2428), new(72, 60)]));
        Assert.False(Validate(Context(fish: 2451, hard: true), 4, [new(5302), new(72, 60)]));
    }

    [Fact]
    public void Forced_decoration_bait_identity_and_count_cannot_be_checked_as_independent_allowlists()
    {
        var context = Context(hard: true, expert: true);
        AnglerReward1458[] valid = [new(2354, 2), new(2442), new(73, 10), new(2675, 5)];
        Assert.True(Validate(context, 150, valid));
        Assert.False(Validate(context, 150, [new(2354, 2), new(73, 10), new(2675, 5)]));
        Assert.False(Validate(context, 150, [new(2354, 2), new(2442), new(73, 10)]));
        Assert.False(Validate(context, 150, [new(2354, 2), new(2442), new(73, 10), new(2674, 5)]));
        Assert.False(Validate(context, 150, [new(2354, 2), new(2442), new(73, 10), new(2675, 4)]));
        Assert.False(Validate(context, 150, [new(2354, 2), new(2442), new(2443), new(73, 10), new(2675, 5)]));
    }

    [Fact]
    public void Unknown_count_narrows_every_possible_character_count_and_cannot_repeat_a_guaranteed_milestone()
    {
        var context = Context();
        AnglerReward1458[] fifth = [new(2428), new(72, 30)];
        Assert.True(VanillaAnglerReward1458.ValidateOutcome(context, AnglerQuestCountEvidence1458.Unknown,
            fifth, out _, out var five));
        Assert.True(five.TryGetKnown(out int count)); Assert.Equal(5, count);
        Assert.False(VanillaAnglerReward1458.ValidateOutcome(context, five, fifth, out _, out var unchanged));
        Assert.Same(five, unchanged);

        // Golden Rod is guaranteed at30 and possible again above75. Do not select
        // the first legal count and discard other source-consistent character histories.
        AnglerReward1458[] ambiguous = [new(2294), new(72, 90), new(2676, 4)];
        Assert.True(VanillaAnglerReward1458.ValidateOutcome(context, AnglerQuestCountEvidence1458.Unknown,
            ambiguous, out _, out var possibilities));
        Assert.False(possibilities.TryGetKnown(out _));
        Assert.True(possibilities.Allows(30)); Assert.True(possibilities.Allows(76));
        for (int before = 0; before < 200; before++)
            Assert.Equal(Validate(context, before, ambiguous), possibilities.Allows(before + 1));
    }

    [Fact]
    public void Stable_tail_is_compressed_and_pathological_counters_and_work_are_bounded()
    {
        var context = Context();
        AnglerReward1458[] tail = [new(2354, 2), new(2442), new(73, 10), new(2675, 7)];
        Assert.True(VanillaAnglerReward1458.ValidateOutcome(context, AnglerQuestCountEvidence1458.Unknown,
            tail, out _, out var next, out int work));
        Assert.True(next.Allows(1730)); Assert.True(next.Allows(VanillaAnglerReward1458.MaximumSupportedOutcomeCount));
        Assert.False(next.Allows(5)); Assert.False(next.TryGetKnown(out _));
        Assert.InRange(next.Ranges.Count, 1, 1731);
        Assert.InRange(work, 1, VanillaAnglerReward1458.MaximumOutcomeWork);
        Assert.True(Validate(context, 1_000_000, tail));
        Assert.False(Validate(context, int.MaxValue, tail));
        Assert.False(Validate(context, VanillaAnglerReward1458.MaximumSupportedOutcomeCount, tail));
        var forged = tail.Append(new AnglerReward1458(2294)).ToArray();
        Assert.False(VanillaAnglerReward1458.ValidateOutcome(context, AnglerQuestCountEvidence1458.Unknown,
            forged, out _, out _, out work));
        Assert.InRange(work, 0, VanillaAnglerReward1458.MaximumOutcomeWork);
        Assert.False(VanillaAnglerReward1458.ValidateOutcome(context, AnglerQuestCountEvidence1458.Unknown,
            Enumerable.Repeat(new AnglerReward1458(71), 65).ToArray(), out _, out _, out work));
        Assert.Equal(0, work);
        Assert.False(VanillaAnglerReward1458.ValidateOutcome(context with { OwnedItems = new int[513] },
            AnglerQuestCountEvidence1458.Unknown, tail, out _, out _, out work));
        Assert.Equal(0, work);
    }

    [Fact]
    public void Unordered_split_stacks_and_normalized_coin_value_keep_one_ordered_source_witness()
    {
        var context = Context();
        AnglerReward1458[] report = [new(71, 2700), new(2354), new(2354)];
        Assert.True(VanillaAnglerReward1458.ValidateOutcome(context, AnglerQuestCountEvidence1458.FromKnown(0),
            report, out var witness, out _));
        Assert.Equal(new AnglerReward1458[] { new(2354, 2), new(72, 27) }, witness);
        Assert.False(Validate(context, 0, [new(2354, 2), new(71, 2701)]));
        Assert.False(Validate(context, 0, [new(2354, 6), new(72, 27)]));
        Assert.False(Validate(context, 0, [new(2354, 2), new(72, 27), new(9999)]));
        Assert.False(Validate(context, 0, [new(2354, 2), new(72, 0)]));
    }

    private static AnglerOutcomeContext1458 Context(int fish = 2450, bool hard = false, bool expert = false,
        IReadOnlyList<int>? owned = null) => new(fish, hard, expert, 1f, owned ?? []);

    private static bool Validate(AnglerOutcomeContext1458 context, int before, IReadOnlyList<AnglerReward1458> offers) =>
        VanillaAnglerReward1458.ValidateOutcome(context, AnglerQuestCountEvidence1458.FromKnown(before), offers, out _, out _);

    private static KeyValuePair<int, int>[] Image(IEnumerable<AnglerReward1458> offers) =>
        offers.GroupBy(static offer => offer.Type).Select(static group =>
            new KeyValuePair<int, int>(group.Key, group.Sum(static offer => offer.Stack))).OrderBy(static pair => pair.Key).ToArray();

    private sealed class SourceRandom(VanillaUnifiedRandom1458 source) : Random
    {
        public override int Next() => source.Next();
        public override int Next(int maximum) => source.Next(maximum);
        public override int Next(int minimum, int maximum) => source.Next(minimum, maximum);
        public override double NextDouble() => source.NextDouble();
    }
}
