using System.Buffers.Binary;
using System.Security.Cryptography;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationMoss184TileFrame1458Tests
{
    [Fact]
    public void Official_long_moss_five_direction_and_retirement_grid_matches()
    {
        // Untouched TerrariaServer.exe 1.4.5.8 WorldGen.TileFrame, seed1458,
        // five calls in x order, 600x500 stone predecessor and source default chamber.
        var store = new WorldTileStore(new WorldDimensions(600, 500));
        var stone = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        for (int x = 0; x < 600; x++) for (int y = 0; y < 500; y++)
            store.Set(x, y, in stone);
        for (int x = 120; x < 180; x++) for (int y = 120; y < 300; y++)
            store.Set(x, y, default);
        foreach ((int x, ushort support, int sx, int sy) in new[] {
            (300, (ushort)179, 300, 201), (320, (ushort)180, 320, 199),
            (340, (ushort)182, 339, 200), (360, (ushort)381, 361, 200),
            (380, (ushort)0, 0, 0) })
        {
            store.Set(x, 200, new WorldTile { Type = 184, Flags = WorldTileFlags.Active, FrameY = 216 });
            if (support != 0)
                store.Set(sx, sy, new WorldTile { Type = support, Flags = WorldTileFlags.Active });
        }
        Assert.Equal("DA95BFCDB65EC549405E29198F18F107023257D34D20DE61A44A06790AE33E60", Hash17(store));

        var random = new Adapter(1458);
        var framing = new GenerationTileFraming1458(store, random);
        foreach (int x in new[] { 300, 320, 340, 360, 380 }) framing.TileFrame(x, 200);

        Assert.Equal("170A2E13BBEED26C4A11A78473AC410C75C898B5D5A35D9752A2FF3C4868F53F", Hash17(store));
        Assert.Equal(1507290721, random.Next());
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
            cell[9] = tile.LiquidAmount; cell[10] = (byte)tile.LiquidKind;
            cell[11] = (byte)(tile.IsHalfBrick ? 1 : 0); cell[12] = tile.SourceSlope;
            cell[13] = tile.TileColor; cell[14] = tile.WallColor;
            ushort flags = 0;
            WorldTileFlags[] extras = [WorldTileFlags.WireRed, WorldTileFlags.WireBlue,
                WorldTileFlags.WireGreen, WorldTileFlags.WireYellow, WorldTileFlags.Actuator,
                WorldTileFlags.Inactive, WorldTileFlags.InvisibleBlock, WorldTileFlags.FullbrightBlock,
                WorldTileFlags.InvisibleWall, WorldTileFlags.FullbrightWall];
            for (int bit = 0; bit < extras.Length; bit++)
                if ((tile.Flags & extras[bit]) != 0) flags |= (ushort)(1 << bit);
            BinaryPrimitives.WriteUInt16LittleEndian(cell[15..17], flags);
            hash.AppendData(cell);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private sealed class Adapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
