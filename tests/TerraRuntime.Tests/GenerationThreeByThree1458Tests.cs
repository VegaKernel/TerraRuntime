using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationThreeByThree1458Tests
{
    [Fact]
    public void Registered_final_cleanup_matches_all_thirty_three_by_three_identities()
    {
        // Untouched official TerrariaServer1.4.5.8 FinalCleanup, netMode1, 1000x500 seed1458.
        // Twenty-eight floor identities plus both Check3x3Wall identities, ten variants each,
        // and a stacked Giant Tiki pair. netMode1 skips the unrelated AnglerQuestSwap epilogue.
        var workspace = CreateWorkspace();

        void Put(int x, int y, ushort type, short fx = 0, short fy = 0, ushort wall = 0, byte shape = 0, bool inactive = false)
        {
            var tile = new WorldTile { Type = type, FrameX = fx, FrameY = fy, Wall = wall, Shape = shape,
                Flags = WorldTileFlags.Active | (inactive ? WorldTileFlags.Inactive : WorldTileFlags.None) };
            workspace.TileStore.Set(x, y, in tile);
        }

        ushort[] identities = [106, 212, 219, 220, 228, 243, 247, 283, 300, 301, 302, 303, 304, 305, 306, 307, 308, 354, 355, 406, 412, 452, 455, 491, 499, 642, 733, 753, 240, 440];
        for (int n = 0; n < identities.Length; n++)
        {
            ushort type = identities[n];
            int x = 60 + n % 8 * 112;
            int bandY = 55 + n / 8 * 110;
            bool wallObject = type is 240 or 440;
            for (int scenario = 0; scenario < 10; scenario++)
            {
                int y = bandY + scenario * 10;
                int style = scenario == 3 ? 54 : 0;
                for (int dx = 0; dx < 3; dx++)
                for (int dy = 0; dy < 3; dy++)
                    if (scenario != 1 || dx != 2 || dy != 2)
                        Put(x + dx, y + dy, type, (short)(style + dx * 18), (short)(style + dy * 18), wall: wallObject ? (ushort)1 : (ushort)0);
                if (wallObject)
                {
                    if (scenario == 2)
                        Put(x + 2, y + 2, type, 36, 36);
                    if (scenario == 5)
                        Put(x + 1, y + 1, type, 18, 18, wall: 1, inactive: true);
                    if (scenario == 6)
                        Put(x + 1, y + 1, type, 18, 18, wall: 2);
                    if (scenario == 7)
                        Put(x + 1, y + 1, type, 18, 18, wall: 1, shape: 1);
                    if (scenario == 8)
                        Put(x + 2, y + 2, 1, wall: 1);
                    if (scenario == 9)
                        for (int dx = 0; dx < 3; dx++)
                        for (int dy = 0; dy < 3; dy++)
                            Put(x + dx, y + dy, type, (short)(dx * 18), (short)(dy * 18));
                }
                else
                {
                    for (int dx = 0; dx < 3; dx++)
                        if (scenario != 2 || dx != 2)
                            Put(x + dx, y + 3, 1, shape: (byte)(scenario == 6 ? 4 : scenario == 7 ? 2 : scenario == 8 ? 1 : 0), inactive: scenario == 5 && dx == 1);
                    if (scenario == 9)
                        Put(x + 2, y + 2, 1);
                }
                if (scenario == 4)
                    Put(x + 2, y + 2, type, 18, 36, wall: wallObject ? (ushort)1 : (ushort)0);
            }
            if (type == 753)
            {
                int y = bandY + 100;
                for (int objectRow = 0; objectRow < 2; objectRow++)
                for (int dx = 0; dx < 3; dx++)
                for (int dy = 0; dy < 3; dy++)
                    Put(x + dx, y + objectRow * 3 + dy, type, (short)(dx * 18), (short)(dy * 18));
                for (int dx = 0; dx < 3; dx++)
                    Put(x + dx, y + 6, 1);
            }
        }

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));
        Assert.Equal("44059F86228733793E1F1B390B8CA68B9A7C89EC19AAA6D4BF1A264B69459001", HashFixture(workspace));
        Assert.Equal(320683462, random.Next());
        for (int n = 0; n < identities.Length; n++)
        for (int scenario = 0; scenario < 10; scenario++)
        {
            bool wallObject = identities[n] is 240 or 440;
            bool survives = wallObject ? scenario is 0 or 3 or 5 or 6 or 7 : scenario is 0 or 3 or 6;
            Assert.Equal(survives, workspace.TileStore.Get(60 + n % 8 * 112, 55 + n / 8 * 110 + scenario * 10).IsActive);
        }
        Assert.Equal((ushort)753, workspace.TileStore.Get(396, 485).Type);
        Assert.True(workspace.TileStore.Get(396, 485).IsActive);
        Assert.True(workspace.TileStore.Get(396, 488).IsActive);
    }

    [Theory]
    [InlineData(true, "F1C54676E405CA05DA6B732453CC0817CDB06F12CE2DD7E40F6B552AFCD6C75D", 1948299158)]
    [InlineData(false, "ACE43F85542BACA3FE05BC2CE8B50557F94C7A5F32540B34451D318DD5D5EECA", 906992634)]
    public void Direct_frame_preserves_the_family_specific_neighbour_revalidation_boundary(bool floorTrigger, string digest, int nextRandom)
    {
        // An actual official TileFrame(903,330) call. The broken neighbour is to the LEFT of
        // the triggering object: Check3x3's halo must revisit it; Check3x3Wall must not do so.
        // A complete outer scan would conceal this ordering difference by visiting both later.
        var workspace = CreateWorkspace();

        void Put(int x, int y, ushort type, short fx = 0, short fy = 0, ushort wall = 0, byte shape = 0, bool inactive = false)
        {
            var tile = new WorldTile { Type = type, FrameX = fx, FrameY = fy, Wall = wall, Shape = shape,
                Flags = WorldTileFlags.Active | (inactive ? WorldTileFlags.Inactive : WorldTileFlags.None) };
            workspace.TileStore.Set(x, y, in tile);
        }

        for (int objectIndex = 0; objectIndex < 2; objectIndex++)
        {
            int x = objectIndex == 0 ? 903 : 900;
            ushort type = (objectIndex == 0) == floorTrigger ? (ushort)243 : (ushort)240;
            for (int dx = 0; dx < 3; dx++)
            for (int dy = 0; dy < 3; dy++)
                if (dy != 2 || dx != (objectIndex == 0 ? 2 : 0))
                    Put(x + dx, 330 + dy, type, (short)(dx * 18), (short)(dy * 18), wall: type == 240 ? (ushort)1 : (ushort)0);
            if (type == 243)
                for (int dx = 0; dx < 3; dx++)
                    Put(x + dx, 333, 1);
        }

        var random = new RandomAdapter(1458);
        new GenerationTileFraming1458(workspace.TileStore, random).TileFrame(903, 330);
        Assert.Equal(digest, HashFixture(workspace));
        Assert.Equal(nextRandom, random.Next());
        int remainingNeighbours = 0;
        for (int x = 900; x < 903; x++)
        for (int y = 330; y < 333; y++)
            if (workspace.TileStore.Get(x, y).IsActive)
                remainingNeighbours++;
        Assert.Equal(floorTrigger ? 0 : 8, remainingNeighbours);
    }

    [Theory]
    [InlineData((byte)3, false, "49E5ECB5197A19DB9F07A2516247A8C6068E597486E8E7252A39A90614C61C8F", 906992634)]
    [InlineData((byte)4, false, "49E5ECB5197A19DB9F07A2516247A8C6068E597486E8E7252A39A90614C61C8F", 906992634)]
    [InlineData((byte)3, true, "6EC117CE3E1907ABFE168A11B1738B6D7710DF43D9DA2FCB606E5C0772E8E885", 1566222742)]
    [InlineData((byte)4, true, "6EC117CE3E1907ABFE168A11B1738B6D7710DF43D9DA2FCB606E5C0772E8E885", 1566222742)]
    public void Independent_halfbrick_flag_rejects_otherwise_valid_bottom_sloped_support(byte slope, bool halfBrick, string digest, int nextRandom)
    {
        // Actual original SolidTileAllowBottomSlope and TileFrame(903,330): independent halfBrick
        // remains a refusal even beside a bottom slope. Shape4/5 + HalfBrickWithSlope is valid state.
        var workspace = CreateWorkspace();
        for (int dx = 0; dx < 3; dx++)
        {
            for (int dy = 0; dy < 3; dy++)
            {
                var member = new WorldTile { Type = 243, FrameX = (short)(dx * 18), FrameY = (short)(dy * 18), Flags = WorldTileFlags.Active };
                workspace.TileStore.Set(903 + dx, 330 + dy, in member);
            }
            var support = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
            Assert.True(support.TrySetSourceShape(halfBrick, slope));
            Assert.True(support.HasValidSourceShape);
            Assert.Equal(halfBrick, support.IsHalfBrick);
            Assert.Equal(slope, support.SourceSlope);
            workspace.TileStore.Set(903 + dx, 333, in support);
        }
        var random = new RandomAdapter(1458);
        new GenerationTileFraming1458(workspace.TileStore, random).TileFrame(903, 330);
        Assert.Equal(digest, HashFixture(workspace));
        Assert.Equal(nextRandom, random.Next());
        Assert.Equal(!halfBrick, workspace.TileStore.Get(903, 330).IsActive);
    }

    private static Workspace CreateWorkspace()
    {
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, false));
        return workspace;
    }

    private static string HashFixture(Workspace workspace)
    {
        var snapshot = new StringBuilder(workspace.WidthTiles * workspace.HeightTiles * 20);
        for (int x = 0; x < workspace.WidthTiles; x++)
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            WorldTile tile = workspace.TileStore.Get(x, y);
            snapshot.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',').Append(tile.LiquidAmount).Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToString())));
    }

    private sealed class Context(WorldGenerationRequest request, Workspace workspace, IWorldGenerationVanillaRandom random) : IWorldGenerationContext
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
        public int Next(int maxValue) => random.Next(maxValue);
        public int Next(int minValue, int maxValue) => random.Next(minValue, maxValue);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
