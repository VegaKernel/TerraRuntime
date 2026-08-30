using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaTileObjectAnchorCatalogTests
{
    [Fact]
    public void Multi_tile_catalog_pins_supported_base_style_geometry()
    {
        Assert.Equal(15, VanillaMultiTileObjectCatalog.All.Length);
        AssertGeometry(VanillaTileIds.Containers, 2, 2, 0, 1, VanillaTileObjectMetadataKind.Chest);
        AssertGeometry(VanillaTileIds.Dressers, 3, 2, 1, 1, VanillaTileObjectMetadataKind.Chest);
        AssertGeometry(VanillaTileIds.Signs, 2, 2, 0, 1, VanillaTileObjectMetadataKind.Sign);
        AssertGeometry(VanillaTileIds.TargetDummy, 2, 3, 1, 2, VanillaTileObjectMetadataKind.TileEntity);
        AssertGeometry(VanillaTileIds.ItemFrame, 2, 2, 0, 1, VanillaTileObjectMetadataKind.TileEntity);
        AssertGeometry(VanillaTileIds.DeadCellsDisplayJar, 1, 2, 0, 0, VanillaTileObjectMetadataKind.TileEntity);
        AssertGeometry(VanillaTileIds.FoodPlatter, 1, 1, 0, 0, VanillaTileObjectMetadataKind.TileEntity);
        AssertGeometry(VanillaTileIds.WeaponsRack2, 3, 3, 1, 1, VanillaTileObjectMetadataKind.TileEntity);
        AssertGeometry(VanillaTileIds.DisplayDoll, 2, 3, 0, 2, VanillaTileObjectMetadataKind.TileEntity);
        AssertGeometry(VanillaTileIds.HatRack, 3, 4, 1, 3, VanillaTileObjectMetadataKind.TileEntity);
        AssertGeometry(VanillaTileIds.TeleportationPylon, 3, 4, 1, 3, VanillaTileObjectMetadataKind.TileEntity);
    }

    [Fact]
    public void Multi_tile_catalog_is_sparse_and_has_unique_type_and_tile_entity_keys()
    {
        Assert.False(VanillaMultiTileObjectCatalog.TryGet(VanillaTileIds.Dirt, out _));
        Assert.Equal(
            VanillaMultiTileObjectCatalog.All.Length,
            VanillaMultiTileObjectCatalog.All.ToArray().Select(definition => definition.TileType).Distinct().Count());

        VanillaMultiTileObjectDefinition[] tileEntities = VanillaMultiTileObjectCatalog.All
            .ToArray()
            .Where(definition => definition.MetadataKind == VanillaTileObjectMetadataKind.TileEntity)
            .ToArray();
        Assert.Equal(
            tileEntities.Length,
            tileEntities.Select(definition => definition.TileEntityKind).Distinct().Count());
        Assert.All(VanillaMultiTileObjectCatalog.All.ToArray(), definition => Assert.True(definition.IsValid));
    }

    [Fact]
    public void Chest_anchor_rules_keep_container_and_dresser_frame_periods_distinct()
    {
        WorldTile chest = ActiveTile(VanillaTileIds.Containers, frameX: 72, frameY: 36);
        WorldTile chest2 = ActiveTile(VanillaTileIds.Containers2, frameX: 36, frameY: 72);
        WorldTile dresser = ActiveTile(VanillaTileIds.Dressers, frameX: 54, frameY: 36);
        WorldTile misalignedDresser = ActiveTile(VanillaTileIds.Dressers, frameX: 36, frameY: 36);

        Assert.True(VanillaTileObjectAnchorCatalog.MatchesChestAnchor(chest));
        Assert.True(VanillaTileObjectAnchorCatalog.MatchesChestAnchor(chest2));
        Assert.True(VanillaTileObjectAnchorCatalog.MatchesChestAnchor(dresser));
        Assert.False(VanillaTileObjectAnchorCatalog.MatchesChestAnchor(misalignedDresser));
    }

    [Theory]
    [InlineData(55)]
    [InlineData(85)]
    [InlineData(425)]
    [InlineData(573)]
    public void Sign_anchor_family_uses_one_verified_frame_rule(int rawTileType)
    {
        Assert.True(VanillaTileIds.TryCreate(rawTileType, out TileTypeId type));

        WorldTile aligned = ActiveTile(type, frameX: 36, frameY: 72);
        WorldTile misaligned = ActiveTile(type, frameX: 18, frameY: 72);

        Assert.True(VanillaTileObjectAnchorCatalog.MatchesSignAnchor(aligned));
        Assert.False(VanillaTileObjectAnchorCatalog.MatchesSignAnchor(misaligned));
    }

    [Fact]
    public void Sign_origin_offsets_use_catalogued_object_geometry()
    {
        WorldTile styledSignCell = ActiveTile(VanillaTileIds.AnnouncementBox, frameX: 54, frameY: 54);

        Assert.True(VanillaTileObjectAnchorCatalog.TryResolveSignOriginOffset(
            styledSignCell,
            out int offsetX,
            out int offsetY));
        Assert.Equal(1, offsetX);
        Assert.Equal(3, offsetY);

        WorldTile ordinary = ActiveTile(VanillaTileIds.Stone, frameX: 0, frameY: 0);
        Assert.False(VanillaTileObjectAnchorCatalog.TryResolveSignOriginOffset(ordinary, out _, out _));
    }

    [Fact]
    public void Tile_entity_anchor_definitions_pin_verified_frame_periods()
    {
        AssertDefinition(WorldTileEntityKind.TrainingDummy, VanillaTileIds.TargetDummy, 36, 0, requireFrameYZero: true);
        AssertDefinition(WorldTileEntityKind.ItemFrame, VanillaTileIds.ItemFrame, 36, 0, requireFrameYZero: true);
        AssertDefinition(WorldTileEntityKind.DeadCellsDisplayJar, VanillaTileIds.DeadCellsDisplayJar, 18, 0, requireFrameYZero: true);
        AssertDefinition(WorldTileEntityKind.FoodPlatter, VanillaTileIds.FoodPlatter, 18, 0, requireFrameYZero: true);
        AssertDefinition(WorldTileEntityKind.WeaponsRack, VanillaTileIds.WeaponsRack2, 54, 0, requireFrameYZero: true);
        AssertDefinition(WorldTileEntityKind.DisplayDoll, VanillaTileIds.DisplayDoll, 36, 0, requireFrameYZero: true);
        AssertDefinition(WorldTileEntityKind.HatRack, VanillaTileIds.HatRack, 54, 0, requireFrameYZero: true);
        AssertDefinition(WorldTileEntityKind.TeleportationPylon, VanillaTileIds.TeleportationPylon, 54, 72, requireFrameYZero: false);
    }

    [Fact]
    public void Tile_entity_matching_requires_active_type_and_anchor_alignment()
    {
        WorldTile pylon = ActiveTile(VanillaTileIds.TeleportationPylon, frameX: 108, frameY: 144);
        Assert.True(VanillaTileObjectAnchorCatalog.MatchesTileEntityAnchor(WorldTileEntityKind.TeleportationPylon, pylon));

        pylon.FrameY = 36;
        Assert.False(VanillaTileObjectAnchorCatalog.MatchesTileEntityAnchor(WorldTileEntityKind.TeleportationPylon, pylon));

        WorldTile itemFrame = ActiveTile(VanillaTileIds.ItemFrame, frameX: 36, frameY: 18);
        Assert.False(VanillaTileObjectAnchorCatalog.MatchesTileEntityAnchor(WorldTileEntityKind.ItemFrame, itemFrame));

        itemFrame.FrameY = 0;
        itemFrame.Flags = WorldTileFlags.None;
        Assert.False(VanillaTileObjectAnchorCatalog.MatchesTileEntityAnchor(WorldTileEntityKind.ItemFrame, itemFrame));
    }

    private static WorldTile ActiveTile(TileTypeId type, short frameX, short frameY)
    {
        var tile = new WorldTile
        {
            Flags = WorldTileFlags.Active,
            FrameX = frameX,
            FrameY = frameY
        };
        Assert.True(tile.TrySetTileType(type));
        return tile;
    }

    private static void AssertDefinition(
        WorldTileEntityKind kind,
        TileTypeId expectedType,
        short expectedFrameXPeriod,
        short expectedFrameYPeriod,
        bool requireFrameYZero)
    {
        Assert.True(VanillaTileObjectAnchorCatalog.TryGetTileEntityAnchorDefinition(kind, out VanillaTileObjectAnchorDefinition definition));
        Assert.Equal(expectedType, definition.TileType);
        Assert.Equal(expectedFrameXPeriod, definition.FrameXPeriod);
        Assert.Equal(expectedFrameYPeriod, definition.FrameYPeriod);
        Assert.Equal(requireFrameYZero, definition.RequireFrameYZero);
        Assert.True(definition.IsValid);
    }

    private static void AssertGeometry(
        TileTypeId type,
        byte width,
        byte height,
        byte originColumn,
        byte originRow,
        VanillaTileObjectMetadataKind metadataKind)
    {
        Assert.True(VanillaMultiTileObjectCatalog.TryGet(type, out VanillaMultiTileObjectDefinition definition));
        Assert.Equal(width, definition.Width);
        Assert.Equal(height, definition.Height);
        Assert.Equal(originColumn, definition.PlacementOriginColumn);
        Assert.Equal(originRow, definition.PlacementOriginRow);
        Assert.Equal(metadataKind, definition.MetadataKind);
        Assert.True(definition.IsValid);
    }
}
