namespace TerraRuntime.Gameplay.Players;

public readonly record struct AnglerReward1458(int Type, int Stack = 1);
public sealed record AnglerRewardContext1458(int QuestsDone, int QuestFish, bool HardMode, bool ExpertMode,
    float PriceAdjustment, IReadOnlyList<int> OwnedItems);

/// <summary>Ordered Player.GetAnglerReward offers. Each yield is the source Item-overload QuickSpawn boundary.</summary>
public static class VanillaAnglerReward1458
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
        float rarity = RarityMultiplier(context.QuestsDone) * ((context.PriceAdjustment + 1f) / 2f);
        AnglerReward1458 main = MainReward(random, context, rarity);
        yield return main;
        if (main.Type is 2417 or 2498)
        {
            yield return new(main.Type + 1);
            yield return new(main.Type + 2);
        }

        float baseChance = 1f - rarity;
        float completion = Math.Min(1f, context.QuestsDone / 100f);
        float decorationChance = baseChance + (1f - baseChance) * completion;
        if (decorationChance >= 1f || (float)random.NextDouble() <= decorationChance)
        {
            int[] decorations = [2442, 2443, 2444, 2445, 2497, 2495, 2446, 2447, 2448, 2449,
                2490, 2496, 5235, 5252, 5256, 5259, 5263, 5264, 5265];
            yield return new(decorations[random.Next(decorations.Length)]);
        }

        // GetAnglerReward calls Money before Bait, despite their declaration order in Player.
        long coins = (context.QuestsDone + 50) / 2;
        coins = (int)((float)(coins * random.Next(50, 201)) * .015f);
        coins = (long)(coins * 1.5d);
        if (context.HardMode) coins *= 2;
        if (context.ExpertMode) coins *= 2;
        yield return coins > 100 ? new(73, (int)Math.Clamp(coins / 100, 1, 10)) : new(72, (int)Math.Clamp(coins, 1, 99));

        if (random.Next((int)(100f * rarity)) <= 50)
        {
            int bait = random.Next((int)(15f * rarity)) == 0 ? 2676 :
                random.Next((int)(5f * rarity)) == 0 ? 2675 : 2674;
            int stack = 1;
            foreach (int threshold in new[] { 25, 50, 100, 150, 200, 250 })
                if (random.Next(threshold) <= context.QuestsDone) stack++;
            yield return new(bait, stack);
        }
    }

    private static AnglerReward1458 MainReward(Random random, AnglerRewardContext1458 context, float rarity)
    {
        int fixedReward = context.QuestsDone switch { 5 => 2428, 10 => 2367, 15 => 2368, 20 => 2369, 25 => 3031, 30 => 2294, _ => 0 };
        if (fixedReward != 0) return new(fixedReward);
        if (context.QuestFish == 2451 && (context.HardMode || random.Next(2) == 0))
            return new(random.Next(2) == 0 ? 5303 : 5302);

        float noAccessory = 1f;
        for (int i = 0; i < 3; i++) noAccessory *= 1f - 1f / (int)(40f * rarity);
        for (int i = 0; i < 3; i++) noAccessory *= 1f - 1f / (int)(30f * rarity);
        noAccessory *= 1f - 1f / (int)(25f * rarity);
        float accessoryChance = (1f - noAccessory) * .8f;
        if (context.QuestsDone > 75 && random.Next((int)(250f * rarity)) == 0) return new(2294);
        if (context.HardMode && context.QuestsDone > 25 && random.Next((int)(100f * rarity)) == 0) return new(2422);
        if (context.HardMode && context.QuestsDone > 10 && random.Next((int)(70f * rarity)) == 0) return new(2494);
        if (context.QuestsDone > 10 && random.Next((int)(70f * rarity)) == 0) return new(3031);
        if (context.QuestsDone > 10 && random.Next((int)(70f * rarity)) == 0) return new(3032);
        foreach (var offer in new[] { (80, 3183), (60, 2360), (60, 4067), (80, 2417), (80, 2498) })
            if (random.Next((int)(offer.Item1 * rarity)) == 0) return new(offer.Item2);

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
            if (random.NextDouble() < accessoryChance) return new(missing[random.Next(missing.Count)]);
        }
        else if (random.NextDouble() < accessoryChance) return new(allAccessories[random.Next(allAccessories.Length)]);
        return new(2354 + random.Next(3), random.Next(2, 6));
    }
}
