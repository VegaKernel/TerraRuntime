namespace TerraRuntime.Gameplay.Players;

public readonly record struct AnglerReward1458(int Type, int Stack = 1);
public sealed record AnglerRewardContext1458(int QuestsDone, int QuestFish, bool HardMode, bool ExpertMode,
    float PriceAdjustment, IReadOnlyList<int> OwnedItems);

/// <summary>Ordered Player.GetAnglerReward offers. Each yield is the source Item-overload QuickSpawn boundary.</summary>
public static partial class VanillaAnglerReward1458
{
    public static float RarityMultiplier(int questsDone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(questsDone);
        float rarity = questsDone <= 50 ? 1f - questsDone * .01f :
            questsDone <= 100 ? .5f - (questsDone - 50) * .005f :
            questsDone <= 150 ? .25f - (questsDone - 100) * .002f : .15f;
        return rarity * .9f;
    }

    public static IEnumerable<AnglerReward1458> Roll(Random random, AnglerRewardContext1458 context)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.OwnedItems);
        if (context.QuestsDone <= 0 || context.QuestsDone > int.MaxValue - 50 ||
            !float.IsFinite(context.PriceAdjustment) || context.PriceAdjustment is < .5f or > 2f)
            throw new ArgumentOutOfRangeException(nameof(context));
        var choices = new SourceChoices(random);
        float rarity = RewardRarity(context);
        foreach (var reward in MainOffers(choices, context, rarity)) yield return reward;
        if (Decoration(choices, context, rarity) is { } decoration) yield return decoration;
        yield return Money(choices, context);
        if (Bait(choices, context, rarity) is { } bait) yield return bait;
    }

    private static float RewardRarity(AnglerRewardContext1458 context) =>
        RarityMultiplier(context.QuestsDone) * ((context.PriceAdjustment + 1f) / 2f);

    private static AnglerReward1458[] MainOffers(RewardChoices choices, AnglerRewardContext1458 context, float rarity)
    {
        AnglerReward1458 main = MainReward(choices, context, rarity);
        return main.Type is 2417 or 2498 ? [main, new(main.Type + 1), new(main.Type + 2)] : [main];
    }

    private static AnglerReward1458? Decoration(RewardChoices choices, AnglerRewardContext1458 context, float rarity)
    {
        float baseChance = 1f - rarity;
        float completion = Math.Min(1f, context.QuestsDone / 100f);
        float decorationChance = baseChance + (1f - baseChance) * completion;
        if (decorationChance >= 1f || choices.SingleAtMost(decorationChance))
        {
            int[] decorations = [2442, 2443, 2444, 2445, 2497, 2495, 2446, 2447, 2448, 2449,
                2490, 2496, 5235, 5252, 5256, 5259, 5263, 5264, 5265];
            return new(decorations[choices.Pick(decorations.Length)]);
        }
        return null;
    }

    private static AnglerReward1458 Money(RewardChoices choices, AnglerRewardContext1458 context)
    {
        // GetAnglerReward calls Money before Bait, despite their declaration order in Player.
        long coins = (context.QuestsDone + 50) / 2;
        coins = (int)((float)(coins * choices.Range(50, 201)) * .015f);
        coins = (long)(coins * 1.5d);
        if (context.HardMode) coins *= 2;
        if (context.ExpertMode) coins *= 2;
        return coins > 100 ? new(73, (int)Math.Clamp(coins / 100, 1, 10)) : new(72, (int)Math.Clamp(coins, 1, 99));
    }

    private static AnglerReward1458? Bait(RewardChoices choices, AnglerRewardContext1458 context, float rarity)
    {
        if (choices.AtMost((int)(100f * rarity), 50))
        {
            int bait = choices.OneIn((int)(15f * rarity)) ? 2676 :
                choices.OneIn((int)(5f * rarity)) ? 2675 : 2674;
            int stack = 1 + choices.CountAtMost([25, 50, 100, 150, 200, 250], context.QuestsDone);
            return new(bait, stack);
        }
        return null;
    }

    private static AnglerReward1458 MainReward(RewardChoices choices, AnglerRewardContext1458 context, float rarity)
    {
        int fixedReward = context.QuestsDone switch { 5 => 2428, 10 => 2367, 15 => 2368, 20 => 2369, 25 => 3031, 30 => 2294, _ => 0 };
        if (fixedReward != 0) return new(fixedReward);
        if (context.QuestFish == 2451 && (context.HardMode || choices.OneIn(2)))
            return new(choices.OneIn(2) ? 5303 : 5302);

        float noAccessory = 1f;
        for (int i = 0; i < 3; i++) noAccessory *= 1f - 1f / (int)(40f * rarity);
        for (int i = 0; i < 3; i++) noAccessory *= 1f - 1f / (int)(30f * rarity);
        noAccessory *= 1f - 1f / (int)(25f * rarity);
        float accessoryChance = (1f - noAccessory) * .8f;
        if (context.QuestsDone > 75 && choices.OneIn((int)(250f * rarity))) return new(2294);
        if (context.HardMode && context.QuestsDone > 25 && choices.OneIn((int)(100f * rarity))) return new(2422);
        if (context.HardMode && context.QuestsDone > 10 && choices.OneIn((int)(70f * rarity))) return new(2494);
        if (context.QuestsDone > 10 && choices.OneIn((int)(70f * rarity))) return new(3031);
        if (context.QuestsDone > 10 && choices.OneIn((int)(70f * rarity))) return new(3032);
        foreach (var offer in new[] { (80, 3183), (60, 2360), (60, 4067), (80, 2417), (80, 2498) })
            if (choices.OneIn((int)(offer.Item1 * rarity))) return new(offer.Item2);

        int[] allAccessories = [2373, 2374, 2375, 3120, 3037, 3096, 5139];
        var missing = new List<int>(allAccessories);
        foreach (int owned in context.OwnedItems)
        {
            switch (owned)
            {
                case 3721 or 5064:
                    missing.Remove(2373); missing.Remove(2375); missing.Remove(2374); break;
                case 3036 or 3123 or 3124 or 5358 or 5359 or 5360 or 5361:
                    missing.Remove(3120); missing.Remove(3037); missing.Remove(3096); break;
                case >= 5140 and <= 5146:
                    missing.Remove(5139); break;
                default: missing.Remove(owned); break;
            }
        }
        if (missing.Count != 0)
        {
            if (choices.DoubleBelow(accessoryChance)) return new(missing[choices.Pick(missing.Count)]);
        }
        else if (choices.DoubleBelow(accessoryChance)) return new(allAccessories[choices.Pick(allAccessories.Length)]);
        return new(2354 + choices.Pick(3), choices.Range(2, 6));
    }

    private abstract class RewardChoices
    {
        internal abstract bool OneIn(int maximum);
        internal abstract bool AtMost(int maximum, int inclusive);
        internal abstract bool DoubleBelow(float threshold);
        internal abstract bool SingleAtMost(float threshold);
        internal abstract int Pick(int count);
        internal abstract int Range(int minimum, int maximum);
        internal abstract int CountAtMost(ReadOnlySpan<int> maxima, int inclusive);
    }

    private sealed class SourceChoices(Random random) : RewardChoices
    {
        internal override bool OneIn(int maximum) => random.Next(maximum) == 0;
        internal override bool AtMost(int maximum, int inclusive) => random.Next(maximum) <= inclusive;
        internal override bool DoubleBelow(float threshold) => random.NextDouble() < threshold;
        internal override bool SingleAtMost(float threshold) => (float)random.NextDouble() <= threshold;
        internal override int Pick(int count) => random.Next(count);
        internal override int Range(int minimum, int maximum) => random.Next(minimum, maximum);
        internal override int CountAtMost(ReadOnlySpan<int> maxima, int inclusive)
        {
            int count = 0;
            foreach (int maximum in maxima) if (random.Next(maximum) <= inclusive) count++;
            return count;
        }
    }
}
