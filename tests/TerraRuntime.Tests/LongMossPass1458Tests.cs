using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for the registered TerrariaServer 1.4.5.8 <c>GenPassNameID.LongMoss</c> pass, whose
/// display name is "Moss Grass".
/// </summary>
/// <remarks>
/// Expectations come from calling the unmodified registered delegate through <c>GenPass.Apply</c> on the same
/// synthetic ground inside the pinned dedicated server, and checking the next four shared RNG values and a
/// SHA-256 over every field of every cell.
///
/// The ground is isolated single blocks on a six-cell pitch, so every block's four neighbours are open and no
/// offer depends on another's outcome. The fixtures then vary one thing each: the blocks are moss, or moss
/// BRICK, or a moss block with a brick two cells away so the cell between them runs both arms of the placement
/// cascade; or every block is a half brick, which the solidity test refuses; or the whole world is flooded; or
/// a fallen log stands in one of the cells a strand would go.
///
/// Two of those are the interesting ones. A world of nothing but moss bricks grows NO long moss, because only
/// a tile in <c>Main.tileMoss</c> offers and the bricks are not in it - they can only decide which way a
/// strand that some moss block offered ends up hanging. And the flooded world produces exactly the same draws
/// as the dry one, because long moss is not in the family a liquid refuses.
/// </remarks>
public sealed class LongMossPass1458Tests
{
    private const int Width = 1000;
    private const int Height = 600;

    [Theory]
    // fixture, seed, four next draws, worldHash
    [InlineData("one", 42, 397470, 152193, 90112, 705152, "e96e225d3df5fc759058416cd7aa4597d0e18674374269a3aee1591e86c8ffdb")]
    [InlineData("one", 1458, 542152, 548956, 401207, 378917, "a0d9fd96fc8c1413161504e44e0edd7c1f78e3cec1dbd122986570eb10ed19f7")]
    [InlineData("dense", 42, 810336, 128322, 107333, 2843, "512ba1aeee01585e03a2e1d3659fa5afadaaf8096c5ab0ebc46440065ed8d969")]
    [InlineData("dense", 1458, 129475, 162015, 309054, 479155, "fc7382697803eaf0295f5acca222f442f6137fadedb78481ae0d94438ef640dc")]
    // Moss blocks: every one of them grows strands on all four sides.
    [InlineData("moss", 42, 540628, 997269, 144177, 392532, "811379bacea3ffaea090c6321bcfbcde3de8f7547a9699625a49f0d431ab3134")]
    [InlineData("moss", 1458, 737689, 706813, 765536, 506744, "16cb57b909a5b390b53170bc6d592eb0716db8b5d1abd69a6d4cce6b19d55f76")]
    // Moss BRICKS only: not one strand, and not one draw spent either - the pass never looks at them.
    [InlineData("brick", 42, 668106, 140907, 125518, 522764, "f4b494916190f55c9853d82e25c92a6c0d97a524e762841961ff10a2e4383bbd")]
    [InlineData("brick", 1458, 422351, 621669, 892512, 757953, "f4b494916190f55c9853d82e25c92a6c0d97a524e762841961ff10a2e4383bbd")]
    // A brick two cells from each moss block, so the cell between them matches both arms of the cascade and
    // spends two rolls on its row, keeping the second.
    [InlineData("both", 42, 98153, 236099, 555997, 573860, "381f9e600ffe6db7a959fb67634a7f8beae922f5631a8959af982fdc72b0ba67")]
    [InlineData("both", 1458, 746690, 622482, 757423, 260438, "91baf970d220551e307e526448a882873743b75c56640357bb31bfacec2284f2")]
    // Half bricks hold nothing: SolidTile refuses them, so the world is untouched and the stream never moves.
    [InlineData("unstable", 42, 668106, 140907, 125518, 522764, "45240cea20522c635c5dda884f13b1c85d294652f67939e5f7bf16dcf24bb5e4")]
    [InlineData("unstable", 1458, 422351, 621669, 892512, 757953, "45240cea20522c635c5dda884f13b1c85d294652f67939e5f7bf16dcf24bb5e4")]
    // Flooded: identical draws to the dry world, because long moss is not liquid-refused.
    [InlineData("wet", 42, 540628, 997269, 144177, 392532, "cf92cfb34148242efc4bdcd622b3fafc0cc3f0942fd1cfe9cdde2594f93e4ae1")]
    [InlineData("wet", 1458, 737689, 706813, 765536, 506744, "a79791dc2735f6468dca19e9b7b159f51b8b8d8a182c0f4c9fc8c0627fd96384")]
    // A fallen log standing where a strand would go, which PlaceTile refuses on its very first line.
    [InlineData("log", 42, 708213, 172792, 533010, 485818, "781aef44de87e59db28a50d6f45fc122fa8bd06e059686562d4258112b6125b4")]
    [InlineData("log", 1458, 184133, 539973, 336839, 538977, "31ea552d07a5149ddf0971a99f9471341179e934638cfe0c39ba41983b0822b5")]
    public void Pass_matches_official(
        string fixture, int seed, int d0, int d1, int d2, int d3, string worldHash)
    {
        WorldTileStore store = CreateStore(fixture);
        var random = new RandomAdapter(seed);
        var framing = new GenerationTileFraming1458(store, random);

        new LongMossPass1458(store, random, framing, TestContext.Current.CancellationToken).Apply();

        string expected = $"{d0}|{d1}|{d2}|{d3}|{worldHash}";
        string actual = $"{random.Next(1000000)}|{random.Next(1000000)}|{random.Next(1000000)}|" +
            $"{random.Next(1000000)}|{Hash(store)}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTileStore CreateStore(string fixture)
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
            store.Set(x, y, new WorldTile { FrameX = -1, FrameY = -1 });

        ushort[] moss = [179, 180, 181, 182, 183, 381, 534, 536, 539, 625, 627];
        ushort[] bricks = [512, 513, 514, 515, 516, 517, 535, 537, 540, 626, 628];
        ushort[] use = fixture == "brick" ? bricks : moss;

        // "one" is a single isolated block: few enough draws that the whole sequence can be reasoned about by
        // hand when the stream diverges.
        int step = fixture is "one" or "dense" ? 100000 : 6;
        int slot = 0;
        for (int gx = 20; gx < Width - 20; gx += step)
        for (int gy = 20; gy < Height - 20; gy += step)
        {
            var block = new WorldTile
            {
                Flags = WorldTileFlags.Active, Type = use[slot % use.Length], FrameX = -1, FrameY = -1
            };
            slot++;
            if (fixture == "unstable")
                block.Shape = 1;
            store.Set(gx, gy, in block);

            if (fixture == "both")
            {
                var brick = new WorldTile
                {
                    Flags = WorldTileFlags.Active, Type = bricks[slot % bricks.Length],
                    FrameX = -1, FrameY = -1
                };
                store.Set(gx + 2, gy, in brick);
            }
        }

        if (fixture == "wet")
        {
            for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                WorldTile tile = store.Get(x, y);
                if (tile.IsActive)
                    continue;
                tile.LiquidAmount = 255;
                tile.LiquidKind = WorldLiquidKind.Water;
                store.Set(x, y, in tile);
            }
        }

