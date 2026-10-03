using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class BroadswordCombat1458Tests
{
    // Independent original Item.SetDefaults scan, not values obtained from our combat catalog.
    // .cache/direct-melee-next-probe/facts.json SHA256:
    // 818d5253896eb8b3026ee86663fb6f4f896cadf907ef8626a807d152813d2550.
    // Item.crit is zero for these rows; Player.ResetEffects supplies the ordinary four percent.
    public static TheoryData<int, int, float, int, int, int> OriginalSwords => new()
    {
        { 4, 12, 5.5f, 4, 20, 20 },
        { 24, 7, 5f, 4, 20, 20 },
        { 482, 61, 6f, 4, 21, 21 },
        { 483, 40, 5f, 4, 19, 19 },
        { 484, 50, 6f, 4, 20, 20 },
        { 653, 11, 6f, 4, 19, 19 },
        { 656, 8, 6f, 4, 19, 19 },
        { 659, 30, 7f, 4, 15, 15 },
        { 921, 11, 6f, 4, 19, 19 },
        { 1185, 49, 5.5f, 4, 22, 22 },
        { 1192, 59, 6f, 4, 22, 22 },
        { 1199, 61, 6f, 4, 20, 20 },
        { 2517, 8, 6f, 4, 19, 19 },
        { 2745, 8, 6f, 4, 20, 20 },
        { 3484, 16, 6.5f, 4, 17, 17 },
        { 3490, 14, 6f, 4, 19, 19 },
        { 3496, 13, 5.5f, 4, 20, 20 },
        { 3502, 10, 5.5f, 4, 20, 20 },
        { 3508, 9, 5.5f, 4, 20, 21 },
        { 3514, 14, 6f, 4, 20, 20 },
        { 3520, 15, 6.5f, 4, 18, 18 },
    };

    [Theory]
    [MemberData(nameof(OriginalSwords))]
    public void Invariant_wood_and_metal_broadswords_match_original_defaults(
        int id, int damage, float knockBack, int crit, int useTime, int animation)
    {
        Assert.True(VanillaItemCombatCatalog.TryGetDirectMelee(new ItemTypeId(id), out var sword));
        Assert.Equal(damage, sword.BaseDamage);
        Assert.Equal(knockBack, sword.BaseKnockBack);
        Assert.Equal(crit, sword.BaseCrit);
        Assert.Equal(useTime, sword.UseTimeTicks);
        Assert.Equal(animation, sword.AnimationTicks);
    }

    [Theory]
    [MemberData(nameof(OriginalSwords))]
    public void Owned_npc_strikes_reject_forged_claims_and_enforce_cadence_without_consuming_sword(
        int id, int damage, float knockBack, int crit, int useTime, int animation)
        => MiningToolCombat1458Tests.AssertOwnedNpcHit(id, damage, knockBack, crit, useTime, animation);

    [Theory]
    [MemberData(nameof(OriginalSwords))]
    public void Owned_pvp_strikes_use_same_sword_facts_and_reject_duplicate_hits(
        int id, int damage, float knockBack, int crit, int useTime, int animation)
        => MiningToolCombat1458Tests.AssertOwnedPvpHit(id, damage, knockBack, crit, useTime, animation);

    [Fact]
    public void Ash_wood_sword_stays_unadmitted_until_world_variant_is_owned()
        => Assert.False(VanillaItemCombatCatalog.TryGetDirectMelee(new ItemTypeId(5284), out _));
}
