using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for the registered TerrariaServer 1.4.5.8 <c>GenPassNameID.SurfaceChests</c> pass.
/// </summary>
/// <remarks>
/// <para>
/// Expectations come from calling the unmodified registered delegate through <c>GenPass.Apply</c> on the same
/// synthetic surface inside the pinned dedicated server, and checking the next four shared RNG values, a
/// SHA-256 over every field of every cell, and every chest's ordered inventory with stacks and prefixes.
/// </para>
/// <para>
/// The fixtures separate the pass's two halves. An EMPTY sample is admitted on its wall alone, so a dirt wall
/// and a flower wall behave identically and a wall of nothing at all admits nothing - the bare fixture takes
/// no chest anywhere and pays two thousand refused attempts for each of the seven it wanted, which is the only
/// way to see the retry counter on its own. An OCCUPIED sample instead sweeps a hundred-and-one-tile square
/// looking for living wood, and the three solid fixtures vary how much of it it finds: none, narrow bands, and
/// everywhere. That sweep spends a value on every candidate whether or not it wins, so the same seven chests
/// cost wildly different amounts of stream in each.
/// </para>
/// </remarks>
public sealed class SurfaceChestsPass1458Tests
{
    private const int Width = 1400;
    private const int Height = 900;
    private const double WorldSurfaceLow = 180.0;
    private const double WorldSurface = 260.0;
    private const double RockLayer = 360.0;
    private const int BeachDistance = 380;

