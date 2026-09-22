using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for the two chest loops of TerrariaServer 1.4.5.8
/// <c>GenPassNameID.UndergroundHousesAndBuriedChests</c>.
/// </summary>
/// <remarks>
/// <para>
/// The pass is four loops and only two of them are chests; the other two build cave houses, which are a
/// separate subsystem. Expectations therefore come from replaying exactly those two loops against the
/// unmodified official <c>WorldGen.AddBuriedChest</c> inside the pinned dedicated server - and the probe
/// cross-checks that replay against the WHOLE registered delegate on the same world, confirming on all ten
/// fixtures that the chests the two loops produce are an exact prefix of the chests the whole pass produces.
/// The cave houses only append.
/// </para>
/// <para>
/// The four counts are what make the loops fragile. All of them come off the shared stream before any loop
/// runs, including the two the house loops own, so a port that read only its own two would desynchronise on
/// its very first sample. They are also scaled and truncated: thirty-five to forty cave chests and cave houses
/// against the world's area, ten to fifteen underworld chests against its width, and two extra desert houses
/// that scale to none at all on anything smaller than a large world.
/// </para>
/// <para>
/// The fixtures vary what the samples land on. A plain cavern world; the same world behind a dirt wall; the
/// same behind wall 87, which the cavern loop refuses outright; a world emptied above the underworld so the
/// first loop can never place and the second does all the work; and a world with no ground at all, where both
/// loops spend their entire ten-thousand budget and take nothing.
/// </para>
/// </remarks>
public sealed class BuriedChestsPass1458Tests
{
    private const int Width = 1400;
    private const int Height = 900;
    private const double WorldSurfaceLow = 180.0;
    private const double WorldSurface = 260.0;
    private const double WorldSurfaceHigh = 300.0;
    private const double RockLayer = 360.0;
    private const int BeachDistance = 380;

