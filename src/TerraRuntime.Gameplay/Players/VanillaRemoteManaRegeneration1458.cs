namespace TerraRuntime.Gameplay.Players;

public readonly record struct PlayerManaRegenerationState1458(
    int Mana, int Maximum, float Delay, int Count, int NebulaCount);

public readonly record struct PlayerManaRegenerationFacts1458(
    float VelocityX, float VelocityY, bool Hooked, bool RegenerationBuff,
    bool ArcaneCrystal, int Bonus, float DelayBonus, int NebulaLevel, bool ManaV2 = true);

/// <summary>Remote UpdateManaRegen; the caller owns reset/equipment facts and the following outer clamp.</summary>
public static class VanillaRemoteManaRegeneration1458
{
    public const int ConstructorBaseMaximum = 20;
    public static PlayerManaRegenerationState1458 ConstructorState => default;

    public static bool TryStep(PlayerManaRegenerationState1458? previous,
        in PlayerManaRegenerationFacts1458 facts, out PlayerManaRegenerationState1458 next, out int rate)
    {
        next = default;
        rate = 0;
        if (previous is not { } state || state.Maximum is < 0 or > 400 ||
            state.Mana is < 0 or > 400 || state.Count is < -119 or > 119 ||
            state.NebulaCount is < 0 or > 5 || facts.NebulaLevel is < 0 or > 3 ||
            facts.Bonus is < 0 or > 400 || !float.IsFinite(state.Delay) ||
            state.Delay is < -62.05f or > 3_600f || !float.IsFinite(facts.DelayBonus) ||
            facts.DelayBonus is < 0f or > 60f || !float.IsFinite(facts.VelocityX) ||
            !float.IsFinite(facts.VelocityY)) return false;

        int mana = state.Mana;
        int nebula = facts.NebulaLevel == 0 ? 0 : state.NebulaCount + facts.NebulaLevel;
        if (nebula >= 6)
        {
            nebula -= 6;
            mana = Math.Min(state.Maximum, mana + 1);
        }
        bool stationary = (double)Math.Abs(facts.VelocityX) < .05 &&
            (double)Math.Abs(facts.VelocityY) < .05;
        bool accelerated = stationary || facts.Hooked || facts.RegenerationBuff;
        float delay = state.Delay;
        if (delay > 0f)
        {
            delay -= 1f;
            delay -= facts.DelayBonus;
            if (accelerated) delay -= 1f;
            if (facts.ArcaneCrystal) delay -= .05f;
        }
        bool slowRegeneration = facts.ManaV2 && delay > 0f && delay < 4f;
        if (delay <= 0f || slowRegeneration)
        {
            int baseRate = state.Maximum / 3 + facts.Bonus + 1;
            int available = baseRate * (accelerated ? 2 : 1);
            if (facts.ArcaneCrystal) available += state.Maximum / 50;
            // Source accepts packet42 maximum0. Its non-finite ratio reaches the minimum-rate branch.
            float fullness = state.Maximum == 0 ? float.NaN : (float)mana / state.Maximum;
            float floor = facts.RegenerationBuff ? 1f : .5f;
            float multiplier = floor + (1f - floor) * fullness;
            if (slowRegeneration) multiplier *= .05f;
            rate = state.Maximum == 0 ? 2 : Math.Max(2, (int)(available * multiplier));
        }
        int accumulated = state.Count + rate;
        int restored = accumulated >= 120 ? accumulated / 120 : 0;
        next = new(restored > 0 ? Math.Min(state.Maximum, mana + restored) : mana, state.Maximum, delay,
            accumulated - restored * 120, nebula);
        return true;
    }
}
