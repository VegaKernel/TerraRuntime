using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaTileWallDefinitionCatalogTests
{
    [Fact]
    public void Tile_catalog_covers_exact_1458_identity_range()
    {
        Assert.Equal(VanillaTileIds.Count, VanillaTileDefinitionCatalog.Count);
        for (int rawType = 0; rawType < VanillaTileDefinitionCatalog.Count; rawType++)
        {
            Assert.True(VanillaTileDefinitionCatalog.TryGet(
                new TileTypeId(rawType),
                out VanillaTileDefinition definition));
            Assert.Equal(rawType, definition.Type.Value);
        }

        Assert.False(VanillaTileDefinitionCatalog.TryGet(
            new TileTypeId(VanillaTileDefinitionCatalog.Count),
            out _));
    }

    [Fact]
    public void Tile_definition_composes_identity_collision_frame_and_metadata_facts()
    {
        Assert.True(VanillaTileDefinitionCatalog.TryGet(VanillaTileIds.Dirt, out VanillaTileDefinition dirt));
        Assert.True(dirt.IsSolid);
        Assert.False(dirt.IsSolidTop);
        Assert.False(dirt.IsFrameImportant);
        Assert.False(dirt.CarriesContainerMetadata);
        Assert.False(dirt.CarriesSignMetadata);

        Assert.True(VanillaTileDefinitionCatalog.TryGet(
            VanillaTileIds.Containers,
            out VanillaTileDefinition container));
        Assert.True(container.IsFrameImportant);
        Assert.True(container.CarriesContainerMetadata);
        Assert.False(container.CarriesSignMetadata);

        Assert.True(VanillaTileDefinitionCatalog.TryGet(VanillaTileIds.Signs, out VanillaTileDefinition sign));
        Assert.True(sign.IsFrameImportant);
        Assert.False(sign.CarriesContainerMetadata);
        Assert.True(sign.CarriesSignMetadata);
    }

    [Fact]
    public void Wall_catalog_covers_exact_1458_identity_range_and_named_ids()
    {
        Assert.Equal(367, VanillaWallIds.Count);
        Assert.Equal(VanillaWallIds.Count, VanillaWallDefinitionCatalog.Count);
        Assert.Equal(0, VanillaWallIds.None.Value);
        Assert.Equal(1, VanillaWallIds.Stone.Value);
        Assert.Equal(2, VanillaWallIds.DirtUnsafe.Value);
        Assert.Equal(16, VanillaWallIds.Dirt.Value);

        for (int rawType = 0; rawType < VanillaWallDefinitionCatalog.Count; rawType++)
        {
            Assert.True(VanillaWallDefinitionCatalog.TryGet(
                new WallTypeId(rawType),
                out VanillaWallDefinition definition));
            Assert.Equal(rawType, definition.Type.Value);
        }

        Assert.False(VanillaWallDefinitionCatalog.TryGet(
            new WallTypeId(VanillaWallDefinitionCatalog.Count),
            out _));
    }

    [Fact]
    public void Wall_definition_preserves_no_wall_housing_dungeon_and_light_semantics()
    {
        Assert.True(VanillaWallDefinitionCatalog.TryGet(VanillaWallIds.None, out VanillaWallDefinition none));
        Assert.False(none.IsPresent);
        Assert.False(none.IsHousingWall);
        Assert.False(none.IsDungeonWall);
        Assert.True(none.LetsLightThrough);

        Assert.True(VanillaWallDefinitionCatalog.TryGet(VanillaWallIds.Stone, out VanillaWallDefinition stone));
        Assert.True(stone.IsPresent);
        Assert.True(stone.IsHousingWall);
        Assert.False(stone.IsDungeonWall);
        Assert.False(stone.LetsLightThrough);

        Assert.True(VanillaWallDefinitionCatalog.TryGet(
            VanillaWallIds.DirtUnsafe,
            out VanillaWallDefinition dirtUnsafe));
        Assert.False(dirtUnsafe.IsHousingWall);

        Assert.True(VanillaWallDefinitionCatalog.TryGet(VanillaWallIds.Dirt, out VanillaWallDefinition dirt));
        Assert.True(dirt.IsHousingWall);

        Assert.True(VanillaWallDefinitionCatalog.TryGet(
            VanillaWallIds.BlueDungeonUnsafe,
            out VanillaWallDefinition dungeon));
        Assert.True(dungeon.IsDungeonWall);
        Assert.False(dungeon.IsHousingWall);
    }

    [Fact]
    public void Wall_capability_tables_match_source_contract_cardinality()
    {
        int housing = 0;
        int dungeon = 0;
        int light = 0;
        for (int rawType = 0; rawType < VanillaWallDefinitionCatalog.Count; rawType++)
        {
            Assert.True(VanillaWallDefinitionCatalog.TryGet(
                new WallTypeId(rawType),
                out VanillaWallDefinition definition));
            housing += definition.IsHousingWall ? 1 : 0;
            dungeon += definition.IsDungeonWall ? 1 : 0;
            light += definition.LetsLightThrough ? 1 : 0;
        }

        Assert.Equal(279, housing);
        Assert.Equal(9, dungeon);
        Assert.Equal(16, light);
    }
}
