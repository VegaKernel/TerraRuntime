using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldSectionGeometryTests
{
    [Fact]
    public void Dimensions_round_partial_edges_up_to_network_sections()
    {
        var dimensions = new WorldDimensions(widthTiles: 421, heightTiles: 301);

        Assert.Equal(3, dimensions.SectionColumns);
        Assert.Equal(3, dimensions.SectionRows);
        Assert.Equal(9, dimensions.SectionCount);
        Assert.Equal(new WorldSectionId(2, 2), TerrariaSectionGeometry.FromTile(dimensions, 420, 300));
        Assert.Equal(new WorldTileBounds(400, 300, 21, 1),
            TerrariaSectionGeometry.GetBounds(dimensions, new WorldSectionId(2, 2)));
    }

    [Fact]
    public void Section_linear_index_round_trips()
    {
        var dimensions = new WorldDimensions(widthTiles: 8400, heightTiles: 2400);
        var section = new WorldSectionId(17, 9);

        int index = TerrariaSectionGeometry.ToLinearIndex(dimensions, section);

        Assert.Equal(section, TerrariaSectionGeometry.FromLinearIndex(dimensions, index));
    }

    [Fact]
    public void Out_of_world_tiles_and_sections_are_rejected()
    {
        var dimensions = new WorldDimensions(widthTiles: 400, heightTiles: 300);

        Assert.Throws<ArgumentOutOfRangeException>(() => TerrariaSectionGeometry.FromTile(dimensions, 400, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TerrariaSectionGeometry.GetBounds(dimensions, new WorldSectionId(2, 0)));
    }
}
