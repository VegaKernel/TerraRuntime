using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class AnglerClientAuthority1458Tests
{
    [Fact]
    public void Client75_waits_for_complete_reports_and_accepts_an_independent_client_reward_without_server_rng()
    {
        using var f = Create("Independent client");
        FundFish(f, 2450);
        Assert.True(f.Players.TryGet(f.Connection, out var member));
        Assert.False(member.AnglerQuestCounts.TryGetKnown(out _));
        var before = f.ItemImage();
        var serverRandom = Random(f);
        serverRandom.CopyStateFrom(new(991));
        var randomBefore = serverRandom.Clone();
        var client = ClientRewards(before, 2450, 5, 1458);
        var serverWouldRoll = VanillaAnglerReward1458.Roll(new SourceRandom(serverRandom.Clone()),
            new(5, 2450, false, false, 1f, Types(before))).ToArray();
        Assert.False(client.SequenceEqual(serverWouldRoll));
        Assert.Contains(client, reward => reward.Type == 2428);
        var after = ClientImage(before, 2450, client);

        Begin(f); Begin(f);
        Assert.Equal(before, f.ItemImage());
        Assert.Empty(f.State.CaptureAnglerSaveState()!.CompletedNames);
        Assert.True(randomBefore.HasSameState(serverRandom));
        Assert.False(member.OwnsAnglerQuestsFinished);
        Reports(f, before, after);
        Assert.Equal(before, f.ItemImage());
        Assert.Empty(f.State.CaptureAnglerSaveState()!.CompletedNames);
        Assert.True(randomBefore.HasSameState(serverRandom));
        Complete(f);

        Assert.Equal(after, f.ItemImage());
        Assert.DoesNotContain(f.ItemImage(), item => item.ItemType.Value == 2450);
        Assert.Contains(f.ItemImage(), item => item.ItemType.Value == 2428 && item.Stack == 1);
        Assert.True(member.AnglerQuestCounts.TryGetKnown(out int count)); Assert.Equal(5, count);
        Assert.Equal(5, member.AnglerQuestsFinished);
        Assert.True(member.OwnsAnglerQuestsFinished);
        Assert.Contains("Independent client", f.State.CaptureAnglerSaveState()!.CompletedNames);
        Assert.True(randomBefore.HasSameState(serverRandom));

        var accepted = f.ItemImage(); var counts = member.AnglerQuestCounts;
        Begin(f); Complete(f); Complete(f);
        Reports(f, accepted, accepted); Complete(f);
        Assert.Equal(accepted, f.ItemImage()); Assert.Same(counts, member.AnglerQuestCounts);
        Assert.Single(f.State.CaptureAnglerSaveState()!.CompletedNames);
        Assert.True(randomBefore.HasSameState(serverRandom));
    }

    [Theory]
    [InlineData("multiple-main")]
    [InlineData("partial-costume")]
    [InlineData("missing-money")]
    [InlineData("fractional-money")]
    [InlineData("extra-item")]
    public void Forbidden_whole_reward_cohorts_preserve_real_fish_rng_and_daily_state_then_allow_a_legal_retry(string profile)
    {
        using var f = Create("R:" + profile);
        FundFish(f, 2450);
        var before = f.ItemImage(); var random = Random(f); var randomBefore = random.Clone();
        AnglerReward1458[] invalid = profile switch
        {
            "multiple-main" => [new(2428), new(2367), new(72, 30)],
            "partial-costume" => [new(2417), new(2419), new(72, 27)],
            "missing-money" => [new(2428)],
            "fractional-money" => [new(2428), new(71, 3001)],
            "extra-item" => [new(2428), new(72, 30), new(9)],
            _ => throw new ArgumentOutOfRangeException(nameof(profile))
        };
        Begin(f);
        Reports(f, before, ClientImage(before, 2450, invalid));
        Complete(f);
        Assert.Equal(before, f.ItemImage());
        Assert.True(randomBefore.HasSameState(random));
        Assert.Empty(f.State.CaptureAnglerSaveState()!.CompletedNames);
        Assert.True(f.Players.TryGet(f.Connection, out var member));
        Assert.False(member.OwnsAnglerQuestsFinished);
        Assert.False(member.AnglerQuestCounts.TryGetKnown(out _));
        int fishSlot = Array.FindIndex(before, item => item.ItemType.Value == 2450);
        Assert.True(FishingCrateAuthority1458Tests.Store(f.Players).CanSpendFishingStock(f.Connection, fishSlot, 2450));
        Assert.Equal(0, f.Items.CopyActive(new WorldItemSnapshot[f.Items.Capacity]));

        var legal = ClientImage(before, 2450, ClientRewards(before, 2450, 5, 1458));
        Begin(f); Reports(f, before, legal); Complete(f);
        Assert.Equal(legal, f.ItemImage());
        Assert.Single(f.State.CaptureAnglerSaveState()!.CompletedNames);
        Assert.True(member.AnglerQuestCounts.TryGetKnown(out int completed)); Assert.Equal(5, completed);
        Assert.True(randomBefore.HasSameState(random));
    }

    [Fact]
    public void Explicit_character29_is_advanced_only_by_a_paid_source_compatible_thirtieth_reward_cohort()
    {
        using var f = Create("Explicit character");
        FundFish(f, 2450);
        var before = f.ItemImage();
        f.State.Apply(new PlayerCharacterProgressRuntimeCommand(f.Connection, 29, 1234));
        Assert.Equal(before, f.ItemImage());
        Assert.True(f.Players.TryGet(f.Connection, out var member));
        Assert.True(member.AnglerQuestCounts.TryGetKnown(out int count)); Assert.Equal(29, count);
        var random = Random(f); var randomBefore = random.Clone();
        var client = ClientRewards(before, 2450, 30, 1458);
        Assert.Contains(client, offer => offer.Type == 2294);
        var after = ClientImage(before, 2450, client);
        Begin(f); Reports(f, before, after);
        Assert.Equal(before, f.ItemImage()); Assert.Equal(29, member.AnglerQuestsFinished);
        Complete(f);
        Assert.Equal(after, f.ItemImage());
        Assert.Equal(30, member.AnglerQuestsFinished); Assert.Equal(1234, member.GolferScoreAccumulated);
        Assert.Contains(f.ItemImage(), item => item.ItemType.Value == 2294 && item.Stack == 1);
        Assert.True(randomBefore.HasSameState(random));
        f.State.Apply(new PlayerCharacterProgressRuntimeCommand(f.Connection, 4, 2345));
        Assert.True(member.AnglerQuestCounts.TryGetKnown(out count)); Assert.Equal(30, count);
        Assert.Equal(30, member.AnglerQuestsFinished); Assert.Equal(2345, member.GolferScoreAccumulated);
        Begin(f); Complete(f);
        Assert.Equal(after, f.ItemImage()); Assert.True(randomBefore.HasSameState(random));
    }

    [Fact]
    public void Inferred_fifth_completion_survives_dawn_and_cannot_be_widened_into_another_fifth_gift()
    {
        using var f = Create("Narrowed character");
        FundFish(f, 2450);
        var before = f.ItemImage();
        Begin(f); Reports(f, before, ClientImage(before, 2450, ClientRewards(before, 2450, 5, 1458))); Complete(f);
        Assert.True(f.Players.TryGet(f.Connection, out var member));
        Assert.True(member.AnglerQuestCounts.TryGetKnown(out int count)); Assert.Equal(5, count);
        Assert.True(f.Players.SwapAnglerQuestAtDawn());
        Assert.Empty(f.State.CaptureAnglerSaveState()!.CompletedNames);
        int fish = VanillaAnglerQuest1458.ItemForQuest(f.State.CaptureAnglerSaveState()!.Quest);
        FundFish(f, checked((short)fish));
        before = f.ItemImage(); var random = Random(f); var randomBefore = random.Clone();
        f.State.Apply(new PlayerCharacterProgressRuntimeCommand(f.Connection, 4, 4321));
        Assert.True(member.AnglerQuestCounts.TryGetKnown(out count)); Assert.Equal(5, count);
        Begin(f); Reports(f, before, ClientImage(before, fish, ClientRewards(before, fish, 5, 1458))); Complete(f);
        Assert.Equal(before, f.ItemImage()); Assert.Empty(f.State.CaptureAnglerSaveState()!.CompletedNames);
        Assert.True(member.AnglerQuestCounts.TryGetKnown(out count)); Assert.Equal(5, count);
        Assert.True(randomBefore.HasSameState(random));

        var sixth = ClientImage(before, fish, ClientRewards(before, fish, 6, 1459));
        Begin(f); Reports(f, before, sixth); Complete(f);
        Assert.Equal(sixth, f.ItemImage());
        Assert.True(member.AnglerQuestCounts.TryGetKnown(out count)); Assert.Equal(6, count);
        Assert.Single(f.State.CaptureAnglerSaveState()!.CompletedNames);
        Assert.True(randomBefore.HasSameState(random));
    }

    private static FishingCrateAuthority1458Tests.Fixture Create(string name)
    {
        var f = new FishingCrateAuthority1458Tests.Fixture();
        f.State.BindAnglerWorld(new WorldFileRuntimeMetadata { WorldSurface = 80, AnglerQuest = new(0, []) });
        AnglerAuthority1458Tests.SetAppearance(f, name);
        AnglerAuthority1458Tests.ConfigureAngler(f);
        return f;
    }

    private static void FundFish(FishingCrateAuthority1458Tests.Fixture f, short fish)
    {
        var drop = f.Drop(fish, 1);
        f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, drop.Handle));
        Assert.Contains(f.ItemImage(), item => item.ItemType.Value == fish && item.Stack == 1);
    }

    private static VanillaUnifiedRandom1458 Random(FishingCrateAuthority1458Tests.Fixture f) =>
        ((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom;

    private static int[] Types(RuntimePlayerInventoryItem[] before) =>
        before.Where(static item => !item.IsEmpty).Select(static item => item.ItemType.Value).ToArray();

    private static AnglerReward1458[] ClientRewards(RuntimePlayerInventoryItem[] before, int fish, int done, int seed) =>
        VanillaAnglerReward1458.Roll(new SourceRandom(new(seed)), new(done, fish, false, false, 1f, Types(before))).ToArray();

    private static RuntimePlayerInventoryItem[] ClientImage(RuntimePlayerInventoryItem[] before, int fish,
        IReadOnlyList<AnglerReward1458> rewards)
    {
        var image = before.Select(static item => item.IsEmpty ? default :
            new WorldItemPickupSlot1458(item.ItemType.Value, item.Stack, item.Prefix.Value, (item.ItemFlags & 1) != 0)).ToArray();
        int source = Array.FindIndex(image, 0, 58, item => item.Type == fish && item.Stack > 0);
        Assert.InRange(source, 0, 57);
        image[source] = image[source].Stack == 1 ? default : image[source] with { Stack = image[source].Stack - 1 };
        foreach (var reward in rewards)
        {
            var incoming = new WorldItemPickupSlot1458(reward.Type, reward.Stack, 0, false);
            Assert.True(VanillaWorldItemOwner1458.TryPlanStorage(incoming, image, false,
                new(false, false, false), out var plan));
            Assert.NotNull(plan); Assert.Equal(0, plan.Residual.Type);
            foreach (var mutation in plan.Mutations)
            {
                Assert.Equal(mutation.Before, image[mutation.Slot]);
                image[mutation.Slot] = mutation.After;
            }
        }
        return image.Select(static item => item.Type == 0 ? default :
            new RuntimePlayerInventoryItem(new(item.Type), checked((short)item.Stack), new(item.Prefix), (byte)(item.Favorite ? 1 : 0))).ToArray();
    }

    private static void Begin(FishingCrateAuthority1458Tests.Fixture f) =>
        f.State.Apply(new CompleteAnglerQuestRuntimeCommand(f.Connection) { ValidateClientReports = true });

    private static void Reports(FishingCrateAuthority1458Tests.Fixture f, RuntimePlayerInventoryItem[] before,
        RuntimePlayerInventoryItem[] after)
    {
        for (short slot = 0; slot < after.Length; slot++)
            if (before[slot] != after[slot])
                f.State.Apply(new PlayerEquipmentRuntimeCommand(f.Connection,
                    after[slot].ToCommitRequest(f.Connection.Player.Slot, slot)));
    }

    private static void Complete(FishingCrateAuthority1458Tests.Fixture f)
    {
        f.State.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        int slices = 0;
        while (!f.Players.CanApplyShopSource(f.Connection.Source))
        { Assert.True(slices++ < 100_000); f.Players.TickShopJournalWork(); }
    }

    private sealed class SourceRandom(VanillaUnifiedRandom1458 source) : Random
    {
        public override int Next() => source.Next();
        public override int Next(int maximum) => source.Next(maximum);
        public override int Next(int minimum, int maximum) => source.Next(minimum, maximum);
        public override double NextDouble() => source.NextDouble();
    }
}
