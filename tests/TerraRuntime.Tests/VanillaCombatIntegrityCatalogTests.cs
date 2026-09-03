using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Buffs;
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


    [Fact]
    public void Server_confirmed_combat_buffs_modify_the_same_authoritative_snapshot()
    {
        VanillaPlayerCombatSnapshot snapshot = VanillaPlayerCombatSnapshot.Baseline;
        BuffTypeId[] buffs =
        [
            VanillaBuffIds.Archery,
            VanillaBuffIds.Rage,
            VanillaBuffIds.Wrath,
            VanillaBuffIds.AmmoReservation,
            VanillaBuffIds.WellFed
        ];

        Assert.True(VanillaPlayerCombatBuffCatalog.TryApply(buffs, ref snapshot));
        Assert.True(snapshot.Archery);
        Assert.True(snapshot.AmmoPotion);
        Assert.Equal(1.15f, snapshot.MeleeDamage, 3);
        Assert.Equal(1.15f, snapshot.RangedDamage, 3);
        Assert.Equal(1.15f, snapshot.MagicDamage, 3);
        Assert.Equal(1.1f, snapshot.ArrowDamage, 3);
        Assert.Equal(16, snapshot.MeleeCrit);
        Assert.Equal(16, snapshot.RangedCrit);
        Assert.Equal(16, snapshot.MagicCrit);
        Assert.Equal(1.05f, snapshot.MeleeAttackSpeed, 3);
        Assert.Equal(2, snapshot.Defense);
    }

    [Fact]
    public void Weak_and_hunger_debuffs_reduce_authoritative_attacker_stats()
    {
        VanillaPlayerCombatSnapshot snapshot = VanillaPlayerCombatSnapshot.Baseline;
        Assert.True(VanillaPlayerCombatBuffCatalog.TryApply(VanillaBuffIds.Weak, ref snapshot));
        Assert.True(VanillaPlayerCombatBuffCatalog.TryApply(VanillaBuffIds.Hunger, ref snapshot));

        Assert.Equal(0.899f, snapshot.MeleeDamage, 3);
        Assert.Equal(0.95f, snapshot.RangedDamage, 3);
        Assert.Equal(0.95f, snapshot.MagicDamage, 3);
        Assert.Equal(0.899f, snapshot.MeleeAttackSpeed, 3);
        Assert.Equal(-6, snapshot.Defense);
        Assert.Equal(2, snapshot.RangedCrit);
    }

    [Fact]
    public void Archery_buff_changes_arrow_damage_and_launch_speed_with_vanilla_cap()
    {
        Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetWeapon(VanillaItemIds.CopperBow, out VanillaProjectileWeaponCombatDefinition weapon));
        Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetArrowAmmo(VanillaItemIds.WoodenArrow, out VanillaProjectileAmmoCombatDefinition ammo));
        VanillaCombatPrefixModifiers prefix = VanillaCombatPrefixModifiers.Identity;
        VanillaPlayerCombatSnapshot attacker = VanillaPlayerCombatSnapshot.Baseline;
        Assert.True(VanillaPlayerCombatBuffCatalog.TryApply(VanillaBuffIds.Archery, ref attacker));

        VanillaLaunchSpeedEnvelope envelope = VanillaProjectileWeaponCombatCatalog.ResolveLaunchSpeedEnvelope(
            in weapon, in ammo, in prefix, in attacker);
        Assert.Equal((6.6f + 3f) * 1.2f, envelope.CanonicalMagnitude, 4);
        Assert.Equal(11, VanillaProjectileWeaponCombatCatalog.ResolveDamage(in weapon, in ammo, in prefix, in attacker));
    }

    [Fact]
    public void Expanded_combat_accessories_project_all_supported_class_stats()
    {
        PlayerEquipmentCommitRequest[] equipment =
        [
            Equipment(VanillaPlayerItemSlotCatalog.ArmorStart + 3, VanillaItemIds.PutridScent),
            Equipment(VanillaPlayerItemSlotCatalog.ArmorStart + 4, VanillaItemIds.SniperScope),
            Equipment(VanillaPlayerItemSlotCatalog.ArmorStart + 5, VanillaItemIds.CelestialEmblem)
        ];

        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(equipment, out VanillaPlayerCombatSnapshot snapshot));
        Assert.Equal(1.05f, snapshot.MeleeDamage, 3);
        Assert.Equal(1.15f, snapshot.RangedDamage, 3);
        Assert.Equal(1.20f, snapshot.MagicDamage, 3);
        Assert.Equal(9, snapshot.MeleeCrit);
        Assert.Equal(19, snapshot.RangedCrit);
        Assert.Equal(9, snapshot.MagicCrit);
    }

    [Fact]
    public void Magma_stone_equipment_and_projectile_status_match_supported_source_slice()
    {
        PlayerEquipmentCommitRequest[] gauntlet =
        [
            Equipment(VanillaPlayerItemSlotCatalog.ArmorStart + 3, VanillaItemIds.FireGauntlet)
        ];
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(gauntlet, out VanillaPlayerCombatSnapshot gauntletSnapshot));
        Assert.True(gauntletSnapshot.MagmaStone);

        PlayerEquipmentCommitRequest[] stone =
        [
            Equipment(VanillaPlayerItemSlotCatalog.ArmorStart + 3, VanillaItemIds.MagmaStone)
        ];
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(stone, out VanillaPlayerCombatSnapshot stoneSnapshot));
        Assert.True(stoneSnapshot.MagmaStone);

        Assert.True(VanillaProjectilePvpCombatFacts.IsAdmittedMeleeProjectile(VanillaProjectileIds.EnchantedBoomerang));
        Assert.True(VanillaProjectilePvpCombatFacts.IsAdmittedMeleeProjectile(VanillaProjectileIds.Waffle));
        Assert.True(VanillaProjectilePvpCombatFacts.IsAdmittedMeleeProjectile(VanillaProjectileIds.MeleeBone));
        Assert.False(VanillaProjectilePvpCombatFacts.IsAdmittedMeleeProjectile(VanillaProjectileIds.FireArrow));

        Assert.True(VanillaProjectilePvpCombatFacts.TryRollMagmaStoneStatus(
            VanillaProjectileIds.EnchantedBoomerang, magmaStone: true, new ConstantRandom(0), out VanillaProjectilePvpStatusEffect longFire));
        Assert.Equal(VanillaBuffIds.OnFire, longFire.Buff);
        Assert.Equal(360, longFire.DurationTicks);

        Assert.True(VanillaProjectilePvpCombatFacts.TryRollMagmaStoneStatus(
            VanillaProjectileIds.EnchantedBoomerang, magmaStone: true, new SequenceRandom(1, 0), out VanillaProjectilePvpStatusEffect mediumFire));
        Assert.Equal(240, mediumFire.DurationTicks);

        Assert.True(VanillaProjectilePvpCombatFacts.TryRollMagmaStoneStatus(
            VanillaProjectileIds.EnchantedBoomerang, magmaStone: true, new ConstantRandom(1), out VanillaProjectilePvpStatusEffect shortFire));
        Assert.Equal(120, shortFire.DurationTicks);
    }

    [Fact]
    public void Admitted_projectile_pvp_statuses_match_source_chances_and_durations()
    {
        var proc = new ConstantRandom(0);
        Assert.True(VanillaProjectilePvpCombatFacts.TryRollAdmittedStatus(
            VanillaProjectileIds.FireArrow, proc, out VanillaProjectilePvpStatusEffect fire));
        Assert.True(fire.IsPresent);
        Assert.Equal(VanillaBuffIds.OnFire, fire.Buff);
        Assert.Equal(180, fire.DurationTicks);

        Assert.True(VanillaProjectilePvpCombatFacts.TryRollAdmittedStatus(
            VanillaProjectileIds.PoisonedKnife, proc, out VanillaProjectilePvpStatusEffect poison));
        Assert.True(poison.IsPresent);
        Assert.Equal(VanillaBuffIds.Poisoned, poison.Buff);
        Assert.Equal(600, poison.DurationTicks);

        var miss = new ConstantRandom(1);
        Assert.True(VanillaProjectilePvpCombatFacts.TryRollAdmittedStatus(
            VanillaProjectileIds.FireArrow, miss, out VanillaProjectilePvpStatusEffect missedFire));
        Assert.False(missedFire.IsPresent);
        Assert.True(VanillaProjectilePvpCombatFacts.TryRollAdmittedStatus(
            VanillaProjectileIds.PoisonedKnife, miss, out VanillaProjectilePvpStatusEffect missedPoison));
        Assert.False(missedPoison.IsPresent);
    }

    [Fact]
    public void Projectile_debuff_duration_and_dot_accumulator_match_player_update_rules()
    {
        Assert.Equal(180, VanillaPlayerBuffRuntimeFacts.ResolveDuration(VanillaBuffIds.OnFire, 180, expertMode: false, masterMode: false));
        Assert.Equal(360, VanillaPlayerBuffRuntimeFacts.ResolveDuration(VanillaBuffIds.OnFire, 180, expertMode: true, masterMode: false));
        Assert.Equal(450, VanillaPlayerBuffRuntimeFacts.ResolveDuration(VanillaBuffIds.OnFire, 180, expertMode: true, masterMode: true));

        int fireCount = 0;
        int fireDamage = 0;
        for (int i = 0; i < 15; i++)
        {
            fireCount += VanillaPlayerBuffRuntimeFacts.GetBadLifeRegenDelta(poisoned: false, onFire: true);
            fireDamage += VanillaPlayerBuffRuntimeFacts.ConsumeBadLifeRegenDamage(ref fireCount);
        }
        Assert.Equal(1, fireDamage);
        Assert.Equal(0, fireCount);

        int poisonCount = 0;
        int poisonDamage = 0;
        for (int i = 0; i < 30; i++)
        {
            poisonCount += VanillaPlayerBuffRuntimeFacts.GetBadLifeRegenDelta(poisoned: true, onFire: false);
            poisonDamage += VanillaPlayerBuffRuntimeFacts.ConsumeBadLifeRegenDamage(ref poisonCount);
        }
        Assert.Equal(1, poisonDamage);
        Assert.Equal(0, poisonCount);

        Assert.Equal(-16, VanillaPlayerBuffRuntimeFacts.GetBadLifeRegenDelta(
            poisoned: false, onFire: true, onFire3: true));
        Assert.Equal(450, VanillaPlayerBuffRuntimeFacts.ResolveDuration(
            VanillaBuffIds.OnFire3, 180, expertMode: true, masterMode: true));
    }

    [Fact]
    public void Hardmode_pvp_armor_sets_project_hallowed_and_frost_combat_state()
    {
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(
            [
                Equipment(VanillaPlayerItemSlotCatalog.ArmorStart + 0, new ItemTypeId(559)),
                Equipment(VanillaPlayerItemSlotCatalog.ArmorStart + 1, new ItemTypeId(551)),
                Equipment(VanillaPlayerItemSlotCatalog.ArmorStart + 2, new ItemTypeId(552))
            ],
            out VanillaPlayerCombatSnapshot hallowed));
        Assert.True(hallowed.HallowedOnHitDodge);
        Assert.Equal(50, hallowed.Defense);
        Assert.Equal(1.17f, hallowed.MeleeDamage, 3);
        Assert.Equal(21, hallowed.MeleeCrit);

        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(
            [
                Equipment(VanillaPlayerItemSlotCatalog.ArmorStart + 0, new ItemTypeId(684)),
                Equipment(VanillaPlayerItemSlotCatalog.ArmorStart + 1, new ItemTypeId(685)),
                Equipment(VanillaPlayerItemSlotCatalog.ArmorStart + 2, new ItemTypeId(686))
            ],
            out VanillaPlayerCombatSnapshot frost));
        Assert.True(frost.FrostBurn);
        Assert.Equal(43, frost.Defense);
        Assert.Equal(1.26f, frost.MeleeDamage, 3);
        Assert.Equal(1.26f, frost.RangedDamage, 3);
        Assert.Equal(15, frost.MeleeCrit);
        Assert.Equal(1.1f, frost.MeleeAttackSpeed, 3);
    }

    [Fact]
    public void Weapon_imbues_frost_and_shimmer_project_source_backed_pvp_state()
    {
        VanillaPlayerCombatSnapshot snapshot = VanillaPlayerCombatSnapshot.Baseline;
        Assert.True(VanillaPlayerCombatBuffCatalog.TryApply(VanillaBuffIds.WeaponImbueIchor, ref snapshot));
        Assert.Equal(5, snapshot.MeleeEnchant);
        Assert.True(VanillaPlayerCombatBuffCatalog.TryApply(VanillaBuffIds.Shimmer, ref snapshot));
        Assert.True(snapshot.Shimmering);

        Assert.True(VanillaProjectilePvpCombatFacts.TryRollMeleeEnchantStatus(
            5, new MinimumRandom(), out VanillaProjectilePvpStatusEffect ichor));
        Assert.Equal(VanillaBuffIds.Ichor, ichor.Buff);
        Assert.Equal(600, ichor.DurationTicks);

        Assert.True(VanillaProjectilePvpCombatFacts.TryRollFrostBurnStatus(
            true, new MinimumRandom(), out VanillaProjectilePvpStatusEffect frostburn));
        Assert.Equal(VanillaBuffIds.Frostburn2, frostburn.Buff);
        Assert.Equal(60, frostburn.DurationTicks);

        Assert.True(VanillaProjectilePvpCombatFacts.CanCarryMeleeEnchantStatus(VanillaProjectileIds.EnchantedBoomerang));
        Assert.False(VanillaProjectilePvpCombatFacts.CanCarryMeleeEnchantStatus(VanillaProjectileIds.FireArrow));
        Assert.True(VanillaProjectilePvpCombatFacts.CanCarryFrostBurnStatus(VanillaProjectileIds.FireArrow));
        Assert.False(VanillaProjectilePvpCombatFacts.CanHitPastShimmer(VanillaProjectileIds.FireArrow));
        Assert.True(VanillaProjectilePvpCombatFacts.CanHitPastShimmer(new ProjectileTypeId(719)));
    }

    [Fact]
    public void Expanded_pvp_dot_subset_matches_player_update_life_regen_values()
    {
        Assert.Equal(-30, VanillaPlayerBuffRuntimeFacts.GetBadLifeRegenDelta(
            poisoned: false, onFire: false, venom: true));
        Assert.Equal(-24, VanillaPlayerBuffRuntimeFacts.GetBadLifeRegenDelta(
            poisoned: false, onFire: false, cursedInferno: true));
        Assert.Equal(-16, VanillaPlayerBuffRuntimeFacts.GetBadLifeRegenDelta(
            poisoned: false, onFire: false, frostburn2: true));
        Assert.Equal(-74, VanillaPlayerBuffRuntimeFacts.GetBadLifeRegenDelta(
            poisoned: true, onFire: true, onFire3: true, venom: true, cursedInferno: true, frostburn: false, frostburn2: false));
    }

    private sealed class MinimumRandom : Random
    {
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }

    private sealed class ConstantRandom(int value) : Random
    {
        public override int Next(int maxValue) => Math.Clamp(value, 0, Math.Max(0, maxValue - 1));
    }

    private sealed class SequenceRandom(params int[] values) : Random
    {
        private int index;

        public override int Next(int maxValue)
        {
            int value = values.Length == 0 ? 0 : values[Math.Min(index++, values.Length - 1)];
            return Math.Clamp(value, 0, Math.Max(0, maxValue - 1));
        }
    }

    private static PlayerEquipmentCommitRequest Equipment(short slot, ItemTypeId item, byte prefix = 0) =>
        new(new PlayerSlotId(0), slot, Stack: 1, Prefix: prefix, ItemNetId: checked((short)item.Value), ItemFlags: 0);

}
