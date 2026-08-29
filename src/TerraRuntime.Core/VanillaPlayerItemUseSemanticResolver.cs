using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

/// <summary>
/// Generation-safe semantic use of a source-verified placeable item. The original detached item-use request is
/// preserved so downstream gameplay never has to recover player identity, inventory slot or prefix from wire state.
/// </summary>
public readonly record struct PlayerItemPlacementUse(
    PlayerItemUseRequest ItemUse,
    TileTypeId TileType,
    bool Consumable)
{
    public bool IsValid =>
        ItemUse.IsValid &&
        VanillaItemDefinitionCatalog.TryGetPlacement(ItemUse.ItemType, out VanillaItemPlacementDefinition placement) &&
        placement.TileType == TileType &&
        placement.Consumable == Consumable;
}

/// <summary>
/// Generation-safe semantic use of a source-verified pick tool. Pick power and tile boost are copied from the
/// immutable item definition at the boundary so downstream tile gameplay does not branch on raw item ids.
/// </summary>
public readonly record struct PlayerItemPickToolUse(
    PlayerItemUseRequest ItemUse,
    short PickPower,
    int TileBoost)
{
    public bool IsValid =>
        ItemUse.IsValid &&
        VanillaItemDefinitionCatalog.TryGetPickTool(ItemUse.ItemType, out VanillaItemPickToolDefinition pickTool) &&
        pickTool.PickPower == PickPower &&
        pickTool.TileBoost == TileBoost;
}

/// <summary>
/// Resolves an already-authoritative selected inventory item into source-backed semantic capabilities. Unsupported
/// or not-yet-verified items fail closed instead of inheriting behavior from ids, slots or guessed defaults.
/// </summary>
public static class VanillaPlayerItemUseSemanticResolver
{
    public static bool TryResolvePlacement(
        in PlayerItemUseRequest itemUse,
        out PlayerItemPlacementUse placementUse)
    {
        if (!itemUse.IsValid ||
            !VanillaItemDefinitionCatalog.TryGetPlacement(
                itemUse.ItemType,
                out VanillaItemPlacementDefinition placement))
        {
            placementUse = default;
            return false;
        }

        placementUse = new PlayerItemPlacementUse(
            itemUse,
            placement.TileType,
            placement.Consumable);
        return true;
    }

    public static bool TryResolvePickTool(
        in PlayerItemUseRequest itemUse,
        out PlayerItemPickToolUse pickToolUse)
    {
        if (!itemUse.IsValid ||
            !VanillaItemDefinitionCatalog.TryGetPickTool(
                itemUse.ItemType,
                out VanillaItemPickToolDefinition pickTool))
        {
            pickToolUse = default;
            return false;
        }

        pickToolUse = new PlayerItemPickToolUse(
            itemUse,
            pickTool.PickPower,
            pickTool.TileBoost);
        return true;
    }
}
