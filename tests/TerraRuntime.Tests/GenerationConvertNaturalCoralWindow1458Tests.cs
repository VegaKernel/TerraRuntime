using System.Buffers.Binary;
using System.Security.Cryptography;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationConvertNaturalCoralWindow1458Tests
{
    private const string OfficialFull17 = "09E49E19C9B49817024B773890EF117D9F5B48B20489472422A83B30F27AC739";

    [Fact]
    public void Direct_official_hallow_convert_leaves_natural_coral81_unchanged()
    {
        // Direct TerrariaServer 1.4.5.8 WorldGen.Convert(7,7,2) on a
        // controlled 15x15 rehost. The center is byte-exact to genuine
        // Anniversary+Remix pre108 (45,296): active81, water255, wall0,
        // frame78/0. The surrounding void is constructed, not a full prefix.
        Workspace world = Create();
        Assert.Equal(OfficialFull17, Hash17(world.TileStore));
        var random = new RandomAdapter(1458);
        var framing = new GenerationTileFraming1458(world.TileStore, random, world,
            useWorldGenerationSolidity: true);
        new GenerationWorldConvert1458(world.TileStore, random, framing)
            .ConvertAt(7, 7, 2, rainbowSandAndBlackSandWalls: false);

        Assert.Equal(OfficialFull17, Hash17(world.TileStore));
        WorldTile coral = world.TileStore.Get(7, 7);
        Assert.True(coral.IsActive);
        Assert.Equal((ushort)81, coral.Type);
        Assert.Equal((ushort)0, coral.Wall);
        Assert.Equal((byte)255, coral.LiquidAmount);
        Assert.Equal((short)78, coral.FrameX);
        Assert.Equal((short)0, coral.FrameY);
        Assert.Equal(906992634, random.Next());
    }

    private static Workspace Create()
    {
        var world = new Workspace(15, 15);
        world.InitializeVanillaGenerationSolidity();
        world.SetVanillaSeedProfile(new(
            VanillaSpecialWorldSeed1458.CelebrationMk10 | VanillaSpecialWorldSeed1458.Remix,
            VanillaSecretWorldSeed1458.None));
        var coral = new WorldTile
        {
            Flags = WorldTileFlags.Active,
            Type = 81,
            LiquidAmount = 255,
            LiquidKind = WorldLiquidKind.Water,
            FrameX = 78,
            FrameY = 0
        };
        world.TileStore.Set(7, 7, in coral);
        return world;
    }

    private static string Hash17(WorldTileStore store)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> cell = stackalloc byte[17];
        foreach (WorldTile tile in store.Tiles)
        {
            cell[0] = (byte)(tile.IsActive ? 1 : 0);
            BinaryPrimitives.WriteUInt16LittleEndian(cell[1..3], tile.Type);
            BinaryPrimitives.WriteUInt16LittleEndian(cell[3..5], tile.Wall);
            BinaryPrimitives.WriteInt16LittleEndian(cell[5..7], tile.FrameX);
            BinaryPrimitives.WriteInt16LittleEndian(cell[7..9], tile.FrameY);
            cell[9] = tile.LiquidAmount;
            cell[10] = (byte)tile.LiquidKind;
            cell[11] = (byte)(tile.IsHalfBrick ? 1 : 0);
            cell[12] = tile.SourceSlope;
            cell[13] = tile.TileColor;
            cell[14] = tile.WallColor;
            ushort flags = 0;
            for (int bit = 0; bit < ExtraFlags.Length; bit++)
                if ((tile.Flags & ExtraFlags[bit]) != 0) flags |= (ushort)(1 << bit);
            BinaryPrimitives.WriteUInt16LittleEndian(cell[15..17], flags);
            hash.AppendData(cell);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static readonly WorldTileFlags[] ExtraFlags =
    [
        WorldTileFlags.WireRed, WorldTileFlags.WireBlue, WorldTileFlags.WireGreen,
        WorldTileFlags.WireYellow, WorldTileFlags.Actuator, WorldTileFlags.Inactive,
        WorldTileFlags.InvisibleBlock, WorldTileFlags.FullbrightBlock,
        WorldTileFlags.InvisibleWall, WorldTileFlags.FullbrightWall
    ];

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
