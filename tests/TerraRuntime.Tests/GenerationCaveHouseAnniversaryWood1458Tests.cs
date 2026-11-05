using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationCaveHouseAnniversaryWood1458Tests
{
    // Untouched TerrariaServer 1.4.5.8 WoodHouseBuilder constructor, then
    // RainbowifyOnTenthAnniversaryWorlds on two 20x10 active rooms, seed stream
    // fresh per case. Original source fixture SHA256 E26A94EFB37EBDBF...
    [Theory]
    [InlineData(1, 160, 44, 3,
        "1aa37a1ab756a10275f636312e6854d862c4f3b90d76680455f7ba4e7f40a3d7",
        "53d73dba29dc026d09df6780b62332e0a3806b2bff998f52485cdebe5b7711ff", 1657007234)]
    [InlineData(42, 160, 44, 3,
        "1aa37a1ab756a10275f636312e6854d862c4f3b90d76680455f7ba4e7f40a3d7",
        "e2dfdf924f9fee510d25da437c4d31f6c2bb473c605bc18295d21fb25811bb6e", 1122627734)]
    [InlineData(1458, 30, 27, 2,
        "82c81bb1618b3a74c02443f2594a69384d6f86bcae7326eb75beed0dead7ad98",
        "80857a4bb031d6a94a105b9936588f32fb812d5bf29a3d5d79c06998e0fb2883", 1916656655)]
    [InlineData(326, 160, 44, 3,
        "82c81bb1618b3a74c02443f2594a69384d6f86bcae7326eb75beed0dead7ad98",
        "5fbe6e496a302756dfc23271ec0dfa042524da0f10abf2da7e4157b12fbcc8d2", 301842919)]
    public void Original_wood_constructor_and_rainbowify_match_palette_paint_and_full_rng(
        int seed, ushort expectedTile, ushort expectedWall, uint expectedIndex,
        string expectedPaintHash, string expectedRngHash, int expectedNext)
    {
        var random = new RandomAdapter(seed);
        var profile = new VanillaWorldSeedProfile1458(VanillaSpecialWorldSeed1458.CelebrationMk10,
            VanillaSecretWorldSeed1458.None);
        random.NextDouble(); // Source PotentiallyConvertToSeedHouse, before its empty choices.
        (ushort tileType, ushort wallType) = CaveHousePlacement1458.RollWoodAnniversaryPalette(random, profile);
        Assert.Equal(expectedTile, tileType);
        Assert.Equal(expectedWall, wallType);
        Assert.Equal((uint)2, random.Index);
        var world = new Workspace(240, 240);
        CaveHouseRoom1458[] rooms = [new(100, 100, 20, 10), new(100, 125, 20, 10)];
        foreach (CaveHouseRoom1458 room in rooms)
            for (int x = room.Left; x < room.Right; x++)
                for (int y = room.Top; y < room.Bottom; y++)
                {
                    var tile = new WorldTile { Type = tileType, Wall = wallType,
                        Flags = WorldTileFlags.Active };
                    world.TileStore.Set(x, y, in tile);
                }
        CaveHousePlacement1458.RainbowifyWoodAnniversaryRooms(world, random, rooms, tileType);
        Assert.Equal(expectedPaintHash, PaintHash(world, rooms));
        Assert.Equal(expectedIndex, random.Index);
        Assert.Equal(expectedRngHash, random.SeedArrayHash());
        Assert.Equal(expectedNext, random.Next());
    }

    [Fact]
    public void Non_anniversary_profile_cannot_enter_anniversary_palette_or_spend_rng()
    {
        var random = new RandomAdapter(1458);
        int expected = new RandomAdapter(1458).Next();
        Assert.Throws<NotSupportedException>(() =>
            CaveHousePlacement1458.RollWoodAnniversaryPalette(random, default));
        Assert.Equal(expected, random.Next());
    }

    private static string PaintHash(Workspace world, ReadOnlySpan<CaveHouseRoom1458> rooms)
    {
        var text = new StringBuilder();
        for (int x = 100; x < 120; x++)
            for (int y = 100; y < 135; y++)
                if (y is >= 100 and < 110 || y is >= 125 and < 135)
                {
                    WorldTile tile = world.TileStore.Get(x, y);
                    text.Append(x).Append(',').Append(y).Append(',')
                        .Append(tile.TileColor).Append(',').Append(tile.WallColor).Append(';');
                }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        public uint Index => (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", Private)!
            .GetValue(random)!;
        public string SeedArrayHash()
        {
            int[] values = (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", Private)!
                .GetValue(random)!;
            byte[] bytes = new byte[values.Length * 4];
            for (int i = 0; i < values.Length; i++)
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4), values[i]);
            return Convert.ToHexStringLower(SHA256.HashData(bytes));
        }
        public int Next() => random.Next();
        public int Next(int maximum) => random.Next(maximum);
        public int Next(int minimum, int maximum) => random.Next(minimum, maximum);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
