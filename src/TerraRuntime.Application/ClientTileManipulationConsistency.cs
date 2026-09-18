using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal enum ClientTileManipulationConsistencyResult : byte
{
    Consistent = 0,
    Mismatch = 1,
    Unsupported = 2
}

/// <summary>
/// TerraRuntime's stricter consistency policy for client-originated packet 17. TerrariaServer 1.4.5.8 itself
/// does not compare packet 17 against selectedItem/inventory; this layer deliberately does. It consumes the
/// shared source-backed item-definition catalog and must not be described as vanilla packet-17 parity.
/// Unknown item-to-tile mappings fail closed until their placement semantics are imported explicitly.
/// </summary>
internal static class ClientTileManipulationConsistency
{
    public static ClientTileManipulationConsistencyResult Evaluate(
        in TerrariaTileManipulationState state,
        in RuntimePlayerInventoryItem selectedItem)
    {
        if (!state.TryGetWireAction(out TerrariaTileManipulationAction action))
            return ClientTileManipulationConsistencyResult.Unsupported;

        if (selectedItem.IsEmpty)
            return ClientTileManipulationConsistencyResult.Mismatch;

        if (action == TerrariaTileManipulationAction.PlaceWall)
        {
            if (!VanillaWallIds.TryCreate(state.Data, out WallTypeId requestedWall))
                return ClientTileManipulationConsistencyResult.Mismatch;

            if (!VanillaDefinitionCatalog.TryGetWallPlacement(selectedItem.ItemType, out WallTypeId itemWall, out _))
                return ClientTileManipulationConsistencyResult.Unsupported;

            return requestedWall == itemWall
                ? ClientTileManipulationConsistencyResult.Consistent
                : ClientTileManipulationConsistencyResult.Mismatch;
        }

        if (action != TerrariaTileManipulationAction.PlaceTile)
            return ClientTileManipulationConsistencyResult.Unsupported;

        if (!VanillaTileIds.TryCreate(state.Data, out TileTypeId requestedTile))
            return ClientTileManipulationConsistencyResult.Mismatch;

        if (!VanillaDefinitionCatalog.TryGetPlacement(
                selectedItem.ItemType,
                out VanillaItemPlacementDefinition placement))
        {
            return ClientTileManipulationConsistencyResult.Unsupported;
        }

        return requestedTile == placement.TileType
            ? ClientTileManipulationConsistencyResult.Consistent
            : ClientTileManipulationConsistencyResult.Mismatch;
    }
}
