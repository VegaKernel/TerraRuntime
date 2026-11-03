using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationTrackGenerator1458Tests
{
    private static readonly JsonDocument Golden = ReadGolden();
    public static IEnumerable<object[]> OriginalCases => Enumerable.Range(0, Golden.RootElement.GetProperty("cases").GetArrayLength()).Select(i => new object[] { i });

    [Theory, MemberData(nameof(OriginalCases))]
    public void Consecutive_track_attempts_match_official_paths_height_and_random_state(int index)
    {
        JsonElement row = Golden.RootElement.GetProperty("cases")[index];
        int profile = row.GetProperty("profile").GetInt32();
        Workspace workspace = Build(profile);
        var random = new RandomAdapter(row.GetProperty("seed").GetInt32());
        var track = new TrackGenerator1458(workspace, random);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            bool left = (attempt == 0) != (profile == 15);
            bool returned = track.Place(left ? 400 : 900, left ? 300 : 400, profile >= 14 ? 400 : 30, profile >= 14 ? 1000 : 120);
            JsonElement expected = row.GetProperty("states")[attempt];
            Assert.Equal(expected.GetProperty("returned").GetBoolean(), returned);
            Assert.Equal(expected.GetProperty("height").GetInt32(), Field("playerHeight"));
            Assert.Equal(expected.GetProperty("count").GetInt32(), Field("count"));
            Assert.Equal(expected.GetProperty("direction").GetInt32(), Field("direction"));
            Assert.Equal(expected.GetProperty("cursor").GetInt32(), random.Cursor);
            Assert.Equal(expected.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32()), random.Slots);
        }
        Assert.Equal(row.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(workspace));
        Assert.Empty(workspace.CaptureGeneratedChests());

        int Field(string name) => (int)typeof(TrackGenerator1458).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(track)!;
    }

    private static Workspace Build(int profile)
    {
        var workspace = new Workspace(1200, 800);
        Assert.True(workspace.TrySetLayers(120, 300));
        workspace.InitializeVanillaGenerationSolidity();
        int identity = 0;
        foreach (JsonElement solid in Golden.RootElement.GetProperty("phase754").EnumerateArray())
            workspace.SetVanillaGenerationSolid(new TileTypeId((ushort)identity++), solid.GetBoolean());
        Assert.True(workspace.TileStore.TryAttachTileEntities([], 1000));
        for (int x = 0; x < 1200; x++)
        for (int y = 0; y < 800; y++)
        {
            ref WorldTile tile = ref workspace.TileStore.Tiles[x * 800 + y];
            int floor = x < 750 ? 400 : 500;
            if (profile == 1) floor += x % 37 / 5;
            if (y >= floor) { tile.Flags |= WorldTileFlags.Active; tile.Type = 1; }
            if (profile is 8 or 9) tile.Wall = 244;
            if (profile == 8 && y >= 390 && y < 405)
            {
                tile.Flags |= WorldTileFlags.WireRed | WorldTileFlags.WireBlue | WorldTileFlags.WireGreen | WorldTileFlags.WireYellow |
                    WorldTileFlags.Actuator | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall;
                tile.TileColor = 17; tile.WallColor = 4; tile.LiquidAmount = 71; tile.LiquidKind = WorldLiquidKind.Lava;
            }
        }
        if (profile == 2) for (int y = 300; y < 450; y++) Cell(430, y).Wall = 7;
        if (profile == 3) for (int x = 430; x < 470; x++) for (int y = 300; y < 500; y++) Activate(x, y, 1);
        if (profile is 4 or 5)
        {
            Cell(450, 399).Type = 314;
            if (profile == 4) Cell(450, 399).Flags |= WorldTileFlags.Active;
            else Cell(450, 399).Flags &= ~WorldTileFlags.Active;
        }
        if (profile == 6) for (int x = 390; x < 550; x++) { Cell(x, 400).Wall = 350; Assert.True(Cell(x, 400).TrySetSourceShape(true, 3)); }
        if (profile == 7) for (int x = 410; x < 420; x++) Activate(x, 397, 135);
        if (profile == 10) for (int x = 390; x < 550; x++) Assert.True(Cell(x, 400).TrySetSourceShape(true, 2));
        if (profile == 11) for (int x = 390; x < 550; x++) Activate(x, 391, 379);
        if (profile == 12) for (int x = 390; x < 550; x++) Activate(x, 392, 3);
        if (profile == 13) for (int y = 300; y < 450; y++) Cell(430, y).Type = 225;
        return workspace;

        ref WorldTile Cell(int x, int y) => ref workspace.TileStore.Tiles[x * 800 + y];
        void Activate(int x, int y, ushort type) { Cell(x, y).Type = type; Cell(x, y).Flags |= WorldTileFlags.Active; }
    }

    private static JsonDocument ReadGolden()
    {
        using Stream stream = typeof(GenerationTrackGenerator1458Tests).Assembly.GetManifestResourceStream("GenerationTrackGenerator1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 source = new(seed);
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        internal int Cursor => Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", Hidden)!.GetValue(source));
        internal int[] Slots => ((int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", Hidden)!.GetValue(source)!).ToArray();
        public int Next() => source.Next();
        public int Next(int maximum) => source.Next(maximum);
        public int Next(int minimum, int maximum) => source.Next(minimum, maximum);
        public double NextDouble() => source.NextDouble();
        public void NextBytes(byte[] buffer) => source.NextBytes(buffer);
    }
}
