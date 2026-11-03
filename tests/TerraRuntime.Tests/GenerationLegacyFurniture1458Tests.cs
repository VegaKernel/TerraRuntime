using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationLegacyFurniture1458Tests
{
    [Theory]
    [InlineData(1, "19D8A2E2E8D1F693D13E5CC4590F240E36F6B3D820E35C06AB72E0DCF5A68AD8", 948385461)]
    [InlineData(42, "19D8A2E2E8D1F693D13E5CC4590F240E36F6B3D820E35C06AB72E0DCF5A68AD8", 732571666)]
    [InlineData(1458, "19D8A2E2E8D1F693D13E5CC4590F240E36F6B3D820E35C06AB72E0DCF5A68AD8", 1764279400)]
    public void Legacy_furniture_matches_registered_original_FinalCleanup(int seed, string expected, int next)
    {
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, TerraRuntime.World.VanillaSpecialWorldSeed1458.None));
        Populate((x, y, type, fx, fy, shape, actuated, active, wall) =>
        {
            var tile = new WorldTile
            {
                Type = type,
                FrameX = fx,
                FrameY = fy,
                Shape = shape,
                Wall = wall,
                Flags = (active ? WorldTileFlags.Active : WorldTileFlags.None) | (actuated ? WorldTileFlags.Inactive : WorldTileFlags.None)
            };
            workspace.TileStore.Set(x, y, in tile);
        });
        var random = new RandomAdapter(seed);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(new Context(workspace, random, seed));
        Assert.Equal(seed switch { 1 => "17F9441C018AA95BC802C50067FAE51829ED15593FD721C37D6ACD119BA490CC", 42 => "17F9441C018AA95BC802C50067FAE51829ED15593FD721C37D6ACD119BA490CC", 1458 => "17F9441C018AA95BC802C50067FAE51829ED15593FD721C37D6ACD119BA490CC", _ => throw new InvalidOperationException() }, GenerationNaturalPrefix49Tests.GridHash(workspace));
        Assert.Equal(seed switch { 1 => "0D11C204B4AF24246B581D4DD3D6FE6089C80CDA6109B96C13EA3177AEE9C882", 42 => "8AAC03DB1A2711082996575AD90C3ABBD89DEC1BE790935C6EE1A1DC2F52C4D6", 1458 => "B1532F049C466CB7B9CFB9B2FA6DDCC654E8F8B6B7A2D006C5CE712DB6DD2959", _ => throw new InvalidOperationException() }, GenerationNaturalPrefix49Tests.RandomHash(random, includeReport: false));
        Assert.Equal(seed switch { 1 => 28, 42 => 25, 1458 => 17, _ => throw new InvalidOperationException() }, workspace.VanillaAnglerQuest!.Quest);
        Assert.Equal(expected, Hash(workspace));
        Assert.Equal(next, random.Next());
    }

    [Theory]
    [InlineData(0, "888461F9B561A995BA12AF69E81B341F7784D534DAAA9E9E0B38913C13153BFD", "5CCC3069036C6D54F3CA441FD8C36C6A4491F8CE77EAEB20E42DF6C870ED87C7", 906992634)]
    [InlineData(1, "A3ED0FACCE55CC880BC3A6A00CDB638BF80AEC23BF25B50EC52931184B968B65", "5CCC3069036C6D54F3CA441FD8C36C6A4491F8CE77EAEB20E42DF6C870ED87C7", 906992634)]
    [InlineData(2, "AB717EC563BC55D53696B744735AD1CFF7B3092E1F7E5E72C5975BD42838825E", "5CCC3069036C6D54F3CA441FD8C36C6A4491F8CE77EAEB20E42DF6C870ED87C7", 906992634)]
    [InlineData(3, "37E9265322A2E1BEB43BB41D4336C741D209C77C4F81BC371C9A8EBBDE37CD7B", "FC8C78CFC52EACAF9370A3BCDF9BDD05EBB199367C119E96C41F81D5A88E5636", 1391346840)]
    public void Christmas_tree_single_call_member_conjunction_matches_actual_original(
        int profile, string expectedGrid, string expectedRandom, int expectedNext)
    {
        // Both untouched original TileFrame and CheckXmasTree agree for these four inputs.
        // A complete later pass masks the single-coordinate distinction by visiting that
        // malformed member again; this probe observes exactly one call at the decorated origin.
        var workspace = new Workspace(80, 80);
        for (int column = 0; column < 4; column++)
            for (int row = 0; row < 8; row++)
            {
                int frameX = column == 0 && row == 0 ? 123 : column;
                int frameY = column == 0 && row == 0 ? 300 : row;
                if (column == 1 && row == 1)
                {
                    if (profile is 2 or 3) frameX = 99;
                    if (profile is 1 or 3) frameY = 99;
                }
                var tile = new WorldTile
                {
                    Type = 171,
                    FrameX = (short)frameX,
                    FrameY = (short)frameY,
                    Flags = WorldTileFlags.Active
                };
                workspace.TileStore.Set(30 + column, 30 + row, in tile);
            }
        var floor = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(31, 38, in floor);
        workspace.TileStore.Set(32, 38, in floor);
        var random = new RandomAdapter(1458);
        new GenerationTileFraming1458(workspace.TileStore, random).TileFrame(30, 30);
        Assert.Equal(expectedGrid, Hash(workspace));
        Assert.Equal(expectedRandom, random.CapturePhysicalRandomHash());
        Assert.Equal(expectedNext, random.Next());
    }

    public static TheoryData<int, bool, int, bool> TopCases
    {
        get
        {
            var cases = new TheoryData<int, bool, int, bool>();
            foreach (int type in new[] { 1, 19, 380 })
                foreach (bool half in new[] { false, true })
                    for (int slope = 0; slope < 5; slope++)
                        cases.Add(type, half, slope, type == 19 ? half : slope < 3);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(TopCases))]
    public void Shared_top_slope_support_matches_original_independent_halfbrick(int type, bool half, int slope, bool expected)
    {
        // Thirty actual official SolidTileAllowTopSlope observations include every combined half/slope state.
        var workspace = new Workspace(80, 80);
        var tile = new WorldTile { Type = (ushort)type, Flags = WorldTileFlags.Active };
        Assert.True(tile.TrySetSourceShape(half, (byte)slope));
        workspace.TileStore.Set(30, 30, in tile);
        var framer = new GenerationTileFraming1458(workspace.TileStore, new RandomAdapter(1));
        MethodInfo query = typeof(GenerationTileFraming1458).GetMethod("SolidTileAllowTopSlope", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Equal(expected, (bool)query.Invoke(framer, [30, 30])!);
    }

    public static TheoryData<int, int, int, bool, bool, bool> TreeCases
    {
        get
        {
            var cases = new TheoryData<int, int, int, bool, bool, bool>();
            foreach (int type in new[] { 5, 72, 583, 584, 585, 586, 587, 588, 589, 596, 616, 634 })
                cases.Add(type, 0, 0, true, false, true);
            foreach ((int fx, int fy) in new[] { (44, 198), (44, 220), (44, 242), (66, 0), (66, 22), (66, 44),
                (66, 198), (66, 220), (66, 242), (88, 66), (88, 88), (88, 110), (22, 198), (22, 199), (22, 242) })
                cases.Add(5, fx, fy, true, false, false);
            cases.Add(5, 22, 243, true, false, true);
            cases.Add(5, 44, 219, true, false, true);
            cases.Add(5, 44, 132, true, false, true);
            cases.Add(323, 0, 0, true, false, true);
            cases.Add(323, 88, 0, true, false, false);
            cases.Add(323, 22, 198, true, false, false);
            cases.Add(72, 44, 198, true, false, false);
            cases.Add(583, 88, 66, true, false, false);
            cases.Add(5, 0, 0, false, false, false);
            cases.Add(5, 0, 0, true, true, true);
            cases.Add(3, 0, 0, true, false, false);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(TreeCases))]
    public void Anchor_read_only_tree_queries_match_actual_original_components(
        int type, int frameX, int frameY, bool active, bool actuated, bool supports)
    {
        // These are direct CheckAnchor observations. They do not validate the independent tree-framing subsystem.
        var workspace = new Workspace(80, 80);
        var anchor = new WorldTile { Type = 723, Flags = WorldTileFlags.Active };
        var support = new WorldTile
        {
            Type = (ushort)type,
            FrameX = (short)frameX,
            FrameY = (short)frameY,
            Flags = (active ? WorldTileFlags.Active : WorldTileFlags.None) | (actuated ? WorldTileFlags.Inactive : WorldTileFlags.None)
        };
        workspace.TileStore.Set(30, 30, in anchor);
        workspace.TileStore.Set(30, 31, in support);
        new GenerationTileFraming1458(workspace.TileStore, new RandomAdapter(1)).TileFrame(30, 30);
        Assert.Equal((short)(supports ? 0 : 72), workspace.TileStore.Get(30, 30).FrameX);
        Assert.True(workspace.TileStore.Get(30, 30).IsActive);
        Assert.Equal(support, workspace.TileStore.Get(30, 31));
    }

    private static void Populate(Action<int, int, ushort, short, short, byte, bool, bool, ushort> write)
    {
        void Put(int x, int y, ushort type, short fx = 0, short fy = 0, byte shape = 0,
            bool actuated = false, bool active = true, ushort wall = 0) => write(x, y, type, fx, fy, shape, actuated, active, wall);
        ushort[] identities = [128, 269, 171, 567, 630, 631, 723, 724];
        for (int n = 0; n < identities.Length; n++)
        {
            ushort type = identities[n];
            int x = 80 + n * 100;
            for (int scenario = 0; scenario < 12; scenario++)
            {
                int y = 60 + scenario * 32;
                if (type is 630 or 631 or 723 or 724)
                {
                    int style = scenario switch { 3 => 1, 4 or 10 => 2, 5 or 11 => 3, 6 => 4, 8 => 1, 9 => 5, _ => 0 };
                    Put(x, y, type, (short)(style * 18 + (scenario == 10 ? 1 : 0)), wall: (ushort)(scenario is 6 or 11 ? 1 : 0));
                    if (scenario is 0 or 1 or 2 or 11) Put(x, y + 1, 1, actuated: scenario == 2);
                    if (scenario is 3 or 11) Put(x, y - 1, 1);
                    if (scenario is 4 or 7 or 10 or 11) Put(x - 1, y, 1);
                    if (scenario is 5 or 11) Put(x + 1, y, 1);
                    continue;
                }
                int width = type == 171 ? 4 : type == 567 ? 1 : 2;
                int height = type == 171 ? 8 : type == 567 ? 2 : 3;
                for (int dx = 0; dx < width; dx++)
                    for (int dy = 0; dy < height; dy++)
                    {
                        if (scenario == 1 && dx == width - 1 && dy == height - 1)
                        {
                            Put(x + dx, y + dy, 1);
                            continue;
                        }
                        int fx = dx * 18, fy = dy * 18;
                        if (type is 128 or 269)
                        {
                            if (scenario == 3) fx += 36;
                            if (scenario == 4) fx += (dy + 1) * 100;
                            if (scenario == 5 && dx == 0 && dy == 0) fx = 72;
                            if (scenario == 11 && dx == 0 && dy == 0) fy = 1;
                        }
                        else if (type == 171)
                        {
                            fx = dx; fy = dy;
                            if (scenario == 3 && dx == 0 && dy == 0) { fx = 123; fy = 300; }
                            if (scenario is 4 or 5 && dx == 1 && dy == 1) { fx = 99; if (scenario == 5) fy = 99; }
                        }
                        else
                        {
                            fx = scenario == 3 ? 56 + dy * 28 : 0;
                            fy = dy * 20;
                            if (scenario == 11 && dy == 1) fy = 18;
                        }
                        Put(x + dx, y + dy, type, (short)fx, (short)fy, active: !(type == 567 && scenario == 4 && dy == 0));
                    }
                for (int dx = 0; dx < width; dx++)
                {
                    if (scenario == 2 && dx == (type == 171 ? 1 : 0)) continue;
                    if (type == 171 && scenario == 10 && dx is 0 or 3) continue;
                    byte shape = scenario switch { 7 => 1, 8 => (byte)(type == 171 ? 2 : 4), 9 => (byte)(type == 171 ? 4 : 2), _ => 0 };
                    ushort support = scenario == 10 && type != 171 || scenario == 11 && type == 171 || scenario == 5 && type == 567
                        ? (ushort)19 : (ushort)1;
                    if (scenario == 11 && type == 171) shape = 2;
                    Put(x + dx, y + height, support, shape: shape, actuated: scenario == 6, active: !(type == 567 && scenario == 5));
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
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);

        public string CapturePhysicalRandomHash()
        {
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
