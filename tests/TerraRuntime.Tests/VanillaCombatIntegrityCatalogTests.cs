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
        Assert.True(VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(VanillaProjectileIds.JestersArrow, out int jester));
        Assert.Equal(-1, jester);
        Assert.False(VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(VanillaProjectileIds.Seed, out _));
    }
}
