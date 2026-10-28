using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Tests;

public sealed class ScalarDebuffRules1458Tests
{
    [Fact]
    public void Weak_can_carry_negative_defense_through_gear_but_default_final_snapshot_admission_stays_closed()
    {
        var baseline = VanillaPlayerCombatSnapshot.Baseline;
        Assert.True(VanillaPlayerCombatBuffs1458.TryApplyBeforeEquipment(in baseline, [new BuffTypeId(33)], out var weak));
        Assert.Equal(-4, weak.Defense);
        Assert.False(VanillaPlayerCombatBuffs1458.TryApply(in weak, [], out _));
        PlayerEquipmentCommitRequest[] gear = [new(default, 59, 1, default, 89, 0)];
        var context = new VanillaPlayerCombatEquipmentContext(false, false, false)
            { SourceBuffDefenseBeforeEquipment = true };
        Assert.True(VanillaPlayerCombatEquipmentCatalog.TryBuild(gear, in context, in weak, out var equipped, out _));
        Assert.Equal(-3, equipped.Defense);
        Assert.Equal(.949f, equipped.MeleeAttackSpeed);
    }

    [Fact]
    public void Bleeding_resets_the_natural_timer_after_Aegis_increment_without_canceling_positive_regeneration()
    {
        var result = VanillaRemotePlayerHealth1458.Step(new(80, 118, 3599f, true), 100,
            false, false, false, true, false, false, 1, false, false, false, true, false, true);
        Assert.NotNull(result);
        Assert.Equal(81, result.Value.Life);
        Assert.Equal(2, result.Value.RegenCount);
        Assert.Equal(0f, result.Value.RegenTime);
    }

    [Fact]
    public void Source_Weak_reciprocal_can_lengthen_the_actual_sword_animation()
    {
        // Independent V8 Player.Update: Weak + Wooden Sword, fresh local start.
        var previous = new PlayerSelectedConsumableState1458(0, 0, 0, 0, true, 0, 0);
        var facts = new PlayerRemoteMeleeItemFacts1458(new ItemTypeId(24), true, true, false, false, false);
        float reciprocal = BitConverter.UInt32BitsToSingle(0x3f86e0fa);
        Assert.True(VanillaRemoteMeleeItemCheck1458.TryStep(previous, in facts, static (_, _) => 2,
            out var result, reciprocal));
        Assert.Equal(21, result.State.AnimationMax);
        Assert.Equal(20, result.State.Animation);
        Assert.False(VanillaRemoteMeleeItemCheck1458.TryStep(previous, in facts, static (_, _) => 2,
            out _, float.MaxValue));
    }
}
