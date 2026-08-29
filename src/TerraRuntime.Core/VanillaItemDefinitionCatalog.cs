using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

/// <summary>
/// Source-backed placement facts for one vanilla item. This is intentionally sparse: absence of a placement
/// definition means TerraRuntime has not verified that capability, not that the vanilla item necessarily cannot place.
/// </summary>
public readonly record struct VanillaItemPlacementDefinition(
    TileTypeId TileType,
    bool Consumable);

/// <summary>
/// Source-backed pick-tool facts for one vanilla item. Values are the exact TerrariaServer 1.4.5.8 defaults
/// currently consumed by authoritative tile-interaction policy.
/// </summary>
public readonly record struct VanillaItemPickToolDefinition(
    short PickPower,
    int TileBoost);

/// <summary>
/// Sparse immutable vanilla item definition. Optional capability records distinguish verified facts from fields
/// TerraRuntime has not yet imported from TerrariaServer 1.4.5.8; an absent capability must never be read as a
/// guessed zero/default vanilla value.
/// </summary>
public readonly record struct VanillaItemDefinition(
    ItemTypeId Type,
    VanillaItemPlacementDefinition? Placement,
    VanillaItemPickToolDefinition? PickTool);

/// <summary>
/// Initial source-verified TerrariaServer 1.4.5.8 item-definition catalog. The catalog grows only with facts
/// independently pinned by repository source-contract probes or equivalent official-source evidence.
/// </summary>
public static class VanillaItemDefinitionCatalog
{
    public const short CopperPickaxePickPower = 35;
    public const int CopperPickaxeTileBoost = -1;

    private static readonly VanillaItemDefinition DirtBlockDefinition = new(
        Type: VanillaItemIds.DirtBlock,
        Placement: new VanillaItemPlacementDefinition(
            TileType: VanillaTileIds.Dirt,
            Consumable: true),
        PickTool: null);

    private static readonly VanillaItemDefinition CopperPickaxeDefinition = new(
        Type: VanillaItemIds.CopperPickaxe,
        Placement: null,
        PickTool: new VanillaItemPickToolDefinition(
            PickPower: CopperPickaxePickPower,
            TileBoost: CopperPickaxeTileBoost));

    public static bool TryGet(ItemTypeId type, out VanillaItemDefinition definition)
    {
        if (type == VanillaItemIds.DirtBlock)
        {
            definition = DirtBlockDefinition;
            return true;
        }

        if (type == VanillaItemIds.CopperPickaxe)
        {
            definition = CopperPickaxeDefinition;
            return true;
        }

        definition = default;
        return false;
    }

    public static bool TryGetPlacement(
        ItemTypeId type,
        out VanillaItemPlacementDefinition placement)
    {
        if (TryGet(type, out VanillaItemDefinition definition) && definition.Placement is { } verified)
        {
            placement = verified;
            return true;
        }

        placement = default;
        return false;
    }

    public static bool TryGetPickTool(
        ItemTypeId type,
        out VanillaItemPickToolDefinition pickTool)
    {
        if (TryGet(type, out VanillaItemDefinition definition) && definition.PickTool is { } verified)
        {
            pickTool = verified;
            return true;
        }

        pickTool = default;
        return false;
    }
}
