using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for the registered TerrariaServer 1.4.5.8 <c>GenPassNameID.UnderwaterChests</c> pass.
/// </summary>
/// <remarks>
/// <para>
/// Expectations come from calling the unmodified registered delegate through <c>GenPass.Apply</c> on the same
/// synthetic world inside the pinned dedicated server, and checking the next four shared RNG values, a SHA-256
/// over every field of every cell, and every chest's ordered inventory with stacks and prefixes.
/// </para>
/// <para>
/// The fixtures separate the two halves and the two scatter bands. A full sea and a sea broken into pools
/// differ only in how often a sample has to be re-rolled, which is the cheapest way to see the re-roll loop.
/// Lava through the middle of the map is not water, so it is skipped by the re-roll and refuses a footprint
/// outright. A water body only above the surface and one only below it disagree about where the pair's first
/// and second chests can land, which is what pins the asymmetric bounds - the first opens at row one and
/// re-rolls from fifty, the second uses the surface for both. The cave fixture adds the two ocean-cave
/// treasure points, whose spiral search is the other half of the pass entirely.
/// </para>
/// </remarks>
public sealed class UnderwaterChestsPass1458Tests
{
    private const int Width = 1400;
    private const int Height = 900;
    private const double WorldSurface = 260.0;
    private const double RockLayer = 360.0;
    private const int BeachDistance = 380;