    [Theory]
    // fixture, seed, four next draws, worldHash, inventories
    [InlineData("caverns", 42, 364956, 343876, 218511, 605135,
        "fe9ba49a98fdea29dfcda134dd6c863d31d1ac634ed49f7eb112422587f5f1ae",
        "[248,430]975x1p66 19x7 279x40 188x3 2351x2 2350x4 282x26 [75,442]997x1 19x3 279x41 188x3 295x2 2350x3 73x1 [873,418]49x1 167x1 21x6 188x3 296x1 282x29 73x1 [79,466]49x1p69 279x49 188x5 2350x2 73x1 [823,514]930x1 931x43 41x49 188x3 2350x3 8x30 73x1 [459,586]997x1 3199x1 51x49 279x41 2350x2 282x30 2198x1 [419,526]1319x1 21x6 296x1 2350x4 73x2 [1329,634]50x1 41x38 2329x1 2350x2 5504x1 [1113,838]220x1p61 167x1 278x62 302x2 282x26 [1290,706]218x1 167x1 265x61 2323x1 302x1 [1319,718]112x1p51 167x1 19x15 227x19 2323x1 2348x1 73x4")]
    [InlineData("caverns", 1458, 729133, 157393, 68734, 128464,
        "b801b2b51c2a83d77d66cc36e8280aeca276f0cbc15c0474783836037fff6dec",
        "[661,574]724x1p59 51x39 2329x2 2350x3 73x1 2198x1 [628,514]987x1p80 5120x1 167x1 188x3 303x1 [195,634]50x1 43x1 167x1 51x41 19x10 41x31 303x2 2350x4 8x27 [254,502]53x1p63 21x9 [75,670]49x1 265x65 227x19 293x1 302x2 73x4 [1191,526]930x1 931x33 19x10 41x31 188x5 302x1 8x24 [1187,574]930x1 931x25 167x1 21x4 279x41 296x1 301x1 2350x2 8x30 73x1 5504x1 [722,418]50x1 167x1 41x43 188x5 2350x3 [304,538]930x1 931x45 167x1 51x38 279x33 296x1 8x22 [1181,442]49x1p68 167x1 279x44 302x2 304x1 73x2 [381,778]220x1p60 19x15 278x64 227x19 282x26 73x4 [679,742]218x1 19x29 278x61 296x1 300x1 4870x2 [1316,730]112x1p47 5010x1 2348x1 282x24 [895,754]96x1 5010x1p69 117x23 278x50 227x19 8x28 73x2")]
    [InlineData("walled", 42, 364956, 343876, 218511, 605135,
        "6483315d2e0333e2a33703b70b01f60aa260ffa95c1a1746600623f311943805",
        "[248,430]975x1p66 19x7 279x40 188x3 2351x2 2350x4 282x26 [75,442]997x1 19x3 279x41 188x3 295x2 2350x3 73x1 [873,418]49x1 167x1 21x6 188x3 296x1 282x29 73x1 [79,466]49x1p69 279x49 188x5 2350x2 73x1 [823,514]930x1 931x43 41x49 188x3 2350x3 8x30 73x1 [459,586]997x1 3199x1 51x49 279x41 2350x2 282x30 2198x1 [419,526]1319x1 21x6 296x1 2350x4 73x2 [1329,634]50x1 41x38 2329x1 2350x2 5504x1 [1113,838]220x1p61 167x1 278x62 302x2 282x26 [1290,706]218x1 167x1 265x61 2323x1 302x1 [1319,718]112x1p51 167x1 19x15 227x19 2323x1 2348x1 73x4")]
    [InlineData("walled", 1458, 729133, 157393, 68734, 128464,
        "e2655439d2b3439269ee914f5e3e7dd37224185652de52276db359c000eba4ec",
        "[661,574]724x1p59 51x39 2329x2 2350x3 73x1 2198x1 [628,514]987x1p80 5120x1 167x1 188x3 303x1 [195,634]50x1 43x1 167x1 51x41 19x10 41x31 303x2 2350x4 8x27 [254,502]53x1p63 21x9 [75,670]49x1 265x65 227x19 293x1 302x2 73x4 [1191,526]930x1 931x33 19x10 41x31 188x5 302x1 8x24 [1187,574]930x1 931x25 167x1 21x4 279x41 296x1 301x1 2350x2 8x30 73x1 5504x1 [722,418]50x1 167x1 41x43 188x5 2350x3 [304,538]930x1 931x45 167x1 51x38 279x33 296x1 8x22 [1181,442]49x1p68 167x1 279x44 302x2 304x1 73x2 [381,778]220x1p60 19x15 278x64 227x19 282x26 73x4 [679,742]218x1 19x29 278x61 296x1 300x1 4870x2 [1316,730]112x1p47 5010x1 2348x1 282x24 [895,754]96x1 5010x1p69 117x23 278x50 227x19 8x28 73x2")]
    [InlineData("hell", 42, 605135, 643675, 605499, 321910,
        "ab22fa211f9b83c0182b35a314d67aa8e4cf97fa977f6b9f054ccd50721883d5",
        "[248,706]220x1p40 167x1 117x22 278x64 227x15 2348x2 2350x2 [272,706]218x1 5010x1p77 167x1 19x24 265x69 227x18 73x3 [873,706]112x1 4737x1 167x1 117x26 278x62 227x19 2350x2 [529,706]96x1 117x28 2345x1 8x21 73x4 [202,706]65x1p55 167x1 2345x1 2350x1 282x21 73x4 2197x1 5258x1 751x66 [1040,706]5011x1 5010x1p79 167x1 2350x1 [780,706]220x1p37 278x73 4870x1 73x3 [188,706]218x1p59 5010x1p72 117x22 227x20 295x1 73x3 [1215,814]112x1p60 19x22 227x19 294x2 [1290,706]96x1 167x1 265x61 2323x1 302x1 [1319,718]65x1p51 167x1 19x15 227x19 2323x1 2348x1 73x4 5254x1 751x67")]
    [InlineData("hell", 1458, 175645, 798011, 525521, 937098,
        "ddeb3987134823286d192540ee45ae058d5daab85df9be9f106a9498f7422c29",
        "[661,706]220x1 278x64 295x2 300x1 73x3 5502x1 [420,706]218x1p57 167x1 19x27 227x16 304x1 [334,706]112x1 167x1 117x24 265x73 227x17 294x2 2345x1 2350x2 282x27 [687,706]96x1p57 4443x1 19x27 [75,706]65x1 5010x1p79 167x1 265x51 294x2 305x2 5258x1 751x88 [846,706]5011x1p37 167x1 19x20 227x16 288x1 300x1 [739,706]220x1p37 167x1 295x1 305x1 2350x1 282x17 73x2 [722,706]218x1 4551x1 265x67 227x20 302x2 73x2 [1376,706]112x1 5010x1 19x28 265x59 288x1 302x2 4870x1 [437,706]96x1 227x16 294x1 302x1 282x26 73x2 [968,778]65x1p3 117x24 278x69 227x19 288x1 5254x1 751x64 [1336,718]5011x1 4443x1 117x28 2323x2 73x2 [922,850]220x1p55 4551x1 278x61 294x2 302x1 2350x1 282x18 [284,790]218x1 4443x1 167x1 19x20 278x61 227x15 295x1 2350x2 282x28 73x2")]
    [InlineData("mixed", 42, 364956, 343876, 218511, 605135,
        "b8be81ac20a345a8a186914126696df3fe15ed038ce5fa224098525b63d06880",
        "[537,430]997x1 51x27 301x2 73x1 [24,514]975x1 43x1 167x1 41x45 188x4 73x1 [873,418]49x1 167x1 21x6 188x3 296x1 282x29 73x1 [79,466]49x1p69 279x49 188x5 2350x2 73x1 [823,514]930x1 931x43 41x49 188x3 2350x3 8x30 73x1 [459,586]997x1 3199x1 51x49 279x41 2350x2 282x30 2198x1 [419,526]1319x1 21x6 296x1 2350x4 73x2 [1329,634]50x1 41x38 2329x1 2350x2 5504x1 [1113,838]220x1p61 167x1 278x62 302x2 282x26 [1290,706]218x1 167x1 265x61 2323x1 302x1 [1319,718]112x1p51 167x1 19x15 227x19 2323x1 2348x1 73x4")]
    [InlineData("mixed", 1458, 175645, 798011, 525521, 937098,
        "7d636c8892e78647017997bc5536ccf7a219331ce2b45c937b10c216087c4ce9",
        "[253,634]53x1p79 43x1 188x5 302x1 8x16 73x1 [1310,598]49x1p76 167x1 41x40 305x1 2350x4 282x17 73x1 [879,358]54x1p66 41x44 188x3 303x2 2350x3 282x19 73x1 [812,574]50x1 21x3 279x48 188x4 296x2 2350x2 73x2 [938,370]930x1 931x39 167x1 21x10 41x30 2350x3 73x2 5502x1 [894,502]54x1 188x4 2350x4 8x27 [920,382]53x1 43x1 167x1 21x4 41x35 8x16 [966,430]930x1 931x32 19x7 303x2 2350x3 73x2 [1286,634]49x1p70 21x5 279x42 2350x2 73x2 [839,478]5011x1p53 41x38 188x5 296x1 282x15 73x2 [337,814]220x1 4737x1 19x23 265x53 227x19 288x1 73x3 [1354,778]218x1 5010x1p73 4737x1 295x2 305x1 4870x2 8x26 [19,802]112x1p35 227x16 288x1 282x21 73x3 5485x1 [20,754]96x1p16 5010x1p69 4443x1 167x1 19x17 265x57 73x2")]
    [InlineData("bare", 42, 739484, 88914, 665221, 183473,
        "deac91d38b9115d763fb28d51ba8af69d79ec198f2bb95e697bd99f8703d4fc6",
        "none")]
    [InlineData("bare", 1458, 608667, 43247, 443183, 588205,
        "deac91d38b9115d763fb28d51ba8af69d79ec198f2bb95e697bd99f8703d4fc6",
        "none")]
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
        var pass = new BuriedChestsPass1458(chests, store, random, WorldSurfaceHigh, RockLayer,
            (WorldSurface + RockLayer) / 2.0 + 40.0, BeachDistance, TestContext.Current.CancellationToken);
        pass.Apply();

