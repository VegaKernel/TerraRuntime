using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime;

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

        if (VanillaItemDefinitionCatalog.TryGetPlacement(
                selectedItem.ItemType,
                out VanillaItemPlacementDefinition placement))
        {
            return requestedTile == placement.TileType
                ? ClientTileManipulationConsistencyResult.Consistent
                : ClientTileManipulationConsistencyResult.Mismatch;
        }

        // Sparse catalog fallback: truly unknown item types that request a valid simple tile are treated
        // as consistent so generic tile placement (stone/sand/etc.) can be validated by the
        // authoritative mutation service without requiring every block item to be pre-catalogued.
        // Known non-placeable items (e.g., pickaxe) remain Unsupported.
        if (VanillaItemDefinitionCatalog.TryGet(selectedItem.ItemType, out VanillaItemDefinition known) &&
            known.Placement is null)
        {
            return ClientTileManipulationConsistencyResult.Unsupported;
        }

        if (VanillaTileIds.TryCreate(requestedTile.Value, out _) &&
            VanillaTileDefinitionCatalog.TryGet(requestedTile, out VanillaTileDefinition definition) &&
            !definition.IsFrameImportant &&
            !VanillaMultiTileObjectCatalog.TryGet(requestedTile, out _))
        {
            return ClientTileManipulationConsistencyResult.Consistent;
        }

        return ClientTileManipulationConsistencyResult.Unsupported;
    }
}