    [Theory]
    // fixture, seed, four next draws, worldHash, inventories
    [InlineData("sea", 42, 192308, 297264, 183045, 29583,
        "394b19a9b0f203f2165ba70ce3baa4c9d53fe8bae75a4085efb08df440c6cd76",
        "[232,88]186x1p7 166x17 965x75 22x7 28x5 290x1 [106,578]186x1 167x1 51x33 188x4 303x1 2350x2 [1053,208]4404x1p64 4425x1 52x1 28x3 299x1 2350x2 8x10 [716,478]4404x1 4460x1 43x1 167x1 21x10 41x41 188x4 73x1 [1195,678]277x1p41 167x1 117x26 265x55 2323x2 2351x1 5503x1 [1256,278]277x1 4425x1 4460x1 166x15 965x100 22x8 42x45 28x3 2329x1 2350x4 8x15")]
    [InlineData("sea", 1458, 529780, 2036, 656691, 130903,
        "992bb4b04bc5bcddbbba2932ceb883e039a739fe1cd39a8b5fcd673f9355b07c",
        "[857,628]186x1p53 4460x1 21x10 279x40 296x2 8x24 73x1 5502x1 [433,458]186x1 167x1 51x46 188x3 303x1 [727,98]4404x1p66 4460x1 166x12 52x1 22x14 40x31 304x2 8x18 72x55 [1120,658]4404x1 117x24 265x60 293x2 300x2 4870x1 [126,648]863x1p70 278x58 227x15 293x2 2351x1 282x23 5509x1 [496,528]863x1p68 4425x1 4460x1 167x1 19x8 279x26 188x4 299x2 2350x4")]
    [InlineData("pools", 42, 192308, 297264, 183045, 29583,
        "b6a6aae4a9a33df747e103b790f3ca6a5441cbabe8bcf0233bcb0c60135a8606",
        "[728,158]186x1p44 4460x1 965x75 22x7 28x5 290x1 [241,658]186x1 167x1 19x27 278x70 295x1 73x2 [891,108]4404x1 166x14 22x8 40x42 28x4 2350x3 72x50 [131,548]4404x1 19x4 279x38 188x3 302x2 73x2 [636,238]277x1p38 4425x1 4460x1 2350x4 72x63 [394,278]277x1 4460x1 166x19 52x1 965x66 21x13 40x46 2350x4 8x15")]
    [InlineData("pools", 1458, 123972, 762166, 640908, 753208,
        "7ae8e0464672e94df5b4a8bd4e2cf1168739421006631eee65af2192ed6f9b9e",
        "[857,628]186x1p53 4460x1 21x10 279x40 296x2 8x24 73x1 5502x1 [752,588]186x1p46 4425x1 4460x1 19x7 188x3 305x2 297x2 2350x4 282x17 73x1 [128,328]4404x1p64 4425x1 4460x1 40x43 28x3 2322x2 8x13 72x74 [142,448]4404x1p75 4425x1 19x9 296x2 2350x4 73x1 [410,88]863x1p67 52x1 21x10 28x5 291x2 8x12 [615,568]863x1p73 4425x1 4460x1 188x5 305x2 2350x3 282x17")]
    [InlineData("lava", 42, 135219, 883395, 186707, 294768,
        "0aadd9ebbc85d63a28696a1977666d0b48eab5e7fb6e5df8edbaa1d0eb953317",
        "[232,88]186x1p7 166x17 965x75 22x7 28x5 290x1 [106,578]186x1 167x1 51x33 188x4 303x1 2350x2 [1053,208]4404x1p64 4425x1 52x1 28x3 299x1 2350x2 8x10 [1256,468]4404x1p73 4425x1 4460x1 43x1 41x48 296x2 297x2 2350x2 8x27 73x1 [1045,488]277x1p54 167x1 41x31 188x3 299x1 301x1 2350x2 5499x1 [1141,658]277x1 278x65 2350x1")]
    [InlineData("lava", 1458, 2036, 656691, 130903, 538042,
        "d4a4c51196c7b3a8a0afe14c375adcfb1baec8444653a6e92cabc5e09ba634b1",
        "[857,628]186x1p53 4460x1 21x10 279x40 296x2 8x24 73x1 5502x1 [1283,598]186x1 4425x1 21x9 41x40 305x1 2350x4 282x17 73x1 [128,328]4404x1p64 4425x1 4460x1 40x43 28x3 2322x2 8x13 72x74 [142,448]4404x1p75 4425x1 19x9 296x2 2350x4 73x1 [1026,238]863x1p63 4460x1 21x8 289x2 2350x3 72x53 [1247,318]863x1p66 4425x1 965x62 40x39 2350x4")]
    [InlineData("caves", 42, 747315, 280508, 339684, 525594,
        "48b5b3c8e808ad3511819c60c50c099b7e00af8e0be2dcb87e15cf12019282ce",
        "[295,198]187x1p65 4425x1 52x1 22x10 40x31 2350x3 8x11 72x78 [695,328]277x1 52x1 965x65 28x4 304x1 8x10 [425,478]186x1 4425x1 167x1 188x3 295x1 301x1 2350x4 282x22 73x2 5499x1 [131,548]186x1 19x4 279x38 188x3 302x2 73x2 [636,238]4404x1p64 4425x1 4460x1 2350x4 72x63 [394,278]4404x1 4460x1 166x19 52x1 965x66 21x13 40x46 2350x4 8x15 [435,128]277x1 4425x1 965x95 22x10 2350x3 72x63 [651,498]277x1p59 4425x1 4460x1 43x1 167x1 279x35 188x5 305x1")]
    [InlineData("caves", 1458, 475040, 367955, 696956, 274864,
        "84cce1328f77fc06a4cf80684198c025063e9d0fbf8000e31ec6d90a4358d293",
        "[298,208]277x1 4425x1 28x4 304x2 [695,318]277x1 22x13 [808,648]186x1p49 19x18 227x16 8x29 73x3 [817,618]186x1p60 4425x1 4460x1 167x1 51x44 188x4 299x2 2350x3 [1145,408]4404x1p64 4425x1 21x3 279x48 188x4 296x2 2350x2 73x2 [928,298]4404x1p69 52x1 21x14 40x30 2350x2 8x17 72x74 5502x1 [784,398]277x1 21x4 188x5 [217,418]277x1p37 4425x1 4460x1 43x1 167x1 41x26 188x4 8x16")]
    [InlineData("shallow", 42, 580242, 18620, 890795, 410925,
        "27c7962604c17928fc6a749a694c0ee0a8d3839200a046d64720cac6c3bf816e",
        "[232,88]186x1p7 166x17 965x75 22x7 28x5 290x1 [619,488]186x1p1 4460x1 43x1 167x1 21x10 41x41 188x4 73x1 [260,118]4404x1p68 4460x1 166x12 299x2 2350x3 8x13 5502x1 [651,498]4404x1p77 4425x1 4460x1 43x1 167x1 279x35 188x5 305x1 [413,238]277x1p59 4425x1 52x1 965x61 28x4 2322x2 2350x4 8x19 [660,428]277x1p57 21x7 188x5 282x29 5503x1")]
    [InlineData("shallow", 1458, 970379, 953910, 201248, 692582,
        "39f007c71e1a186e46c36ef2027afe0046d553d92d5ef133c46258c226e07dc0",
        "[961,168]186x1p15 4460x1 965x77 2350x3 72x65 [697,368]186x1p43 4425x1 43x1 167x1 19x7 188x5 305x2 2350x3 282x17 [1252,118]4404x1 4425x1 4460x1 166x14 965x61 22x10 40x43 28x4 2350x4 8x14 [672,388]4404x1p78 4460x1 296x1 282x20 [479,258]863x1p70 4425x1 4460x1 22x14 8x13 [680,388]863x1 4425x1 21x10 279x37 296x1 2329x1 2350x4")]
    [InlineData("deepsea", 42, 404004, 590794, 400029, 124949,
        "4a2727dc22ba4840ec7e3bff667cb20bf774562cf02964e70d29b59b5a857b1a",
        "[383,378]186x1p36 4425x1 43x1 19x3 296x2 [102,398]186x1p48 51x25 21x8 41x45 188x4 73x1 [138,518]4404x1 4425x1 4460x1 19x9 41x26 2326x1 2350x3 8x17 73x2 [1228,518]4404x1p62 51x27 19x9 41x30 305x2 2351x1 5503x1 [329,438]277x1p37 4460x1 43x1 167x1 188x3 305x1 2326x1 [1065,318]277x1p38 4425x1 965x80 21x9 42x36 2322x1 8x12")]
    [InlineData("deepsea", 1458, 529780, 2036, 656691, 130903,
        "67a57c0d604d2e58fe71e929dc5287b0e70a0906fb8c798d0824a6e5b24c12a7",
        "[857,628]186x1p53 4460x1 21x10 279x40 296x2 8x24 73x1 5502x1 [433,458]186x1 167x1 51x46 188x3 303x1 [174,468]4404x1 4425x1 43x1 295x1 297x1 [693,528]4404x1p63 4425x1 167x1 [126,648]863x1p70 278x58 227x15 293x2 2351x1 282x23 5509x1 [496,528]863x1p68 4425x1 4460x1 167x1 19x8 279x26 188x4 299x2 2350x4")]
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

