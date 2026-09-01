using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

public sealed class VanillaPlayerItemUseSemanticResolverTests
{
    [Fact]
    public void Source_backed_overstack_cannot_cross_the_item_use_boundary()
    {
        PlayerItemUseRequest itemUse = Request(
            VanillaItemIds.DirtBlock,
            stack: 10_000,
            inventorySlot: 0,
            generation: 1);

        Assert.False(itemUse.IsValid);
        Assert.False(VanillaPlayerItemUseSemanticResolver.TryResolvePlacement(in itemUse, out _));
    }

    [Fact]
    public void Dirt_block_resolves_to_generation_safe_placement_use()
    {
        PlayerItemUseRequest itemUse = Request(
            VanillaItemIds.DirtBlock,
            stack: 17,
            inventorySlot: 6,
            generation: 3);

        Assert.True(VanillaPlayerItemUseSemanticResolver.TryResolvePlacement(
            in itemUse,
            out PlayerItemPlacementUse placement));

        Assert.True(placement.IsValid);
        Assert.Equal(itemUse, placement.ItemUse);
        Assert.Equal(itemUse.Player, placement.ItemUse.Player);
        Assert.Equal(VanillaTileIds.Dirt, placement.TileType);
        Assert.True(placement.Consumable);
        Assert.Equal(VanillaItemUseStyle.Swing, placement.Timing.Style);
        Assert.Equal(15, placement.Timing.AnimationTicks);
        Assert.Equal(10, placement.Timing.UseTimeTicks);
        Assert.True(placement.Timing.AutoReuse);
        Assert.True(placement.Timing.UseTurn);
        Assert.False(VanillaPlayerItemUseSemanticResolver.TryResolvePickTool(in itemUse, out _));
    }

    [Fact]
    public void Copper_pickaxe_resolves_to_generation_safe_pick_tool_use()
    {
        PlayerItemUseRequest itemUse = Request(
            VanillaItemIds.CopperPickaxe,
            stack: 1,
            inventorySlot: 2,
            generation: 7);

        Assert.True(VanillaPlayerItemUseSemanticResolver.TryResolvePickTool(
            in itemUse,
            out PlayerItemPickToolUse pickTool));

        Assert.True(pickTool.IsValid);
        Assert.Equal(itemUse, pickTool.ItemUse);
        Assert.Equal((short)35, pickTool.PickPower);
        Assert.Equal(-1, pickTool.TileBoost);
        Assert.Equal(VanillaItemUseStyle.Swing, pickTool.Timing.Style);
        Assert.Equal(23, pickTool.Timing.AnimationTicks);
        Assert.Equal(15, pickTool.Timing.UseTimeTicks);
        Assert.True(pickTool.Timing.AutoReuse);
        Assert.True(pickTool.Timing.UseTurn);
        Assert.False(VanillaPlayerItemUseSemanticResolver.TryResolvePlacement(in itemUse, out _));
    }

    [Fact]
    public void Unverified_item_does_not_inherit_semantics_from_its_numeric_id()
    {
        PlayerItemUseRequest itemUse = Request(
            new ItemTypeId(1),
            stack: 1,
            inventorySlot: 0,
            generation: 1);

        Assert.True(itemUse.IsValid);
        Assert.False(VanillaPlayerItemUseSemanticResolver.TryResolvePlacement(in itemUse, out _));
        Assert.False(VanillaPlayerItemUseSemanticResolver.TryResolvePickTool(in itemUse, out _));
    }

    [Fact]
    public void Invalid_item_use_is_rejected_before_definition_lookup()
    {
        PlayerItemUseRequest invalid = Request(
            VanillaItemIds.DirtBlock,
            stack: 0,
            inventorySlot: 0,
            generation: 1);

        Assert.False(invalid.IsValid);
        Assert.False(VanillaPlayerItemUseSemanticResolver.TryResolvePlacement(in invalid, out _));
        Assert.False(VanillaPlayerItemUseSemanticResolver.TryResolvePickTool(in invalid, out _));
    }

    [Fact]
    public void Resolved_use_keeps_exact_player_generation_instead_of_only_the_slot()
    {
        PlayerItemUseRequest first = Request(
            VanillaItemIds.DirtBlock,
            stack: 1,
            inventorySlot: 0,
            generation: 11);
        PlayerItemUseRequest replacement = Request(
            VanillaItemIds.DirtBlock,
            stack: 1,
            inventorySlot: 0,
            generation: 12);

        Assert.True(VanillaPlayerItemUseSemanticResolver.TryResolvePlacement(in first, out PlayerItemPlacementUse firstUse));
        Assert.True(VanillaPlayerItemUseSemanticResolver.TryResolvePlacement(in replacement, out PlayerItemPlacementUse replacementUse));

        Assert.NotEqual(firstUse.ItemUse.Player, replacementUse.ItemUse.Player);
        Assert.Equal(firstUse.ItemUse.Player.Slot, replacementUse.ItemUse.Player.Slot);
    }

    private static PlayerItemUseRequest Request(
        ItemTypeId itemType,
        short stack,
        short inventorySlot,
        ulong generation) =>
        new(
            new ConnectionHandle(
                GameCommandSourceId.FromConnection(9000 + checked((long)generation)),
                new PlayerHandle(
                    new PlayerSlotId(4),
                    new PlayerSessionGeneration(generation))),
            inventorySlot,
            itemType,
            stack,
            Prefix: default,
            ItemFlags: 0);
}
