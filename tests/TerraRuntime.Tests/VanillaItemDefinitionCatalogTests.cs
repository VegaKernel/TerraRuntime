using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaItemDefinitionCatalogTests
{
    [Fact]
    public void Dirt_block_exposes_only_verified_placement_defaults()
    {
        Assert.True(VanillaItemDefinitionCatalog.TryGet(
            VanillaItemIds.DirtBlock,
            out VanillaItemDefinition definition));

        Assert.Equal(VanillaItemIds.DirtBlock, definition.Type);
        Assert.Equal(new VanillaItemRuntimeDefaults(12, 12, 9_999), definition.RuntimeDefaults);
        Assert.True(definition.UseTiming.HasValue);
        Assert.True(definition.Placement.HasValue);
        Assert.False(definition.PickTool.HasValue);
        Assert.False(definition.WorldDrop.HasValue);

        VanillaItemPlacementDefinition placement = definition.Placement.Value;
        Assert.Equal(VanillaTileIds.Dirt, placement.TileType);
        Assert.True(placement.Consumable);

        VanillaItemUseTimingDefinition timing = definition.UseTiming.Value;
        Assert.Equal(VanillaItemUseStyle.Swing, timing.Style);
        Assert.Equal(15, timing.AnimationTicks);
        Assert.Equal(10, timing.UseTimeTicks);
        Assert.True(timing.AutoReuse);
        Assert.True(timing.UseTurn);
    }

    [Fact]
    public void Copper_pickaxe_exposes_only_verified_pick_tool_defaults()
    {
        Assert.True(VanillaItemDefinitionCatalog.TryGet(
            VanillaItemIds.CopperPickaxe,
            out VanillaItemDefinition definition));

        Assert.Equal(VanillaItemIds.CopperPickaxe, definition.Type);
        Assert.Equal(new VanillaItemRuntimeDefaults(24, 28, 9_999), definition.RuntimeDefaults);
        Assert.True(definition.UseTiming.HasValue);
        Assert.False(definition.Placement.HasValue);
        Assert.True(definition.PickTool.HasValue);
        Assert.False(definition.WorldDrop.HasValue);

        VanillaItemPickToolDefinition pickTool = definition.PickTool.Value;
        Assert.Equal((short)35, pickTool.PickPower);
        Assert.Equal(-1, pickTool.TileBoost);

        VanillaItemUseTimingDefinition timing = definition.UseTiming.Value;
        Assert.Equal(VanillaItemUseStyle.Swing, timing.Style);
        Assert.Equal(23, timing.AnimationTicks);
        Assert.Equal(15, timing.UseTimeTicks);
        Assert.True(timing.AutoReuse);
        Assert.True(timing.UseTurn);
    }

    [Theory]
    [InlineData(23, 10, 12, VanillaItemPrefixFamily.None)]
    [InlineData(1309, 26, 28, VanillaItemPrefixFamily.Summon)]
    [InlineData(3318, 24, 24, VanillaItemPrefixFamily.None)]
    [InlineData(4797, 16, 30, VanillaItemPrefixFamily.None)]
    [InlineData(4929, 14, 14, VanillaItemPrefixFamily.None)]
    public void Loot_items_expose_source_backed_world_drop_defaults(
        int rawType,
        int width,
        int height,
        VanillaItemPrefixFamily prefixFamily)
    {
        var type = new ItemTypeId(rawType);

        Assert.True(VanillaItemDefinitionCatalog.TryGetWorldDrop(
            type,
            out VanillaItemWorldDropDefinition definition));

        Assert.Equal(width, definition.Width);
        Assert.Equal(height, definition.Height);
        Assert.False(definition.NoGravity);
        Assert.Equal(prefixFamily, definition.PrefixFamily);
        Assert.True(VanillaItemDefinitionCatalog.TryGetRuntimeDefaults(type, out VanillaItemRuntimeDefaults runtime));
        Assert.Equal(width, runtime.Width);
        Assert.Equal(height, runtime.Height);
        Assert.Equal((short)9_999, runtime.MaximumStack);
    }

    [Fact]
    public void Known_stack_maxima_fail_closed_without_guessing_unimported_item_defaults()
    {
        Assert.True(VanillaItemDefinitionCatalog.IsValidKnownStack(VanillaItemIds.DirtBlock, 9_999));
        Assert.False(VanillaItemDefinitionCatalog.IsValidKnownStack(VanillaItemIds.DirtBlock, 10_000));
        Assert.True(VanillaItemDefinitionCatalog.IsValidKnownStack(new ItemTypeId(1), short.MaxValue));
        Assert.False(VanillaItemDefinitionCatalog.IsValidKnownStack(new ItemTypeId(1), 0));
    }

    [Fact]
    public void Slime_staff_exposes_verified_use_timing_without_claiming_an_item_use_capability()
    {
        Assert.True(VanillaItemDefinitionCatalog.TryGetUseTiming(
            VanillaItemIds.SlimeStaff,
            out VanillaItemUseTimingDefinition timing));
        Assert.Equal(VanillaItemUseStyle.Swing, timing.Style);
        Assert.Equal(28, timing.AnimationTicks);
        Assert.Equal(28, timing.UseTimeTicks);
        Assert.True(timing.AutoReuse);
        Assert.False(timing.UseTurn);
    }

    [Fact]
    public void Capability_queries_fail_closed_for_unverified_item_fields()
    {
        Assert.False(VanillaItemDefinitionCatalog.TryGetPickTool(
            VanillaItemIds.DirtBlock,
            out _));
        Assert.False(VanillaItemDefinitionCatalog.TryGetPlacement(
            VanillaItemIds.CopperPickaxe,
            out _));
        Assert.False(VanillaItemDefinitionCatalog.TryGetWorldDrop(
            VanillaItemIds.DirtBlock,
            out _));
        Assert.False(VanillaItemDefinitionCatalog.TryGetUseTiming(
            VanillaItemIds.Gel,
            out _));
        Assert.False(VanillaItemDefinitionCatalog.TryGet(
            new ItemTypeId(1),
            out _));
    }

    [Fact]
    public void Tile_interaction_compatibility_facade_reads_the_definition_catalog()
    {
        Assert.True(VanillaTileInteractionItemFacts.TryGetPlacementTile(
            VanillaItemIds.DirtBlock,
            out TileTypeId tileType,
            out bool consumable));
        Assert.Equal(VanillaTileIds.Dirt, tileType);
        Assert.True(consumable);

        Assert.True(VanillaTileInteractionItemFacts.TryGetPickPower(
            VanillaItemIds.CopperPickaxe,
            out short pickPower,
            out int tileBoost));
        Assert.Equal(VanillaItemDefinitionCatalog.CopperPickaxePickPower, pickPower);
        Assert.Equal(VanillaItemDefinitionCatalog.CopperPickaxeTileBoost, tileBoost);
    }
}
