using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
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
    [Fact]
    public void Equipment_snapshot_applies_supported_functional_slots_and_ignores_vanity()
    {
        PlayerEquipmentCommitRequest[] equipment =
        [
            Equipment(VanillaPlayerItemSlotCatalog.ArmorStart + 0, VanillaItemIds.CopperHelmet),
            Equipment(VanillaPlayerItemSlotCatalog.ArmorStart + 1, VanillaItemIds.CopperChainmail),
            Equipment(VanillaPlayerItemSlotCatalog.ArmorStart + 2, VanillaItemIds.CopperGreaves),
            Equipment(VanillaPlayerItemSlotCatalog.ArmorStart + 3, VanillaItemIds.WarriorEmblem, prefix: 72),
            Equipment(VanillaPlayerItemSlotCatalog.VanityArmorStart, new ItemTypeId(999))
        ];

        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(equipment, out VanillaPlayerCombatSnapshot snapshot));
        Assert.Equal(6, snapshot.Defense);
        Assert.Equal(1.19f, snapshot.MeleeDamage, 3);
        Assert.Equal(1.04f, snapshot.RangedDamage, 3);
        Assert.Equal(4, snapshot.MeleeCrit);
    }

    [Fact]
    public void Equipment_snapshot_fails_closed_for_unknown_active_accessory_and_locked_extra_slot()
    {
        Assert.False(VanillaPlayerCombatEquipmentCatalog.TryBuild(
            [Equipment(VanillaPlayerItemSlotCatalog.ArmorStart + 3, new ItemTypeId(999))], out _));
        Assert.False(VanillaPlayerCombatEquipmentCatalog.TryBuild(
            [Equipment(VanillaPlayerItemSlotCatalog.ArmorStart + 8, VanillaItemIds.WarriorEmblem)], out _));
    }

    [Fact]
    public void Bow_launch_speed_is_a_source_calculated_magnitude_envelope()
    {
        Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetWeapon(VanillaItemIds.CopperBow, out VanillaProjectileWeaponCombatDefinition weapon));
        Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetArrowAmmo(VanillaItemIds.UnholyArrow, out VanillaProjectileAmmoCombatDefinition ammo));
        Assert.True(VanillaItemCombatCatalog.TryGetRangedPrefixModifiers(new PrefixId(18), out VanillaCombatPrefixModifiers rapid));
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(
            [Equipment(VanillaPlayerItemSlotCatalog.ArmorStart + 3, VanillaItemIds.MagicQuiver)], out VanillaPlayerCombatSnapshot attacker));

        VanillaLaunchSpeedEnvelope envelope = VanillaProjectileWeaponCombatCatalog.ResolveLaunchSpeedEnvelope(
            in weapon, in ammo, in rapid, in attacker);
        float expected = (6.6f * 1.15f + 3.4f) * 1.1f;
        Assert.Equal(expected, envelope.MinLaunchSpeed, 4);
        Assert.Equal(expected, envelope.MaxLaunchSpeed, 4);
        Assert.True(envelope.ContainsMagnitude(expected));
        Assert.False(envelope.ContainsMagnitude(expected + 0.05f));
    }

    [Fact]
    public void Pvp_target_mitigation_is_equipment_and_difficulty_aware()
    {
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(
            [
                Equipment(VanillaPlayerItemSlotCatalog.ArmorStart + 0, VanillaItemIds.CopperHelmet),
                Equipment(VanillaPlayerItemSlotCatalog.ArmorStart + 1, VanillaItemIds.CopperChainmail),
                Equipment(VanillaPlayerItemSlotCatalog.ArmorStart + 2, VanillaItemIds.CopperGreaves)
            ], out VanillaPlayerCombatSnapshot target));
        var player = new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1));
        var attack = new AuthoritativeAttackDamage(DamageSource.FromPlayerItem(player), 20, 0, false, 4.5f, 1);

        Assert.True(VanillaCombatDamagePipeline.TryResolvePvp(in attack, in target, false, out FinalDamageToHp classic));
        Assert.Equal(17, classic.Damage);
        Assert.True(VanillaCombatDamagePipeline.TryResolvePvp(in attack, in target, false, out FinalDamageToHp expert, expertMode: true));
        Assert.Equal(15, expert.Damage);
        Assert.True(VanillaCombatDamagePipeline.TryResolvePvp(in attack, in target, false, out FinalDamageToHp master, expertMode: true, masterMode: true));
        Assert.Equal(14, master.Damage);
    }

    private static PlayerEquipmentCommitRequest Equipment(short slot, ItemTypeId item, byte prefix = 0) =>
        new(new PlayerSlotId(0), slot, Stack: 1, Prefix: prefix, ItemNetId: checked((short)item.Value), ItemFlags: 0);

}
