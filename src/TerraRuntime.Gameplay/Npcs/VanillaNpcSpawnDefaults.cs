using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Authoritative inputs sampled at NewNPC time. Difficulty is the effective value after seed adjustment.</summary>
public readonly record struct VanillaNpcSpawnContext(float Difficulty, int ActivePlayers, bool GoodWorld)
{
    public bool HardMode { get; init; }
    public bool DownedPlantera { get; init; }
    public bool SkeletronActive { get; init; }
    public bool IsValid => float.IsFinite(Difficulty) && Difficulty is >= .5f and <= 4f &&
        ActivePlayers is >= 0 and <= 255;
}

public readonly record struct VanillaNpcSpawnDefaults(
    VanillaNpcHitboxSize Hitbox, float Scale, int LifeMax, int Damage, int Defense)
{
    public float? KnockBackResist { get; init; }
    /// <summary>
    /// NPC.SetDefaults -> getGoodAdjustments -> ScaleStats for Destroyer, Probe, Prime and Skeletron (1.4.5.8).
    /// Other families keep their existing definition defaults until their type-specific scaling is verified.
    /// </summary>
    public static bool TryResolve(in VanillaNpcDefinition definition, in VanillaNpcSpawnContext context,
        out VanillaNpcSpawnDefaults defaults)
    {
        defaults = default;
        if (context.IsValid && (definition.Type == VanillaNpcIds.DarkCaster || definition.Type == VanillaNpcIds.WaterSphere))
        {
            defaults = ResolveCaster(in definition, in context);
            return true;
        }
        bool probe = definition.Type == VanillaNpcIds.Probe;
        bool skeletronHead = definition.Type == VanillaNpcIds.SkeletronHead;
        bool skeletronHand = definition.Type == VanillaNpcIds.SkeletronHand;
        bool skeletron = skeletronHead || skeletronHand;
        bool prime = definition.Type == VanillaNpcIds.SkeletronPrime || definition.Type == VanillaNpcIds.PrimeCannon ||
            definition.Type == VanillaNpcIds.PrimeSaw || definition.Type == VanillaNpcIds.PrimeVice || definition.Type == VanillaNpcIds.PrimeLaser;
        if (!context.IsValid || (!probe && !prime && !skeletron && definition.Type != VanillaNpcIds.Destroyer &&
            definition.Type != VanillaNpcIds.DestroyerBody && definition.Type != VanillaNpcIds.DestroyerTail)) return false;

        int width = definition.Width, height = definition.Height;
        float scale = definition.Scale;
        if (context.GoodWorld)
        {
            scale *= probe ? 1.6f : prime ? 1.1f : skeletronHead ? 1.25f : skeletronHand ? 1.15f : 1.3f;
            // getGoodAdjustments multiplies the already-scaled dimensions by the new visual scale.
            width = (int)(width * scale);
            height = (int)(height * scale);
        }
        float difficulty = context.Difficulty;
        int life = (int)(definition.LifeMax * difficulty);
        float damageMultiplier = difficulty <= 3f ? difficulty : 3f + (difficulty - 3f) * (5.3333335f - 3f);
        int damage = (int)(definition.Damage * damageMultiplier);
        float expertLifeTweak = skeletronHead ? 1f : skeletronHand ? 1.3f : .75f;
        float lifeTweak = Ramp(difficulty, 1f, 2f, expertLifeTweak) * Ramp(difficulty, 2f, 3f, probe ? 1f : .85f);
        life = (int)Math.Round(life * lifeTweak);
        float expertDamageTweak = probe ? .8f : skeletron ? 1.1f : definition.Type == VanillaNpcIds.Destroyer ? 2f : .85f;
        damage = (int)Math.Round(damage * Ramp(difficulty, 1f, 2f, expertDamageTweak));
        if (difficulty >= 2f)
        {
            if (!prime && !skeletron) scale *= 1.05f;
            float balance = PlayerBalance(context.ActivePlayers);
            double multiplier = probe ? 1d + (balance - 1d) * (2d / 3d) : balance;
            life = (int)Math.Round(life * multiplier);
        }
        defaults = new(new(width, height), scale, life, damage, definition.Defense);
        return true;
    }

    private static VanillaNpcSpawnDefaults ResolveCaster(in VanillaNpcDefinition definition, in VanillaNpcSpawnContext context)
    {
        bool sphere = definition.Type == VanillaNpcIds.WaterSphere;
        bool skeletron = context.GoodWorld && context.SkeletronActive;
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
                damage = (int)(damage * factor * .9f);
                if (!sphere)
                {
                    defense = (int)(defense * factor);
                    life = (int)((double)(life * factor) * 1.1d);
                }
            }
        }
        if (!sphere) life = (int)(life * difficulty);
        float damageMultiplier = difficulty <= 3f ? difficulty : 3f + (difficulty - 3f) * (5.3333335f - 3f);
        damage = (int)(damage * damageMultiplier);
        if (!sphere && skeletron)
        {
            life = (int)Math.Round(life * (Ramp(difficulty, 1f, 2f, 1.5f) * Ramp(difficulty, 2f, 3f, .85f)));
            if (difficulty >= 2f) defense += 6;
        }
        if (!sphere) life = Math.Max(6, life);
        return new(new(definition.Width, definition.Height), definition.Scale, life, damage, defense)
        {
            KnockBackResist = definition.KnockBackResist * Ramp(difficulty, 1f, 3f, .8f)
        };
    }

    private static float Ramp(float difficulty, float lower, float upper, float end) =>
        1f + (end - 1f) * Math.Clamp((difficulty - lower) / (upper - lower), 0f, 1f);

    private static float PlayerBalance(int players)
    {
        float balance = 1f, increment = .35f;
        for (int player = 1; player < players; player++)
        {
            balance += increment;
            increment += (1f - increment) / 3f;
        }
        if (balance > 8f) balance = (balance * 2f + 8f) / 3f;
        return Math.Min(balance, 1000f);
    }
}
