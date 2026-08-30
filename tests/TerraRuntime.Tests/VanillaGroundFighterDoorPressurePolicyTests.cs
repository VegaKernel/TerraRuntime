using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaGroundFighterDoorPressurePolicyTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(21)]
    [InlineData(691)]
    public void Source_restricted_types_reset_without_blood_moon_or_graveyard(int rawType)
    {
        var decision = VanillaGroundFighterDoorPressurePolicy.Resolve(
            new NpcTypeId(rawType),
            bloodMoonActive: false,
            getGoodWorld: false,
            graveyardRollSucceeded: false,
            targetInsideUnbreakableWalls: false);

        Assert.True(decision.ResetProgress);
        Assert.Equal(0, decision.BonusProgress);
        Assert.False(decision.ForceOpen);
    }

    [Fact]
    public void GetGoodWorld_suppresses_blood_moon_for_restricted_types_but_graveyard_still_wins()
    {
        var seedReset = VanillaGroundFighterDoorPressurePolicy.Resolve(
            VanillaNpcIds.Zombie,
            bloodMoonActive: true,
            getGoodWorld: true,
            graveyardRollSucceeded: false,
            targetInsideUnbreakableWalls: false);
        var graveyard = VanillaGroundFighterDoorPressurePolicy.Resolve(
            VanillaNpcIds.Skeleton,
            bloodMoonActive: true,
            getGoodWorld: true,
            graveyardRollSucceeded: true,
            targetInsideUnbreakableWalls: false);

        Assert.True(seedReset.ResetProgress);
        Assert.False(graveyard.ResetProgress);
    }

    [Fact]
    public void Ordinary_blood_moon_or_graveyard_roll_preserves_restricted_progress()
    {
        var bloodMoon = VanillaGroundFighterDoorPressurePolicy.Resolve(
            VanillaNpcIds.Zombie,
            bloodMoonActive: true,
            getGoodWorld: false,
            graveyardRollSucceeded: false,
            targetInsideUnbreakableWalls: false);
        var graveyard = VanillaGroundFighterDoorPressurePolicy.Resolve(
            VanillaNpcIds.Skeleton,
            bloodMoonActive: false,
            getGoodWorld: false,
            graveyardRollSucceeded: true,
            targetInsideUnbreakableWalls: false);

        Assert.False(bloodMoon.ResetProgress);
        Assert.False(graveyard.ResetProgress);
    }

    [Fact]
    public void Inside_unbreakable_walls_disables_reset_and_adds_six()
    {
        var decision = VanillaGroundFighterDoorPressurePolicy.Resolve(
            VanillaNpcIds.Zombie,
            bloodMoonActive: false,
            getGoodWorld: true,
            graveyardRollSucceeded: false,
            targetInsideUnbreakableWalls: true);

        Assert.False(decision.ResetProgress);
        Assert.Equal(6, decision.BonusProgress);
    }

    [Theory]
    [InlineData(27, 1)]
    [InlineData(31, 6)]
    [InlineData(294, 6)]
    [InlineData(295, 6)]
    [InlineData(296, 6)]
    public void Source_special_types_receive_exact_bonus(int rawType, int expectedBonus)
    {
        var decision = VanillaGroundFighterDoorPressurePolicy.Resolve(
            new NpcTypeId(rawType),
            bloodMoonActive: false,
            getGoodWorld: false,
            graveyardRollSucceeded: false,
            targetInsideUnbreakableWalls: false);

        Assert.False(decision.ResetProgress);
        Assert.Equal(expectedBonus, decision.BonusProgress);
    }

    [Fact]
    public void Type460_forces_open_and_type26_surfaces_destroy_branch()
    {
        VanillaGroundFighterDoorPressureDecision force = VanillaGroundFighterDoorPressurePolicy.Resolve(
            new NpcTypeId(460), false, false, false, false);
        VanillaGroundFighterDoorPressureDecision destroy = VanillaGroundFighterDoorPressurePolicy.Resolve(
            new NpcTypeId(26), false, false, false, false);

        Assert.True(force.ForceOpen);
        Assert.False(force.DestroyDoorInsteadOfOpen);
        Assert.True(destroy.DestroyDoorInsteadOfOpen);
        Assert.False(destroy.ForceOpen);
    }
}
