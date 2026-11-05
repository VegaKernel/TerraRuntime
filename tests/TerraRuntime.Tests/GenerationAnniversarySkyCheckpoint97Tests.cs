using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationAnniversarySkyCheckpoint97Tests
{
    [Theory]
    [InlineData(1458,
        "d1db0987eb53053f2a91b2ffc4c17a75bfe22eba4bce45aa06d09677aec9d41d",
        "63f8c119f2561c2d36afadb87ba399be906df2c999d85a3d08cab794c70a5ee8",
        1993530578)]
    [InlineData(42,
        "f986a9f663b4228c7ab2202d78968af372d8cfba7beca8ed88db4e99205f5ef9",
        "01e41e302a976797e15abf01e371727832bbe8869b4e7962320e1bd8e833c983",
        188741157)]
    public void Direct_official_sky_helper_on_committed_anniversary_checkpoint97_matches_grid_and_rng(
        int seed, string preHash, string postHash, int nextRandom)
    {
        // Whole 4200x1200 committed official checkpoint immediately before
        // Micro98, imported without a later pass. Both source and runtime call
        // ConvertSkyIslands(2,true) directly with a fresh seed. This is a
        // detached helper differential, not continuous FinalCleanup input.
        Workspace world = Import(seed);
        Assert.Equal(preHash, HashSixFields(world));
        var random = new RandomAdapter(seed);

        AnniversarySkyIslandConversion1458.ConvertClearGrassOffers(world, random,
            TestContext.Current.CancellationToken);

        Assert.Equal(postHash, HashSixFields(world));
        Assert.Equal(nextRandom, random.Next());
    }

    private static Workspace Import(int seed)
    {
        using var json = JsonDocument.Parse(Resource($"checkpoint97-Anniversary-{seed}.json.gz"));
        JsonElement input = json.RootElement;
        var world = new Workspace(4200, 1200);
        world.InitializeVanillaGenerationSolidity();
        Assert.True(world.TrySetLayers(input.GetProperty("result").GetProperty("worldSurface").GetDouble(),
            input.GetProperty("result").GetProperty("rockLayer").GetDouble()));
        world.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.CelebrationMk10,
            VanillaSecretWorldSeed1458.None));
        int identity = 0;
        foreach (JsonElement solid in input.GetProperty("expectedSolidity").EnumerateArray())
            world.SetVanillaGenerationSolid(new TileTypeId(identity++), solid.GetBoolean());
        using var stream = Resource($"checkpoint97-Anniversary-{seed}.bin.gz");
        using var reader = new BinaryReader(stream);
        foreach (ref WorldTile tile in world.TileStore.Tiles)
        {
            bool active = reader.ReadByte() != 0;
            tile.Type = reader.ReadUInt16(); tile.Wall = reader.ReadUInt16();
            tile.FrameX = reader.ReadInt16(); tile.FrameY = reader.ReadInt16();
            tile.LiquidAmount = reader.ReadByte(); tile.LiquidKind = (WorldLiquidKind)reader.ReadByte();
            bool half = reader.ReadByte() != 0; byte slope = reader.ReadByte();
            tile.TileColor = reader.ReadByte(); tile.WallColor = reader.ReadByte();
            ushort flags = reader.ReadUInt16();
            if (active) tile.Flags |= WorldTileFlags.Active;
            Assert.True(tile.TrySetSourceShape(half, slope));
            for (int bit = 0; bit < Masks.Length; bit++)
                if ((flags & (1 << bit)) != 0) tile.Flags |= Masks[bit];
        }
        return world;
    }

    private static readonly WorldTileFlags[] Masks =
    [
        WorldTileFlags.WireRed, WorldTileFlags.WireBlue, WorldTileFlags.WireGreen,
        WorldTileFlags.WireYellow, WorldTileFlags.Actuator, WorldTileFlags.Inactive,
        WorldTileFlags.InvisibleBlock, WorldTileFlags.FullbrightBlock,
        WorldTileFlags.InvisibleWall, WorldTileFlags.FullbrightWall
    ];

    private static Stream Resource(string name)
    {
        Stream source = typeof(GenerationAnniversarySkyCheckpoint97Tests).Assembly
            .GetManifestResourceStream("GenerationSpecialMicro98." + name) ??
            throw new InvalidOperationException("Missing source97 checkpoint fixture.");
        using var zip = new GZipStream(source, CompressionMode.Decompress);
        var memory = new MemoryStream();
        zip.CopyTo(memory);
        memory.Position = 0;
        return memory;
    }

    private static string HashSixFields(Workspace world)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ProjectSixFields(world))));
    }

    private static string ProjectSixFields(Workspace world)
    {
        var text = new StringBuilder(4200 * 1200 * 20);
        for (int x = 0; x < 4200; x++)
            for (int y = 0; y < 1200; y++)
            {
                WorldTile tile = world.TileStore.Get(x, y);
                text.Append(tile.IsActive ? '1' : '0').Append(',')
                    .Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                    .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',')
                    .Append(tile.LiquidAmount).Append(';');
            }
        return text.ToString();
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int maximum) => random.Next(maximum);
        public int Next(int minimum, int maximum) => random.Next(minimum, maximum);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
