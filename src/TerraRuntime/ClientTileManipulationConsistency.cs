using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime;

internal enum ClientTileManipulationConsistencyResult : byte
{
    Consistent = 0,
    Mismatch = 1,
    Unsupported = 2
}

/// <summary>
/// TerraRuntime's stricter consistency policy for client-originated packet 17. TerrariaServer 1.4.5.8 itself
/// does not compare packet 17 against selectedItem/inventory; this layer deliberately does. It uses only
/// source-backed item facts and must not be described as vanilla packet-17 parity.
/// </summary>
internal static class ClientTileManipulationConsistency
{
    public static ClientTileManipulationConsistencyResult Evaluate(
        in TerrariaTileManipulationState state,
        in RuntimePlayerInventoryItem selectedItem)
    {
        if (!state.TryGetKnownAction(out TerrariaTileManipulationAction action))
            return ClientTileManipulationConsistencyResult.Unsupported;

        if (action != TerrariaTileManipulationAction.PlaceTile)
            return ClientTileManipulationConsistencyResult.Unsupported;

        if (selectedItem.IsEmpty ||
            !VanillaTileIds.TryCreate(state.Data, out TileTypeId requestedTile))
        {
            return ClientTileManipulationConsistencyResult.Mismatch;
        }

        if (!VanillaTileInteractionItemFacts.TryGetPlacementTile(
                selectedItem.ItemType,
                out TileTypeId itemTile,
                out _))
        {
            return ClientTileManipulationConsistencyResult.Unsupported;
        }

        return requestedTile == itemTile
            ? ClientTileManipulationConsistencyResult.Consistent
            : ClientTileManipulationConsistencyResult.Mismatch;
    }
}
