using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for TerrariaServer 1.4.5.8 <c>WorldGen.AddBuriedChest</c>.
/// </summary>
/// <remarks>
/// Expectations come from calling the unmodified method inside the pinned dedicated server on the same
/// synthetic ground, and checking four things: whether a chest was taken, where it landed, the next four
/// shared RNG values, a SHA-256 over every field of every cell, and the chest's ordered inventory with stacks
/// and prefixes. The inventory is the decisive one - the tile hash only proves a two-by-two chest of the right
/// style appeared, while the loot is where the other hundred and eighty draws went.
///
/// The ground is a stack of floors twelve rows apart in seven material bands, so the same call lands on dirt,
/// stone, snow, ice, mud, sand or marble depending only on the column, which is what the frozen branch keys
/// on. The fixtures then walk the four depth bands and the identities that override them: a surface chest, an
/// underground one, a cavern one, one just above the underworld and one inside it, an explicit signature item,
/// a frozen chest, a gold one, a Skyware one, and a desert container. Two fixtures refuse - one because a chest
/// already stands beside the site, one because the column has no ground at all - and both refuse before taking
/// a single value, which is what proves the refusals are not paid for.
/// </remarks>
public sealed class BuriedChest1458Tests
{
    private const int Width = 1400;
    private const int Height = 900;
    private const double WorldSurface = 220.0;
    private const double RockLayer = 320.0;

