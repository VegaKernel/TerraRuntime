using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class SurfaceOreStone1458Tests
{
    [Theory]
    [InlineData(false, "B4928BC6F590034AF7B253ECEF67EBD15A15EE2C4B102B4DFB4D2B16A760168C", 1375162615)]
    [InlineData(true, "5B8CBF81F1E8DEE183F97820392690732F5CE10D877759FE54415DA76A7A2822", 288056266)]
    public void Registered_pass_matches_official_terrain_fixtures(bool openSurface, string expectedHash, int expectedNext)
    {
        // Direct TerrariaServer 1.4.5.8 PassLegacy("Surface Ore and Stone"), seed 1458,
        // 1200x500; the open variant reaches source PlaceTile/PlaceSmallPile decorations.
        Verify(1200, 500, openSurface ? 100 : 102, openSurface ? 107 : 102,
            95, 103, openSurface, 1, 688, expectedHash, expectedNext);
    }

    [Fact]
    public void Registered_pass_matches_official_canonical_world_fixture()
    {
        // Direct official PassLegacy, ordinary seed 1458, canonical 4200x1200 geometry.
        Verify(4200, 1200, 240, 247, 235, 243, true, 7, 2004,
            "C0E767F1C76D61753E1CBD9CB68F5CD8A4FCF3E5DFDABDC91CF5E5CE683B7E96",
            1029019387);
    }

    [Fact]
    public void Registered_pass_matches_official_canonical_mixed_terrain_fixture()
    {
        // The official pass rejects sand, cloud, dungeon, no-wall and non-Conversion.Grass strips;
        // crimson subterrain rejects only StonePatch, not OrePatch.
        Verify(4200, 1200, 240, 247, 235, 243, true, 6, 2004,
            "75A21AEADA7707CE22A9DF3B61652AA8CAB975D775BFBD1E7A21AAB1C8354996",
            1038617205, mixedTerrain: true);
    }

    private static void Verify(int width, int height, int grassRow, int wallRow,
        int surfaceLow, int surface, bool openSurface, int anchorCount, int firstAnchor,
        string expectedHash, int expectedNext, bool mixedTerrain = false)
    {
        var store = new WorldTileStore(new WorldDimensions(width, height));
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            ref WorldTile tile = ref store.Tiles[store.GetUncheckedIndex(x, y)];
            if (!openSurface || y >= grassRow) tile.Flags |= WorldTileFlags.Active;
            tile.Type = openSurface ? y == grassRow ? (ushort)2 : (ushort)0 :
                y < grassRow ? (ushort)2 : (ushort)0;
            tile.Wall = y >= wallRow ? (ushort)1 : (ushort)0;
            if (mixedTerrain)
            {
                if (y == 240 && x is >= 3500 and < 3700) tile.Type = 661;
                if (y is >= 247 and < 320)
                {
                    if (x is >= 390 and < 700) tile.Type = 53;
                    if (x is >= 900 and < 1200) tile.Type = 189;
                    if (x is >= 1400 and < 1650) tile.Type = 41;
                    if (x is >= 2500 and < 2700) tile.Type = 199;
                    if (x is >= 3000 and < 3300) tile.Wall = 0;
                }
            }
        }

        var random = new RandomAdapter(1458);
        var pass = new SurfaceOreStone1458(store, random, surfaceLow, surface, 7, 6, CancellationToken.None);
        pass.Apply();
        Assert.True(pass.Changed > 0);
        var anchors = (List<int>)typeof(SurfaceOreStone1458)
            .GetField("oreAnchors", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(pass)!;
        Assert.Equal(anchorCount, anchors.Count);
        Assert.Equal(firstAnchor, anchors[0]);
        Assert.Equal(expectedNext, random.Next());

        var snapshot = new StringBuilder(width * height * 12);
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            WorldTile tile = store.Get(x, y);
            snapshot.Append(tile.IsActive ? '1' : '0').Append(',')
                .Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',')
                .Append(tile.LiquidAmount).Append(';');
        }
        Assert.Equal(expectedHash,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToString()))));
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
