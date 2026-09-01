using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class VanillaNpcLootWorldItemMaterializerTests
{
    [Fact]
    public void Summon_prefix_catalog_matches_pinned_family_and_slime_staff_validity()
    {
        ReadOnlySpan<PrefixId> prefixes =
            VanillaItemPrefixCatalog.GetRollablePrefixes(VanillaItemPrefixFamily.Summon);

        Assert.Equal(
            new[] { 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 55, 38, 54, 53, 57, 40, 56, 41, 39 },
            prefixes.ToArray().Select(static prefix => prefix.Value));
        Assert.True(VanillaItemPrefixCatalog.HasReducedNaturalChance(new PrefixId(39)));
        Assert.True(VanillaItemPrefixCatalog.HasReducedNaturalChance(new PrefixId(40)));
        Assert.True(VanillaItemPrefixCatalog.HasReducedNaturalChance(new PrefixId(41)));
        Assert.True(VanillaItemPrefixCatalog.HasReducedNaturalChance(new PrefixId(56)));
        Assert.False(VanillaItemPrefixCatalog.HasReducedNaturalChance(new PrefixId(85)));

        Assert.False(VanillaItemPrefixCatalog.IsValidForItem(VanillaItemIds.SlimeStaff, new PrefixId(55)));
        Assert.False(VanillaItemPrefixCatalog.IsValidForItem(VanillaItemIds.SlimeStaff, new PrefixId(89)));
        Assert.False(VanillaItemPrefixCatalog.IsValidForItem(VanillaItemIds.SlimeStaff, new PrefixId(91)));
        Assert.True(VanillaItemPrefixCatalog.IsValidForItem(VanillaItemIds.SlimeStaff, new PrefixId(85)));
        Assert.True(VanillaItemPrefixCatalog.IsValidForItem(VanillaItemIds.SlimeStaff, new PrefixId(40)));
    }

    [Fact]
    public void Gel_materialization_skips_prefix_rng_and_uses_gravity_velocity()
    {
        var random = new ScriptedRollSource(5, -20);
        var origin = new NpcLootWorldItemOrigin(22f, 29f);
        var drop = new NpcLootDrop(VanillaItemIds.Gel, 2);

        Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(
            in origin,
            in drop,
            random,
            out WorldItemDropStateUpdate item));

        Assert.Equal(17f, item.PositionX);
        Assert.Equal(23f, item.PositionY);
        Assert.Equal(0.5f, item.VelocityX);
        Assert.Equal(-2f, item.VelocityY);
        Assert.Equal((short)2, item.Stack);
        Assert.Equal((byte)0, item.Prefix);
        Assert.Equal((short)VanillaItemIds.Gel.Value, item.ItemNetId);
        Assert.Equal(WorldItemOwnershipMode.None, item.Ownership);
        Assert.Equal(new[] { "rng:-30:31", "rng:-40:-15" }, random.Calls);
    }

    [Fact]
    public void Slime_staff_natural_prefix_can_take_initial_no_prefix_branch()
    {
        var random = new ScriptedRollSource(0, 3, -20);
        var origin = new NpcLootWorldItemOrigin(22f, 29f);
        var drop = new NpcLootDrop(VanillaItemIds.SlimeStaff, 1);

        Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(
            in origin,
            in drop,
            random,
            out WorldItemDropStateUpdate item));

        Assert.Equal(9f, item.PositionX);
        Assert.Equal(15f, item.PositionY);
        Assert.Equal((byte)0, item.Prefix);
        Assert.Equal(new[] { "rng:0:4", "rng:-30:31", "rng:-40:-15" }, random.Calls);
    }

    [Fact]
    public void Slime_staff_natural_prefix_selects_normal_summon_prefix_before_velocity()
    {
        var random = new ScriptedRollSource(1, 0, 2, -20);
        var origin = new NpcLootWorldItemOrigin(22f, 29f);
        var drop = new NpcLootDrop(VanillaItemIds.SlimeStaff, 1);

        Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(
            in origin,
            in drop,
            random,
            out WorldItemDropStateUpdate item));

        Assert.Equal((byte)85, item.Prefix);
        Assert.Equal(
            new[] { "rng:0:4", "rng:0:22", "rng:-30:31", "rng:-40:-15" },
            random.Calls);
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(0, 40)]
    public void Reduced_natural_chance_can_clear_or_keep_selected_prefix(int reductionRoll, int expectedPrefix)
    {
        var random = new ScriptedRollSource(1, 18, reductionRoll, 2, -20);
        var origin = new NpcLootWorldItemOrigin(22f, 29f);
        var drop = new NpcLootDrop(VanillaItemIds.SlimeStaff, 1);

        Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(
            in origin,
            in drop,
            random,
            out WorldItemDropStateUpdate item));

        Assert.Equal((byte)expectedPrefix, item.Prefix);
        Assert.Equal(
            new[] { "rng:0:4", "rng:0:22", "rng:0:3", "rng:-30:31", "rng:-40:-15" },
            random.Calls);
    }

    [Fact]
    public void Slime_staff_rerolls_prefix_that_rounds_back_to_unmodified_damage()
    {
        var random = new ScriptedRollSource(1, 4, 1, 0, 2, -20);
        var origin = new NpcLootWorldItemOrigin(22f, 29f);
        var drop = new NpcLootDrop(VanillaItemIds.SlimeStaff, 1);

        Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(
            in origin,
            in drop,
            random,
            out WorldItemDropStateUpdate item));

        Assert.Equal((byte)85, item.Prefix);
        Assert.Equal(
            new[]
            {
                "rng:0:4", "rng:0:22",
                "rng:0:4", "rng:0:22",
                "rng:-30:31", "rng:-40:-15"
            },
            random.Calls);
    }

    [Fact]
    public void Unsupported_item_fails_closed_without_rng()
    {
        var random = new ScriptedRollSource();
        var origin = new NpcLootWorldItemOrigin(0f, 0f);
        var drop = new NpcLootDrop(VanillaItemIds.DirtBlock, 1);

        Assert.False(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(
            in origin,
            in drop,
            random,
            out _));
        Assert.Empty(random.Calls);
    }

    private sealed class ScriptedRollSource(params int[] results) : INpcLootRollSource
    {
        private readonly Queue<int> _results = new(results);

        public List<string> Calls { get; } = [];

        public int RollLuck(int chanceDenominator) =>
            throw new InvalidOperationException("Materializer must not perform loot luck rolls.");

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Calls.Add($"rng:{inclusiveMin}:{exclusiveMax}");
            int value = _results.Dequeue();
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            return value;
        }
    }
}
