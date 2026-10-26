namespace TerraRuntime.Gameplay.Players;

public enum ToolSelfHurtKind1458 : byte
{
    Altar,
    Cactus
}

public readonly record struct ToolSelfHurtRequest1458(int Damage, int HitDirection,
    VanillaPlayerImmunityChannel1458 ImmunityChannel, int SourceOtherReason);

/// <summary>Qualified Player tool self-hurt inputs, before Player.Hurt mitigation and effects.</summary>
public static class VanillaToolSelfHurt1458
{
    // Call only after the source tool rule selects an altar refusal or a cactus
    // hit in the DontStarve seed. This does not infer those world permissions.
    public static bool TryPrepare(ToolSelfHurtKind1458 kind, int life, float luck, int direction,
        Func<int, int, int> nextInteger, Func<double> nextDouble,
        out ToolSelfHurtRequest1458 request)
    {
        request = default;
        if (life is < 1 or > short.MaxValue || !float.IsFinite(luck) ||
            direction is not (-1 or 1)) return false;
        if (kind == ToolSelfHurtKind1458.Altar)
        {
            // Preserve zero at life1: Hurt subsequently applies its minimum
            // damage, so this is not a reason to skip the source Hurt call.
            request = new(life / 2, -direction, VanillaPlayerImmunityChannel1458.General, 4);
            return true;
        }
        if (kind != ToolSelfHurtKind1458.Cactus || nextInteger is null || nextDouble is null) return false;

        // Main.DamageVar(6,-luck), evaluated before Hurt's immunity/god-mode
        // gates. The conditional sample is Utils.NextFloat, not a double
        // comparison; near a luck threshold that conversion is observable.
        int first = nextInteger(-15, 16);
        if (first is < -15 or > 15) return false;
        float varied = VaryCactus(first);
        float effectiveLuck = -luck;
        if (effectiveLuck != 0f)
        {
            double sample = nextDouble();
            if (!double.IsFinite(sample) || sample < 0d || sample >= 1d) return false;
            if ((float)sample < MathF.Abs(effectiveLuck))
            {
                int second = nextInteger(-15, 16);
                if (second is < -15 or > 15) return false;
                float alternative = VaryCactus(second);
                varied = effectiveLuck > 0f ? MathF.Max(varied, alternative) : MathF.Min(varied, alternative);
            }
        }
        request = new((int)Math.Round(varied, MidpointRounding.ToEven), 0,
            VanillaPlayerImmunityChannel1458.TileContact, 3);
        return true;
    }

    private static float VaryCactus(int percent) => 6f * (1f + percent * .01f);
}
