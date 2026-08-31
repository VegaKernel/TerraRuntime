using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class ClientTileManipulationConsistencyTests
{
    [Fact]
    public void Dirt_block_matches_source_backed_dirt_placement()
    {
        RuntimePlayerInventoryItem item = Item(VanillaItemIds.DirtBlock, stack: 17);
        TerrariaTileManipulationState request = Place(VanillaTileIds.Dirt.Value);

        ClientTileManipulationConsistencyResult result =
            ClientTileManipulationConsistency.Evaluate(in request, in item);

        Assert.Equal(ClientTileManipulationConsistencyResult.Consistent, result);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(169, 53)]
    public void Explicit_source_backed_block_items_authorize_only_their_mapped_tile(int itemType, int tileType)
    {
        RuntimePlayerInventoryItem item = Item(new ItemTypeId(itemType), stack: 17);
        TerrariaTileManipulationState request = Place(tileType);

        Assert.Equal(
            ClientTileManipulationConsistencyResult.Consistent,
            ClientTileManipulationConsistency.Evaluate(in request, in item));
    }

    [Fact]
    public void Empty_or_wrong_selected_item_cannot_claim_dirt_placement()
    {
        RuntimePlayerInventoryItem empty = default;
        RuntimePlayerInventoryItem pickaxe = Item(VanillaItemIds.CopperPickaxe, stack: 1);
        TerrariaTileManipulationState request = Place(VanillaTileIds.Dirt.Value);

        Assert.Equal(
            ClientTileManipulationConsistencyResult.Mismatch,
            ClientTileManipulationConsistency.Evaluate(in request, in empty));
        Assert.Equal(
            ClientTileManipulationConsistencyResult.Unsupported,
            ClientTileManipulationConsistency.Evaluate(in request, in pickaxe));
    }

    [Fact]
    public void Unknown_item_cannot_authorize_an_arbitrary_simple_tile()
    {
        RuntimePlayerInventoryItem unknown = Item(new ItemTypeId(1), stack: 1);
        TerrariaTileManipulationState request = Place(VanillaTileIds.Stone.Value);

        Assert.Equal(
            ClientTileManipulationConsistencyResult.Unsupported,
            ClientTileManipulationConsistency.Evaluate(in request, in unknown));
    }

    [Fact]
    public void Dirt_item_cannot_authorize_a_different_tile_type()
    {
        RuntimePlayerInventoryItem item = Item(VanillaItemIds.DirtBlock, stack: 1);
        TerrariaTileManipulationState request = Place(tileType: 1);

        Assert.Equal(
            ClientTileManipulationConsistencyResult.Mismatch,
            ClientTileManipulationConsistency.Evaluate(in request, in item));
    }

    [Fact]
    public void Known_but_not_yet_modeled_actions_remain_unsupported()
    {
        RuntimePlayerInventoryItem pickaxe = Item(VanillaItemIds.CopperPickaxe, stack: 1);
        var request = new TerrariaTileManipulationState(
            (byte)TerrariaTileManipulationAction.KillTile,
            TileX: 10,
            TileY: 10,
            Data: 0,
            Style: 0);

        Assert.Equal(
            ClientTileManipulationConsistencyResult.Unsupported,
            ClientTileManipulationConsistency.Evaluate(in request, in pickaxe));
    }

    private static RuntimePlayerInventoryItem Item(ItemTypeId type, short stack) =>
        new(type, stack, new PrefixId(0), ItemFlags: 0);

    private static TerrariaTileManipulationState Place(int tileType) =>
        new(
            (byte)TerrariaTileManipulationAction.PlaceTile,
            TileX: 10,
            TileY: 10,
            Data: checked((short)tileType),
            Style: 0);
}
