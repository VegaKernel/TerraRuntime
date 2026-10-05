using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

public readonly partial record struct VanillaNpcSpawnDefaults
{
    /// <summary>Slimer HitEffect's NewNPC(81) followed by SetDefaults(-2), using the original platform arithmetic.</summary>
    public static bool TryResolveSlimerChild(in VanillaNpcSpawnContext context, bool windowsArithmetic,
        out VanillaNpcSpawnDefaults defaults)
    {
        defaults = default;
        if (!context.IsValid || !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.CorruptSlime,
            VanillaNpcNetVariantCatalog.Slimer2, out var definition)) return false;
        float firstKnockback = (float)Ramp(context.Difficulty, 1f, 3f, .8f, windowsArithmetic);
        definition = definition with { KnockBackResist = windowsArithmetic
            ? (float)((double)firstKnockback * 1.2f) : firstKnockback * 1.2f };
        var scaled = ResolveOrdinary(in definition, in context, windowsArithmetic);
        float scale = context.GoodWorld ? (.9f + .9f * .9f) / 2f : .9f;
        int width = windowsArithmetic ? (int)(40d * scale) : (int)(40f * scale);
        int height = windowsArithmetic ? (int)(30d * scale) : (int)(30f * scale);
        int rawMoney = 100;
        if (context.Difficulty >= 2f && context.HardMode)
        {
            const int budget = 45 + 20 + 90 / 4;
            int threshold = context.DownedPlantera ? 100 : 80;
            if (budget < threshold) rawMoney = (int)((double)(rawMoney * (threshold / budget)) * .8d);
        }
        defaults = scaled with { Hitbox = new(width, height), Scale = scale, Difficulty = context.Difficulty,
            VerifiedMoneyValue = VanillaNpcMoneyDefaults1458.ScaleBaseline(rawMoney, context.Difficulty) };
        return true;
    }
}
