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
    public void Tile_catalog_exposes_source_pinned_skyblock_biome_identities()
    {
        Assert.Equal(23, VanillaTileIds.CorruptGrass.Value);
        Assert.Equal(25, VanillaTileIds.Ebonstone.Value);
        Assert.Equal(53, VanillaTileIds.Sand.Value);
        Assert.Equal(59, VanillaTileIds.Mud.Value);
        Assert.Equal(60, VanillaTileIds.JungleGrass.Value);
        Assert.Equal(147, VanillaTileIds.SnowBlock.Value);
        Assert.Equal(161, VanillaTileIds.IceBlock.Value);
        Assert.Equal(199, VanillaTileIds.CrimsonGrass.Value);
        Assert.Equal(203, VanillaTileIds.Crimstone.Value);
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

        Assert.Equal(2, VanillaTileIds.Grass.Value);
        Assert.True(VanillaTileDefinitionCatalog.TryGet(VanillaTileIds.Grass, out VanillaTileDefinition grass));
        Assert.True(grass.IsSolid);
        Assert.False(grass.IsFrameImportant);

        Assert.Equal(10, VanillaTileIds.ClosedDoor.Value);
        Assert.Equal(388, VanillaTileIds.TallGateClosed.Value);
        Assert.True(VanillaTileIds.IsClosedDoor(VanillaTileIds.ClosedDoor));
        Assert.True(VanillaTileIds.IsClosedDoor(VanillaTileIds.TallGateClosed));
        Assert.False(VanillaTileIds.IsClosedDoor(VanillaTileIds.Stone));

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
    public void Simple_cell_contextual_drop_families_are_definition_driven()
    {
        AssertContextualSimpleCell(VanillaTileIds.Vines, VanillaTileContextualDropKind.CordageVine);
        AssertContextualSimpleCell(VanillaTileIds.JungleVines, VanillaTileContextualDropKind.CordageVine);
        AssertContextualSimpleCell(VanillaTileIds.VineFlowers, VanillaTileContextualDropKind.CordageVine);
        AssertContextualSimpleCell(VanillaTileIds.MushroomVines, VanillaTileContextualDropKind.MushroomVine);
        AssertContextualSimpleCell(VanillaTileIds.Hive, VanillaTileContextualDropKind.Hive);

        VanillaTileDefinition torch = VanillaTileDefinitionCatalog.Get(VanillaTileIds.Torches);
        Assert.Equal(VanillaTileBreakPath.FrameImportant, torch.BreakPath);
        Assert.Equal(VanillaTileDropRuleKind.Contextual, torch.DropRule.Kind);
        Assert.Equal(VanillaTileContextualDropKind.None, torch.ContextualDropKind);
    }

    [Fact]
    public void Every_non_frame_contextual_1458_tile_has_an_explicit_simple_cell_strategy()
    {
        for (int rawType = 0; rawType < VanillaTileDefinitionCatalog.Count; rawType++)
        {
            VanillaTileDefinition definition = VanillaTileDefinitionCatalog.Get(new TileTypeId(rawType));
            if (definition.BreakPath != VanillaTileBreakPath.SimpleCell ||
                definition.DropRule.Kind != VanillaTileDropRuleKind.Contextual)
            {
                continue;
            }

            Assert.NotEqual(VanillaTileContextualDropKind.None, definition.ContextualDropKind);
        }
    }

    [Fact]
    public void Wall_catalog_covers_exact_1458_identity_range_and_named_ids()
    {
        Assert.Equal(367, VanillaWallIds.Count);
        Assert.Equal(VanillaWallIds.Count, VanillaWallDefinitionCatalog.Count);
        Assert.Equal(0, VanillaWallIds.None.Value);
        Assert.Equal(1, VanillaWallIds.Stone.Value);
        Assert.Equal(2, VanillaWallIds.DirtUnsafe.Value);
        Assert.Equal(15, VanillaWallIds.MudUnsafe.Value);
        Assert.Equal(16, VanillaWallIds.Dirt.Value);
        Assert.Equal(40, VanillaWallIds.SnowUnsafe.Value);
        Assert.Equal(59, VanillaWallIds.RockyDirtUnsafe.Value);
        Assert.Equal(61, VanillaWallIds.OldStoneUnsafe.Value);
        Assert.Equal(63, VanillaWallIds.GrassUnsafe.Value);
        Assert.Equal(64, VanillaWallIds.JungleUnsafe.Value);
        Assert.Equal(71, VanillaWallIds.IceUnsafe.Value);
        Assert.Equal(244, VanillaWallIds.LivingWoodUnsafe.Value);

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
    private static void AssertContextualSimpleCell(
        TileTypeId type,
        VanillaTileContextualDropKind expectedKind)
    {
        VanillaTileDefinition definition = VanillaTileDefinitionCatalog.Get(type);
        Assert.Equal(VanillaTileBreakPath.SimpleCell, definition.BreakPath);
        Assert.Equal(VanillaTileDropRuleKind.Contextual, definition.DropRule.Kind);
        Assert.Equal(expectedKind, definition.ContextualDropKind);
    }

}
