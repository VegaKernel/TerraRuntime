using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationRemixSecondSparse1458Tests
{
    [Fact]
    public void Sparse_second_scan_matches_original_wall_death_rock_and_rng_cadence()
    {
        // Unmodified TerrariaServer.exe 1.4.5.8 WorldGen.FinishRemixWorld helper,
        // 1000x800/seed1458/worldSurface200/rockLayer201. The five measured cells
        // survive the helper's later QuickWater, spawn and tree passes unchanged.
        Workspace workspace = new(1000, 800);
        Assert.True(workspace.TrySetLayers(200, 201));
        workspace.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.Remix,
            VanillaSecretWorldSeed1458.None));
        workspace.SetVanillaBootstrapState(new VanillaWorldGenerationBootstrapState1458
        {
            EffectiveCrimson = false, CrimsonLeft = false,
            HellChestItems = [], TreeX = [], TreeStyle = [], CaveBackX = [],
            CaveBackStyle = [], ForestBackgroundStyles = []
        });
        Put(workspace, 100, 50, 0, 178);
        Put(workspace, 120, 180, 0, 180);
        Put(workspace, 140, 100, 56, 0, active: true);
        Put(workspace, 160, 250, 0, 0, active: true);
        Put(workspace, 500, 50, 0, 87);
        CountingRandom random = new(1458);
        GenerationTileFraming1458 framing = new(workspace.TileStore, random, workspace);

        int deepestCloud = FinalPass1458.FinishRemixFirstScan(workspace, random,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixSecondScanSparse(workspace, deepestCloud, random, framing,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, deepestCloud);
        Assert.Equal((ushort)0, workspace.TileStore.Get(100, 50).Wall);
        Assert.Equal((ushort)180, workspace.TileStore.Get(120, 180).Wall);
        Assert.False(workspace.TileStore.Get(140, 100).IsActive);
        Assert.Equal((ushort)0, workspace.TileStore.Get(140, 100).Type);
        Assert.True(workspace.TileStore.Get(160, 250).IsActive);
        Assert.Equal((ushort)2, workspace.TileStore.Get(160, 250).Type);
        Assert.Equal((ushort)87, workspace.TileStore.Get(500, 50).Wall);
        // The source calls Next(3) and Next(19,22) for every interior cell even
        // when Convert returns on air; this admitted normal profile has no split draws.
        Assert.Equal(2 * (1000 - 50) * (800 - 50), random.Count);

        // On this air fixture the rest of the original helper spends two AddTrees
        // density doubles, one grass2 branch offer, then the three cavern-style
        // draws. Its two QuickWater calls, spawn clearing and palette have no
        // shared RNG effect. Bridge those independently sourced tail calls to
        // the actual unmodified helper's final next value.
        random.NextDouble();
        random.NextDouble();
        if (random.Next(7) == 0)
            random.Next(2);
        random.Next(2, 5);
        random.Next(390, 610);
        random.Next(10, 31);
        Assert.Equal(1802539725, random.Next());
    }

    private static void Put(Workspace workspace, int x, int y, ushort type, ushort wall, bool active = false)
    {
        WorldTile tile = new() { Type = type, Wall = wall,
            Flags = active ? WorldTileFlags.Active : WorldTileFlags.None };
        workspace.TileStore.Set(x, y, tile);
    }

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
