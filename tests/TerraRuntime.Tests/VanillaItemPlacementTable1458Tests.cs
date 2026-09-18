using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

/// <summary>
/// Pins the source-derived item placement table against the official defaults and against the hand-written
/// definitions that predate it.
/// </summary>
/// <remarks>
/// Expectations come from running <c>Item.SetDefaults</c> for each id inside the pinned dedicated server
/// (<c>.cache/itemdefs-probe</c>, regenerated through <c>tools/ci/generate_item_placement_table.py</c>). The
/// table is generated, so these tests are what stops a bad regeneration landing silently: the totals catch a
/// truncated run, the spot checks catch a shifted record layout, and the cross-check catches the generated
/// values drifting from the entries this repository had verified one at a time.
/// </remarks>
public sealed class VanillaItemPlacementTable1458Tests
{
    [Fact]
    public void Table_carries_every_placing_item_the_source_defines()
    {
        Assert.Equal(3523, VanillaItemPlacementTable1458.Count);
        Assert.Equal(3231, VanillaItemPlacementTable1458.TilePlacingCount);
        Assert.Equal(292, VanillaItemPlacementTable1458.WallPlacingCount);
        Assert.Equal(
            VanillaItemPlacementTable1458.Count,
            VanillaItemPlacementTable1458.TilePlacingCount + VanillaItemPlacementTable1458.WallPlacingCount);
    }

    [Theory]
    // item, createTile, placeStyle
    [InlineData(2, 0, 0)]
    [InlineData(3, 1, 0)]
    [InlineData(8, 4, 0)]
    [InlineData(9, 30, 0)]
    [InlineData(129, 38, 0)]
    [InlineData(169, 53, 0)]
    public void Tile_placing_items_match_the_source_defaults(int item, int tile, int style)
    {
        Assert.True(VanillaItemPlacementTable1458.TryGet(new ItemTypeId(item), out var record));
        Assert.True(record.PlacesTile);
        Assert.False(record.PlacesWall);
        Assert.Equal(tile, record.CreateTile);
        Assert.Equal(style, record.PlaceStyle);
        Assert.True(record.Consumable);
    }

    [Theory]
    // item, createWall
    [InlineData(26, 1)]
    [InlineData(30, 16)]
    [InlineData(93, 4)]
    [InlineData(130, 5)]
    [InlineData(132, 6)]
    [InlineData(135, 17)]
    public void Wall_placing_items_match_the_source_defaults(int item, int wall)
    {
        Assert.True(VanillaItemPlacementTable1458.TryGet(new ItemTypeId(item), out var record));
        Assert.True(record.PlacesWall);
        Assert.False(record.PlacesTile);
        Assert.Equal(wall, record.CreateWall);
        Assert.True(record.Consumable);
    }

    [Fact]
    public void An_item_that_places_nothing_is_absent()
    {
        // Copper Pickaxe is a tool: the source leaves createTile and createWall at -1, so it must not appear.
        Assert.False(VanillaItemPlacementTable1458.TryGet(VanillaItemIds.CopperPickaxe, out _));
        Assert.False(VanillaItemPlacementTable1458.TryGet(new ItemTypeId(0), out _));
    }

    [Fact]
    public void Hand_written_definitions_agree_with_the_generated_table()
    {
        // These three were the entire placeable catalog before the table existed. If a regeneration ever moved
        // them, one of the two sources is wrong and the build should say so here rather than in a live world.
        (ItemTypeId Item, TileTypeId Tile)[] handWritten =
        [
            (VanillaItemIds.DirtBlock, VanillaTileIds.Dirt),
            (VanillaItemIds.StoneBlock, VanillaTileIds.Stone),
            (VanillaItemIds.SandBlock, VanillaTileIds.Sand)
        ];

        foreach ((ItemTypeId item, TileTypeId tile) in handWritten)
        {
            Assert.True(VanillaDefinitionCatalog.TryGetPlacement(item, out var placement));
            Assert.Equal(tile, placement.TileType);

            Assert.True(VanillaItemPlacementTable1458.TryGet(item, out var record));
            Assert.Equal(tile.Value, record.CreateTile);
            Assert.Equal(placement.Consumable, record.Consumable);
        }
    }

    [Fact]
    public void The_catalog_now_resolves_items_it_never_carried_before()
    {
        // Wood: a tile-placing item with no hand-written definition. Before the table this resolved to nothing
        // and the server answered every attempt to place it with a correction.
        Assert.True(VanillaDefinitionCatalog.TryGetPlacement(new ItemTypeId(9), out var wood));
        Assert.Equal(30, wood.TileType.Value);
        Assert.True(wood.Consumable);

        // Stone Wall: wall placement had no representation at all.
        Assert.True(VanillaDefinitionCatalog.TryGetWallPlacement(new ItemTypeId(26), out WallTypeId wall, out bool consumable));
        Assert.Equal(1, wall.Value);
        Assert.True(consumable);

        Assert.False(VanillaDefinitionCatalog.TryGetWallPlacement(VanillaItemIds.DirtBlock, out _, out _));
    }
}