        if (fixture == "dense")
        {
            // Two blocks with one cell between them: the strand there has a moss neighbour above AND below,
            // which is the only way the order of the framing's four-way chain shows.
            Moss(store, 110, 100);
            Moss(store, 110, 102);

            // A block to the strand's left and a BRICK directly below it. The chain tries below first, so the
            // brick is what decides the colour - the only arrangement where the brick half of the colour map
            // is consulted.
            Moss(store, 130, 100);
            Brick(store, 131, 101);

            // A block to the left again, and below the strand a moss block that is TOP-SLOPED. The framing's
            // below guard refuses that one, so the strand must fall through to the left.
            Moss(store, 139, 101);
            // WorldTile.Shape offsets the vanilla slope by one: the probe sets slope(2), which is Shape 3.
            Moss(store, 140, 102, shape: 3);

            // A block to the right of a strand, and above it a moss block that is BOTTOM-SLOPED. The
            // framing's above guard refuses that one, so the strand falls through to the right.
            Moss(store, 146, 101);
            Moss(store, 145, 100, shape: 5);

            // Two blocks side by side, so each offers into the other's occupied cell.
            Moss(store, 150, 100);
            Moss(store, 151, 100);

            // Close to the scan's border on both sides of it.
            Moss(store, 7, 100);
            Moss(store, 3, 100);
        }

        if (fixture == "log")
        {
            for (int gx = 20; gx < Width - 20; gx += 6)
            for (int gy = 20; gy < Height - 20; gy += 6)
                store.Set(gx + 1, gy,
                    new WorldTile { Flags = WorldTileFlags.Active, Type = 488, FrameX = -1, FrameY = -1 });
        }

        return store;
    }

    private static void Moss(WorldTileStore store, int x, int y, byte shape = 0) =>
        store.Set(x, y, new WorldTile
        {
            Flags = WorldTileFlags.Active, Type = 179, FrameX = -1, FrameY = -1, Shape = shape
        });

    private static void Brick(WorldTileStore store, int x, int y) =>
        store.Set(x, y, new WorldTile
        {
            Flags = WorldTileFlags.Active, Type = 513, FrameX = -1, FrameY = -1
        });

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
