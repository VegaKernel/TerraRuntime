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

public sealed class GenerationGrassCheckpoint97Terrain1458Tests
{
    [Theory]
    [InlineData(60, 400, 877, "e3f72288e8d2d23451c312990d75a13c11d579376ef03bd447415307028586a1")]
    [InlineData(70, 437, 1014, "8af490a96e96a6d6d348289cc3aa7c7a06caaa6d35e722d901166915a3b493ca")]
    public void Actual_checkpoint97_mud_window_matches_official_SpreadGrass_full_grid_and_rng(
        ushort target, int anchorX, int anchorY, string expectedHash)
    {
        // Direct official WorldGen.SpreadGrass(x,y,59,target,true) on an authentic
        // 21x21 NotTheBees checkpoint97 window, rehosted in an otherwise stone
        // canonical-size scene. This is not the exact pre-FinishNotTheBees state.
        Workspace workspace = Fixture(anchorX, anchorY);
        var random = new RandomAdapter(1458);
        var framing = new GenerationTileFraming1458(
            workspace.TileStore, random, workspace, useWorldGenerationSolidity: true);
        var spread = new GenerationGrass1458(
            workspace.TileStore, random, TestContext.Current.CancellationToken,
            worldSurface: 140, frameSquare: (x, y) => framing.SquareTileFrame(x, y), workspace: workspace);
        Assert.NotEqual(expectedHash, Hash(workspace));

        spread.Apply(anchorX, anchorY, 59, target);

        Assert.Equal(target, workspace.TileStore.Get(anchorX, anchorY).Type);
        Assert.Equal(expectedHash, Hash(workspace));
        Assert.Equal(906992634, random.Next());
    }

    private static Workspace Fixture(int anchorX, int anchorY)
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
