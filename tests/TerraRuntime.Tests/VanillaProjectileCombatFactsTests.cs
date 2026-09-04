using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Tests;

public sealed class VanillaProjectileCombatFactsTests
{
    [Theory]
    [InlineData(1, VanillaProjectileDamageClass.Ranged)]
    [InlineData(3, VanillaProjectileDamageClass.Ranged)]
    [InlineData(6, VanillaProjectileDamageClass.Melee)]
    [InlineData(14, VanillaProjectileDamageClass.Ranged)]
    [InlineData(21, VanillaProjectileDamageClass.Ranged)]
    [InlineData(48, VanillaProjectileDamageClass.Ranged)]
    [InlineData(54, VanillaProjectileDamageClass.Ranged)]
    [InlineData(318, VanillaProjectileDamageClass.Ranged)]
    [InlineData(330, VanillaProjectileDamageClass.Ranged)]
    [InlineData(599, VanillaProjectileDamageClass.Ranged)]
    [InlineData(981, VanillaProjectileDamageClass.Ranged)]
    public void Admitted_projectiles_have_source_backed_damage_class(int type, VanillaProjectileDamageClass expected)
    {
        Assert.True(VanillaProjectileCombatFacts.TryGetDamageClass(new ProjectileTypeId(type), out VanillaProjectileDamageClass actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Pve_ranged_hit_uses_ranged_crit_damage_variance_and_generic_armor_penetration()
    {
        VanillaPlayerCombatSnapshot owner = VanillaPlayerCombatSnapshot.Baseline with
        {
            RangedCrit = 25,
            ArmorPenetration = 3,
            MeleeArmorPenetration = 7
        };

        Assert.True(VanillaProjectileCombatFacts.TryResolvePveHit(
            VanillaProjectileIds.Shuriken, 20, in owner, critRollPercent: 25, damageVariationPercent: 15, out VanillaProjectileResolvedHit hit));

        Assert.Equal(23, hit.Damage);
        Assert.True(hit.Critical);
        Assert.Equal(3, hit.ArmorPenetration);
    }

    [Fact]
    public void Pve_melee_projectile_uses_melee_crit_and_melee_armor_penetration()
    {
        VanillaPlayerCombatSnapshot owner = VanillaPlayerCombatSnapshot.Baseline with
        {
            MeleeCrit = 10,
            ArmorPenetration = 2,
            MeleeArmorPenetration = 5
        };

        Assert.True(VanillaProjectileCombatFacts.TryResolvePveHit(
            VanillaProjectileIds.EnchantedBoomerang, 20, in owner, critRollPercent: 10, damageVariationPercent: -15, out VanillaProjectileResolvedHit hit));

        Assert.Equal(17, hit.Damage);
        Assert.True(hit.Critical);
        Assert.Equal(7, hit.ArmorPenetration);
    }

    [Fact]
    public void Pvp_ranged_projectile_does_not_roll_ranged_crit_but_melee_projectile_can_crit()
    {
        VanillaPlayerCombatSnapshot owner = VanillaPlayerCombatSnapshot.Baseline with
        {
            RangedCrit = 100,
            MeleeCrit = 100,
            ArmorPenetration = 99,
            MeleeArmorPenetration = 99
        };

        Assert.True(VanillaProjectileCombatFacts.TryResolvePvpHit(
            VanillaProjectileIds.Shuriken, 20, in owner, meleeCritRollPercent: 1, damageVariationPercent: 0, out VanillaProjectileResolvedHit ranged));
        Assert.False(ranged.Critical);
        Assert.Equal(0, ranged.ArmorPenetration);

        Assert.True(VanillaProjectileCombatFacts.TryResolvePvpHit(
            VanillaProjectileIds.EnchantedBoomerang, 20, in owner, meleeCritRollPercent: 100, damageVariationPercent: 0, out VanillaProjectileResolvedHit melee));
        Assert.True(melee.Critical);
        Assert.Equal(0, melee.ArmorPenetration);
    }
}
