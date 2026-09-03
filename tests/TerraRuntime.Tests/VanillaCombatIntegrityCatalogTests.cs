using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Tests;

public sealed class VanillaCombatIntegrityCatalogTests
{
    [Fact]
    public void Direct_melee_catalog_is_opt_in_and_source_backed()
    {
        Assert.True(VanillaItemCombatCatalog.TryGetDirectMelee(VanillaItemIds.Muramasa, out VanillaDirectMeleeCombatDefinition muramasa));
        Assert.Equal(24, muramasa.BaseDamage);
        Assert.Equal(18, muramasa.UseTimeTicks);
        Assert.Equal(18, muramasa.AnimationTicks);
        Assert.Equal(VanillaItemCombatCatalog.VanillaBaseMeleeCrit, muramasa.BaseCrit);

        Assert.True(VanillaItemCombatCatalog.TryGetDirectMelee(VanillaItemIds.CopperPickaxe, out VanillaDirectMeleeCombatDefinition pickaxe));
        Assert.Equal((4, 2f, 15, 23), (pickaxe.BaseDamage, pickaxe.BaseKnockBack, pickaxe.UseTimeTicks, pickaxe.AnimationTicks));

        Assert.True(VanillaItemCombatCatalog.TryGetDirectMelee(VanillaItemIds.CopperAxe, out VanillaDirectMeleeCombatDefinition axe));
        Assert.Equal((3, 4.5f, 21, 30), (axe.BaseDamage, axe.BaseKnockBack, axe.UseTimeTicks, axe.AnimationTicks));

        Assert.True(VanillaItemCombatCatalog.TryGetDirectMelee(VanillaItemIds.CopperHammer, out VanillaDirectMeleeCombatDefinition hammer));
        Assert.Equal((4, 5.5f, 23, 33), (hammer.BaseDamage, hammer.BaseKnockBack, hammer.UseTimeTicks, hammer.AnimationTicks));

        Assert.True(VanillaItemCombatCatalog.TryGetDirectMelee(VanillaItemIds.CopperBroadsword, out VanillaDirectMeleeCombatDefinition broadsword));
        Assert.Equal((9, 5.5f, 20, 21), (broadsword.BaseDamage, broadsword.BaseKnockBack, broadsword.UseTimeTicks, broadsword.AnimationTicks));

        Assert.True(VanillaItemCombatCatalog.TryGetPrefixModifiers(new PrefixId(57), out VanillaCombatPrefixModifiers ruthless));
        Assert.Equal(1.18f, ruthless.DamageMultiplier);
        Assert.Equal(0.90f, ruthless.KnockBackMultiplier);

        Assert.False(VanillaItemCombatCatalog.TryGetDirectMelee(new ItemTypeId(1), out _));
        Assert.False(VanillaItemCombatCatalog.TryGetPrefixModifiers(new PrefixId(1), out _));
    }

    [Fact]
    public void Projectile_npc_penetration_catalog_fails_closed_for_unverified_types()
    {
        Assert.True(VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(VanillaProjectileIds.WoodenArrowFriendly, out int arrow));
        Assert.Equal(1, arrow);
        Assert.True(VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(VanillaProjectileIds.Shuriken, out int shuriken));
        Assert.Equal(4, shuriken);
        Assert.False(VanillaProjectileNpcCombatFacts.UsesSharedOwnerNpcImmunity(VanillaProjectileIds.WoodenArrowFriendly));
        Assert.True(VanillaProjectileNpcCombatFacts.UsesSharedOwnerNpcImmunity(VanillaProjectileIds.Shuriken));
        Assert.True(VanillaProjectileNpcCombatFacts.UsesSharedOwnerNpcImmunity(VanillaProjectileIds.EnchantedBoomerang));
        Assert.True(VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(VanillaProjectileIds.JestersArrow, out int jester));
        Assert.Equal(-1, jester);
        Assert.False(VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(VanillaProjectileIds.Seed, out _));
    }
}
