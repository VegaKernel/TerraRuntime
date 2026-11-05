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

public sealed class GenerationLarvaCheckpoint97ClassMatrix1458Tests
{
    [Theory]
    [InlineData(382, 240, 5, true, "a91f91ee85f0251eb9af685ef81241bfffb4c3fb0de5c57ecad363861d9d6ea3")]
    [InlineData(367, 799, 62, true, "c7e22cce75ee0453bdd3d41ecede3cc2d7999c3631810164a02c202d957e6797")]
    [InlineData(351, 632, 185, true, "fb83c003966e8284613be62dcd8ec86708371c0cd85b5362e179eccea08c3760")]
    [InlineData(404, 253, 488, false, "8d32161e4d76663260993c03996ef39cd643067752b0ea9c3cd2338927eb27a2")]
    [InlineData(3633, 226, 52, true, "89c13380159e3cf0ee175d62568b2efa521ac221829eb8fe7d1ea60d73e4f861")]
    [InlineData(419, 631, 165, true, "e454a8b1a14d2054acb761cb3df880aa481184de2044735ffc89050fe1550f79")]
    [InlineData(375, 300, 186, true, "09d09c7e781d1f8aa19ebc4cf2d8e876500d5ebd5ff83a367af00d585749a135")]
    [InlineData(2659, 859, 231, true, "3869722e436f7fcbc42b54a11cfc827296721cd3e7ec341594138965f0f5a2ad")]
    [InlineData(351, 395, 7, true, "fe21d83919b2db7e7f39940302a94f14db133774191fcb3b7ebacad9285186d6")]
    public void Checkpoint97_varied_anchor_class_matches_official_PlaceTile231(
        int anchorX, int anchorY, ushort expectedType, bool expectedResult, string expectedHash)
    {
        // Direct official 1.4.5.8 PlaceTile231 on 21x21 actual NotTheBees
        // checkpoint97 terrain, rehosted in a canonical-size otherwise stone
        // scene. Not the exact pre-Finish pass state.
        Workspace workspace = Fixture(anchorX, anchorY);
        var random = new RandomAdapter(1458);
        var framing = new GenerationTileFraming1458(
            workspace.TileStore, random, workspace, useWorldGenerationSolidity: true);
        Assert.Equal(expectedType, workspace.TileStore.Get(anchorX, anchorY).Type);

        bool placed = framing.TryPlaceLarva(anchorX, anchorY);

        Assert.Equal(expectedResult, placed);
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
