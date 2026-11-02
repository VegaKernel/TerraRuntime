using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GenerationSign1458Tests
{
    [Theory]
    [InlineData(1, "6D69C640E388583CC32EEAA4612560D9E6A74B5E6E5615497690356481C17A8B")]
    [InlineData(1458, "2CC082076B27FCD746102B788707258A63286ABB7BD760D1B79068A93E4610B4")]
    public void Single_sign_frame_platform_support_matches_original_frames_and_zero_choice_rng(int seed, string expected)
    {
        var results = new StringBuilder();
        short[] frames = [short.MinValue, -19, -18, -17, -1, 0, 1, 17, 18, 126, 143,
            144, 198, 216, 288, 305, 306, 432, 450, 468, 485, 486, short.MaxValue];
        foreach (short frame in frames)
        foreach (bool half in new[] { false, true })
        for (byte slope = 0; slope <= 4; slope++)
        foreach (bool actuated in new[] { false, true })
        {
            var workspace = new Workspace(80, 80);
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            {
                var tile = new WorldTile { Type = 55, FrameX = (short)(x * 18), FrameY = (short)(y * 18),
                    Flags = WorldTileFlags.Active };
                workspace.TileStore.Set(40 + x, 40 + y, in tile);
            }
            for (int x = 0; x < 2; x++)
            {
                var platform = new WorldTile { Type = 19, FrameX = frame, Flags = WorldTileFlags.Active |
                    (actuated ? WorldTileFlags.Inactive : WorldTileFlags.None) };
                Assert.True(platform.TrySetSourceShape(half, slope));
                workspace.TileStore.Set(40 + x, 42, in platform);
            }
            var random = new RandomAdapter(seed);
            new GenerationTileFraming1458(workspace.TileStore, random).TileFrame(40, 40);
            Assert.True(random.HasInitialState(seed));
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            {
                WorldTile tile = workspace.TileStore.Get(40 + x, 40 + y);
                results.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',')
                    .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(';');
            }
            results.Append(random.Next()).Append('|');
        }
        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(results.ToString()))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Single_sign_frame_has_no_extra_halo_on_success_or_after_removal(bool broken)
    {
        // Independent single-call original observations: a successful sign leaves a nearby
        // unsupported torch untouched; broken-sign KillTile frames neighbours only while
        // destroyObject is raised, leaving an adjacent unsupported AnnouncementBox intact.
        var workspace = new Workspace(80, 80);
        void Put(int x, int y, ushort type, short fx = 0, short fy = 0)
        {
            var tile = new WorldTile { Type = type, FrameX = fx, FrameY = fy, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(x, y, in tile);
        }
        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 2; y++)
            Put(40 + x, 40 + y, 55, (short)(x * 18), (short)(y * 18));
        if (broken)
        {
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
                Put(42 + x, 40 + y, 425, (short)(x * 18), (short)(y * 18));
        }
        else
        {
            Put(40, 42, 1);
            Put(41, 42, 1);
            Put(39, 39, 4);
        }
        var random = new RandomAdapter(1458);
        new GenerationTileFraming1458(workspace.TileStore, random).TileFrame(40, 40);
        Assert.Equal(!broken, workspace.TileStore.Get(40, 40).IsActive);
        Assert.True(workspace.TileStore.Get(broken ? 42 : 39, broken ? 40 : 39).IsActive);
        Assert.True(random.HasInitialState(1458));
        Assert.Equal(906992634, random.Next());
    }

    [Fact]
    public void Final_cleanup_five_empty_metadata_sign_families_match_official_pass()
    {
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        Populate((x, y, type, fx, fy, active, half, slope, actuated, wall) =>
        {
            var tile = new WorldTile { Type = type, FrameX = fx, FrameY = fy, Wall = wall,
                Flags = (active ? WorldTileFlags.Active : WorldTileFlags.None) |
                    (actuated ? WorldTileFlags.Inactive : WorldTileFlags.None) };
            Assert.True(tile.TrySetSourceShape(half, slope));
            workspace.TileStore.Set(x, y, in tile);
        });
        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "EmptySignMetadata", 1458, 1000, 500), workspace, random));
        // Both original netMode0/1 give this grid. Mode1 skips the unrelated
        // AnglerQuestSwap epilogue; mode0's next value is261937618.
        Assert.Equal("D9B6967894A2A34FB92EE1F23AE6D8BCE17724BB80EF4305D14461484E0FEEDD", Hash(workspace));
        Assert.Equal(1056314798, random.Next());
        int[] originalOrientations =
        [
            0, 0, 1, -1, -1, -1, -1, 0, -1, -1, 0, -1, 0, -1,
            1, 1, 0, -1, -1, -1, -1, 1, -1, 1, 1, -1, -1, 1,
            2, 2, 0, -1, -1, -1, -1, 2, -1, -1, 2, -1, -1, -1,
            3, 3, 0, -1, -1, -1, -1, 3, -1, -1, 3, -1, -1, -1,
            4, 4, 0, -1, -1, -1, -1, 4, 4, 4, 4, -1, 4, 4
        ];
        for (int index = 0; index < 350; index++)
        {
            WorldTile tile = workspace.TileStore.Get(30 + index % 24 * 40, 40 + index / 24 * 30);
            int orientation = originalOrientations[index % 70];
            Assert.Equal(orientation >= 0, tile.IsActive);
            Assert.Equal(orientation >= 0 ? orientation * 36 : -1, tile.FrameX);
        }
    }

    // Author-written common predecessor input. The original-only probe receives the same rows;
    // its registered pass and independent result determine the golden, not this setup routine.
    internal static void Populate(Action<int, int, ushort, short, short, bool, bool, byte, bool, ushort> write)
    {
        void Put(int x, int y, ushort type, int fx = 0, int fy = 0, bool active = true,
            bool half = false, byte slope = 0, bool actuated = false, ushort wall = 0) =>
            write(x, y, type, (short)fx, (short)fy, active, half, slope, actuated, wall);
        int index = 0;
        foreach (ushort type in new ushort[] { 55, 425, 510, 511, 573 })
        for (int orientation = 0; orientation < 5; orientation++)
        for (int scenario = 0; scenario < 14; scenario++, index++)
        {
            int left = 30 + index % 24 * 40;
            int top = 40 + index / 24 * 30;
            int styleY = scenario % 2 * 36;
            bool walls = orientation == 4 || scenario is 1 or 2;
            for (int column = 0; column < 2; column++)
            for (int row = 0; row < 2; row++)
            {
                bool last = column == 1 && row == 1;
                int extraX = scenario == 7 ? 13 : 0;
                int extraY = scenario == 7 ? 11 : 0;
                Put(left + column, top + row, scenario == 5 && last ? (ushort)1 : type,
                    orientation * 36 + column * 18 + extraX,
                    styleY + row * 18 + extraY + (scenario == 6 && last ? 18 : 0),
                    active: scenario != 4 || !last,
                    half: orientation == 4 && scenario is 9 or 13,
                    slope: orientation == 4 && scenario == 13 ? (byte)3 : (byte)0,
                    actuated: orientation == 4 && scenario == 8,
                    wall: walls && scenario != 3 && !(scenario == 11 && last) ? (ushort)1 : (ushort)0);
            }
            void Anchor(int side, bool enabled = true, bool half = false, byte slope = 0, bool actuated = false)
            {
                for (int member = 0; member < 2; member++)
                {
                    int x = side is 0 or 1 ? left + member : side == 2 ? left - 1 : left + 2;
                    int y = side == 0 ? top + 2 : side == 1 ? top - 1 : top + member;
                    Put(x, y, 1, active: enabled, half: half, slope: slope, actuated: actuated);
                }
            }
            if (scenario is 1 or 2)
            {
                for (int side = 0; side < 4; side++) Anchor(side);
                if (scenario == 2)
                {
                    if (orientation < 4) Anchor(orientation, enabled: false);
                    else
                        for (int column = 0; column < 2; column++)
                        for (int row = 0; row < 2; row++)
                            Put(left + column, top + row, type, orientation * 36 + column * 18,
                                styleY + row * 18);
                }
            }
            else if (scenario != 3 && orientation < 4)
                Anchor(orientation);
            if (orientation < 4)
            {
                if (scenario == 8) Anchor(orientation, actuated: true);
                if (scenario == 9) Anchor(orientation, half: true);
                byte acceptedSlope = orientation switch { 0 => 3, 1 => 1, 2 => 2, _ => 1 };
                byte rejectedSlope = orientation switch { 0 => 1, 1 => 3, 2 => 1, _ => 2 };
                if (scenario == 10) Anchor(orientation, slope: acceptedSlope);
                if (scenario == 11) Anchor(orientation, slope: rejectedSlope);
                if (scenario == 13) Anchor(orientation, half: true, slope: acceptedSlope);
            }
            if (scenario == 12)
            {
                if (orientation is 0 or 1 or 4)
                {
                    int trapY = orientation == 1 ? top - 1 : top + 2;
                    Put(left, trapY, 387);
                    Put(left + 1, trapY, 387, fx: 18);
                    Put(left - 1, trapY, 1);
                    Put(left + 2, trapY, 1);
                }
                else
                {
                    int gateX = orientation == 2 ? left - 1 : left + 2;
                    for (int row = 0; row < 5; row++)
                        Put(gateX, top - 1 + row, 388, fy: row == 0 ? 0 : 20 + (row - 1) * 18);
                    Put(gateX, top - 2, 1);
                    Put(gateX, top + 4, 1);
                }
            }
        }
    }

    private static string Hash(Workspace workspace)
    {
        var text = new StringBuilder();
        for (int x = 0; x < 1000; x++)
        for (int y = 0; y < 500; y++)
        {
            WorldTile tile = workspace.TileStore.Get(x, y);
            text.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',').Append(tile.Wall)
                .Append(',').Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',').Append(tile.LiquidAmount).Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
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

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public bool HasInitialState(int initialSeed) => random.HasSameState(new VanillaUnifiedRandom1458(initialSeed));
        public int Next() => random.Next();
        public int Next(int maximum) => random.Next(maximum);
        public int Next(int minimum, int maximum) => random.Next(minimum, maximum);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
