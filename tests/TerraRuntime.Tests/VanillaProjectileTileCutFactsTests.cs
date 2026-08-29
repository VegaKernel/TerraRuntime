using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Tests;

public sealed class VanillaProjectileTileCutFactsTests
{
    [Fact]
    public void Terraria_1458_tile_cut_catalog_matches_source_verified_shape()
    {
        int[] expected =
        [
            3, 24, 28, 32, 51, 52, 61, 62, 69, 71, 73, 74, 82, 83, 84, 110, 113, 115, 184, 201,
            205, 231, 236, 254, 352, 382, 444, 454, 484, 485, 518, 519, 528, 529, 549, 636, 637, 638,
            654, 655, 711
        ];

        int found = 0;
        for (int rawType = 0; rawType <= ushort.MaxValue; rawType++)
        {
            bool cuttable = VanillaProjectileTileCutFacts.IsCuttable(new TileTypeId(rawType));
            if (cuttable)
                found++;
        }

        Assert.Equal(VanillaProjectileTileCutFacts.CuttableTileTypeCount, found);
        Assert.Equal(expected.Length, found);
        foreach (int rawType in expected)
            Assert.True(VanillaProjectileTileCutFacts.IsCuttable(new TileTypeId(rawType)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(253)]
    [InlineData(255)]
    [InlineData(712)]
    [InlineData(ushort.MaxValue)]
    [InlineData(ushort.MaxValue + 1)]
    public void Non_cuttable_or_out_of_storage_range_types_are_rejected(int rawType)
    {
        Assert.False(VanillaProjectileTileCutFacts.IsCuttable(new TileTypeId(rawType)));
    }
}
