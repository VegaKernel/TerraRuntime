using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

public readonly partial record struct VanillaNpcSpawnDefaults
{
    private static bool TryResolveContainedFamily(in VanillaNpcDefinition definition,
        in VanillaNpcSpawnContext context, bool windowsArithmetic, out VanillaNpcSpawnDefaults defaults)
    {
        bool lava = definition.Type == VanillaNpcIds.LavaSlime;
        var baseline = definition;
        int rawMoney = lava ? 120 : 0;
        if (lava && context.RemixWorld)
        {
            // SetDefaults(59) keeps its identity, scale, alpha and physical seed adjustment.
            baseline = baseline with { Damage = 7, Defense = 2, LifeMax = 25 };
            rawMoney = 25;
        }
        if (context.Difficulty >= 2f && context.HardMode)
        {
            int budget = Math.Max(1, baseline.Damage + baseline.Defense + baseline.LifeMax / 4);
            int threshold = context.DownedPlantera ? 100 : 80;
            if (budget < threshold) rawMoney = (int)((double)(rawMoney * (threshold / budget)) * .8d);
        }
        defaults = ResolveOrdinary(in baseline, in context, windowsArithmetic) with
        {
            Difficulty = context.Difficulty,
            VerifiedMoneyValue = VanillaNpcMoneyDefaults1458.ScaleBaseline(rawMoney, context.Difficulty)
        };
        if (!lava)
        {
            // ScaleStats_ByDifficulty_Tweaks runs after hardmode/base scaling, before final minimum life.
            float lifeTweak = (float)Ramp(context.Difficulty, 1f, 2f, .6f, windowsArithmetic);
            double damageTweak = Ramp(context.Difficulty, 1f, 2f, .6f, windowsArithmetic);
            int life = (int)Math.Round(windowsArithmetic ? defaults.LifeMax * (double)lifeTweak : defaults.LifeMax * lifeTweak);
            int damage = (int)Math.Round(windowsArithmetic ? defaults.Damage * damageTweak : defaults.Damage * (float)damageTweak);
            int defense = windowsArithmetic
                ? (int)(defaults.Defense * Ramp(context.Difficulty, 1f, 2f, .8f, true))
                : (int)(defaults.Defense * (float)Ramp(context.Difficulty, 1f, 2f, .8f, false));
            defaults = defaults with { LifeMax = Math.Max(6, life), Damage = damage, Defense = defense };
        }
        return true;
    }
}
