namespace TerraRuntime.Gameplay.Items;

public enum VanillaPvpDodgeKind : byte
{
    None = 0,
    MysticSash = 1,
    BlackBelt = 2,
    BrainOfConfusion = 3,
    ShadowDodge = 4
}

/// <summary>
/// Source-ordered Player.Hurt dodge selection for dodgeable PvP hits in TerrariaServer 1.4.5.8. The caller owns
/// the RNG; this method consumes rolls only when the matching effect is active, preserving the vanilla branch order.
/// </summary>
public static class VanillaPlayerPvpDodge
{
    public const int StandardDodgeImmunityTicks = 80;
    public const int LongInvincibilityDodgeImmunityTicks = 120;
    public const int BrainOfConfusionCooldownTicks = 240;

    public static VanillaPvpDodgeKind Resolve(in VanillaPlayerCombatSnapshot target, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (target.MysticSashDodge && random.Next(10) == 0)
            return VanillaPvpDodgeKind.MysticSash;
        if (target.BlackBeltDodge && random.Next(10) == 0)
            return VanillaPvpDodgeKind.BlackBelt;
        if (target.BrainOfConfusionDodge && !target.BrainOfConfusionCooldown && random.Next(6) == 0)
            return VanillaPvpDodgeKind.BrainOfConfusion;
        if (target.ShadowDodge)
            return VanillaPvpDodgeKind.ShadowDodge;
        return VanillaPvpDodgeKind.None;
    }

    public static int GetPostDodgeImmunityTicks(in VanillaPlayerCombatSnapshot target) =>
        target.LongInvincibility ? LongInvincibilityDodgeImmunityTicks : StandardDodgeImmunityTicks;
}
