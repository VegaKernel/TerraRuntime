using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;
using System.Security.Cryptography;
using System.Text;

namespace TerraRuntime.Tests;

public sealed class GenerationFinalCleanupMissingObjects1458Tests
{
    [Fact]
    public void Pumpkin_and_legacy_rack_match_original_registered_final_cleanup_grid_and_rng()
    {
        // Direct original TerrariaServer.exe 1.4.5.8 PassLegacy("Final Cleanup"),
        // seed1458, netMode0, 1000x500. The pristine official assembly was run by
        // .cache/spider-probe with the exact fixture below; all six fields of all
        // 500000 cells and the post-pass Next() are independent expectations.
        Workspace workspace = new(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new CountingRandom(1), 4200,
            false, VanillaSpecialWorldSeed1458.None));
        void Pumpkin(int left, short style, ushort support)
        {
            for (int dx = 0; dx < 2; dx++)
            {
                for (int dy = 0; dy < 2; dy++)
                    Put(workspace.TileStore, left + dx, 100 + dy, 254,
                        (short)(style + dx * 18), (short)(dy * 18));
                Put(workspace.TileStore, left + dx, 102, support, 0, 0);
            }
        }
        Pumpkin(120, 0, 2);
        Pumpkin(180, 0, 1);
        Pumpkin(240, 72, 1);
        Pumpkin(300, 108, 1);
        for (int rack = 0; rack < 2; rack++)
            for (int dx = 0; dx < 3; dx++)
                for (int dy = 0; dy < 3; dy++)
                {
                    int left = 400 + rack * 70;
                    short frame = (short)(rack == 0 ? dx * 18 : (dx + 1) * 5000);
                    Put(workspace.TileStore, left + dx, 100 + dy, 334, frame, (short)(dy * 18));
                    WorldTile tile = workspace.TileStore.Get(left + dx, 100 + dy);
                    tile.Wall = (ushort)(rack == 1 && dx == 1 && dy == 1 ? 0 : 1);
                    workspace.TileStore.Set(left + dx, 100 + dy, tile);
                }

        CountingRandom random = new();
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500),
                workspace, random));

        Assert.Equal("83F42C618916DCC32D0C5EB38BF0B9D35940DFFF180225811A233A4564D6E52E", Hash(workspace));
        Assert.Equal(1588211491, random.Next());
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(109, true)]
    [InlineData(477, true)]
    [InlineData(492, true)]
    [InlineData(1, false)]
    public void Pumpkin_requires_one_of_the_four_source_grass_supports(int ground, bool survives)
    {
        WorldTileStore store = new(new WorldDimensions(80, 80));
        PutRectangle(store, 254, 20, 20, 2, 2, 0);
        for (int x = 20; x < 22; x++)
            Put(store, x, 22, (ushort)ground, 0, 0);

        new GenerationTileFraming1458(store, new CountingRandom()).TileFrame(20, 20);

        for (int x = 20; x < 22; x++)
            for (int y = 20; y < 22; y++)
                Assert.Equal(survives, store.Get(x, y).IsActive);
    }

    [Fact]
    public void Broken_pumpkin_spends_fragment_dust_before_style_reward_roll()
    {
        WorldTileStore store = new(new WorldDimensions(80, 80));
        PutRectangle(store, 254, 20, 20, 2, 2, 72);
        for (int x = 20; x < 22; x++)
            Put(store, x, 22, 1, 0, 0);
        CountingRandom actual = new();
        new GenerationTileFraming1458(store, actual).TileFrame(20, 20);

        // TerrariaServer 1.4.5.8 WorldGen.Check2x2Style kills all four fragments
        // before evaluating the style-2 seed reward; KillTile_MakeTileDust uses ten
        // appearance draws for each pumpkin fragment in this style band.
        VanillaUnifiedRandom1458 sourceSequence = new(1458);
        for (int draw = 0; draw < 40; draw++)
            sourceSequence.Next(3);
        int reward = sourceSequence.Next(2);
        if (reward == 0)
            sourceSequence.Next(1, 3);
        Assert.Equal(41 + (reward == 0 ? 1 : 0), actual.Count);
        Assert.Equal(sourceSequence.Next(), actual.Next());
        Assert.False(store.Get(20, 20).IsActive);
        Assert.False(store.Get(21, 21).IsActive);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 5000)]
    public void Legacy_weapons_rack_validates_wall_and_5000_frame_bands(bool removeWall, int band)
    {
        WorldTileStore store = new(new WorldDimensions(80, 80));
        for (int x = 0; x < 3; x++)
            for (int y = 0; y < 3; y++)
            {
                Put(store, 20 + x, 20 + y, 334,
                    (short)(band == 0 ? x * 18 : band * (x + 1)), (short)(y * 18));
                WorldTile tile = store.Get(20 + x, 20 + y);
                tile.Wall = (ushort)(removeWall && x == 1 && y == 1 ? 0 : 1);
                store.Set(20 + x, 20 + y, tile);
            }
        CountingRandom random = new();
        new GenerationTileFraming1458(store, random).TileFrame(21, 21);

        for (int x = 20; x < 23; x++)
            for (int y = 20; y < 23; y++)
                Assert.Equal(!removeWall, store.Get(x, y).IsActive);
        Assert.Equal(0, random.Count);
    }

    private static void PutRectangle(WorldTileStore store, ushort type, int left, int top,
        int width, int height, int frameX)
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                Put(store, left + x, top + y, type, (short)(frameX + x * 18), (short)(y * 18));
    }

    private static void Put(WorldTileStore store, int x, int y, ushort type, short frameX, short frameY) =>
        store.Set(x, y, new WorldTile { Type = type, Flags = WorldTileFlags.Active,
            FrameX = frameX, FrameY = frameY });

    private static string Hash(Workspace workspace)
    {
        StringBuilder snapshot = new(workspace.WidthTiles * workspace.HeightTiles * 20);
        for (int x = 0; x < workspace.WidthTiles; x++)
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                snapshot.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',')
                    .Append(tile.Wall).Append(',').Append(tile.FrameX).Append(',')
                    .Append(tile.FrameY).Append(',').Append(tile.LiquidAmount).Append(';');
            }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToString())));
    }

    private sealed class Context(WorldGenerationRequest request, Workspace workspace,
        IWorldGenerationVanillaRandom random) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => request;
        public IWorldGenerationWorkspace Workspace => workspace;
        public IWorldGenerationMetadataWorkspace Metadata => workspace;
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom VanillaRandom => random;
        public CancellationToken CancellationToken => CancellationToken.None;
        public void ReportProgress(double fraction, string? message = null) { }
    }

    private sealed class CountingRandom(int seed = 1458) : IWorldGenerationVanillaRandom
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
