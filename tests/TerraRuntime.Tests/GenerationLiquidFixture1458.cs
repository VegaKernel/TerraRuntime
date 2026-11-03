using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

internal static class GenerationLiquidFixture1458
{
    internal static void Configure(Workspace workspace, bool laterPass)
    {
        workspace.InitializeVanillaGenerationSolidity();
        foreach (ushort type in new ushort[] { 481, 482, 483, 659 })
            workspace.SetVanillaGenerationSolid(new TileTypeId(type), false);
        if (!laterPass)
            foreach (ushort type in new ushort[] { 137, 192, 484 })
                workspace.SetVanillaGenerationSolid(new TileTypeId(type), false);
        if (workspace.VanillaLiquidLines is null)
            workspace.SetVanillaLiquidLines(300, 400);
    }
    internal static void AssertState(Workspace workspace, IWorldGenerationVanillaRandom adapter,
        string expectedGrid, string expectedRandom)
    {
        Assert.Equal(expectedGrid, GenerationNaturalPrefix49Tests.GridHash(workspace));
        Assert.Equal(expectedRandom, GenerationNaturalPrefix49Tests.RandomHash(adapter, includeReport: false));
    }
}
