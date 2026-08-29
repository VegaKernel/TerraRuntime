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
        Assert.True(definition.Placement.HasValue);
        Assert.False(definition.PickTool.HasValue);

        VanillaItemPlacementDefinition placement = definition.Placement.Value;
        Assert.Equal(VanillaTileIds.Dirt, placement.TileType);
        Assert.True(placement.Consumable);
    }

    [Fact]
    public void Copper_pickaxe_exposes_only_verified_pick_tool_defaults()
    {
        Assert.True(VanillaItemDefinitionCatalog.TryGet(
            VanillaItemIds.CopperPickaxe,
            out VanillaItemDefinition definition));

        Assert.Equal(VanillaItemIds.CopperPickaxe, definition.Type);
        Assert.False(definition.Placement.HasValue);
        Assert.True(definition.PickTool.HasValue);

        VanillaItemPickToolDefinition pickTool = definition.PickTool.Value;
        Assert.Equal((short)35, pickTool.PickPower);
        Assert.Equal(-1, pickTool.TileBoost);
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
