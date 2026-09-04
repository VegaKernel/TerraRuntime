using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Tests;

public sealed class VanillaIncomingPlayerDamageFacts1458Tests
{
    [Fact]
    public void Hostile_projectile_damage_applies_vanilla_variation_then_x2()
    {
        Assert.Equal(170, VanillaIncomingPlayerDamageFacts1458.ResolveHostileProjectileDamage(100, -15));
        Assert.Equal(200, VanillaIncomingPlayerDamageFacts1458.ResolveHostileProjectileDamage(100, 0));
        Assert.Equal(230, VanillaIncomingPlayerDamageFacts1458.ResolveHostileProjectileDamage(100, 15));
    }

    [Fact]
    public void Phantasmal_bolt_uses_boss_no_cheese_immunity_channel()
    {
        Assert.Equal(
            VanillaPlayerImmunityChannel1458.BossNoCheese,
            VanillaIncomingPlayerDamageFacts1458.GetHostileProjectileImmunityChannel(new ProjectileTypeId(462)));
    }

    [Theory]
    [InlineData(1, 20)]
    [InlineData(2, 40)]
    [InlineData(250, 40)]
    public void Pve_immunity_ticks_match_Player_Hurt_baseline(int damage, int expected)
    {
        Assert.Equal(expected, VanillaIncomingPlayerDamageFacts1458.ResolvePveImmunityTicks(damage));
    }
}
