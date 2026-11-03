using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class GenerationCrimsonCallGraph1458Tests
{
    public static IEnumerable<object[]> SourceCases()
    {
        using JsonDocument source = ReadSource();
        foreach (JsonElement row in source.RootElement.GetProperty("rows").EnumerateArray())
            yield return [row.GetProperty("name").GetString()!, row.GetProperty("seed").GetInt32()];
    }

    [Theory, MemberData(nameof(SourceCases))]
    public void Crimson_geometry_hearts_and_borrowed_solidity_match_actual_original(string name, int seed)
    {
        using JsonDocument source = ReadSource();
        JsonElement d = source.RootElement.GetProperty("cases").EnumerateArray().Single(x => x.GetProperty("name").GetString() == name);
        JsonElement expected = source.RootElement.GetProperty("rows").EnumerateArray().Single(x => x.GetProperty("name").GetString() == name && x.GetProperty("seed").GetInt32() == seed);
        string kind = d.GetProperty("kind").GetString()!; int v = d.GetProperty("variant").GetInt32();
        var workspace = new Workspace(1000, 800); workspace.InitializeVanillaGenerationSolidity();
        int index = 0; foreach (JsonElement solid in source.RootElement.GetProperty("solidity").EnumerateArray()) workspace.SetVanillaGenerationSolid(new((ushort)index++), solid.GetBoolean());
        WorldTileStore store = workspace.TileStore;
        for (int x = 0; x < 1000; x++) for (int y = 300; y < 800; y++) At(x, y) = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        if (kind == "entrance") for (int x = 450; x < 550; x++) for (int y = 325; y < 380; y++) At(x, y).Type = 203;
        if (kind == "start")
        {
            ushort type = (ushort)(v is 1 or 3 or 4 ? 484 : v == 2 ? 659 : v == 5 ? 225 : v == 7 ? 481 : 1);
            for (int x = 200; x < 800; x++)
            {
                ref WorldTile tile = ref At(x, 240); tile.Flags |= WorldTileFlags.Active; tile.Type = type;
                if (v == 3) Assert.True(tile.TrySetSourceShape(true, 3)); if (v == 4) tile.Flags |= WorldTileFlags.Inactive;
            }
        }
        var random = new RandomAdapter(seed);
        var caves = new CrimsonCaves1458(store, random, 220, TestContext.Current.CancellationToken, workspace.IsVanillaGenerationSolid);
        if (kind == "hearts")
        {
            var hearts = (List<(int X, int Y)>)typeof(CrimsonCaves1458).GetField("hearts", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(caves)!;
            foreach (JsonElement point in d.GetProperty("endpoints").EnumerateArray()) hearts.Add((point[0].GetInt32(), point[1].GetInt32()));
            for (int x = 430; x < 570; x++) for (int y = 330; y < 370; y++)
            {
                ref WorldTile tile = ref At(x, y); tile.Type = (ushort)(v < 4 ? 59 : 31); tile.FrameX = (short)(v == 7 ? 36 : 123); tile.FrameY = (short)(v == 7 ? 0 : 87);
                tile.TileColor = 7; tile.WallColor = 9; Assert.True(tile.TrySetSourceShape(v == 2, (byte)(v % 5))); tile.LiquidAmount = 123; tile.LiquidKind = (WorldLiquidKind)(v % 4);
                tile.Flags |= WorldTileFlags.WireRed | WorldTileFlags.Actuator | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock | WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall;
            }
        }
        if (kind == "altar")
        {
            for (int x = 499; x <= 501; x++)
            {
                ref WorldTile tile = ref At(x, 200); tile.Flags |= WorldTileFlags.Active; tile.Type = (ushort)(v == 0 ? 1 : v == 1 ? 659 : v == 2 ? 484 : v == 3 ? 665 : 19);
                Assert.True(tile.TrySetSourceShape(v is 6 or 7, (byte)(v >= 4 ? v % 2 + 1 : 0)));
            }
            for (int x = 499; x <= 501; x++) for (int y = 198; y <= 199; y++)
            {
                ref WorldTile tile = ref At(x, y); tile.TileColor = 7; tile.WallColor = 9; tile.LiquidAmount = 100; tile.LiquidKind = (WorldLiquidKind)(v % 4);
                tile.Flags |= WorldTileFlags.WireRed | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightWall;
            }
        }
        if (kind == "quick")
        {
            ushort[] types = [1, 138, 484, 546, 659, 10, 137, 190];
            for (int x = 100; x < 900; x++) for (int y = 200; y <= 207; y++) At(x, y) = new WorldTile { Type = types[v], Flags = WorldTileFlags.Active };
            for (int x = 100; x < 900; x += 17) { At(x, 190).LiquidAmount = 255; At(x, 190).LiquidKind = (WorldLiquidKind)(v % 4); }
            if (v % 2 != 0) foreach (ushort type in new ushort[] { 138, 484, 546, 664, 711, 712, 713, 714, 715, 716, 379 }) workspace.SetVanillaGenerationSolid(new(type), false);
        }
        int step = 0;
        if (kind == "start") { caves.Start(500, 210); if (v == 6) caves.Start(650, 210); Check(); caves.PlaceHearts(); Check(); }
        else if (kind == "vein") { Invoke("Vein", [500.25, 360.75, d.GetProperty("vx").GetDouble(), d.GetProperty("vy").GetDouble()]); Check(); caves.PlaceHearts(); Check(); }
        else if (kind == "entrance") { Invoke("Entrance", [500.25, 340.75, d.GetProperty("direction").GetInt32()]); Check(); }
        else if (kind == "hearts") { caves.PlaceHearts(); Check(); }
        else if (kind == "altar") { EvilAltarPlacement1458.TryPlace(store, 500, 199, true, solidity: workspace.IsVanillaGenerationSolid); Check(); }
        else { UnderworldLiquidPreparation1458.Settle(store, 450, TestContext.Current.CancellationToken, workspace); Check(); }

        ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
        void Invoke(string method, object[] args) => typeof(CrimsonCaves1458).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(caves, args);
        void Check()
        {
            JsonElement expectedStep = expected.GetProperty("steps")[step++];
            Assert.Equal(expectedStep.GetProperty("gridHash").GetString(), GenerationNaturalPrefix49Tests.GridHash(workspace));
            JsonElement physical = expectedStep.GetProperty("random"); using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory);
            writer.Write(physical.GetProperty("cursor").GetUInt32()); foreach (JsonElement slot in physical.GetProperty("slots").EnumerateArray()) writer.Write(slot.GetInt32()); writer.Flush();
            Assert.Equal(Convert.ToHexString(SHA256.HashData(memory.ToArray())), GenerationNaturalPrefix49Tests.RandomHash(random, includeReport: false));
            Assert.Equal(physical.GetProperty("next").GetInt32(), random.PeekNext());
            var original = expectedStep.GetProperty("hearts").EnumerateArray().Select(point => (point.GetProperty("X").GetInt32(), point.GetProperty("Y").GetInt32())).ToArray();
            Assert.Equal(original, caves.HeartPositions);
            Assert.Equal(expectedStep.GetProperty("solidity").EnumerateArray().Select(s => s.GetBoolean()), Enumerable.Range(0, 754).Select(i => workspace.IsVanillaGenerationSolid(new((ushort)i))));
        }
    }

    private static JsonDocument ReadSource()
    {
        using Stream compressed = typeof(GenerationCrimsonCallGraph1458Tests).Assembly.GetManifestResourceStream("GenerationCrimsonCallGraph1458")!;
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress); return JsonDocument.Parse(gzip);
    }
    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int PeekNext()
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var clone = new VanillaUnifiedRandom1458(0); var cursor = typeof(VanillaUnifiedRandom1458).GetField("inext", fields)!; var slots = typeof(VanillaUnifiedRandom1458).GetField("seedArray", fields)!;
            cursor.SetValue(clone, cursor.GetValue(random)); Array.Copy((int[])slots.GetValue(random)!, (int[])slots.GetValue(clone)!, 56); return clone.Next();
        }
        public int Next() => random.Next(); public int Next(int max) => random.Next(max); public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble(); public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
