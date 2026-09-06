using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaLiquidInteractionFacts1458Tests
{
    [Fact]
    public void Final_obsidian_kill_table_has_source_pinned_count_and_overrides()
    {
        int found = 0;
        for (int raw = 0; raw < VanillaTileIds.Count; raw++)
        {
            if (VanillaLiquidInteractionFacts1458.IsObsidianKill(new TileTypeId(raw)))
                found++;
        }

        Assert.Equal(VanillaLiquidInteractionFacts1458.ObsidianKillTileTypeCount, found);
        Assert.True(VanillaLiquidInteractionFacts1458.IsObsidianKill(VanillaTileIds.Cobweb));
        Assert.True(VanillaLiquidInteractionFacts1458.IsObsidianKill(VanillaTileIds.ClosedDoor));
        Assert.False(VanillaLiquidInteractionFacts1458.IsObsidianKill(VanillaTileIds.Dressers));
    }

    [Fact]
    public void Container_set_is_exactly_the_three_1458_identities()
    {
        Assert.True(VanillaLiquidInteractionFacts1458.IsContainer(VanillaTileIds.Containers));
        Assert.True(VanillaLiquidInteractionFacts1458.IsContainer(VanillaTileIds.Containers2));
        Assert.True(VanillaLiquidInteractionFacts1458.IsContainer(VanillaTileIds.Dressers));
        Assert.False(VanillaLiquidInteractionFacts1458.IsContainer(VanillaTileIds.Stone));
    }

    [Theory]
    [InlineData(WorldLiquidKind.Lava, WorldLiquidKind.Water, VanillaTileChangeType1458.LavaWater)]
    [InlineData(WorldLiquidKind.Honey, WorldLiquidKind.Water, VanillaTileChangeType1458.HoneyWater)]
    [InlineData(WorldLiquidKind.Honey, WorldLiquidKind.Lava, VanillaTileChangeType1458.HoneyLava)]
    [InlineData(WorldLiquidKind.Shimmer, WorldLiquidKind.Water, VanillaTileChangeType1458.ShimmerWater)]
    [InlineData(WorldLiquidKind.Shimmer, WorldLiquidKind.Lava, VanillaTileChangeType1458.ShimmerLava)]
    [InlineData(WorldLiquidKind.Shimmer, WorldLiquidKind.Honey, VanillaTileChangeType1458.ShimmerHoney)]
    public void Liquid_merge_change_type_matches_Terraria_wire_identity(
        WorldLiquidKind first,
        WorldLiquidKind second,
        VanillaTileChangeType1458 expected)
    {
        Assert.Equal(expected, VanillaLiquidMergeCatalog1458.ResolveTileChangeType(first, second));
        Assert.Equal(expected, VanillaLiquidMergeCatalog1458.ResolveTileChangeType(second, first));
    }
}