        string expected = $"{d0}|{d1}|{d2}|{d3}|{worldHash}|{inventory}";
        string actual = $"{random.Next(1000000)}|{random.Next(1000000)}|{random.Next(1000000)}|" +
            $"{random.Next(1000000)}|{Hash(store)}|{Inventory(chests)}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");

        // The two house counts belong to the cave-house row, but they are drawn here, so they are checked
        // here: eight to ten houses and no extra desert houses at this world size.
        Assert.InRange(pass.CaveHouseCount, 8, 10);
        Assert.Equal(0, pass.AdditionalDesertHouseCount);
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTileStore CreateStore(string fixture)
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
            store.Set(x, y, new WorldTile { FrameX = -1, FrameY = -1 });

        if (fixture == "bare")
            return store;

        ushort[] floors = [0, 1, 147, 161, 59, 53, 367];
        for (int y = 60; y < Height - 6; y += 12)
        for (int x = 0; x < Width; x++)
            store.Set(x, y, new WorldTile
            {
                Flags = WorldTileFlags.Active, Type = floors[(x / 170) % floors.Length],
                FrameX = -1, FrameY = -1
            });

        ushort wall = fixture switch { "walled" => 2, "mixed" => 87, _ => (ushort)0 };
        if (wall != 0)
        {
            for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                WorldTile tile = store.Get(x, y);
                if (tile.IsActive)
                    continue;
                tile.Wall = wall;
                store.Set(x, y, in tile);
            }
        }

        if (fixture == "mixed")
        {
            for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                WorldTile tile = store.Get(x, y);
                if (tile.IsActive || (x / 200) % 2 != 0)
                    continue;
                tile.Wall = 2;
                store.Set(x, y, in tile);
            }
        }

        // "hell" empties everything above the underworld, so the first loop can never place and the second
        // one does all the work.
        if (fixture == "hell")
        {
            for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height - 200; y++)
            {
                WorldTile tile = store.Get(x, y);
                tile.Flags &= ~WorldTileFlags.Active;
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