    [Theory]
    // fixture, seed, taken, chestX, chestY, four next draws, worldHash, inventory
    [InlineData("surface", 42, 1, 199, 39, 147761, 906212, 692589, 516413,
        "32adf37bf04fbc4c17967d78e60b8c3872fb18d8005d2ab6b8ad9a2a9034d25e",
        "[199,38]3069x1 282x58 22x4 965x75 28x3 2350x3 290x1 8x17 5508x1")]
    [InlineData("surface", 1458, 1, 199, 39, 378917, 592272, 287113, 471335,
        "32adf37bf04fbc4c17967d78e60b8c3872fb18d8005d2ab6b8ad9a2a9034d25e",
        "[199,38]953x1p78 22x6 40x39 2350x5")]
    [InlineData("under", 42, 1, 199, 255, 539970, 44147, 711154, 147761,
        "f24f041a4f28cc3b3244d6759dcea6bc233a2973708ae4e22214fd0b4f9415c9",
        "[199,254]5011x1 166x12 21x7 42x33 28x3 289x2 2350x3 8x10")]
    [InlineData("under", 1458, 1, 199, 255, 859310, 542152, 548956, 401207,
        "f24f041a4f28cc3b3244d6759dcea6bc233a2973708ae4e22214fd0b4f9415c9",
        "[199,254]53x1p78 52x1 21x9 2350x3")]
    [InlineData("cavern", 42, 1, 199, 399, 147761, 906212, 692589, 516413,
        "07f8ca8f2dfc4f5eee5abb289ac40d5a794a4d29726a3f5527d75ffe69c9a0f1",
        "[199,398]5011x1p38 21x4 279x33 188x3 296x2 2350x2 282x28 5508x1")]
    [InlineData("cavern", 1458, 1, 199, 399, 859310, 542152, 548956, 401207,
        "07f8ca8f2dfc4f5eee5abb289ac40d5a794a4d29726a3f5527d75ffe69c9a0f1",
        "[199,398]53x1p70 167x1 21x6")]
    [InlineData("deep", 42, 1, 199, 663, 577196, 397470, 152193, 90112,
        "e85a515924c7f05e0a7bb2c834da66f2a8e3d5b0c92986fa850151ce01faedef",
        "[199,662]53x1 167x1 265x68 301x1 8x18 5484x1")]
    [InlineData("deep", 1458, 1, 199, 663, 581833, 745894, 954365, 145804,
        "e85a515924c7f05e0a7bb2c834da66f2a8e3d5b0c92986fa850151ce01faedef",
        "[199,662]50x1 227x19 302x2 5506x1")]
    [InlineData("hell", 42, 1, 199, 699, 147761, 906212, 692589, 516413,
        "19f0c61ea08e910793ca293cd9bfd7e54048c02584e2a70ef8ce533d8806d5e6",
        "[199,698]220x1p37 5010x1p65 167x1 265x62 227x17 294x1 300x1 4870x1 5508x1")]
    [InlineData("hell", 1458, 1, 199, 699, 378917, 592272, 287113, 471335,
        "19f0c61ea08e910793ca293cd9bfd7e54048c02584e2a70ef8ce533d8806d5e6",
        "[199,698]220x1 19x21 300x2 282x23")]
    [InlineData("surfacemain", 42, 1, 199, 39, 147761, 906212, 692589, 516413,
        "32adf37bf04fbc4c17967d78e60b8c3872fb18d8005d2ab6b8ad9a2a9034d25e",
        "[199,38]3069x1 282x58 22x4 965x75 28x3 2350x3 290x1 8x17 5508x1")]
    [InlineData("surfacemain", 1458, 1, 199, 39, 378917, 592272, 287113, 471335,
        "32adf37bf04fbc4c17967d78e60b8c3872fb18d8005d2ab6b8ad9a2a9034d25e",
        "[199,38]953x1p78 22x6 40x39 2350x5")]
    [InlineData("primary", 42, 1, 199, 399, 577196, 397470, 152193, 90112,
        "07f8ca8f2dfc4f5eee5abb289ac40d5a794a4d29726a3f5527d75ffe69c9a0f1",
        "[199,398]285x1p64 43x1 51x31 188x5 8x19 5484x1")]
    [InlineData("primary", 1458, 1, 199, 399, 145804, 859310, 542152, 548956,
        "07f8ca8f2dfc4f5eee5abb289ac40d5a794a4d29726a3f5527d75ffe69c9a0f1",
        "[199,398]285x1p73 279x34 303x2")]
    [InlineData("ice", 42, 1, 699, 399, 539970, 44147, 711154, 147761,
        "7a35bb050a5021181f641730031688a922d5dbe468be96a4a4ac8e36d0f80fbe",
        "[699,398]987x1 5120x1 167x1 279x31 188x4 2350x3 282x24 73x1 2198x1")]
    [InlineData("ice", 1458, 1, 699, 399, 548956, 401207, 378917, 592272,
        "7a35bb050a5021181f641730031688a922d5dbe468be96a4a4ac8e36d0f80fbe",
        "[699,398]950x1p76 167x1 21x6")]
    [InlineData("gold", 42, 1, 199, 399, 147761, 906212, 692589, 516413,
        "07f8ca8f2dfc4f5eee5abb289ac40d5a794a4d29726a3f5527d75ffe69c9a0f1",
        "[199,398]5011x1p38 21x4 279x33 188x3 296x2 2350x2 282x28 5508x1")]
    [InlineData("gold", 1458, 1, 199, 399, 859310, 542152, 548956, 401207,
        "07f8ca8f2dfc4f5eee5abb289ac40d5a794a4d29726a3f5527d75ffe69c9a0f1",
        "[199,398]53x1p70 167x1 21x6")]
    [InlineData("sky", 42, 1, 199, 39, 147761, 906212, 692589, 516413,
        "4c734e8ee2b663b277369276b55bb42776b2cc007f3068dfee682c8a8a859d95",
        "[199,38]5011x1 166x12 21x7 42x33 28x3 289x2 2350x3 8x10 5238x1 751x52")]
    [InlineData("sky", 1458, 1, 199, 39, 401207, 378917, 592272, 287113,
        "4c734e8ee2b663b277369276b55bb42776b2cc007f3068dfee682c8a8a859d95",
        "[199,38]53x1p78 52x1 21x9 2350x3 2197x1 5388x1 751x77")]
    [InlineData("water", 42, 1, 899, 399, 147761, 906212, 692589, 516413,
        "f5ccfd7de6fbf17c0f34880f0079efa1d0887a8df39ec05276e7f6276162e663",
        "[899,398]5011x1p38 21x4 279x33 188x3 296x2 2350x2 282x28 5508x1")]
    [InlineData("water", 1458, 1, 899, 399, 859310, 542152, 548956, 401207,
        "f5ccfd7de6fbf17c0f34880f0079efa1d0887a8df39ec05276e7f6276162e663",
        "[899,398]53x1p70 167x1 21x6")]
    [InlineData("near", 42, 0, 0, 0, 668106, 140907, 125518, 522764,
        "8631bfc2a95ef3ae75c62608beca5f337364b5f2650a154de9ff3512619c83cc",
        "none")]
    [InlineData("near", 1458, 0, 0, 0, 422351, 621669, 892512, 757953,
        "8631bfc2a95ef3ae75c62608beca5f337364b5f2650a154de9ff3512619c83cc",
        "none")]
    [InlineData("noground", 42, 0, 0, 0, 668106, 140907, 125518, 522764,
        "3a1553704a5471d07e0cc38321ce8b468e7adc45c1e528ce2ae94aee22547508",
        "none")]
    [InlineData("noground", 1458, 0, 0, 0, 422351, 621669, 892512, 757953,
        "3a1553704a5471d07e0cc38321ce8b468e7adc45c1e528ce2ae94aee22547508",
        "none")]
    public void Chest_matches_official(
        string fixture, int seed, int taken, int chestX, int chestY,
        int d0, int d1, int d2, int d3, string worldHash, string inventory)
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

