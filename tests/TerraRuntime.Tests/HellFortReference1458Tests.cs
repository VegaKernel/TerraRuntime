using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class HellFortReference1458Tests
{
    // Independently captured from official TerrariaServer1.4.5.8 HellFort.
    // Complete normalized16-byte cells, x-major, and next UnifiedRandom value.
    [Theory]
    [InlineData(75, 0, 42, "7ABAC9A54E498146619DCDADF34611F5B160AF5FEDBF82030F2737548DDF37A3", 741756990)]
    [InlineData(75, 0, 1458, "A7FEDB113C3001D896961544F9B43AB182BBE9CC730CA3602D3632BDDFBE6AD3", 1381926108)]
    [InlineData(75, 0, 8675309, "24BA29828AEF469638DCC030B3EC3AA104BC20499227711874951784C13F1C97", 2070751151)]
    [InlineData(75, -1, 42, "A86767511786ACF946842BC5F5755955A3096916FF6772509F7B882B0C024E69", 741756990)]
    [InlineData(75, -1, 1458, "4DEEA113A1A5EBA09B36B231EDA657F266683870464B86F03E364E0935C44FD8", 1381926108)]
    [InlineData(75, -1, 8675309, "285AFA8AFB80EE05E0795B675FA32FCC0B6D18E10C0EFFA9844107219E5009A4", 2070751151)]
    [InlineData(76, 0, 42, "F73BC51AD150CE0E799863DDFFFAB0AF3A46CD28F0ECF6F88B2542470DBC7786", 741756990)]
    [InlineData(76, 0, 1458, "A782C3ED32BE999B650CF889B1A3B5B07FA66AE09426EF41870EB28A7EC28A86", 1381926108)]
    [InlineData(76, 0, 8675309, "DEF85B495C347F3DC372977BB4EEF52D8420E4D8C87B7BF20FAE94470F25D70E", 2070751151)]
    [InlineData(76, -1, 42, "E4323680235DECE006FE2C80102B2FA77BCD17F274FBD70F5E912324696A726F", 741756990)]
    [InlineData(76, -1, 1458, "2C3797B085612FD3FA481FCD388590DE9DF66D87DD3FBA70599C8CB5B55CB9C2", 1381926108)]
    [InlineData(76, -1, 8675309, "93B155481A0264294E979E3BADB8F6196DF9E601BFD0C277686450A45148E869", 2070751151)]
    public void Fort_matches_official_cells_and_random(ushort brick, short frame, int seed, string hash, int next)
    {
        var store = new WorldTileStore(new WorldDimensions(600, 400));
        for (int i = 0; i < store.Tiles.Length; i++)
            store.Tiles[i] = new WorldTile { FrameX = frame, FrameY = frame };
        var random = new RandomAdapter(seed);
        HellFortGenerator1458.Build(store, 300, 320, brick, (ushort)(brick == 75 ? 14 : 13), random, TestContext.Current.CancellationToken);
        var cells = new WorldTile[600 * 400];
        for (int x = 0; x < 600; x++)
        for (int y = 0; y < 400; y++) cells[x * 400 + y] = store.Get(x, y);
        Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(cells.AsSpan()))));
        Assert.Equal(next, random.Next());
    }

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
