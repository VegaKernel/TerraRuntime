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

public sealed class GenerationLarvaCheckpoint97FramingCausal1458Tests
{
    [Fact]
    public void Original_PlaceTile231_frames_and_removes_an_unsupported_neighbour_vine()
    {
        // The 21x21 terrain is actual checkpoint97-NotTheBees-1458 around
        // (400,877). The one deliberate edit makes a neighbouring vine lose
        // its support, as a later terrain pass can. The anchor is occupied mud.
        Workspace workspace = Fixture();
        WorldTile vine = workspace.TileStore.Get(401, 877);
        vine.Type = 62;
        vine.Flags |= WorldTileFlags.Active;
        vine.FrameX = vine.FrameY = 0;
        workspace.TileStore.Set(401, 877, in vine);
        WorldTile above = workspace.TileStore.Get(401, 876);
        above.Type = 0;
        above.Flags &= ~WorldTileFlags.Active;
        workspace.TileStore.Set(401, 876, in above);
        var random = new RandomAdapter(1458);
        var framing = new GenerationTileFraming1458(
            workspace.TileStore, random, workspace, useWorldGenerationSolidity: true);
        Assert.True(workspace.TileStore.Get(401, 877).IsActive);

        Assert.True(framing.TryPlaceLarva(400, 877));

        WorldTile after = workspace.TileStore.Get(401, 877);
        Assert.False(after.IsActive);
        Assert.Equal((ushort)0, after.Type);
        Assert.Equal((short)-1, after.FrameX);
        Assert.Equal((short)-1, after.FrameY);
        Assert.Equal("c368dfa1b055624008eae37e8163cff87f4cef336510d2c6e43e3e8d1ad22f27", Hash(workspace));
        Assert.Equal(906992634, random.Next());
    }

    private static Workspace Fixture()
    {
        var workspace = new Workspace(4200, 1200);
        workspace.InitializeVanillaGenerationSolidity();
        WorldTile stone = new() { Type = 1, Flags = WorldTileFlags.Active };
        for (int x = 0; x < 4200; x++)
            for (int y = 0; y < 1200; y++)
                workspace.TileStore.Set(x, y, in stone);
        for (int x = 120; x < 180; x++)
            for (int y = 120; y < 300; y++)
                workspace.TileStore.Set(x, y, default);
        using (Stream stream = Resource("checkpoint97-NotTheBees-1458.json.gz"))
        using (var json = JsonDocument.Parse(stream))
        {
            int id = 0;
            foreach (JsonElement solid in json.RootElement.GetProperty("expectedSolidity").EnumerateArray())
                workspace.SetVanillaGenerationSolid(new TileTypeId((ushort)id++), solid.GetBoolean());
        }
        using Stream binary = Resource("checkpoint97-NotTheBees-1458.bin.gz");
        using var memory = new MemoryStream();
        binary.CopyTo(memory);
        byte[] data = memory.ToArray();
        const int sourceHeight = 1200, record = 17;
        for (int x = 390; x <= 410; x++)
            for (int y = 867; y <= 887; y++)
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
                ?? throw new InvalidOperationException("Missing checkpoint97 metadata resource.");
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
