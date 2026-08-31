using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

/// <summary>
/// Compatibility facade for the client tile-manipulation authority boundary. Source-backed item facts now live
/// in <see cref="VanillaItemDefinitionCatalog"/> so placement/tool gameplay does not grow a second item database.
/// </summary>
public static class VanillaTileInteractionItemFacts
{
    public const short CopperPickaxePickPower = VanillaItemDefinitionCatalog.CopperPickaxePickPower;
    public const int CopperPickaxeTileBoost = VanillaItemDefinitionCatalog.CopperPickaxeTileBoost;

    public static bool TryGetPlacementTile(
        ItemTypeId itemType,
        out TileTypeId tileType,
        out bool consumable)
    {
        if (VanillaItemDefinitionCatalog.TryGetPlacement(
                itemType,
                out VanillaItemPlacementDefinition placement))
        {
            tileType = placement.TileType;
            consumable = placement.Consumable;
            return true;
        }

        tileType = default;
        consumable = false;
        return false;
    }

    public static bool TryGetPickPower(
        ItemTypeId itemType,
        out short pickPower,
        out int tileBoost)
    {
        if (VanillaItemDefinitionCatalog.TryGetPickTool(
                itemType,
                out VanillaItemPickToolDefinition pickTool))
        {
            pickPower = pickTool.PickPower;
            tileBoost = pickTool.TileBoost;
            return true;
        }

        pickPower = 0;
        tileBoost = 0;
        return false;
    }
}
