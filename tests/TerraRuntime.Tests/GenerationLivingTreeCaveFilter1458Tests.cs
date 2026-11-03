using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class GenerationLivingTreeCaveFilter1458Tests
{
    [Theory]
    [InlineData(-50, "6E2D0DF5E4FE2C6DAD6C08720DD472E27D21552D8A0035BB849363C0FF8C632E", "7D474CA73395EE5070B93C005D42601A37B2ADF1AEFEA2C6A5FDE6CE56E445DB", 265634126)]
    [InlineData(-49, "6E2D0DF5E4FE2C6DAD6C08720DD472E27D21552D8A0035BB849363C0FF8C632E", "7CA168DC7DE16C602906D933DD033CBD906287AD1075B245E0334BA9AC1753AC", 442756017)]
    [InlineData(0, "6E2D0DF5E4FE2C6DAD6C08720DD472E27D21552D8A0035BB849363C0FF8C632E", "7CA168DC7DE16C602906D933DD033CBD906287AD1075B245E0334BA9AC1753AC", 442756017)]
    [InlineData(49, "6E2D0DF5E4FE2C6DAD6C08720DD472E27D21552D8A0035BB849363C0FF8C632E", "7CA168DC7DE16C602906D933DD033CBD906287AD1075B245E0334BA9AC1753AC", 442756017)]
    [InlineData(50, "6E2D0DF5E4FE2C6DAD6C08720DD472E27D21552D8A0035BB849363C0FF8C632E", "7D474CA73395EE5070B93C005D42601A37B2ADF1AEFEA2C6A5FDE6CE56E445DB", 265634126)]
    public void Primary_tree_cave_exclusion_has_strict_fifty_tile_edges(
        int caveOffset, string expectedGrid, string expectedRandom, int expectedNext)
    {
        // Actual registered original LivingTrees lambda; repeated five-case capture.
        // Source1458's first eligible column is2518 (label from a separate original UnifiedRandom).
        // A floating stone line blocks the grower's later clearance, so this isolates the cave guard's
        // admission and RNG consumption without growing a tree, opening a room or invoking loot.
        var workspace = new Workspace(4200, 600);
        Assert.True(workspace.TrySetLayers(240, 260));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        workspace.SetVanillaMountainCaves([new WorldGenerationPoint(2518 + caveOffset, 200)]);
        for (int x = 0; x < 4200; x++)
        {
            var dirt = new WorldTile { Type = 0, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, 200, in dirt);
            if (x == 2518) continue;
            var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, 80, in stone);
        }
        var random = new RandomAdapter(1458);
        new JungleStructurePass1458(JungleStructureStage1458.LivingTrees, new JungleStructureState1458())
            .Execute(new Context(workspace, random));
        Assert.Equal(expectedGrid, GenerationNaturalPrefix49Tests.GridHash(workspace));
        Assert.Equal(expectedRandom, GenerationNaturalPrefix49Tests.RandomHash(random, includeReport: false));
        Assert.Equal(expectedNext, random.Next());
        Assert.Equal(0, workspace.GeneratedChestCount);
    }
    private sealed class Context(Workspace workspace, IWorldGenerationVanillaRandom random) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => new(Provider1458.GeneratorId, "CaveBoundary", 1458, 4200, 600);
        public IWorldGenerationWorkspace Workspace => workspace;
        public IWorldGenerationMetadataWorkspace Metadata => workspace;
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom VanillaRandom => random;
        public CancellationToken CancellationToken => CancellationToken.None;
        public void ReportProgress(double fraction, string? message = null) { }
    }
    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next(); public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
