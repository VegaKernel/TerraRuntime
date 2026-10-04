using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs.Loot;

// Execute against a cloned RNG and a bounded planning sink, never live state.
public readonly record struct VanillaNpcMoneyContext1458(
    float Value, int ExtraValue, float Luck, bool Midas, bool BloodMoon);

public interface INpcMoneyRandom1458
{
    int NextInt32(int inclusiveMin, int exclusiveMax);
    float NextFloat();
}

public interface INpcMoneyPlanningSink1458
{
    // Must materialize immediately: Item.NewItem's velocity draws precede the next split.
    bool TryMaterialize(ItemTypeId type, int stack, INpcMoneyRandom1458 random);
}

public static class VanillaNpcMoneyLoot1458
{
    // Source: NPC.NPCLoot_DropMoney. Attempt budget is a runtime admission policy, not vanilla.
    public static bool TryPlan(in VanillaNpcMoneyContext1458 context, INpcMoneyRandom1458 random,
        INpcMoneyPlanningSink1458 sink, int maximumSplitAttempts)
    {
        if (!float.IsFinite(context.Value) || context.Value < 0 || context.ExtraValue < 0 ||
            !float.IsFinite(context.Luck) || maximumSplitAttempts <= 0)
            return false;
        int iterations = random.NextFloat() < Math.Abs(context.Luck) ? 2 : 1;
        float amount = 0f;
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            float candidate = context.Value;
            if (context.Midas)
                candidate *= 1f + random.NextInt32(30, 51) * 0.01f;
            candidate *= 1f + random.NextInt32(-20, 76) * 0.01f;
            if (random.NextInt32(0, 2) == 0) candidate *= 1f + random.NextInt32(5, 11) * 0.01f;
            if (random.NextInt32(0, 4) == 0) candidate *= 1f + random.NextInt32(10, 21) * 0.01f;
            if (random.NextInt32(0, 8) == 0) candidate *= 1f + random.NextInt32(15, 31) * 0.01f;
            if (random.NextInt32(0, 16) == 0) candidate *= 1f + random.NextInt32(20, 41) * 0.01f;
            if (random.NextInt32(0, 32) == 0) candidate *= 1f + random.NextInt32(25, 51) * 0.01f;
            if (random.NextInt32(0, 64) == 0) candidate *= 1f + random.NextInt32(50, 101) * 0.01f;
            if (context.BloodMoon) candidate *= 1f + random.NextInt32(0, 101) * 0.01f;
            if (iteration == 0 || (context.Luck < 0f ? candidate < amount : candidate > amount))
                amount = candidate;
        }
        amount += context.ExtraValue;
        int attempts = 0;
        while ((int)amount > 0)
        {
            if (++attempts > maximumSplitAttempts) return false;
            int denomination;
            ItemTypeId type;
            if (amount > 1_000_000f) { denomination = 1_000_000; type = VanillaCoinFacts.PlatinumCoin; }
            else if (amount > 10_000f) { denomination = 10_000; type = VanillaCoinFacts.GoldCoin; }
            else if (amount > 100f) { denomination = 100; type = VanillaCoinFacts.SilverCoin; }
            else { denomination = 1; type = VanillaCoinFacts.CopperCoin; }
            int stack = (int)(amount / denomination);
            if (stack > 50 && random.NextInt32(0, 5) == 0)
                stack /= random.NextInt32(0, 3) + 1;
            if (random.NextInt32(0, 5) == 0)
                stack /= random.NextInt32(0, denomination == 1 ? 4 : 3) + 1;
            if (denomination == 1 && stack < 1) stack = 1;
            int remaining = stack;
            if (denomination == 1_000_000)
            {
                while (remaining > 999)
                {
                    remaining -= 999;
                    if (!sink.TryMaterialize(type, 999, random)) return false;
                }
            }
            amount -= unchecked(denomination * stack);
            // Item.NewItem stack<=0 returns400 before slot allocation and before any RNG.
            if (remaining > 0 && !sink.TryMaterialize(type, remaining, random)) return false;
        }
        return true;
    }
}
