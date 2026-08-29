using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

/// <summary>
/// Minimal TerrariaServer 1.4.5.8 item facts used by the client tile-manipulation authority boundary.
/// This is intentionally not a speculative Item database: every entry here is pinned by the source-contract CI.
/// </summary>
public static class VanillaTileInteractionItemFacts
{
    public const short CopperPickaxePickPower = 35;
    public const int CopperPickaxeTileBoost = -1;

    public static bool TryGetPlacementTile(
        ItemTypeId itemType,
        out TileTypeId tileType,
        out bool consumable)
    {
        if (itemType == VanillaItemIds.DirtBlock)
        {
            tileType = VanillaTileIds.Dirt;
            consumable = true;
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
        if (itemType == VanillaItemIds.CopperPickaxe)
        {
            pickPower = CopperPickaxePickPower;
            tileBoost = CopperPickaxeTileBoost;
            return true;
        }

        pickPower = 0;
        tileBoost = 0;
        return false;
    }
}
