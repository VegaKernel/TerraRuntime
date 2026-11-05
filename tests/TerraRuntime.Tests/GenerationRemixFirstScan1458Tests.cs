using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationRemixFirstScan1458Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void First_scan_preserves_original_cloud_and_wall_mapping_including_drunk_boundary(bool drunk)
    {
        // Direct original 1.4.5.8 WorldGen.FinishRemixWorld helper on this all-air
        // 600x500, worldSurface50 input. The helper preserves all three converted
        // active cloud identities and their walls after its later scans.
        // Source profile uses GenVars.crimsonLeft=true and genRand seed1458.
        Workspace workspace = new(600, 500);
        workspace.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.Remix |
            (drunk ? VanillaSpecialWorldSeed1458.DrunkWorld : VanillaSpecialWorldSeed1458.None),
            VanillaSecretWorldSeed1458.None));
        workspace.SetVanillaBootstrapState(Bootstrap(crimsonLeft: true));
        Put(workspace, 100, 150, 189, 73);
        Put(workspace, 120, 160, 202, 82);
        Put(workspace, 140, 170, 717, 73);
        Put(workspace, 160, 180, 0, 82);
        for (int x = 298; x <= 302; x++)
            Put(workspace, x, 150, 189, 73);
        CountingRandom random = new(1458);

        int deepestCloud = FinalPass1458.FinishRemixFirstScan(workspace, random,
            TestContext.Current.CancellationToken);

        Assert.Equal(170, deepestCloud);
        Assert.Equal((ushort)(drunk ? 195 : 474), workspace.TileStore.Get(100, 150).Type);
        Assert.Equal((ushort)(drunk ? 77 : 233), workspace.TileStore.Get(100, 150).Wall);
        Assert.Equal((ushort)(drunk ? 195 : 474), workspace.TileStore.Get(120, 160).Type);
        Assert.Equal((ushort)(drunk ? 195 : 474), workspace.TileStore.Get(140, 170).Type);
        Assert.Equal((ushort)(drunk ? 77 : 233), workspace.TileStore.Get(120, 160).Wall);
        Assert.Equal((ushort)(drunk ? 77 : 233), workspace.TileStore.Get(140, 170).Wall);
        Assert.Equal((ushort)(drunk ? 77 : 233), workspace.TileStore.Get(160, 180).Wall);
        ushort[] expectedTiles = drunk ? [195, 195, 195, 474, 474] : [474, 474, 474, 474, 474];
        ushort[] expectedWalls = drunk ? [77, 77, 77, 233, 233] : [233, 233, 233, 233, 233];
        for (int offset = 0; offset < 5; offset++)
        {
            Assert.Equal(expectedTiles[offset], workspace.TileStore.Get(298 + offset, 150).Type);
            Assert.Equal(expectedWalls[offset], workspace.TileStore.Get(298 + offset, 150).Wall);
        }
        Assert.Equal(drunk ? 17 : 0, random.Count);
    }

    [Fact]
    public void Anniversary_without_worthy_records_depth_but_does_not_convert_clouds_or_walls()
    {
        // Direct original helper with tenthAnniversaryWorldGen=true preserves the
        // same cloud189/wall73 at x100 and all five centre sites after return.
        Workspace workspace = new(600, 500);
        workspace.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.Remix |
            VanillaSpecialWorldSeed1458.CelebrationMk10, VanillaSecretWorldSeed1458.None));
        workspace.SetVanillaBootstrapState(Bootstrap(crimsonLeft: false));
        Put(workspace, 100, 150, 189, 73);
        CountingRandom random = new(1458);

        Assert.Equal(150, FinalPass1458.FinishRemixFirstScan(workspace, random,
            TestContext.Current.CancellationToken));
        Assert.Equal((ushort)189, workspace.TileStore.Get(100, 150).Type);
        Assert.Equal((ushort)73, workspace.TileStore.Get(100, 150).Wall);
        Assert.Equal(0, random.Count);
    }

    [Fact]
    public void Ordinary_crimson_remix_uses_retained_effective_evil_without_rng()
    {
        // Original helper with WorldGen.crimson=true and no Drunk sets the five
        // centre cloud189 sites to195/wall77, irrespective of their x position.
        Workspace workspace = new(600, 500);
        workspace.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.Remix,
            VanillaSecretWorldSeed1458.None));
        workspace.SetVanillaBootstrapState(new VanillaWorldGenerationBootstrapState1458
        {
            EffectiveCrimson = true,
            CrimsonLeft = false,
            HellChestItems = [], TreeX = [], TreeStyle = [], CaveBackX = [],
            CaveBackStyle = [], ForestBackgroundStyles = []
        });
        for (int x = 298; x <= 302; x++)
            Put(workspace, x, 150, 189, 73);
        CountingRandom random = new(1458);

        Assert.Equal(150, FinalPass1458.FinishRemixFirstScan(workspace, random,
            TestContext.Current.CancellationToken));
        for (int x = 298; x <= 302; x++)
        {
            Assert.Equal((ushort)195, workspace.TileStore.Get(x, 150).Type);
            Assert.Equal((ushort)77, workspace.TileStore.Get(x, 150).Wall);
        }
        Assert.Equal(0, random.Count);
    }

    private static void Put(Workspace workspace, int x, int y, ushort type, ushort wall)
    {
        WorldTile tile = new() { Type = type, Wall = wall,
            Flags = type == 0 ? WorldTileFlags.None : WorldTileFlags.Active };
        workspace.TileStore.Set(x, y, tile);
    }

    private static VanillaWorldGenerationBootstrapState1458 Bootstrap(bool crimsonLeft) => new()
    {
        EffectiveCrimson = false,
        CrimsonLeft = crimsonLeft,
        HellChestItems = [], TreeX = [], TreeStyle = [], CaveBackX = [],
        CaveBackStyle = [], ForestBackgroundStyles = []
    };

    private sealed class CountingRandom(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 source = new(seed);
        public int Count { get; private set; }
        public int Next() { Count++; return source.Next(); }
        public int Next(int max) { Count++; return source.Next(max); }
        public int Next(int min, int max) { Count++; return source.Next(min, max); }
        public double NextDouble() { Count++; return source.NextDouble(); }
        public void NextBytes(byte[] bytes) { Count++; source.NextBytes(bytes); }
    }
}
