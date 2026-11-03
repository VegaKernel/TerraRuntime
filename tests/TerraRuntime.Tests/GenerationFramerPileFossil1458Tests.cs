using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationFramerPileFossil1458Tests
{
    private static readonly JsonDocument Golden = ReadGolden();
    public static IEnumerable<object[]> OriginalCases => Enumerable.Range(0, Golden.RootElement.GetProperty("cases").GetArrayLength()).Select(i => new object[] { i });

    [Theory, MemberData(nameof(OriginalCases))]
    public void Whole_generation_callbacks_match_original_grid_and_physical_random_state(int index)
    {
        JsonElement row = Golden.RootElement.GetProperty("cases")[index];
        var workspace = new Workspace(80, 80);
        Assert.True(workspace.TrySetLayers(20, 50));
        workspace.InitializeVanillaGenerationSolidity();
        int identity = 0;
        foreach (JsonElement solid in Golden.RootElement.GetProperty("phase754").EnumerateArray())
            workspace.SetVanillaGenerationSolid(new TileTypeId((ushort)identity++), solid.GetBoolean());
        var random = new RandomAdapter(row.GetProperty("seed").GetInt32());
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace, useWorldGenerationSolidity: true);
        if (row.GetProperty("kind").GetString() == "coin")
        {
            ushort type = row.GetProperty("type").GetUInt16();
            int style = row.GetProperty("style").GetInt32(), trigger = row.GetProperty("trigger").GetInt32();
            for (int x = 39; x < 42; x++)
            {
                Set(x, 42, new WorldTile { Type = (ushort)(type == 187 && style < 6 ? 59 : type == 187 && style < 9 ? 57 : 1), Flags = WorldTileFlags.Active });
                for (int y = 40; y < 42; y++)
                    Set(x, y, new WorldTile { Type = type, Flags = WorldTileFlags.Active, FrameX = (short)(style * 54 + (x - 39) * 18), FrameY = (short)((y - 40) * 18) });
            }
            if (row.GetProperty("broken").GetBoolean())
            {
                int x = 39 + (trigger / 2 + 1) % 3, y = 41 - trigger % 2;
                WorldTile missing = workspace.TileStore.Get(x, y);
                missing.Flags &= ~WorldTileFlags.Active;
                Set(x, y, missing);
            }
            framing.TileFrame(39 + trigger / 2, 40 + trigger % 2);
        }
        else
        {
            int mask = row.GetProperty("mask").GetInt32(), profile = row.GetProperty("profile").GetInt32(), bit = 0;
            for (int x = 39; x <= 41; x++)
            for (int y = 39; y <= 41; y++)
            {
                bool active = x == 40 && y == 40 || (mask & (1 << bit++)) != 0;
                Set(x, y, new WorldTile { Type = 404, Flags = WorldTileFlags.WireRed | WorldTileFlags.WireGreen | (active ? WorldTileFlags.Active : 0), TileColor = 17, Wall = 64, WallColor = 3, FrameX = 36, FrameY = 18 });
            }
            for (int x = 39; x <= 41; x++)
            {
                var floor = new WorldTile { Type = 1, Flags = profile != 1 ? WorldTileFlags.Active : 0 };
                if (profile == 2)
                {
                    Assert.True(floor.TrySetSourceShape(x != 41, (byte)(x == 40 ? 3 : 0)));
                    if (x == 41) floor.Flags |= WorldTileFlags.Inactive;
                }
                Set(x, 42, floor);
            }
            framing.KillTile(40, 40);
        }
        Assert.Equal(row.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(workspace));
        Assert.Equal(row.GetProperty("cursor").GetInt32(), random.Cursor);
        Assert.Equal(row.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32()), random.Slots);
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());

        void Set(int x, int y, WorldTile tile) => workspace.TileStore.Set(x, y, in tile);
    }

    private static JsonDocument ReadGolden()
    {
        using Stream stream = typeof(GenerationFramerPileFossil1458Tests).Assembly.GetManifestResourceStream("GenerationFramerPileFossil1458")!;
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