    [Theory]
    // fixture, seed, four next draws, worldHash, inventories
    [InlineData("plain", 42, 890763, 969192, 156181, 587529,
        "65a6f669bbf812ff0cac6a0a15ffd962058362b13c50b4451b7c830faea5a1d1",
        "[867,198]281x1p21 20x7 965x69 290x1 8x17 5508x1 [853,228]284x1 4345x1 20x3 965x89 40x31 292x1 9x71 [827,198]280x1p53 282x65 279x220 3093x1 22x3 965x53 40x48 2350x4 292x1 8x18 9x64 [785,238]280x1p61 3093x1 168x3 22x7 42x43 28x4 2350x4 31x10 72x15 9x60 [621,188]6165x1 282x51 168x3 2350x3 290x2 8x12 72x14 [534,228]3068x1p64 965x77 42x28 28x3 2322x1 72x12 [566,228]3068x1p77 22x8 965x64 2350x3 2322x2")]
    [InlineData("plain", 1458, 234231, 29674, 307521, 620245,
        "9b6348bffb1a2c3a0986dc4cf0675d7a344ad1a4a4a76f3e3595ca149f1d7286",
        "[621,228]4341x1p70 22x10 965x77 42x49 28x5 2350x4 299x2 8x10 72x15 9x75 [740,238]6165x1p62 282x69 168x4 965x65 42x45 292x2 31x11 72x14 9x81 5505x1 [790,248]3084x1p68 282x67 3093x1 168x4 20x7 965x53 42x43 28x5 2350x4 292x1 [383,188]6165x1p70 3093x1 168x4 22x9 2350x5 2325x1 72x22 [698,198]953x1p68 279x186 2350x5 290x1 72x20 9x91 [554,258]281x1 282x45 168x3 20x7 965x62 40x35 28x5 2350x4 2322x1 72x19 9x79 [466,228]6165x1p72 3093x2 22x10 965x66 42x36 28x4 2350x4 298x1")]
    [InlineData("trees", 42, 112829, 877988, 642278, 676611,
        "1118149550eaa437cc1bb18bf75cf30f13824d05aafab4cbce1de8f355e5a07e",
        "[867,198]281x1p21 20x7 965x69 290x1 8x17 5508x1 [853,228]284x1 4345x1 20x3 965x89 40x31 292x1 9x71 [827,198]280x1p53 282x65 279x220 3093x1 22x3 965x53 40x48 2350x4 292x1 8x18 9x64 [785,238]280x1p61 3093x1 168x3 22x7 42x43 28x4 2350x4 31x10 72x15 9x60 [621,188]6165x1 282x51 168x3 2350x3 290x2 8x12 72x14 [998,238]284x1p57 22x7 42x40 2350x5 8x13 [517,228]281x1p46 965x82 42x25 28x5 72x19")]
    [InlineData("trees", 1458, 49363, 571054, 796951, 938101,
        "85f2a6316637cf6f48bd831d75ce8d15bf26588d9032c1accc0ca288ee853333",
        "[621,228]4341x1p70 22x10 965x77 42x49 28x5 2350x4 299x2 8x10 72x15 9x75 [740,238]6165x1p62 282x69 168x4 965x65 42x45 292x2 31x11 72x14 9x81 5505x1 [638,188]3068x1 168x3 28x4 2350x5 299x1 72x20 [945,208]946x1p43 279x229 4345x1 20x4 40x37 2350x4 31x16 9x99 [496,238]946x1 965x76 28x5 2350x3 2325x2 9x89 [589,188]3068x1 3093x1 965x90 40x47 28x3 2350x3 2322x1 72x12 9x61 [885,218]285x1 3093x2 168x3 22x6 965x52 28x4 2350x4 298x1")]
    [InlineData("flower", 42, 890763, 969192, 156181, 587529,
        "512724d7fb0f4586f8073280b7b370ef943e6dd9d44f613fca3f28ccac98c737",
        "[867,198]281x1p21 20x7 965x69 290x1 8x17 5508x1 [853,228]284x1 4345x1 20x3 965x89 40x31 292x1 9x71 [827,198]280x1p53 282x65 279x220 3093x1 22x3 965x53 40x48 2350x4 292x1 8x18 9x64 [785,238]280x1p61 3093x1 168x3 22x7 42x43 28x4 2350x4 31x10 72x15 9x60 [621,188]6165x1 282x51 168x3 2350x3 290x2 8x12 72x14 [534,228]3068x1p64 965x77 42x28 28x3 2322x1 72x12 [566,228]3068x1p77 22x8 965x64 2350x3 2322x2")]
    [InlineData("flower", 1458, 234231, 29674, 307521, 620245,
        "b22e9b97ffc7065f9f3275f7977b3dd48f6356295b6d324d021f25f042278def",
        "[621,228]4341x1p70 22x10 965x77 42x49 28x5 2350x4 299x2 8x10 72x15 9x75 [740,238]6165x1p62 282x69 168x4 965x65 42x45 292x2 31x11 72x14 9x81 5505x1 [790,248]3084x1p68 282x67 3093x1 168x4 20x7 965x53 42x43 28x5 2350x4 292x1 [383,188]6165x1p70 3093x1 168x4 22x9 2350x5 2325x1 72x22 [698,198]953x1p68 279x186 2350x5 290x1 72x20 9x91 [554,258]281x1 282x45 168x3 20x7 965x62 40x35 28x5 2350x4 2322x1 72x19 9x79 [466,228]6165x1p72 3093x2 22x10 965x66 42x36 28x4 2350x4 298x1")]
    [InlineData("bare", 42, 679701, 587757, 665260, 938504,
        "11c4906a4f384a55cf40397dd18a9950df8e4a2cb91dd2be9d999e19c849599a",
        "none")]
    [InlineData("bare", 1458, 336170, 807371, 756263, 14134,
        "11c4906a4f384a55cf40397dd18a9950df8e4a2cb91dd2be9d999e19c849599a",
        "none")]
    [InlineData("mixed", 42, 367493, 453789, 763407, 662566,
        "5ae2568f5c60a8bb354b7eb2dc027e6f8a7866165a6f202a26f2580ed45fc527",
        "[867,198]281x1p21 20x7 965x69 290x1 8x17 5508x1 [853,228]284x1 4345x1 20x3 965x89 40x31 292x1 9x71 [827,198]280x1p53 282x65 279x220 3093x1 22x3 965x53 40x48 2350x4 292x1 8x18 9x64 [471,228]946x1p46 965x67 40x30 28x3 299x1 72x12 9x66 [1015,198]6165x1p67 168x3 20x10 40x47 28x3 2350x4 292x2 31x14 72x29 9x77 [444,258]3069x1 42x32 28x4 2350x3 290x1 8x19 9x71 [672,258]6165x1 22x5 40x38 2350x3 292x1 8x11 72x15")]
    [InlineData("mixed", 1458, 265815, 922455, 774132, 150402,
        "9016cbb8a2344e1b5d1f00b55a4ef3612e0b1d5fddbe97302d5f0b4f5bb30c03",
        "[621,228]4341x1p70 22x10 965x77 42x49 28x5 2350x4 299x2 8x10 72x15 9x75 [465,228]3084x1p67 168x4 22x4 28x3 292x1 72x17 9x79 [807,208]280x1p60 965x62 42x40 292x1 9x68 [606,248]3068x1p63 168x3 22x4 965x61 42x34 72x19 [844,258]6165x1p62 279x231 3093x1 4345x1 965x68 40x26 28x3 2325x1 8x14 9x62 [473,248]284x1p53 168x4 42x42 28x3 2350x5 2322x1 9x66 [634,218]3084x1p78 279x300 168x3 20x7 965x98 42x35 2350x5 298x2 72x21 9x63")]
    [InlineData("dense", 42, 783566, 658948, 509169, 652995,
        "fc714979a33c470e1cadea1aebbebf512b703e7dc3f627cd5658d812c493a6aa",
        "[875,148]281x1p36 279x170 42x32 2350x4 31x10 72x15 9x60 [637,148]3069x1p47 3093x1 22x8 72x29 9x50 [756,148]953x1p75 168x5 22x7 965x80 42x30 2350x3 299x1 9x75 [392,148]3068x1p63 279x177 965x100 28x3 2350x3 299x1 8x16 72x27 [609,148]285x1p67 282x60 168x3 40x49 72x27 5504x1 [974,148]3068x1 4345x1 965x64 40x32 2350x5 72x10 [515,148]3084x1p80 20x6 965x56 292x2 31x17")]
    [InlineData("dense", 1458, 708710, 243234, 337850, 672380,
        "06ac4f2164b5b22ea45cf26121bbf52dd041413672b0770f10c0d30dda1fcee5",
        "[868,148]3068x1 282x41 20x10 965x55 2350x3 290x2 [638,148]3084x1 3093x1 20x6 40x29 2350x3 2325x1 72x15 9x80 [725,148]285x1p67 279x153 20x10 42x45 2350x3 292x2 [511,148]285x1p64 4345x1 168x5 22x4 42x28 2322x1 31x12 72x19 [756,148]281x1p45 282x46 168x5 2350x5 8x15 9x51 [979,148]285x1p65 20x4 965x92 2350x3 72x22 9x89 [395,148]285x1p72 282x75 5507x1")]
    [InlineData("solid", 42, 454524, 789939, 999155, 719643,
        "92174981cff8fe84fcd2b1de7b39e1b32156c4aa61dc969e9162500f273323f2",
        "[885,148]946x1p13 4345x1 168x5 22x8 2350x3 290x1 9x67 [914,148]284x1p57 168x4 20x8 2350x5 298x1 [857,148]4341x1p75 282x54 279x277 168x4 28x4 2322x2 8x13 72x20 [539,148]6165x1p75 282x73 3093x1 22x4 965x67 31x18 5501x1 [1014,148]3084x1p72 40x30 28x4 2350x5 8x11 72x15 9x76 [670,148]285x1p66 3093x1 40x33 2350x3 8x11 72x25 9x80 [475,148]946x1p15 279x161 22x5 42x27 2350x3 2325x1 8x19 72x28")]
    [InlineData("solid", 1458, 799537, 975992, 77692, 4780,
        "cdd4fa792e8deaf947308ec588e0a7f6ffadb9151e7f22d70fdc253d33c98aa3",
        "[920,148]3068x1p65 28x5 2350x4 8x15 72x13 9x51 [510,148]281x1 168x3 22x10 40x34 28x3 2350x4 31x12 72x11 9x70 [891,148]284x1 3093x1 168x3 965x84 28x4 2350x3 299x1 31x15 72x27 [1055,148]285x1p62 168x4 42x40 28x4 2350x5 290x1 9x64 [999,148]281x1p82 3093x2 168x5 965x50 40x49 28x4 72x27 9x73 [584,148]3068x1p74 965x87 28x4 8x14 72x18 9x90 [471,148]3068x1 279x212 965x58 42x29 28x5 2322x2")]
    public void Pass_matches_official(
        string fixture, int seed, int d0, int d1, int d2, int d3, string worldHash, string inventory)
    {
        WorldTileStore store = CreateStore(fixture);
        var random = new RandomAdapter(seed);
        var context = new BuriedChestContext1458
        {
            Height = Height,
            WorldSurface = WorldSurface,
            RockLayer = RockLayer,
            LavaLine = Height - 300,
            CopperBar = 20,
            IronBar = 22,
            SilverBar = 21,
            GoldBar = 19,
            TungstenIsSilverTier = false,
            DesertHiveLow = 500,
            DesertHiveHigh = 600,
            HellChestItem = [220, 218, 112, 96, 65, 5011]
        };

        var chests = new BuriedChest1458(store, random, context);
        new SurfaceChestsPass1458(chests, store, random, WorldSurface, WorldSurfaceLow,
            (WorldSurface + RockLayer) / 2.0 + 40.0, BeachDistance,
            TestContext.Current.CancellationToken).Apply();

        string expected = $"{d0}|{d1}|{d2}|{d3}|{worldHash}|{inventory}";
        string actual = $"{random.Next(1000000)}|{random.Next(1000000)}|{random.Next(1000000)}|" +
            $"{random.Next(1000000)}|{Hash(store)}|{Inventory(chests)}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTileStore CreateStore(string fixture)
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
            store.Set(x, y, new WorldTile { FrameX = -1, FrameY = -1 });

        // Floors ten rows apart through the whole surface band, so every admitted sample finds ground within
        // a few rows and the chest actually lands.
        for (int y = 150; y < Height - 6; y += 10)
        for (int x = 0; x < Width; x++)
            store.Set(x, y, new WorldTile
            {
                Flags = WorldTileFlags.Active, Type = (ushort)((x / 200) % 2 == 0 ? 0 : 1),
                FrameX = -1, FrameY = -1
            });

        // The solid fixtures fill the band instead, so every sample lands on an occupied cell and takes the
        // living-tree search rather than the wall test.
        if (fixture is "solid" or "dense")
        {
            for (int y = 150; y < 400; y++)
            for (int x = 0; x < Width; x++)
                store.Set(x, y, new WorldTile
                {
                    Flags = WorldTileFlags.Active, Type = 0, FrameX = -1, FrameY = -1
                });
            for (int y = 156; y < 400; y += 10)
            for (int x = 0; x < Width; x++)
            {
                WorldTile tile = store.Get(x, y);
                tile.Flags &= ~WorldTileFlags.Active;
                store.Set(x, y, in tile);
            }
        }

        ushort wall = fixture switch
        {
            "flower" => 59,
            "trees" or "dense" or "solid" => 244,
            "bare" => 0,
            _ => 2
        };
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            WorldTile tile = store.Get(x, y);
            if (tile.IsActive)
                continue;
            tile.Wall = wall;
            store.Set(x, y, in tile);
        }

