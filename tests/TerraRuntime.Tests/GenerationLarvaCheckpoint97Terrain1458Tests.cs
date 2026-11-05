using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationLarvaCheckpoint97Terrain1458Tests
{
    [Theory]
    [InlineData(1458, 362, 443, "3ca48889322a655a1a647432917d983cae514853df390992ce90bceff2d748f9", 906992634)]
    [InlineData(42, 351, 425, "5884ecc7fcc87ef78df13ee49ec1cfacdcc45169ebeb27d38e8ef0a10ef66d68", 1434747710)]
    public void Checkpoint97_hive_window_matches_official_PlaceTile231_full_grid_and_rng(
        int seed, int anchorX, int anchorY, string expectedHash, int expectedNext)
    {
        // Actual source checkpoint97-NotTheBees terrain window (21x21), rehosted at
        // its unchanged coordinates in a 600x500 local helper scene. Checkpoint97
        // predates Micro98, not the exact pre-FinishNotTheBees pass boundary.
        Workspace workspace = Fixture(seed, anchorX, anchorY);
        var random = new RandomAdapter(seed);
        var framing = new GenerationTileFraming1458(
            workspace.TileStore, random, workspace, useWorldGenerationSolidity: true);
        Assert.NotEqual(expectedHash, Hash(workspace));

        bool placed = framing.TryPlaceLarva(anchorX, anchorY);

        Assert.True(placed);
        Assert.Equal((ushort)231, workspace.TileStore.Get(anchorX, anchorY).Type);
        string actualHash = Hash(workspace);
        Assert.True(expectedHash == actualHash,
            $"Expected {expectedHash}, actual {actualHash}; " +
            string.Join(";", from x in Enumerable.Range(anchorX - 3, 7)
                from y in Enumerable.Range(anchorY - 3, 7)
                let t = workspace.TileStore.Get(x, y)
                select $"{x},{y}:{t.IsActive},{t.Type},{t.Wall},{t.FrameX},{t.FrameY},{t.LiquidAmount},{t.TileColor}"));
        Assert.Equal(expectedNext, random.Next());
    }

    [Fact]
    public void Occupied_checkpoint97_stone_anchor_is_reframed_by_source_PlaceTile231()
    {
        // Actual source97 stone at (352,433) has frame(-1,-1). Place3x3
        // rejects the occupied footprint; PlaceTile resets the anchor frame
        // before framing. Omitting both SquareTileFrame calls is not detected
        // by this fixture, so it is not evidence for physical framing effects.
        Workspace workspace = Fixture(1458, 352, 433);
        WorldTile before = workspace.TileStore.Get(352, 433);
        Assert.Equal((ushort)1, before.Type);
        Assert.Equal((short)-1, before.FrameX);
        Assert.Equal((short)-1, before.FrameY);
        var random = new RandomAdapter(1458);
        var framing = new GenerationTileFraming1458(
            workspace.TileStore, random, workspace, useWorldGenerationSolidity: true);

        Assert.True(framing.TryPlaceLarva(352, 433));

        WorldTile after = workspace.TileStore.Get(352, 433);
        Assert.Equal((ushort)1, after.Type);
        Assert.Equal((short)0, after.FrameX);
        Assert.Equal((short)0, after.FrameY);
        Assert.Equal("f13120f11ded50bbc9a0e7d6b0aa2036a6c1847cf36a235d6d340ff9fe8d923e", Hash(workspace));
        Assert.Equal(906992634, random.Next());
    }

    [Theory]
    [InlineData(400, 877, 59, true, "bfeca0645ea54a99e759c72a6cf83c476b45540327b6296828517a81a63acf39")]
    [InlineData(397, 878, 71, false, "b03bd8f37d50f6f2290c0a38a4593016081adf88e439f0f04bf8dd8f91836682")]
    [InlineData(362, 444, 225, true, "a2f6fcbd412591c15f9766c78b56b589233c58914f9eed3c2e6d5b467abae5be")]
    public void Varied_occupied_checkpoint97_anchor_matches_official_PlaceTile231(
        int anchorX, int anchorY, ushort expectedType, bool expectedChanged, string expectedHash)
    {
        Workspace workspace = Fixture(1458, anchorX, anchorY, canonical: true);
        var random = new RandomAdapter(1458);
        var framing = new GenerationTileFraming1458(
            workspace.TileStore, random, workspace, useWorldGenerationSolidity: true);
        Assert.Equal(expectedType, workspace.TileStore.Get(anchorX, anchorY).Type);
        Assert.Equal(expectedChanged, expectedHash != Hash(workspace));

        Assert.True(framing.TryPlaceLarva(anchorX, anchorY));

        Assert.Equal(expectedType, workspace.TileStore.Get(anchorX, anchorY).Type);
        string actualHash = Hash(workspace);
        Assert.True(expectedHash == actualHash,
            $"Expected {expectedHash}, actual {actualHash}; " +
            string.Join(";", from x in Enumerable.Range(anchorX - 3, 7)
                from y in Enumerable.Range(anchorY - 3, 7)
                let t = workspace.TileStore.Get(x, y)
                select $"{x},{y}:{t.IsActive},{t.Type},{t.Wall},{t.FrameX},{t.FrameY},{t.LiquidAmount},{t.TileColor}"));
        Assert.Equal(906992634, random.Next());
    }

    private static Workspace Fixture(int seed, int anchorX, int anchorY, bool canonical = false)
    {
        var workspace = canonical ? new Workspace(4200, 1200) : new Workspace(600, 500);
        workspace.InitializeVanillaGenerationSolidity();
        WorldTile stone = new() { Type = 1, Flags = WorldTileFlags.Active };
        for (int x = 0; x < workspace.WidthTiles; x++)
            for (int y = 0; y < workspace.HeightTiles; y++)
                workspace.TileStore.Set(x, y, in stone);
        for (int x = 120; x < 180; x++)
            for (int y = 120; y < 300; y++)
                workspace.TileStore.Set(x, y, default);

        using (Stream stream = Resource($"checkpoint97-NotTheBees-{seed}.json.gz"))
        using (var json = JsonDocument.Parse(stream))
        {
            int id = 0;
            foreach (JsonElement solid in json.RootElement.GetProperty("expectedSolidity").EnumerateArray())
                workspace.SetVanillaGenerationSolid(new TileTypeId((ushort)id++), solid.GetBoolean());
        }

        using Stream binary = Resource($"checkpoint97-NotTheBees-{seed}.bin.gz");
        using var memory = new MemoryStream();
        binary.CopyTo(memory);
        byte[] data = memory.ToArray();
        const int sourceHeight = 1200, record = 17;
        for (int x = anchorX - 10; x <= anchorX + 10; x++)
            for (int y = anchorY - 10; y <= anchorY + 10; y++)
            {
                int offset = (x * sourceHeight + y) * record;
                WorldTile tile = new()
                {
                    Type = BitConverter.ToUInt16(data, offset + 1),
                    Wall = BitConverter.ToUInt16(data, offset + 3),
                    FrameX = BitConverter.ToInt16(data, offset + 5),
                    FrameY = BitConverter.ToInt16(data, offset + 7),
                    LiquidAmount = data[offset + 9],
                    LiquidKind = (WorldLiquidKind)data[offset + 10],
                    TileColor = data[offset + 13],
                    WallColor = data[offset + 14],
                    Flags = data[offset] != 0 ? WorldTileFlags.Active : WorldTileFlags.None
                };
                Assert.True(tile.TrySetSourceShape(data[offset + 11] != 0, data[offset + 12]));
                Assert.Equal(0, BitConverter.ToUInt16(data, offset + 15));
                workspace.TileStore.Set(x, y, in tile);
            }
        return workspace;
    }

    private static Stream Resource(string name)
    {
        Stream source = name.EndsWith(".bin.gz", StringComparison.Ordinal)
            ? File.OpenRead(Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "data", "SpecialMicro98", name)))
            : typeof(GenerationDetachedSpecialMicroBiomes1458Tests).Assembly
                .GetManifestResourceStream("GenerationSpecialMicro98." + name)
                ?? throw new InvalidOperationException("Missing committed checkpoint97 metadata resource.");
        return new GZipStream(source, CompressionMode.Decompress);
    }

    private static string Hash(Workspace workspace)
    {
        var snapshot = new StringBuilder(workspace.WidthTiles * workspace.HeightTiles * 20);
        for (int x = 0; x < workspace.WidthTiles; x++)
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                snapshot.Append(tile.IsActive ? '1' : '0').Append(',')
                    .Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                    .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',')
                    .Append(tile.LiquidAmount).Append(';')
                    .Append((byte)tile.LiquidKind).Append(',')
                    .Append(tile.TileColor).Append(',').Append(tile.WallColor).Append(';');
            }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToString())));
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
