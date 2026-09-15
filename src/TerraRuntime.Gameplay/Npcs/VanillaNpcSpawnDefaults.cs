using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Authoritative inputs sampled at NewNPC time. Difficulty is the effective value after seed adjustment.</summary>
public readonly record struct VanillaNpcSpawnContext(float Difficulty, int ActivePlayers, bool GoodWorld)
{
    public bool IsValid => float.IsFinite(Difficulty) && Difficulty is >= .5f and <= 4f &&
        ActivePlayers is >= 0 and <= 255;
}

public readonly record struct VanillaNpcSpawnDefaults(
    VanillaNpcHitboxSize Hitbox, float Scale, int LifeMax, int Damage, int Defense)
{
    /// <summary>
    /// NPC.SetDefaults -> getGoodAdjustments -> ScaleStats for the Destroyer family and Probe (1.4.5.8).
    /// Other families keep their existing definition defaults until their type-specific scaling is verified.
    /// </summary>
    public static bool TryResolve(in VanillaNpcDefinition definition, in VanillaNpcSpawnContext context,
        out VanillaNpcSpawnDefaults defaults)
    {
        defaults = default;
        bool probe = definition.Type == VanillaNpcIds.Probe;
        if (!context.IsValid || (!probe && definition.Type != VanillaNpcIds.Destroyer &&
            definition.Type != VanillaNpcIds.DestroyerBody && definition.Type != VanillaNpcIds.DestroyerTail)) return false;

        int width = definition.Width, height = definition.Height;
        float scale = definition.Scale;
        if (context.GoodWorld)
        {
            scale *= probe ? 1.6f : 1.3f;
            // getGoodAdjustments multiplies the already-scaled dimensions by the new visual scale.
            width = (int)(width * scale);
            height = (int)(height * scale);
        }
        float difficulty = context.Difficulty;
        int life = (int)(definition.LifeMax * difficulty);
        float damageMultiplier = difficulty <= 3f ? difficulty : 3f + (difficulty - 3f) * (5.3333335f - 3f);
        int damage = (int)(definition.Damage * damageMultiplier);
        float lifeTweak = Ramp(difficulty, 1f, 2f, .75f) * Ramp(difficulty, 2f, 3f, probe ? 1f : .85f);
        life = (int)Math.Round(life * lifeTweak);
        float expertDamageTweak = probe ? .8f : definition.Type == VanillaNpcIds.Destroyer ? 2f : .85f;
        damage = (int)Math.Round(damage * Ramp(difficulty, 1f, 2f, expertDamageTweak));
        if (difficulty >= 2f)
        {
            scale *= 1.05f;
            float balance = PlayerBalance(context.ActivePlayers);
            double multiplier = probe ? 1d + (balance - 1d) * (2d / 3d) : balance;
            life = (int)Math.Round(life * multiplier);
        }
        defaults = new(new(width, height), scale, life, damage, definition.Defense);
        return true;
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
