using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationWorldConvertThorns1458Tests
{
    [Theory]
    [InlineData(1, "5E24D1EAC6E674BC30504E6DC3B791C573F265A4D8E36FE230639EF4B26B4F00", 32, 1916656655, 2)]
    [InlineData(2, "DBBBAABC7C214F3CA2C2B2E26660F835C17D24C42D1F942DAC6FD5BB80F330CB", 0, 861586060, 22)]
    [InlineData(4, "732C956BAFF583C8EBABFAF4662F6C2A877757FA489922486E86360755A43539", 352, 1916656655, 2)]
    public void Thorn_conversion_matches_original_grid_and_kill_dust_rng(int conversionType,
        string sourceHash, ushort outputType, int next, int draws)
    {
        // Direct unmodified 1.4.5.8 WorldGen.Convert on source
        // TileID.Sets.Conversion.Thorn={32,69,352,655} after the seven-site
        // baseline. Corrupt/Crimson remap all four; Hallow KillTile removes
        // all four and only identities32/352 spend ten dust choices each.
        WorldTileStore store = new(new WorldDimensions(200, 200));
        Put(store, 20, 100, 1);
        Put(store, 30, 100, 2);
        Put(store, 40, 100, 161);
        Put(store, 50, 100, 53);
        Put(store, 60, 100, 56);
        Put(store, 70, 100, 0, 1);
        Put(store, 80, 100, 1, 1);
        Put(store, 30, 101, 1);
        Put(store, 50, 101, 1);
        ushort[] thornIds = [32, 69, 352, 655];
        for (int index = 0; index < thornIds.Length; index++)
            Put(store, 20 + index * 20, 120, thornIds[index]);
        CountingRandom random = new(1458);
        GenerationWorldConvert1458 converter = new(store, random, new GenerationTileFraming1458(store, random));

        foreach (int x in new[] { 20, 30, 40, 50, 60, 70, 80 })
            converter.ConvertAt(x, 100, conversionType, rainbowSandAndBlackSandWalls: false);
        for (int index = 0; index < thornIds.Length; index++)
            converter.ConvertAt(20 + index * 20, 120, conversionType, rainbowSandAndBlackSandWalls: false);

        Assert.Equal(sourceHash, Hash(store));
        Assert.Equal(draws, random.Count);
        Assert.Equal(next, random.Next());
        for (int index = 0; index < thornIds.Length; index++)
        {
            WorldTile thorn = store.Get(20 + index * 20, 120);
            Assert.Equal(outputType, thorn.Type);
            Assert.Equal(conversionType != 2, thorn.IsActive);
            if (conversionType == 2)
            {
                Assert.Equal((short)-1, thorn.FrameX);
                Assert.Equal((short)-1, thorn.FrameY);
            }
        }
    }

    private static void Put(WorldTileStore store, int x, int y, ushort type, ushort wall = 0) =>
        store.Set(x, y, new WorldTile { Type = type, Wall = wall,
            Flags = type == 0 ? WorldTileFlags.None : WorldTileFlags.Active });

    private static string Hash(WorldTileStore store)
    {
        StringBuilder snapshot = new(200 * 200 * 20);
        for (int x = 0; x < 200; x++)
            for (int y = 0; y < 200; y++)
            {
                WorldTile tile = store.Get(x, y);
                snapshot.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',')
                    .Append(tile.Wall).Append(',').Append(tile.FrameX).Append(',')
                    .Append(tile.FrameY).Append(',').Append(tile.LiquidAmount).Append(';');
            }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToString())));
    }

    private sealed class CountingRandom(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 source = new(seed);
        public int Count { get; private set; }
        public int Next() { Count++; return source.Next(); }
        public int Next(int max) { Count++; return source.Next(max); }
        public int Next(int min, int max) { Count++; return source.Next(min, max); }
        public double NextDouble() { Count++; return source.NextDouble(); }
        public void NextBytes(byte[] bytes) { Count++; source.NextBytes(bytes); }
    }
}
