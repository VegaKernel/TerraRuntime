using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Authoritative inputs sampled at NewNPC time. Difficulty is the effective value after seed adjustment.</summary>
public readonly record struct VanillaNpcSpawnContext(float Difficulty, int ActivePlayers, bool GoodWorld)
{
    public bool HardMode { get; init; }
    public bool DownedPlantera { get; init; }
    public bool SkeletronActive { get; init; }
    public bool TenthAnniversaryWorld { get; init; }
    public bool IsValid => float.IsFinite(Difficulty) && Difficulty is >= .5f and <= 4f &&
        ActivePlayers is >= 0 and <= 255;
}

public readonly record struct VanillaNpcSpawnDefaults(
    VanillaNpcHitboxSize Hitbox, float Scale, int LifeMax, int Damage, int Defense)
{
    public float? KnockBackResist { get; init; }
    public float? Difficulty { get; init; }
    /// <summary>
    /// NPC.SetDefaults -> special-seed adjustments -> ScaleStats for Destroyer, Probe, Prime, Skeletron and Duke Fishron (1.4.5.8).
    /// Other families keep their existing definition defaults until their type-specific scaling is verified.
    /// </summary>
    public static bool TryResolve(in VanillaNpcDefinition definition, in VanillaNpcSpawnContext context,
        out VanillaNpcSpawnDefaults defaults) => TryResolve(in definition, in context, OperatingSystem.IsWindows(), out defaults);

    /// <summary>Resolves the pinned original platform arithmetic, also used for cross-platform differential verification.</summary>
    public static bool TryResolve(in VanillaNpcDefinition definition, in VanillaNpcSpawnContext context,
        bool windowsArithmetic, out VanillaNpcSpawnDefaults defaults)
    {
        defaults = default;
        if (context.IsValid && (definition.Type == VanillaNpcIds.Bunny || definition.Type == VanillaNpcIds.ExplosiveBunny))
        {
            // ScaleStats does not enter its scaling block for these five-life, zero-damage critters.
            defaults = new(new(definition.Width, definition.Height), definition.Scale, definition.LifeMax, definition.Damage, definition.Defense)
            { KnockBackResist = definition.KnockBackResist, Difficulty = 1f };
            return true;
        }
        if (context.IsValid && (definition.Type == VanillaNpcIds.DarkCaster || definition.Type == VanillaNpcIds.WaterSphere ||
            definition.Type == VanillaNpcIds.Demon || definition.Type == VanillaNpcIds.VoodooDemon))
        {
            defaults = ResolveOrdinary(in definition, in context, windowsArithmetic);
            return true;
        }
        bool probe = definition.Type == VanillaNpcIds.Probe;
        bool skeletronHead = definition.Type == VanillaNpcIds.SkeletronHead;
        bool skeletronHand = definition.Type == VanillaNpcIds.SkeletronHand;
        bool skeletron = skeletronHead || skeletronHand;
        bool duke = definition.Type == VanillaNpcIds.DukeFishron;
        bool prime = definition.Type == VanillaNpcIds.SkeletronPrime || definition.Type == VanillaNpcIds.PrimeCannon ||
            definition.Type == VanillaNpcIds.PrimeSaw || definition.Type == VanillaNpcIds.PrimeVice || definition.Type == VanillaNpcIds.PrimeLaser;
        if (!context.IsValid || (!probe && !prime && !skeletron && !duke && definition.Type != VanillaNpcIds.Destroyer &&
            definition.Type != VanillaNpcIds.DestroyerBody && definition.Type != VanillaNpcIds.DestroyerTail)) return false;

        int width = definition.Width, height = definition.Height;
        float scale = definition.Scale;
        // NPC.SetDefaults calls getGoodAdjustments first and only calls getTenthAnniversaryAdjustments in
        // the alternative branch. A composite test context must retain that source precedence too.
        if (context.GoodWorld)
        {
            scale *= probe ? 1.6f : prime ? 1.1f : skeletronHead ? 1.25f : skeletronHand ? 1.15f : 1.3f;
            // getGoodAdjustments multiplies the already-scaled dimensions by the new visual scale.
            width = (int)(width * scale);
            height = (int)(height * scale);
        }
        else if (duke && context.TenthAnniversaryWorld)
        {
            // NPC.getTenthAnniversaryAdjustments halves type 370's visual scale, then materializes the same
            // half-scale physical dimensions before ScaleStats runs.
            scale *= .5f;
            width = (int)(width * scale);
            height = (int)(height * scale);
        }
        float difficulty = context.Difficulty;
        int life = windowsArithmetic ? (int)(definition.LifeMax * (double)difficulty) : (int)(definition.LifeMax * difficulty);
        double damageMultiplier = DamageMultiplier(difficulty, windowsArithmetic);
        int damage = windowsArithmetic ? (int)(definition.Damage * damageMultiplier) : (int)(definition.Damage * (float)damageMultiplier);
        float expertLifeTweak = duke ? .65f : skeletronHead ? 1f : skeletronHand ? 1.3f : .75f;
        float masterLifeTweak = duke ? .85f : probe ? 1f : .85f;
        float lifeTweak = (float)(Ramp(difficulty, 1f, 2f, expertLifeTweak, windowsArithmetic) * Ramp(difficulty, 2f, 3f, masterLifeTweak, windowsArithmetic));
        life = (int)Math.Round(windowsArithmetic ? life * (double)lifeTweak : life * lifeTweak);
        float expertDamageTweak = duke ? .7f : probe ? .8f : skeletron ? 1.1f : definition.Type == VanillaNpcIds.Destroyer ? 2f : .85f;
        double damageTweak = Ramp(difficulty, 1f, 2f, expertDamageTweak, windowsArithmetic);
        damage = (int)Math.Round(windowsArithmetic ? damage * damageTweak : damage * (float)damageTweak);
        if (difficulty >= 2f)
        {
            if (!prime && !skeletron && !duke) scale *= 1.05f;
            float balance = PlayerBalance(context.ActivePlayers, windowsArithmetic);
            double multiplier = probe ? 1d + (balance - 1d) * (2d / 3d) : balance;
            life = (int)Math.Round(life * multiplier);
        }
        defaults = new(new(width, height), scale, life, damage, definition.Defense);
        return true;
    }

    private static VanillaNpcSpawnDefaults ResolveOrdinary(in VanillaNpcDefinition definition, in VanillaNpcSpawnContext context, bool windowsArithmetic)
    {
        bool sphere = definition.Type == VanillaNpcIds.WaterSphere;
        bool skeletron = (sphere || definition.Type == VanillaNpcIds.DarkCaster) && context.GoodWorld && context.SkeletronActive;
        float difficulty = context.Difficulty;
        int life = definition.LifeMax, damage = definition.Damage, defense = definition.Defense;
        // ScaleStats_ForExpertHardmode uses integer division before converting the budget ratio to float.
        if (difficulty >= 2f && context.HardMode && !skeletron)
        {
            int budget = Math.Max(1, damage + defense + life / 4);
            int threshold = context.DownedPlantera ? 100 : 80;
            if (budget < threshold)
            {
                float factor = threshold / budget;
                damage = windowsArithmetic ? (int)(damage * (double)factor * .9f) : (int)(damage * factor * .9f);
                if (!sphere)
                {
                    defense = (int)(defense * factor);
                    life = (int)((double)(life * factor) * 1.1d);
                }
            }
        }
        if (!sphere) life = windowsArithmetic ? (int)(life * (double)difficulty) : (int)(life * difficulty);
        double damageMultiplier = DamageMultiplier(difficulty, windowsArithmetic);
        damage = windowsArithmetic ? (int)(damage * damageMultiplier) : (int)(damage * (float)damageMultiplier);
        if (!sphere && skeletron)
        {
            float tweak = (float)(Ramp(difficulty, 1f, 2f, 1.5f, windowsArithmetic) * Ramp(difficulty, 2f, 3f, .85f, windowsArithmetic));
            life = (int)Math.Round(windowsArithmetic ? life * (double)tweak : life * tweak);
            if (difficulty >= 2f) defense += 6;
        }
        if (!sphere) life = Math.Max(6, life);
        return new(new(definition.Width, definition.Height), definition.Scale, life, damage, defense)
        {
            KnockBackResist = windowsArithmetic
                ? (float)(definition.KnockBackResist * Ramp(difficulty, 1f, 3f, .8f, true))
                : definition.KnockBackResist * (float)Ramp(difficulty, 1f, 3f, .8f, false)
        };
    }

    // The original Windows x86 CLR carries wider expression results into integer conversions
    // and Math.Round. The two life ramps are stored as Single before the final product;
    // the single damage ramp is retained wider. These are source boundaries, not a global precision policy.
    private static double Ramp(float difficulty, float lower, float upper, float end, bool windowsArithmetic) =>
        windowsArithmetic
            ? 1d + ((double)end - 1d) * Math.Clamp(((double)difficulty - lower) / ((double)upper - lower), 0d, 1d)
            : 1f + (end - 1f) * Math.Clamp((difficulty - lower) / (upper - lower), 0f, 1f);

    private static double DamageMultiplier(float difficulty, bool windowsArithmetic) => difficulty <= 3f ? difficulty :
        windowsArithmetic ? 3d + ((double)difficulty - 3d) * ((double)5.3333335f - 3d) :
        3f + (difficulty - 3f) * (5.3333335f - 3f);

    private static float PlayerBalance(int players, bool windowsArithmetic)
    {
        float balance = 1f, increment = .35f;
        for (int player = 1; player < players; player++)
        {
            balance += increment;
            increment = windowsArithmetic ? (float)(increment + (1d - increment) / 3d) : increment + (1f - increment) / 3f;
        }
        if (balance > 8f) balance = windowsArithmetic ? (float)((balance * 2d + 8d) / 3d) : (balance * 2f + 8f) / 3f;
        return Math.Min(balance, 1000f);
    }
}