        (int X, int Y)[] treasure = fixture == "caves" ? [(300, 200), (700, 320)] : [];
        var chests = new BuriedChest1458(store, random, context);
        new UnderwaterChestsPass1458(chests, store, random, WorldSurface, BeachDistance, treasure,
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

        // Floors ten rows apart from row sixty down to the underworld, so every water cell has ground within
        // a few rows below it.
        for (int y = 60; y < Height - 6; y += 10)
        for (int x = 0; x < Width; x++)
            store.Set(x, y, new WorldTile
            {
                Flags = WorldTileFlags.Active, Type = (ushort)((x / 200) % 2 == 0 ? 0 : 1),
                FrameX = -1, FrameY = -1
            });

        for (int x = 100; x < Width - 100; x++)
        for (int y = 20; y < Height - 210; y++)
        {
            WorldTile tile = store.Get(x, y);
            if (tile.IsActive)
                continue;
            bool wet = fixture switch
            {
                "pools" => (x / 60) % 2 == 0,
                "shallow" => y < WorldSurface,
                "deepsea" => y > WorldSurface,
                _ => true
            };
            if (!wet)
                continue;
            tile.LiquidAmount = 255;
            store.Set(x, y, in tile);
        }

        // "shallow" still needs water below the surface somewhere or the second scatter loop never
        // terminates, so it gets one deep pool.
        if (fixture == "shallow")
        {
            for (int x = 600; x < 700; x++)
            for (int y = 300; y < 600; y++)
            {
                WorldTile tile = store.Get(x, y);
                if (tile.IsActive)
                    continue;
                tile.LiquidAmount = 255;
                store.Set(x, y, in tile);
            }
        }

        // Lava through the middle of the map: a lava cell is not water, so the re-roll skips it, and lava in
        // a chest's footprint refuses the placement outright.
        if (fixture == "lava")
        {
            for (int x = 400; x < 1000; x++)
            for (int y = 300; y < 600; y++)
            {
                WorldTile tile = store.Get(x, y);
                if (tile.IsActive)
                    continue;
                tile.LiquidAmount = 255;
                tile.LiquidKind = WorldLiquidKind.Lava;
                store.Set(x, y, in tile);
            }
        }

        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            WorldTile tile = store.Get(x, y);
            if (tile.IsActive)
                continue;
            tile.Wall = 2;
            store.Set(x, y, in tile);
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
