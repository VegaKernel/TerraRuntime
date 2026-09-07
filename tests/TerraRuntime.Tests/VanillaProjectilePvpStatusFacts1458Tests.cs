using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Buffs;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Tests;

public sealed class VanillaProjectilePvpStatusFacts1458Tests
{
    [Fact]
    public void Admitted_type_specific_StatusPvP_rules_match_1458()
    {
        AssertRule(VanillaProjectileIds.FireArrow, VanillaBuffIds.OnFire, 180, 3);
        AssertRule(VanillaProjectileIds.Flamelash, VanillaBuffIds.OnFire, 240, 2);
        AssertRule(VanillaProjectileIds.PoisonedKnife, VanillaBuffIds.Poisoned, 600, 2);

        Assert.False(VanillaProjectilePvpStatusFacts1458.TryGetTypeSpecificRule(
            VanillaProjectileIds.WoodenArrowFriendly,
            out _));
        Assert.False(VanillaProjectilePvpStatusFacts1458.TryGetTypeSpecificRule(
            VanillaProjectileIds.RainbowRodBullet,
            out _));
    }

    [Fact]
    public void Main_pvpBuff_source_table_contains_supported_debuffs_and_rejects_normal_buffs()
    {
        Assert.True(VanillaPvpBuffFacts1458.IsRelayable(VanillaBuffIds.Poisoned));
        Assert.True(VanillaPvpBuffFacts1458.IsRelayable(VanillaBuffIds.OnFire));
        Assert.True(VanillaPvpBuffFacts1458.IsRelayable(VanillaBuffIds.OnFire3));
        Assert.False(VanillaPvpBuffFacts1458.IsRelayable(VanillaBuffIds.Regeneration));
        Assert.False(VanillaPvpBuffFacts1458.IsRelayable(VanillaBuffIds.None));
    }

    private static void AssertRule(
        ProjectileTypeId projectile,
        BuffTypeId buff,
        int duration,
        int chanceDenominator)
    {
        Assert.True(VanillaProjectilePvpStatusFacts1458.TryGetTypeSpecificRule(projectile, out var rule));
        Assert.True(rule.IsValid);
        Assert.Equal(buff, rule.BuffType);
        Assert.Equal(duration, rule.DurationTicks);
        Assert.Equal(chanceDenominator, rule.ChanceDenominator);
    }
}
