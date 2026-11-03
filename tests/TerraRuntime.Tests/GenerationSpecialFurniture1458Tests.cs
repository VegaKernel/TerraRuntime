using System.Security.Cryptography;
using System.Text;
using System.Reflection;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationSpecialFurniture1458Tests
{
    [Theory]
    [InlineData(1, "B35831D97EE78A56293052DCFE9907BF310B3C6EB71C1141EDE806D41776165E", 284750318)]
    [InlineData(42, "B35831D97EE78A56293052DCFE9907BF310B3C6EB71C1141EDE806D41776165E", 574434532)]
    [InlineData(1458, "B35831D97EE78A56293052DCFE9907BF310B3C6EB71C1141EDE806D41776165E", 1105659209)]
    public void Special_furniture_matches_registered_official_FinalCleanup(int seed, string expectedHash, int expectedNext)
    {
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, TerraRuntime.World.VanillaSpecialWorldSeed1458.None));
        Populate((x, y, type, fx, fy, shape, actuated) =>
        {
            var tile = new WorldTile
            {
                Type = type,
                FrameX = fx,
                FrameY = fy,
                Shape = shape,
                Flags = WorldTileFlags.Active | (actuated ? WorldTileFlags.Inactive : WorldTileFlags.None)
            };
            workspace.TileStore.Set(x, y, in tile);
        });
        var random = new RandomAdapter(seed);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(new Context(workspace, random, seed));
        Assert.Equal(seed switch { 1 => "6036546AFB9A509EE1027DAB557BC5CF91E38F24B5A6FC8415069F7295F389E3", 42 => "6036546AFB9A509EE1027DAB557BC5CF91E38F24B5A6FC8415069F7295F389E3", 1458 => "6036546AFB9A509EE1027DAB557BC5CF91E38F24B5A6FC8415069F7295F389E3", _ => throw new InvalidOperationException() }, GenerationNaturalPrefix49Tests.GridHash(workspace));
        Assert.Equal(seed switch { 1 => "59B839C349AA4754363E7CFF0D78D566BEEF90B52ADB6B0981306C143B168015", 42 => "62F5F8AB6A9F2B9C911DEB63031EF3AE56774B86448767BF79BCAF3425AA86FF", 1458 => "B0F03AA92ACDA9EE67976F481CED788E3B10C1FD830D1D0E8C6F9D954B055AF4", _ => throw new InvalidOperationException() }, GenerationNaturalPrefix49Tests.RandomHash(random, includeReport: false));
        Assert.Equal(seed switch { 1 => 17, 42 => 0, 1458 => 37, _ => throw new InvalidOperationException() }, workspace.VanillaAnglerQuest!.Quest);
        Assert.Equal(expectedHash, Hash(workspace));
        // Official mode0 includes the AnglerQuestSwap epilogue.
        Assert.Equal(expectedNext, random.Next());
    }

    [Theory]
    [InlineData(0, 775110646, "1BB0651A7BD1D645C012AB17D2127E55010268D00A98F54BFBB9411382A1E468")]
    [InlineData(13, 1510654968, "139CDAD33BD8FE3B9FF49EE255CD04CC2A2AF83E56775CAA45D7F7589A60948E")]
    [InlineData(11, 2002728273, "38FD473C0D6BA2B0D3740790BE276D7B9111A9F2A55C9513CDB8625BC3505F5C")]
    [InlineData(9, 1103308829, "3F058E42398A2F77170C498BC7F7F5182F7DE504E694A772BC505A181797B20C")]
    [InlineData(8, 1449977331, "11A570B46FA5CAC8226FB9CA6DDA12D1C739E4E9D60340940AB6C05903E8442E")]
    [InlineData(6, 1762481460, "6829595AE51B254C33F66DCBE658945B4891B017448FBC4DB6E3B1422AD2B95C")]
    [InlineData(4, 1054634378, "4F59DE3B57561B395CBDEC09F9805C6CB09447B441366D1443D915DA21E20B1C")]
    [InlineData(2, 425912789, "7AC7504A89563DF2B0843F6562FAF8D5B6492E63AC771ADA4F8007C719ECD3F7")]
    [InlineData(1, 683759645, "037E0764D98986BFAA6F5EA352A17A8A68F6C30A38B780971A1663806DBCB929")]
    public void Pigronata_all_nine_reward_selectors_match_original_complete_random_state(
        int seed, int expectedNext, string expectedRandom)
    {
        // Listed seeds select source branches0..8 after the actual120dustdraws.
        // An untouched original CheckChand454 supplies every literal; a separate
        // original KillTile control verifies the dust prelude's complete state.
        var workspace = new Workspace(80, 80);
        for (int column = 0; column < 4; column++)
            for (int row = 0; row < 3; row++)
            {
                var tile = new WorldTile
                {
                    Type = 454,
                    FrameX = (short)(column * 18),
                    FrameY = (short)(row * 18),
                    Flags = WorldTileFlags.Active
                };
                workspace.TileStore.Set(30 + column, 30 + row, in tile);
            }
        var random = new RandomAdapter(seed);
        new GenerationTileFraming1458(workspace.TileStore, random).TileFrame(30, 30);
        Assert.Equal("D4A8F3D9D1E34D0A7CE847EF0FDDE1EA43013DF175156FB4123757FFBBB148E7", Hash(workspace));
        Assert.Equal(expectedRandom, random.CapturePhysicalRandomHash());
        Assert.Equal(expectedNext, random.Next());
    }

    [Fact]
    public void Dry_beehive_destruction_requires_its_projectile_owner_before_mutation()
    {
        var workspace = new Workspace(40, 40);
        var tile = new WorldTile { Type = 444, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(20, 20, in tile);
        Assert.Throws<NotSupportedException>(() =>
            new GenerationTileFraming1458(workspace.TileStore, new RandomAdapter(1)).TileFrame(20, 20));
        // Dry destruction needs projectile655; the unowned call refuses before changing the tile.
        Assert.Equal(tile, workspace.TileStore.Get(20, 20));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void Banner_rejects_nonplatform_solid_top_ceiling_even_when_shaped(byte shape)
    {
        // Actual original CheckBanner91 with solid/solidTop14 at each shape rejects all four.
        // AnchorValid(SolidBottom) would permit the halfbrick/top-slope pair: it is not this rule.
        var workspace = new Workspace(80, 80);
        var anchor = new WorldTile { Type = 14, Shape = shape, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(30, 29, in anchor);
        for (int row = 0; row < 3; row++)
        {
            var tile = new WorldTile { Type = 91, FrameY = (short)(row * 18), Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(30, 30 + row, in tile);
        }
        new GenerationTileFraming1458(workspace.TileStore, new RandomAdapter(1)).TileFrame(30, 30);
        Assert.False(workspace.TileStore.Get(30, 30).IsActive);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(216, true)]
    [InlineData(450, true)]
    public void Geyser_halfbrick_platform_anchor_retains_source_frame_band_precedence(short frame, bool survives)
    {
        // Actual original CheckSuper443 accepts halfbrick in bands12..16/25..26,
        // but not0..7. The disjunction's precedence is deliberate source behavior.
        var workspace = new Workspace(80, 80);
        for (int column = 0; column < 2; column++)
        {
            var member = new WorldTile { Type = 443, FrameX = (short)(column * 18), Flags = WorldTileFlags.Active };
            var anchor = new WorldTile { Type = 19, FrameX = frame, Shape = 1, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(30 + column, 30, in member);
            workspace.TileStore.Set(30 + column, 31, in anchor);
        }
        new GenerationTileFraming1458(workspace.TileStore, new RandomAdapter(1)).TileFrame(30, 30);
        Assert.Equal(survives, workspace.TileStore.Get(30, 30).IsActive);
    }

    private static void Populate(Action<int, int, ushort, short, short, byte, bool> put)
    {
        ushort[] identities = [376, 443, 485, 91, 454];
        for (int n = 0; n < identities.Length; n++)
        {
            ushort type = identities[n];
            int x = 100 + n * 160;
            int width = type == 91 ? 1 : type == 454 ? 4 : 2;
            int height = type == 443 ? 1 : type is 91 or 454 ? 3 : 2;
            for (int scenario = 0; scenario < 12; scenario++)
            {
                int y = 65 + scenario * 30;
                int baseFx = scenario == 3 ? width * 18 : 0;
                int baseFy = scenario == 3 && type is 91 or 454 ? 54 : 0;
                for (int dx = 0; dx < width; dx++)
                    for (int dy = 0; dy < height; dy++)
                        if (scenario != 1 || dx != width - 1 || dy != height - 1)
                            put(x + dx, y + dy, type, (short)(baseFx + dx * 18),
                                (short)(baseFy + dy * 18 + (scenario == 11 && dx == 0 && dy == 0 ? 1 : 0)), 0, false);
                byte shape = scenario switch { 5 => 1, 6 => 2, 7 => 4, _ => 0 };
                ushort support = scenario == 8 ? (ushort)19 : (ushort)1;
                if (type is 91 or 454)
                {
                    if (scenario != 2)
                        put(x + (type == 454 ? 1 : 0), y - 1, support, 0, 0, shape, scenario == 4);
                }
                else
                {
                    if (scenario is not (2 or 9))
                        for (int dx = 0; dx < width; dx++)
                            put(x + dx, y + height, support, 0, 0, shape, scenario == 4);
                    if (scenario is 9 or 10)
                        for (int dx = 0; dx < width; dx++)
                            put(x + dx, y - 1, 1, 0, 0, 0, false);
                }
            }
        }
    }

    private static string Hash(Workspace workspace)
    {
        var text = new StringBuilder();
        for (int x = 0; x < workspace.WidthTiles; x++)
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                text.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                    .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',').Append(tile.LiquidAmount).Append(';');
            }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private sealed class Context(Workspace workspace, IWorldGenerationVanillaRandom random, int seed) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => new(Provider1458.GeneratorId, "Fixture", (ulong)seed, 1000, 500);
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
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);

        public string CapturePhysicalRandomHash()
        {
            // Test-only inspection of the complete owned RNG; no shipping reflection.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            uint cursor = (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random)!;
            int[] slots = (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!;
            using var bytes = new MemoryStream();
            using var writer = new BinaryWriter(bytes);
            writer.Write(cursor);
            foreach (int value in slots) writer.Write(value);
            return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
        }
    }
}
