using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationLargeObjects1458Tests
{
    [Fact]
    public void Registered_final_cleanup_matches_all_forty_five_large_object_identities()
    {
        // Independent untouched official1.4.5.8 registered FinalCleanup on a configured synthetic
        // predecessor: 41 six-by-three cages and four wall footprints, ten variants per identity.
        // netMode1 skips AnglerQuestSwap. Full grid six-field digest and all56 RNG slots are pinned.
        var workspace = CreateWorkspace();
        void Put(int x, int y, ushort type, short fx = 0, short fy = 0, ushort wall = 0, byte shape = 0, bool inactive = false, bool independentHalf = false)
        {
            var tile = new WorldTile { Type = type, FrameX = fx, FrameY = fy, Wall = wall,
                Flags = WorldTileFlags.Active | (inactive ? WorldTileFlags.Inactive : WorldTileFlags.None) };
            Assert.True(tile.TrySetSourceShape(shape == 1 || independentHalf, (byte)(shape > 1 ? shape - 1 : 0)));
            workspace.TileStore.Set(x, y, in tile);
        }
        ushort[] identities = [275, 276, 277, 278, 279, 280, 281, 296, 297, 309, 358, 359, 413, 414, 542, 550, 551, 553, 554, 558, 559, 599, 600, 601, 602, 603, 604, 605, 606, 607, 608, 609, 610, 611, 612, 632, 640, 643, 644, 645, 710, 245, 246, 241, 242];
        for (int n = 0; n < identities.Length; n++)
        {
            ushort type = identities[n];
            bool wallObject = n >= 41;
            int objectWidth = type == 245 ? 2 : type == 246 ? 3 : type == 241 ? 4 : 6;
            int objectHeight = type == 246 ? 2 : type == 242 ? 4 : 3;
            int x = 60 + n % 8 * 112;
            int bandY = 45 + n / 8 * 76;
            for (int scenario = 0; scenario < 10; scenario++)
            {
                int y = bandY + scenario * 6;
                int styleX = wallObject && scenario is 3 or 9 && (type is 245 or 242) ? type == 245 ? (scenario == 3 ? 72 : 36) : scenario == 3 ? 108 : 0 : 0;
                int styleY = !wallObject ? 0 : type == 242 ? scenario == 3 ? 26 * 72 : scenario == 9 ? 27 * 72 : 0 :
                    type is 246 or 241 && scenario is 3 or 9 ? (scenario == 3 ? 2 : 1) * objectHeight * 18 : 0;
                for (int dx = 0; dx < objectWidth; dx++)
                for (int dy = 0; dy < objectHeight; dy++)
                    if (scenario != 1 || dx != objectWidth - 1 || dy != objectHeight - 1)
                        Put(x + dx, y + dy, type, (short)(styleX + dx * 18), (short)(styleY + dy * 18), wall: wallObject && scenario != 8 ? (ushort)1 : (ushort)0);
                if (wallObject)
                {
                    if (scenario == 2)
                        Put(x + objectWidth - 1, y + objectHeight - 1, type, (short)((objectWidth - 1) * 18), (short)((objectHeight - 1) * 18));
                    if (scenario == 4)
                        Put(x + objectWidth - 1, y + objectHeight - 1, type, 0, (short)((objectHeight - 1) * 18), wall: 1);
                    if (scenario == 5)
                        Put(x, y, type, wall: 1, inactive: true);
                    if (scenario == 6)
                        Put(x, y, type, wall: 1, shape: 4, independentHalf: true);
                    if (scenario == 7)
                        Put(x + objectWidth - 1, y + objectHeight - 1, 1, wall: 1);
                }
                else
                {
                    for (int dx = 0; dx < 6; dx++)
                        if (scenario != 2 || dx != 5)
                            Put(x + dx, y + 3, 1, shape: (byte)(scenario is 6 or 8 ? 4 : scenario == 7 ? 1 : 0),
                                inactive: scenario == 5 && dx == 5, independentHalf: scenario == 8);
                    if (scenario == 3)
                        Put(x + 5, y + 2, type, 18, 36);
                    if (scenario == 4)
                        Put(x + 5, y + 2, 1);
                    if (scenario == 9)
                        Put(x + 2, y + 1, type, 36, 18, inactive: true);
                }
            }
        }

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));
        Assert.Equal("A3E8EAA2CFFEBF11BF004ED84242ADF2C4D1EA104DEFD8C9B0D6A10D5A977D7C", HashFixture(workspace));
        Assert.Equal("5465461D92F04705A620CC2F7D380153C73F960D043F48929A42BC8CC2AD9EA1", random.Fingerprint());
        Assert.Equal(229004103, random.Next());
        for (int n = 0; n < identities.Length; n++)
        for (int scenario = 0; scenario < 10; scenario++)
        {
            bool survives = n < 41 ? scenario is 0 or 6 or 9 :
                scenario is 0 or 3 or 5 or 6 || scenario == 9 && identities[n] != 242;
            Assert.Equal(survives, workspace.TileStore.Get(60 + n % 8 * 112, 45 + n / 8 * 76 + scenario * 6).IsActive);
        }
    }

    [Theory]
    [InlineData(true, "ABE6EF3B0336FAD340683F3B4465CB3E673D0F9A350A3D3D4BC41409772CBA44", "4740D5B7CD485EE3C900C71DDC3E712E62DDE996E5A24CBC6BC5249C8DAEFEB5", 1987637751)]
    [InlineData(false, "75068073B973D1B35E56361416B663BB9B3706EC62EC72F6D4ACA4A30C1A04AC", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    public void Cage_halo_revalidates_neighbours_but_wall_removal_has_no_halo(bool cageTrigger, string digest, string rngDigest, int nextRandom)
    {
        var workspace = CreateWorkspace();
        void Put(int x, int y, ushort type, short fx = 0, short fy = 0, ushort wall = 0, byte shape = 0, bool inactive = false, bool independentHalf = false)
        {
            var tile = new WorldTile { Type = type, FrameX = fx, FrameY = fy, Wall = wall,
                Flags = WorldTileFlags.Active | (inactive ? WorldTileFlags.Inactive : WorldTileFlags.None) };
            Assert.True(tile.TrySetSourceShape(shape == 1 || independentHalf, (byte)(shape > 1 ? shape - 1 : 0)));
            workspace.TileStore.Set(x, y, in tile);
        }
        int cageX = cageTrigger ? 903 : 897;
        int wallX = cageTrigger ? 901 : 903;
        for (int dx = 0; dx < 6; dx++)
        {
            for (int dy = 0; dy < 3; dy++)
                if (dy != 2 || dx != (cageTrigger ? 5 : 0))
                    Put(cageX + dx, 330 + dy, 275, (short)(dx * 18), (short)(dy * 18));
            Put(cageX + dx, 333, 1);
        }
        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 3; dy++)
            if (dy != 2 || dx != (cageTrigger ? 0 : 1))
                Put(wallX + dx, 330 + dy, 245, (short)(dx * 18), (short)(dy * 18), wall: 1);
        var random = new RandomAdapter(1458);
        new GenerationTileFraming1458(workspace.TileStore, random).TileFrame(903, 330);
        Assert.Equal(digest, HashFixture(workspace));
        Assert.Equal(rngDigest, random.Fingerprint());
        Assert.Equal(nextRandom, random.Next());
        int neighbours = 0;
        for (int x = cageTrigger ? 901 : 897; x < 903; x++)
        for (int y = 330; y < 333; y++)
            if (workspace.TileStore.Get(x, y).IsActive)
                neighbours++;
        Assert.Equal(cageTrigger ? 0 : 17, neighbours);
    }

    [Theory]
    [InlineData(false, (byte)0, false, (short)0, (short)0, false, true, "BB405B1FC70874C7B8A2FBD4CFAE4276CD464BE777698E0D0AB36075FDBC4880", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData(false, (byte)4, false, (short)0, (short)0, false, true, "BB405B1FC70874C7B8A2FBD4CFAE4276CD464BE777698E0D0AB36075FDBC4880", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData(false, (byte)4, true, (short)0, (short)0, false, false, "D71F21E7A933F5784DD9299FEE5F59BDB22FF8629F7BDE340DE8AE22FD3E502D", "69E40D8AE7D70DA3BD0604E33C92682889D3B85ACE2029D6B3992F28EDFA90FC", 149912414)]
    [InlineData(true, (byte)1, false, (short)0, (short)0, false, true, "E9EA9A03D3BA5697BB78B3180450652D61DB3FD435E85DC05A56094B368E8525", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData(true, (byte)1, false, (short)0, (short)18, false, false, "1DFE36C36B0024C7FE60A6EC11652F45CECC83DD39006C0B7ECCB83BD2978E4B", "69E40D8AE7D70DA3BD0604E33C92682889D3B85ACE2029D6B3992F28EDFA90FC", 149912414)]
    [InlineData(true, (byte)0, false, (short)0, (short)0, true, false, "646D319AA453681EA7266A7AFD894E63D1AA3B5242847329E9578EF2C42FAA09", "69E40D8AE7D70DA3BD0604E33C92682889D3B85ACE2029D6B3992F28EDFA90FC", 149912414)]
    [InlineData(true, (byte)2, false, (short)144, (short)0, false, true, "00E7C9A5D56323C97033A8ABEA17FD04533C91EE8D197C3FECFFFC11F132DCAF", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData(true, (byte)2, false, (short)144, (short)18, false, false, "B0975E7ECECFFCCC8845E333196EB4C1150C03B9A7D9B6A1B5D284E223770ED8", "69E40D8AE7D70DA3BD0604E33C92682889D3B85ACE2029D6B3992F28EDFA90FC", 149912414)]
    [InlineData(true, (byte)0, false, (short)0, (short)18, false, true, "B544BFA389C3B23A04C67F948EF0C17D0ED867E2BBEE4425B98404D7A9503769", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData(true, (byte)4, true, (short)0, (short)0, false, true, "E9EA9A03D3BA5697BB78B3180450652D61DB3FD435E85DC05A56094B368E8525", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData(true, (byte)4, true, (short)0, (short)18, false, false, "1DFE36C36B0024C7FE60A6EC11652F45CECC83DD39006C0B7ECCB83BD2978E4B", "69E40D8AE7D70DA3BD0604E33C92682889D3B85ACE2029D6B3992F28EDFA90FC", 149912414)]
    public void Cage_platform_fallback_checks_frame_y_but_preserves_source_shape_exceptions(bool platform, byte shape, bool independentHalf,
        short frameX, short frameY, bool inactive, bool survives, string digest, string rngDigest, int nextRandom)
    {
        var workspace = CreateWorkspace();
        void Put(int x, int y, ushort type, short fx = 0, short fy = 0, ushort wall = 0, byte shape = 0, bool inactive = false, bool independentHalf = false)
        {
            var tile = new WorldTile { Type = type, FrameX = fx, FrameY = fy, Wall = wall,
                Flags = WorldTileFlags.Active | (inactive ? WorldTileFlags.Inactive : WorldTileFlags.None) };
            Assert.True(tile.TrySetSourceShape(shape == 1 || independentHalf, (byte)(shape > 1 ? shape - 1 : 0)));
            workspace.TileStore.Set(x, y, in tile);
        }
        for (int dx = 0; dx < 6; dx++)
        {
            for (int dy = 0; dy < 3; dy++)
                Put(903 + dx, 330 + dy, 275, (short)(dx * 18), (short)(dy * 18));
            Put(903 + dx, 333, platform ? (ushort)19 : (ushort)1, frameX, frameY, shape: shape, inactive: inactive, independentHalf: independentHalf);
        }
        var random = new RandomAdapter(1458);
        new GenerationTileFraming1458(workspace.TileStore, random).TileFrame(903, 330);
        Assert.Equal(digest, HashFixture(workspace));
        Assert.Equal(rngDigest, random.Fingerprint());
        Assert.Equal(nextRandom, random.Next());
        Assert.Equal(survives, workspace.TileStore.Get(903, 330).IsActive);
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
        public string Fingerprint()
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var cursor = typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random);
            var slots = (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!;
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cursor + ";" + string.Join(",", slots))));
        }
        public int Next() => random.Next();
        public int Next(int maxValue) => random.Next(maxValue);
        public int Next(int minValue, int maxValue) => random.Next(minValue, maxValue);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
