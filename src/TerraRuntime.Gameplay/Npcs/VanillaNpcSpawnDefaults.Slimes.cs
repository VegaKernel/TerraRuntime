using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

public readonly partial record struct VanillaNpcSpawnDefaults
{
    private static bool TryResolveSlimeFamily(in VanillaNpcDefinition definition,
        in VanillaNpcSpawnContext context, bool windowsArithmetic, out VanillaNpcSpawnDefaults defaults)
    {
        defaults = default;
        if (!VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.BlueSlime, out var canonical))
            return false;
        bool variant = definition != canonical;
        int rawMoney = 25;
        if (variant)
        {
            bool recognized = false;
            foreach (var entry in VanillaNpcNetVariantCatalog.All)
                if (entry.Type == VanillaNpcIds.BlueSlime && entry.ApplyTo(in canonical) == definition)
                {
                    // SetDefaultsFromNetId overwrites canonical combat/value, then calls ScaleStats again.
                    rawMoney = entry.NetId.Value switch
                    {
                        -3 => 3, -4 => 10000, -5 => 10, -6 => 20,
                        -7 => 10, -8 => 8, -9 => 10, -10 => 500, _ => -1
                    };
                    recognized = rawMoney >= 0;
                    break;
                }
            if (!recognized) return false;
        }
        var baseline = definition;
        if (variant)
        {
            // The first canonical ScaleStats knockback survives the variant's multiplicative adjustment.
            float canonicalKnockback = (float)Ramp(context.Difficulty, 1f, 3f, .8f, windowsArithmetic);
            baseline = baseline with { KnockBackResist = canonicalKnockback * baseline.KnockBackResist };
        }
        var scaled = ResolveOrdinary(in baseline, in context, windowsArithmetic);
        float scale = definition.Scale;
        if (variant && context.GoodWorld) scale = (scale + scale * scale) / 2f;
        int width = (int)(canonical.BaseWidth * scale), height = (int)(canonical.BaseHeight * scale);
        // Only the explicit size override path in SetDefaults corrects these collision heights.
        if (variant && height is 16 or 32) height++;
        if (context.Difficulty >= 2f && context.HardMode)
        {
            int budget = Math.Max(1, definition.Damage + definition.Defense + definition.LifeMax / 4);
            int threshold = context.DownedPlantera ? 100 : 80;
            if (budget < threshold) rawMoney = (int)((double)(rawMoney * (threshold / budget)) * .8d);
        }
        defaults = scaled with
        {
            Hitbox = new(width, height), Scale = scale, Difficulty = context.Difficulty,
            VerifiedMoneyValue = VanillaNpcMoneyDefaults1458.ScaleBaseline(rawMoney, context.Difficulty)
        };
        return true;
    }
}
