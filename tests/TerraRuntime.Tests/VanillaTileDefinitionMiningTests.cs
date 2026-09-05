using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaTileDefinitionMiningTests
{
    [Fact]
    public void Ordinary_tiles_are_described_by_definition_instead_of_simple_kill_allowlist()
    {
        VanillaTileDefinition stone = VanillaTileDefinitionCatalog.Get(VanillaTileIds.Stone);
        VanillaTileDefinition snow = VanillaTileDefinitionCatalog.Get(VanillaTileIds.SnowBlock);
        VanillaTileDefinition grass = VanillaTileDefinitionCatalog.Get(VanillaTileIds.Grass);
        VanillaTileDefinition lihzahrd = VanillaTileDefinitionCatalog.Get(VanillaTileIds.LihzahrdBrick);

        Assert.Equal(VanillaTileBreakPath.SimpleCell, stone.BreakPath);
        Assert.Equal(VanillaTileDropRuleKind.Fixed, stone.DropRule.Kind);
        Assert.False(stone.TransformsOnFailedPick);

        Assert.Equal(VanillaTileBreakPath.SimpleCell, snow.BreakPath);
        Assert.Equal(VanillaTileDropRuleKind.Fixed, snow.DropRule.Kind);

        Assert.Equal(VanillaTileBreakPath.SimpleCell, grass.BreakPath);
        Assert.True(grass.FailedPickTransformTarget.HasValue);
        Assert.Equal(VanillaTileIds.Dirt, grass.FailedPickTransformTarget.GetValueOrDefault());

        Assert.Equal(VanillaTileMiningProfile.LihzahrdTemple, lihzahrd.MiningProfile);
    }

    [Fact]
    public void Frame_important_and_multi_tile_content_selects_non_simple_paths()
    {
        VanillaTileDefinition chest = VanillaTileDefinitionCatalog.Get(VanillaTileIds.Containers);
        VanillaTileDefinition tree = VanillaTileDefinitionCatalog.Get(VanillaTileIds.Trees);

        Assert.Equal(VanillaTileBreakPath.MultiTileObject, chest.BreakPath);
        Assert.Equal(VanillaTileDropRuleKind.Object, chest.DropRule.Kind);
        Assert.NotEqual(VanillaTileBreakPath.SimpleCell, tree.BreakPath);

        VanillaTileDefinition waterCandle = VanillaTileDefinitionCatalog.Get(VanillaTileIds.WaterCandle);
        VanillaTileDefinition switchTile = VanillaTileDefinitionCatalog.Get(VanillaTileIds.Switches);
        VanillaTileDefinition ordinaryPlatform = VanillaTileDefinitionCatalog.Get(VanillaTileIds.Platforms);

        Assert.Equal(VanillaTileBreakPath.FrameImportantSingleCell, waterCandle.BreakPath);
        Assert.Equal(VanillaTileDropRuleKind.Fixed, waterCandle.DropRule.Kind);
        Assert.Equal(VanillaTileBreakPath.FrameImportantSingleCell, switchTile.BreakPath);
        Assert.Equal(VanillaTileDropRuleKind.Fixed, switchTile.DropRule.Kind);
        Assert.Equal(VanillaTileBreakPath.FrameImportant, ordinaryPlatform.BreakPath);
    }
}
