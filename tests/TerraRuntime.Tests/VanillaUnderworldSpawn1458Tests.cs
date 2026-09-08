using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class VanillaUnderworldSpawn1458Tests
{
    [Theory]
    [InlineData(1200)]
    [InlineData(1800)]
    [InlineData(2400)]
    public void Underworld_boundary_is_strict_and_not_the_spawn_budget_boundary(int height)
    {
        Assert.False(VanillaUnderworldSpawn1458.IsUnderworld(height - 200, height));
        Assert.False(VanillaUnderworldSpawn1458.IsUnderworld(height - 190, height));
        Assert.True(VanillaUnderworldSpawn1458.IsUnderworld(height - 189, height));
    }

    [Theory]
    [InlineData(39, new[] { 8, 40 }, new[] { 1, 0 })]
    [InlineData(24, new[] { 8, 40, 14 }, new[] { 1, 1, 0 })]
    [InlineData(66, new[] { 8, 40, 14, 7, 10 }, new[] { 1, 1, 1, 0, 0 })]
    [InlineData(62, new[] { 8, 40, 14, 7, 10 }, new[] { 1, 1, 1, 0, 1 })]
    [InlineData(59, new[] { 8, 40, 14, 7, 3 }, new[] { 1, 1, 1, 1, 0 })]
    [InlineData(60, new[] { 8, 40, 14, 7, 3 }, new[] { 1, 1, 1, 1, 1 })]
    public void Prehardmode_preserves_branch_order_and_ranges(int expected, int[] bounds, int[] rolls)
    {
        var random = new ScriptedRandom(bounds, rolls);
        Assert.True(VanillaUnderworldSpawn1458.TrySelect(default, random, out NpcTypeId type));
        Assert.Equal(expected, type.Value);
        random.AssertConsumed();
    }

    [Theory]
    [InlineData(156, new[] { 8, 40, 14, 7, 10, 5 }, new[] { 1, 1, 1, 0, 1, 1 })]
    [InlineData(62, new[] { 8, 40, 14, 7, 10, 5 }, new[] { 1, 1, 1, 0, 1, 0 })]
    [InlineData(151, new[] { 8, 40, 14, 7, 3, 5 }, new[] { 1, 1, 1, 1, 1, 1 })]
    [InlineData(60, new[] { 8, 40, 14, 7, 3, 5 }, new[] { 1, 1, 1, 1, 1, 0 })]
    public void Mechanical_progression_unlocks_red_devil_and_lava_bat(int expected, int[] bounds, int[] rolls)
    {
        var random = new ScriptedRandom(bounds, rolls);
        var facts = new VanillaUnderworldSpawnFacts1458(true, true, true, false, false);
        Assert.True(VanillaUnderworldSpawn1458.TrySelect(in facts, random, out var type));
        Assert.Equal(expected, type.Value);
        random.AssertConsumed();
    }

    [Fact]
    public void Tortured_soul_precedes_bait_and_stops_further_rolls()
    {
        var random = new ScriptedRandom([20], [0]);
        var facts = new VanillaUnderworldSpawnFacts1458(true, false, false, false, false);
        Assert.True(VanillaUnderworldSpawn1458.TrySelect(in facts, random, out var type));
        Assert.Equal(534, type.Value);
        random.AssertConsumed();
    }

    [Fact]
    public void Existing_soul_and_serpent_do_not_skip_their_source_random_draws()
    {
        var random = new ScriptedRandom([20, 8, 40, 14], [0, 1, 0, 0]);
        var facts = new VanillaUnderworldSpawnFacts1458(true, false, false, true, true);
        Assert.True(VanillaUnderworldSpawn1458.TrySelect(in facts, random, out var type));
        Assert.Equal(24, type.Value);
        random.AssertConsumed();
    }

    [Fact]
    public void Unadmitted_multi_actor_bait_branch_does_not_fall_back_to_a_slime()
    {
        var random = new ScriptedRandom([8], [0]);
        Assert.False(VanillaUnderworldSpawn1458.TrySelect(default, random, out var type));
        Assert.Equal(default, type);
        random.AssertConsumed();
    }

    private sealed class ScriptedRandom(int[] bounds, int[] rolls) : IVanillaNpcRandom
    {
        private int index;
        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Assert.True(index < bounds.Length, "Unexpected random draw after selected branch.");
            Assert.Equal(0, inclusiveMin);
            Assert.Equal(bounds[index], exclusiveMax);
            return rolls[index++];
        }
        public void AssertConsumed() => Assert.Equal(bounds.Length, index);
    }
}
