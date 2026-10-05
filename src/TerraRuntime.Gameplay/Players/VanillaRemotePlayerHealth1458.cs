using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Players;

public readonly record struct PlayerNpcHealthWorld1458(bool ExpertMode, bool VampireSeed);

/// <summary>Bounded Player.UpdateLifeRegen for ordinary remote actors without equipment/environment regeneration.</summary>
public static class VanillaRemotePlayerHealth1458
{
    // Packet16 base maximum is Int16; each of the 44 source Lifeforce slots adds the same integer increment.
    public const int MaximumSupportedLifeMax = short.MaxValue + 44 * (short.MaxValue / 5 / 20 * 20);
    public static PlayerNpcHealthState1458? Step(PlayerNpcHealthState1458? previous, int maximum,
        bool outside, bool ghost, bool dead, bool supported, bool expert, bool moving,
        int regenerationSlots, bool poisoned, bool onFire, bool cursedInferno)
    {
        // Player.Update returns outside the world before Ghost/UpdateDead and before regeneration.
        if (outside || ghost) return previous;
        if (dead) return previous is { } old ? old with { Life = 0 } : new(0, null, null);
        if (!supported || maximum is < 0 or > MaximumSupportedLifeMax ||
            previous is not { RegenCount: { } count, RegenTime: { } time } state ||
            !float.IsFinite(time) || time < 0f || time > 3600f || count is < -119 or > 119 ||
            regenerationSlots is < 0 or > 44)
            return null;

        int rate = regenerationSlots * 4;
        if (poisoned) ApplyDot(ref rate, ref time, 4);
        if (onFire) ApplyDot(ref rate, ref time, 8);
        if (cursedInferno) ApplyDot(ref rate, ref time, 24);
        time++;
        float natural = 0f;
        ReadOnlySpan<int> boundaries = [300, 600, 900, 1200, 1500, 1800, 2400, 3000];
        foreach (int boundary in boundaries)
            if (time >= boundary) natural++;
        if (time >= 3600f)
        {
            natural++;
            time = 3600f;
        }
        natural *= moving ? .5f : 1.25f;
        if (expert) natural /= 2f;
        natural *= (float)maximum / 400f * .85f + .15f;
        rate += (int)Math.Round(natural);
        long accumulated = (long)count + rate;
        if (accumulated is < int.MinValue or > int.MaxValue) return null;
        count = (int)accumulated;
        int life = state.Life;
        if (count >= 120)
        {
            int amount = count / 120;
            count %= 120;
            life = (int)Math.Min((long)life + amount, maximum);
        }
        // Remote DoT does not invoke local KillMe; negative source life is intentional.
        if (count <= -120)
        {
            int damage = count / -120;
            count %= 120;
            if ((long)life - damage < int.MinValue) return null;
            life -= damage;
        }
        return state with { Life = Math.Min(life, maximum), RegenCount = count, RegenTime = time };
    }

    private static void ApplyDot(ref int rate, ref float time, int amount)
    {
        rate = Math.Min(rate, 0) - amount;
        time = 0f;
    }
}
