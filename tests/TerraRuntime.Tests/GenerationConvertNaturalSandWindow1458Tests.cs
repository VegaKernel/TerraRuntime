using System.Buffers.Binary;
using System.Security.Cryptography;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationConvertNaturalSandWindow1458Tests
{
    private const string SourcePre17 = "7832E0778EF73CD0DC0FF15E5A2795CFAB8C87605948581282DBE1312210C628";
    private const string SourcePost17 = "A39EF1D80163209730D74F86A03D193D89202CA613426D601479478622A05FFB";

    [Fact]
    public void Direct_official_hallow_convert_matches_natural_nine_cell_window_and_rng()
    {
        // Untouched 1.4.5.8 WorldGen.Convert(7,7,2) on a controlled 15x15
        // rehost. Its central 3x3 input is byte-exact to genuine canonical
        // Anniversary+Remix pre108 around (25,303), including two slope-2
        // sand cells and three inactive water255 cells. The outer halo is
        // constructed and this does not claim registered pass parity.
        Workspace world = Create();
        Assert.Equal(SourcePre17, Hash17(world.TileStore));
        var random = new RandomAdapter(1458);
        var framing = new GenerationTileFraming1458(world.TileStore, random, world,
            useWorldGenerationSolidity: true);
        new GenerationWorldConvert1458(world.TileStore, random, framing)
            .ConvertAt(7, 7, 2, rainbowSandAndBlackSandWalls: false);

        Assert.Equal(SourcePost17, Hash17(world.TileStore));
        Assert.Equal((ushort)116, world.TileStore.Get(7, 7).Type);
        Assert.Equal((byte)2, world.TileStore.Get(7, 7).SourceSlope);
        Assert.Equal(906992634, random.Next());
    }

    [Fact]
    public void Omitting_natural_window_conversion_fails_official_full_grid()
    {
        Workspace world = Create();
        Assert.Equal(SourcePre17, Hash17(world.TileStore));
        Assert.NotEqual(SourcePost17, Hash17(world.TileStore));
    }

    private static Workspace Create()
    {
        var world = new Workspace(15, 15);
        world.InitializeVanillaGenerationSolidity();
        world.SetVanillaSeedProfile(new(
            VanillaSpecialWorldSeed1458.CelebrationMk10 | VanillaSpecialWorldSeed1458.Remix,
            VanillaSecretWorldSeed1458.None));
        Put(6, 6, false, 0, 255); Put(6, 7, false, 0, 255);
        Put(6, 8, true, 112, 0); Put(7, 6, false, 0, 255);
        Put(7, 7, true, 112, 0, slope: 2); Put(7, 8, true, 112, 0);
        Put(8, 6, true, 112, 0, slope: 2); Put(8, 7, true, 112, 0);
        Put(8, 8, true, 112, 0);
        return world;

        void Put(int x, int y, bool active, ushort type, byte liquid, byte slope = 0)
        {
            var tile = new WorldTile
            {
                Type = type,
                FrameX = -1,
                FrameY = -1,
                LiquidAmount = liquid,
                LiquidKind = WorldLiquidKind.Water,
                Flags = active ? WorldTileFlags.Active : WorldTileFlags.None
            };
            Assert.True(tile.TrySetSourceShape(false, slope));
            world.TileStore.Set(x, y, in tile);
        }
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