        var placer = new BuriedChest1458(store, random, context);
        (int x, int y, int primary, bool notNear, int style, ushort tileType) call = Call(fixture);
        bool placed = placer.TryAdd(call.x, call.y, out int actualX, out int actualY,
            call.primary, call.notNear, call.style, trySlope: false, call.tileType);

        string expected = $"{taken}|{chestX}|{chestY}|{d0}|{d1}|{d2}|{d3}|{worldHash}|{inventory}";
        string actual = $"{(placed ? 1 : 0)}|{actualX}|{actualY}|" +
            $"{random.Next(1000000)}|{random.Next(1000000)}|{random.Next(1000000)}|{random.Next(1000000)}|" +
            $"{Hash(store)}|{Inventory(placer)}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");
    }

    private static (int X, int Y, int Primary, bool NotNear, int Style, ushort TileType) Call(string fixture) =>
        fixture switch
        {
            "surface" => (200, 40, 0, false, -1, (ushort)0),
            "under" => (200, 250, 0, false, -1, (ushort)0),
            "cavern" => (200, 400, 0, false, -1, (ushort)0),
            "deep" => (200, 660, 0, false, -1, (ushort)0),
            "hell" => (200, 700, 0, false, -1, (ushort)0),
            "surfacemain" => (200, 40, 0, false, 0, (ushort)0),
            "primary" => (200, 400, 285, false, -1, (ushort)0),
            "ice" => (700, 400, 0, false, 11, (ushort)0),
            "gold" => (200, 400, 0, false, 1, (ushort)0),
            "sky" => (200, 40, 0, false, 13, (ushort)0),
            "water" => (900, 400, 0, false, 4, (ushort)467),
            "near" => (200, 400, 0, true, -1, (ushort)0),
            _ => (1200, 400, 0, false, -1, (ushort)0)
        };

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTileStore CreateStore(string fixture)
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
            store.Set(x, y, new WorldTile { FrameX = -1, FrameY = -1 });

        // Floors twelve rows apart so the descent always finds ground quickly, in seven material bands:
        // dirt, stone, snow, ice, mud, sand, marble. Column 1150 onward is hollow so one fixture falls
        // through the whole world.
        ushort[] floors = [0, 1, 147, 161, 59, 53, 367];
        for (int y = 40; y < Height - 6; y += 12)
        for (int x = 0; x < 1150; x++)
            store.Set(x, y, new WorldTile
            {
                Flags = WorldTileFlags.Active, Type = floors[(x / 170) % floors.Length],
                FrameX = -1, FrameY = -1
            });

        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            WorldTile tile = store.Get(x, y);
            if (tile.IsActive)
                continue;
            tile.Wall = 2;
            store.Set(x, y, in tile);
        }

        if (fixture == "near")
        {
            for (int column = 0; column < 2; column++)
            for (int row = 0; row < 2; row++)
            {
                WorldTile tile = store.Get(210 + column, 398 + row);
                tile.Flags |= WorldTileFlags.Active;
                tile.Type = 21;
                tile.FrameX = (short)(column * 18);
                tile.FrameY = (short)(row * 18);
                store.Set(210 + column, 398 + row, in tile);
            }
        }

        return store;
    }

    private static string Inventory(BuriedChest1458 placer)
    {
        var sb = new StringBuilder();
        foreach (BuriedChestResult1458 chest in placer.Chests)
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