        // "trees" and "dense" carry living wood in bands only, so the reservoir sample has both winners and
        // losers inside one square rather than a uniform field.
        if (fixture is "trees" or "dense")
        {
            for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                WorldTile tile = store.Get(x, y);
                if (tile.IsActive || (x / 40) % 3 == 0)
                    continue;
                tile.Wall = 2;
                store.Set(x, y, in tile);
            }
        }

        // "mixed" alternates an admitted wall with one that is refused, so successes and refusals interleave
        // and the retry counter advances between chests instead of only at the start.
        if (fixture == "mixed")
        {
            for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                WorldTile tile = store.Get(x, y);
                if (tile.IsActive || (x / 100) % 2 == 0)
                    continue;
                tile.Wall = 30;
                store.Set(x, y, in tile);
            }
        }

        return store;
    }

    private static string Inventory(BuriedChest1458 chests)
    {
        var sb = new StringBuilder();
        foreach (BuriedChestResult1458 chest in chests.Chests)
        {
            sb.Append('[').Append(chest.Left).Append(',').Append(chest.Top).Append(']');
            foreach (WorldGenerationChestItem item in chest.Items)
            {
                if (item.ItemType.Value == 0 || item.Stack == 0)
                    continue;
                sb.Append(item.ItemType.Value).Append('x').Append(item.Stack);
                if (item.Prefix.Value != 0)
                    sb.Append('p').Append(item.Prefix.Value);
                sb.Append(' ');
            }
        }

        return sb.Length == 0 ? "none" : sb.ToString().TrimEnd();
    }

    private static string Hash(WorldTileStore store)
    {
        var sb = new StringBuilder();
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            WorldTile tile = store.Get(x, y);
            int slope = tile.Shape >= 2 ? tile.Shape - 1 : 0;
            sb.Append(tile.IsActive ? '1' : '0').Append(',')
              .Append(tile.Type).Append(',')
              .Append(tile.Wall).Append(',')
              .Append(tile.FrameX).Append(',')
              .Append(tile.FrameY).Append(',')
              .Append(tile.LiquidAmount).Append(',')
              .Append((int)tile.LiquidKind).Append(',')
              .Append(slope).Append(',')
              .Append(tile.Shape == 1 ? '1' : '0').Append(';');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
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
