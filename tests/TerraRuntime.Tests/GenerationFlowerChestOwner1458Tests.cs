using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class GenerationFlowerChestOwner1458Tests
{
    private static readonly JsonDocument Golden = ReadGolden();
    public static IEnumerable<object[]> OriginalCases => Golden.RootElement.EnumerateArray()
        .Select(x => new object[] { x.GetProperty("key").GetString()! });

    [Theory, MemberData(nameof(OriginalCases))]
    public void Plant_placement_uses_the_canonical_filled_or_empty_owner_for_a_partial_chest(string key)
    {
        JsonElement row = Golden.RootElement.EnumerateArray().Single(x => x.GetProperty("key").GetString() == key);
        var workspace = new Workspace(80, 120);
        ushort type = row.GetProperty("type").GetUInt16();
        for (int dx = 0; dx < 2; dx++)
            for (int dy = 0; dy < 2; dy++)
            {
                var tile = new WorldTile { Type = type, FrameX = (short)(dx * 18), FrameY = (short)(dy * 18), Flags = WorldTileFlags.Active };
                workspace.TileStore.Set(40 + dx, 40 + dy, in tile);
            }
        var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        var grass = new WorldTile { Type = 2, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(40, 42, in stone); workspace.TileStore.Set(41, 42, in grass);
        WorldChestItem[] items = Enumerable.Range(0, 40).Select(slot => row.GetProperty("filled").GetBoolean()
            ? new WorldChestItem(slot + 1, 9, 0) : default).ToArray();
        if (row.GetProperty("registered").GetBoolean())
            Assert.True(workspace.TryAddGeneratedChest(40, 40, "Source owner", items));
        WorldTile missing = workspace.TileStore.Get(41, 41); missing.Flags &= ~WorldTileFlags.Active;
        workspace.TileStore.Set(41, 41, in missing);

        var random = new RandomAdapter(row.GetProperty("seed").GetInt32());
        Assert.Equal(row.GetProperty("result").GetBoolean(), SurfacePlantPass1458.TryPlacePlant(
            workspace.TileStore, random, 41, 41, 3, workspace));
        Assert.Equal(row.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(workspace));
        Assert.Equal(row.GetProperty("rng").GetString(), GenerationNaturalPrefix49Tests.RandomHash(random, includeReport: false));
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
        WorldChest[] actual = workspace.CaptureGeneratedChests();
        JsonElement[] expected = row.GetProperty("chests").EnumerateArray().ToArray();
        Assert.Equal(expected.Length, actual.Length);
        for (int chest = 0; chest < expected.Length; chest++)
        {
            Assert.Equal(expected[chest].GetProperty("slot").GetInt16(), actual[chest].SlotId);
            Assert.Equal(expected[chest].GetProperty("x").GetInt32(), actual[chest].X);
            Assert.Equal(expected[chest].GetProperty("y").GetInt32(), actual[chest].Y);
            Assert.Equal(expected[chest].GetProperty("name").GetString(), actual[chest].Name);
            Assert.Equal(40, actual[chest].Items.Length);
            JsonElement[] sourceItems = expected[chest].GetProperty("items").EnumerateArray().ToArray();
            for (int slot = 0; slot < 40; slot++)
            {
                Assert.Equal(sourceItems[slot].GetProperty("type").GetInt32(), actual[chest].Items[slot].ItemType);
                Assert.Equal(sourceItems[slot].GetProperty("stack").GetInt32(), actual[chest].Items[slot].Stack);
                Assert.Equal(sourceItems[slot].GetProperty("prefix").GetByte(), actual[chest].Items[slot].Prefix);
            }
        }
    }

    private static JsonDocument ReadGolden()
    {
        using var source = typeof(GenerationFlowerChestOwner1458Tests).Assembly.GetManifestResourceStream("GenerationFlowerChestOwner1458")!;
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next(); public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
