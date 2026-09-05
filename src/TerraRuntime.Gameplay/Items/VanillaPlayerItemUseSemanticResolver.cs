using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>
/// Generation-safe semantic use of a source-verified placeable item. The original detached item-use request is
/// preserved so downstream gameplay never has to recover player identity, inventory slot or prefix from wire state.
/// </summary>
public readonly record struct PlayerItemPlacementUse(
    PlayerItemUseRequest ItemUse,
    TileTypeId TileType,
    bool Consumable,
    VanillaItemUseTimingDefinition Timing)
{
    public bool IsValid =>
        ItemUse.IsValid &&
        VanillaDefinitionCatalog.TryGetPlacement(ItemUse.ItemType, out VanillaItemPlacementDefinition placement) &&
        VanillaDefinitionCatalog.TryGetUseTiming(ItemUse.ItemType, out VanillaItemUseTimingDefinition timing) &&
        placement.TileType == TileType &&
        placement.Consumable == Consumable &&
        timing == Timing;
}

/// <summary>
/// Generation-safe semantic use of a source-verified pick tool. Pick power and tile boost are copied from the
/// immutable item definition at the boundary so downstream tile gameplay does not branch on raw item ids.
/// </summary>
public readonly record struct PlayerItemPickToolUse(
    PlayerItemUseRequest ItemUse,
    short PickPower,
    int TileBoost,
    VanillaItemUseTimingDefinition Timing)
{
    public bool IsValid =>
        ItemUse.IsValid &&
        VanillaDefinitionCatalog.TryGetPickTool(ItemUse.ItemType, out VanillaItemPickToolDefinition pickTool) &&
        VanillaDefinitionCatalog.TryGetUseTiming(ItemUse.ItemType, out VanillaItemUseTimingDefinition timing) &&
        pickTool.PickPower == PickPower &&
        pickTool.TileBoost == TileBoost &&
        timing == Timing;
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
            !VanillaDefinitionCatalog.TryGetPlacement(
                itemUse.ItemType,
                out VanillaItemPlacementDefinition placement) ||
            !VanillaDefinitionCatalog.TryGetUseTiming(
                itemUse.ItemType,
                out VanillaItemUseTimingDefinition timing))
        {
            placementUse = default;
            return false;
        }

        placementUse = new PlayerItemPlacementUse(
            itemUse,
            placement.TileType,
            placement.Consumable,
            timing);
        return true;
    }

    public static bool TryResolvePickTool(
        in PlayerItemUseRequest itemUse,
        out PlayerItemPickToolUse pickToolUse)
    {
        if (!itemUse.IsValid ||
            !VanillaDefinitionCatalog.TryGetPickTool(
                itemUse.ItemType,
                out VanillaItemPickToolDefinition pickTool) ||
            !VanillaDefinitionCatalog.TryGetUseTiming(
                itemUse.ItemType,
                out VanillaItemUseTimingDefinition timing))
        {
            pickToolUse = default;
            return false;
        }

        pickToolUse = new PlayerItemPickToolUse(
            itemUse,
            pickTool.PickPower,
            pickTool.TileBoost,
            timing);
        return true;
    }
}
