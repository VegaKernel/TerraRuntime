using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GenerationPotsAndPlants1458Tests
{
    [Theory]
    [InlineData(1, "30D9C209BDFDE30B30FA9B85A27965A1E2B850415345B00DC344A8DF36E645FB")]
    [InlineData(1458, "2085CF41792B9EB62F11FD7ADC83DEC25E1C5A35EA11D10F77B76B952D401AB2")]
    public void Pot_styles_and_floor_shapes_match_original_with_conditional_gore_argument_rng(int seed, string expected)
    {
        var result = new StringBuilder();
        foreach (ushort type in new ushort[] { 28, 653 })
            for (int style = 0; style < 40; style++)
                foreach (ushort floorType in new ushort[] { 1, 19 })
                    foreach (bool half in new[] { false, true })
                        for (byte slope = 0; slope <= 4; slope++)
                            foreach (bool actuated in new[] { false, true })
                            {
                                var workspace = new Workspace(80, 80);
                                for (int x = 0; x < 2; x++)
                                {
                                    for (int y = 0; y < 2; y++)
                                    {
                                        var member = new WorldTile
                                        {
                                            Type = type,
                                            FrameX = (short)(x * 18),
                                            FrameY = (short)(style * 36 + y * 18),
                                            Flags = WorldTileFlags.Active
                                        };
                                        workspace.TileStore.Set(40 + x, 40 + y, in member);
                                    }
                                    var floor = new WorldTile
                                    {
                                        Type = floorType,
                                        Flags = WorldTileFlags.Active |
                                        (actuated ? WorldTileFlags.Inactive : WorldTileFlags.None)
                                    };
                                    Assert.True(floor.TrySetSourceShape(half, slope));
                                    workspace.TileStore.Set(40 + x, 42, in floor);
                                }
                                var random = new RandomAdapter(seed);
                                new GenerationTileFraming1458(workspace.TileStore, random).TileFrame(40, 40);
                                for (int x = 0; x < 2; x++)
                                    for (int y = 0; y < 2; y++)
                                    {
                                        WorldTile tile = workspace.TileStore.Get(40 + x, 40 + y);
                                        result.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',')
                                            .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(';');
                                    }
                                for (int sample = 0; sample < 5; sample++)
                                    result.Append(random.Next()).Append(',');
                                result.Append('|');
                            }
        Assert.Equal(expected, Digest(result));
    }

    [Fact]
    public void Final_cleanup_pots_oasis_and_coral_match_registered_original_pass()
    {
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, TerraRuntime.World.VanillaSpecialWorldSeed1458.None));
        Populate((x, y, type, fx, fy, active, half, slope, actuated) =>
        {
            var tile = new WorldTile
            {
                Type = type,
                FrameX = fx,
                FrameY = fy,
                Flags = (active ? WorldTileFlags.Active : WorldTileFlags.None) |
                    (actuated ? WorldTileFlags.Inactive : WorldTileFlags.None)
            };
            Assert.True(tile.TrySetSourceShape(half, slope));
            workspace.TileStore.Set(x, y, in tile);
        });
        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "PotsAndPlants", 1458, 1000, 500), workspace, random));
        var result = new StringBuilder();
        for (int x = 0; x < 1000; x++)
            for (int y = 0; y < 500; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                result.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',').Append(tile.Wall)
                    .Append(',').Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',').Append(tile.LiquidAmount).Append(';');
            }
        Assert.Equal("7CB5F4F35B2A1A8FB2DB3397DE070C08312691EA4FD6B94A985129BC0308CB87", GenerationNaturalPrefix49Tests.GridHash(workspace));
        Assert.Equal("E7B25872A2FA97D1B7340E5B297820F502F3E2F6F0350801536C1B15DC3010E7", GenerationNaturalPrefix49Tests.RandomHash(random, includeReport: false));
        Assert.Equal(38, workspace.VanillaAnglerQuest!.Quest);
        Assert.Equal("19D646FB13A7AADA53E0A5C67D659A6F37F8C618D9E1FF09F5E33A7D474AD229", Digest(result));
        Assert.Equal(2027167023, random.Next());
    }

    [Theory]
    [InlineData((ushort)28, true)]
    [InlineData((ushort)653, true)]
    [InlineData((ushort)530, false)]
    public void Removed_object_halo_matches_original_guarded_neighbor_behavior(ushort type, bool neighborSurvives)
    {
        var workspace = new Workspace(80, 80);
        void Put(int x, int y, ushort tileType, short fx, short fy)
        {
            var tile = new WorldTile { Type = tileType, FrameX = fx, FrameY = fy, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, y, in tile);
        }
        int columns = type == 530 ? 3 : 2;
        for (int x = 0; x < columns; x++)
            for (int y = 0; y < 2; y++)
                Put(40 + x, 40 + y, type, (short)(x * 18), (short)(y * 18));
        for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
                Put(40 + columns + x, 40 + y, 425, (short)(x * 18), (short)(y * 18));
        var random = new RandomAdapter(1458);
        new GenerationTileFraming1458(workspace.TileStore, random).TileFrame(40, 40);
        Assert.False(workspace.TileStore.Get(40, 40).IsActive);
        Assert.Equal(neighborSurvives, workspace.TileStore.Get(40 + columns, 40).IsActive);
        Assert.Equal(906992634, random.Next());
    }

    [Fact]
    public void Sand_conversion_and_coral_actuation_profiles_match_direct_original()
    {
        var result = new StringBuilder();
        foreach (ushort type in new ushort[] { 530, 81 })
            foreach (ushort floorType in new ushort[] { 1, 19, 53, 112, 116, 234, 397 })
                foreach (bool half in new[] { false, true })
                    for (byte slope = 0; slope <= 4; slope++)
                        foreach (bool actuated in new[] { false, true })
                            for (int above = 0; above < (type == 81 ? 3 : 1); above++)
                            {
                                var workspace = new Workspace(80, 80);
                                int columns = type == 530 ? 3 : 1, rows = type == 530 ? 2 : 1;
                                for (int x = 0; x < columns; x++)
                                {
                                    for (int y = 0; y < rows; y++)
                                    {
                                        var tile = new WorldTile
                                        {
                                            Type = type,
                                            FrameX = (short)(x * 18),
                                            FrameY = (short)(y * 18),
                                            Flags = WorldTileFlags.Active
                                        };
                                        workspace.TileStore.Set(40 + x, 40 + y, in tile);
                                    }
                                    var floor = new WorldTile
                                    {
                                        Type = floorType,
                                        Flags = WorldTileFlags.Active |
                                        (actuated ? WorldTileFlags.Inactive : WorldTileFlags.None)
                                    };
                                    Assert.True(floor.TrySetSourceShape(half, slope));
                                    workspace.TileStore.Set(40 + x, 40 + rows, in floor);
                                }
                                if (above > 0)
                                {
                                    var roof = new WorldTile
                                    {
                                        Type = 1,
                                        Flags = WorldTileFlags.Active |
                                        (above == 2 ? WorldTileFlags.Inactive : WorldTileFlags.None)
                                    };
                                    workspace.TileStore.Set(40, 39, in roof);
                                }
                                var random = new RandomAdapter(1458);
                                new GenerationTileFraming1458(workspace.TileStore, random).TileFrame(40, 40);
                                for (int x = 0; x < columns; x++)
                                    for (int y = 0; y < rows; y++)
                                    {
                                        WorldTile tile = workspace.TileStore.Get(40 + x, 40 + y);
                                        result.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',')
                                            .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(';');
                                    }
                                result.Append(random.Next()).Append('|');
                            }
        Assert.Equal("13B66D1A36C0D43A9B635522791546F550EE0C130FFB95099D452D39BF5632CB", Digest(result));
    }

    internal static void Populate(Action<int, int, ushort, short, short, bool, bool, byte, bool> put)
    {
        for (int index = 0; index < 360; index++)
        {
            int left = 24 + index % 26 * 36, top = 30 + index / 26 * 30;
            void Put(int x, int y, ushort type, int fx = 0, int fy = 0, bool active = true,
                bool half = false, byte slope = 0, bool actuated = false) =>
                put(x, y, type, (short)fx, (short)fy, active, half, slope, actuated);
            if (index < 320)
            {
                ushort type = index < 160 ? (ushort)28 : (ushort)653;
                int style = index % 160 / 4, scenario = index % 4;
                for (int x = 0; x < 2; x++)
                {
                    for (int y = 0; y < 2; y++)
                        Put(left + x, top + y, scenario == 2 && x == 1 && y == 1 ? (ushort)1 : type,
                            x * 18 + (scenario == 3 ? 37 : 0), style * 36 + y * 18);
                    if (scenario != 1)
                        Put(left + x, top + 2, 1);
                }
                continue;
            }
            if (index < 340)
            {
                int scenario = index - 320;
                for (int x = 0; x < 3; x++)
                {
                    for (int y = 0; y < 2; y++)
                        Put(left + x, top + y, scenario == 3 && x == 2 && y == 1 ? (ushort)1 : (ushort)530,
                            x * 18 + scenario / 10 * 54, y * 18 + (scenario == 4 ? 36 : 0),
                            active: !(scenario == 5 && x == 0 && y == 1));
                    if (scenario != 1)
                        Put(left + x, top + 2, scenario == 2 ? (ushort)1 : (ushort)53,
                            half: scenario == 6, slope: (byte)(scenario is 7 or 8 ? scenario - 6 : 0), actuated: scenario == 9);
                }
                if (scenario == 1)
                    Put(left - 1, top - 1, 4);
                continue;
            }
            int coralCase = index - 340;
            Put(left, top, 81, coralCase * 18);
            Put(left, top + 1, coralCase == 1 ? (ushort)0 : (ushort)1,
                active: coralCase != 2, half: coralCase is 3 or 9,
                slope: (byte)(coralCase is >= 4 and <= 7 ? coralCase - 3 : coralCase == 9 ? 3 : 0),
                actuated: coralCase == 8);
            if (coralCase >= 10)
                Put(left, top - 1, 1, active: coralCase != 11, actuated: coralCase == 12);
        }
    }

    private static string Digest(StringBuilder text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));

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

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int maximum) => random.Next(maximum);
        public int Next(int minimum, int maximum) => random.Next(minimum, maximum);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
