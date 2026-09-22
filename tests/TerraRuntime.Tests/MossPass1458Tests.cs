using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for the registered TerrariaServer 1.4.5.8 <c>GenPassNameID.MossAndMossCaves</c>
/// pass, which is six stages under one registration.
/// </summary>
/// <remarks>
/// Expectations come from calling the unmodified registered delegate through <c>GenPass.Apply</c> on the same
/// synthetic ground inside the pinned dedicated server, and checking the next four shared RNG values and a
/// SHA-256 over every field of every cell.
///
/// The ground is a real world's shape - 4200 by 1200, so the neon biome count of one per 2100 columns is not
/// zero - with a dirt band over stone and cave pockets cut into the stone in graded sizes. The sizes matter:
/// the flood counter has a floor of ten and a ceiling of 2500, and a fixture whose pockets all sit inside that
/// window cannot tell either bound from the other. Every pocket is enclosed in stone because any wall at all
/// caps the count, which would make an oversized cave and a papered one indistinguishable.
///
/// Rings of ice and mushroom around two pockets reach the counter's material refusals; a band of each foreign
/// material the neon biome's box scan rejects reaches that scan's every arm; and a lava field in the lower
/// band reaches the lava moss census, which needs more than twenty lava cells within twenty-five tiles.
///
/// The fixtures then vary one thing each: no lava anywhere, every pocket papered, and the shimmer anchor moved
/// over the main cave field. That last one is the only way to see the refusal, because it guards three of the
/// six stages and neither of the two on either end of them.
/// </remarks>
public sealed class MossPass1458Tests
{
    private const int Width = 4200;
    private const int Height = 1200;
    private const double WorldSurface = 337.0;
    private const double RockLayer = 457.0;
    private const int WaterLine = 700;
    private const int LavaLine = 800;

    [Theory]
    // fixture, seed, four next draws, worldHash
    // The banded cave field. Pockets under, inside and over the flood counter's window; a narrow marker
    // band per refused material; a cave reaching in from the world edge; shimmer; half-bricked and
    // self-papered stone with a pocket cut into it; and lava in a wide cave, a small one and the deep band.
    [InlineData("caves", 42, 546402, 323061, 400290, 651471, "2ff65037c0ac6a0f4740e1c5db512697417a9e3c49b514be8f66a9d4a2fe6ed5")]
    [InlineData("caves", 1458, 493594, 103007, 280825, 159044, "953a8ed616f8448cca1cd413cff8d9e70ba32bee79e3a775a72f318324008579")]
    // No lava anywhere. The counter's lava rule never fires and stage six spends its whole budget a
    // thousandth at a time without laying a single lava moss.
    [InlineData("dry", 42, 412640, 985381, 859283, 892404, "680dc1c204d9bb4716bd14918d754d81e0e4e73a6d1ab2e95a5b6ca0e792051b")]
    [InlineData("dry", 1458, 5250, 121250, 342263, 999905, "c66de90cab3d17a50fd4de03c2cbb971aaadf68a629a856c146e5538936582d9")]
    // Every pocket papered. The flood counter caps on the first cell of any cave it is offered, so no cave
    // is ever taken, and the moss spread that does run paints only the rim it is started on.
    [InlineData("papered", 42, 680814, 577961, 603253, 916167, "17fc1a71cbd535939ffdd9fc3829bc6f290ba6d7020defb3e6b93349b225b2c9")]
    [InlineData("papered", 1458, 647564, 662445, 525538, 662112, "a2227d4746efd067110c33c1d1d0a3701ef607d1aceeb78084646c2f7be3153e")]
    // The shimmer anchor over the cave field the middle three stages work in.
    [InlineData("shimmer", 42, 950542, 416582, 420924, 70285, "08ba58929753b370b0351b8c1bd8a9be14ce7397bc9296a1ac38ac5b5bcfdb00")]
    [InlineData("shimmer", 1458, 618984, 886565, 831419, 713, "2e5de4359fc390da311511c8088e3c9d9e10f077b45e6d056a028a0f40ee6e96")]
    // The anchor over lava-moss ground instead. The stream is identical to "caves" because the last stage
    // takes no shimmer refusal at all, and only the world differs - which is what proves the asymmetry.
    [InlineData("shimmerdeep", 42, 546402, 323061, 400290, 651471, "86930a113a89ebc1fd74b90de65e1562c807246bd2a52090404e19dc97d1225e")]
    [InlineData("shimmerdeep", 1458, 493594, 103007, 280825, 159044, "7f8e8218967b617248e41118917f1630bedbae2db0d4e165e1199602363fbd8f")]
    // Wide foreign fields with clean windows barely wider than the box scan, so nearly every site is
    // refused and which material refuses it depends on where the sample landed. This is the only fixture in
    // which dropping one arm of the nine-material cascade changes anything.
    [InlineData("bands", 42, 790492, 146874, 417248, 534815, "79a7c6a5bc2487c33a0f2f8ae89a22afca7a6c351b1370d8caf9a79474b22bc2")]
    [InlineData("bands", 1458, 32328, 311029, 207949, 556083, "dca77807389209e2c8525cfd4e88ce3caed54251ac1203b3c6c7836fefffe451")]
    public void Pass_matches_official(
        string fixture, int seed, int d0, int d1, int d2, int d3, string worldHash)
    {
        WorldTileStore store = CreateStore(fixture, out double shimmerX, out double shimmerY);
        var random = new RandomAdapter(seed);
        var grass = new GenerationGrass1458(
            store, random, TestContext.Current.CancellationToken, WorldSurface, (_, _) => { });

        new MossPass1458(
                store, random, WorldSurface, RockLayer, WaterLine, LavaLine, shimmerX, shimmerY,
                TestContext.Current.CancellationToken)
            .Apply(grass);

        string expected = $"{d0}|{d1}|{d2}|{d3}|{worldHash}";
        string actual = $"{random.Next(1000000)}|{random.Next(1000000)}|{random.Next(1000000)}|" +
            $"{random.Next(1000000)}|{Hash(store)}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTileStore CreateStore(string fixture, out double shimmerX, out double shimmerY)
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));
        bool dry = fixture == "dry";
        bool papered = fixture == "papered";

        // The shimmer refusal guards stages three, four and five and neither the neon biomes nor the lava
        // moss. Parking the anchor over the main cave field proves it fires where those stages would
        // otherwise have taken the cell.
        // "shimmer" parks the anchor over the cave field the middle stages work in; "shimmerdeep" parks it
        // over lava-moss ground instead, which is the only way to prove the last stage does NOT take the
        // refusal the middle three do.
        shimmerX = fixture switch { "shimmer" => 700.0, "shimmerdeep" => 920.0, _ => -10000.0 };
        shimmerY = fixture switch { "shimmer" => 560.0, "shimmerdeep" => 850.0, _ => -10000.0 };

        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            var tile = new WorldTile();
            if (y < (int)WorldSurface)
            {
                tile.FrameX = -1;
                tile.FrameY = -1;
            }
            else
            {
                tile.Flags = WorldTileFlags.Active;
                tile.Type = y < (int)RockLayer ? (ushort)0 : (ushort)1;
            }

            store.Set(x, y, tile);
        }

        // Cave pockets of graded size, so the flood counter sees components under its floor of ten, inside
        // its ten-to-2500 window and over its ceiling.
        ushort pocketWall = papered ? (ushort)2 : (ushort)0;
        for (int col = 0; col < 40; col++)
        {
            int left = 120 + col * 100;
            if (left > Width - 200)
                break;
            if (left is > 1550 and < 2650)
                continue;

            int size = 3 + col % 9 * 7;
            Carve(store, left, 500, size, size, pocketWall);
            Carve(store, left + 40, 620, size + 20, 6, pocketWall);
        }

        // One oversized cavern, which the counter must reject for being over its ceiling rather than for any
        // of the material reasons.
        Carve(store, 3300, 520, 120, 40, pocketWall);

        // The materials the neon biome's box scan refuses, inside the rows that scan reaches.
        // One band per refused material, spaced wider than twice the scan's fifty-tile reach so that dropping
        // any single arm of the cascade opens a real hole rather than being shadowed by the neighbouring band.
        // In the ordinary fixtures these are narrow markers spaced wider than twice the scan's fifty-tile
        // reach, so no band is shadowed by its neighbour. In "bands" they are wide fields whose clean windows
        // are barely wider than the scan itself, so nearly every sample is refused and WHICH material refuses
        // it depends on where the sample landed. That is the only arrangement in which dropping a single arm
        // of the cascade is visible: in a mostly clean world the sampler is accepted on its first or second
        // try and never reaches most of the materials.
        ushort[] foreign = [70, 60, 367, 368, 161, 147, 396, 397, 41];
        int bandPitch = fixture == "bands" ? 280 : 220;
        int bandWidth = fixture == "bands" ? 230 : 20;
        for (int band = 0; band < foreign.Length; band++)
        {
            // The sampler re-rolls any column in the middle 24 percent of the map, so a band placed there is
            // never the one that refuses a site. Five bands go left of that gap and four right of it.
            int left = band < 5 ? 180 + band * bandPitch : 2650 + (band - 5) * bandPitch;
            for (int x = left; x < left + bandWidth && x < Width; x++)
            for (int y = 470; y < 780; y++)
            {
                WorldTile tile = store.Get(x, y);
                if (!tile.IsActive)
                    continue;
                tile.Type = foreign[band];
                store.Set(x, y, tile);
            }
        }

        // A single column of a refused material. An off-by-one on the box's hundred-and-one-wide sweep only
        // shows when a foreign cell sits exactly on its boundary ring, which a wide field cannot do.
        for (int y = 470; y < 780; y++)
        {
            WorldTile tile = store.Get(4000, y);
            if (!tile.IsActive)
                continue;
            tile.Type = 70;
            store.Set(4000, y, tile);
        }

        // Ice and mushroom inside cave walls, so the counter's material refusals fire on a component that is
        // otherwise exactly the right size.
        Carve(store, 900, 560, 30, 10, pocketWall);
        Ring(store, 900, 560, 30, 10, 161);
        Carve(store, 1100, 560, 30, 10, pocketWall);
        Ring(store, 1100, 560, 30, 10, 70);

        if (!dry)
        {
            // Lava for the counter's refusal up top, and a broad field down in the stage-six band where a
            // cell needs more than twenty lava cells within twenty-five tiles to take lava moss.
            // Wide enough for the flood stage to actually sample it: the counter's lava rule and stage
            // three's lava term are both invisible if the only wet cave is thirty columns of four thousand
            // two hundred.
            Carve(store, 2900, 560, 400, 12, 0);
            Fill(store, 2900, 566, 400, 5);

            // A pool sized so the census lands between ten and twenty, which is the only place the
            // threshold's exact value and the box's exact bounds can be seen.
            Carve(store, 2400, 760, 10, 6, 0);
            Fill(store, 2400, 763, 5, 3);

            // A SMALL wet cave, inside the flood counter's ten-to-2500 window. The wide one above is
            // rejected for its size long before its lava matters, so only this one can show that the counter
            // caps on lava and that the flood stage refuses a cave for carrying any.
            Carve(store, 2500, 520, 40, 8, 0);
            Fill(store, 2500, 524, 40, 3);

            for (int pool = 0; pool < 16; pool++)
            {
                int left = 200 + pool * 240;
                if (left > Width - 120)
                    break;
                Carve(store, left, 820 + pool % 3 * 40, 40, 14, 0);
                Fill(store, left + 2, 826 + pool % 3 * 40, 36, 7);
            }
        }

        // A corridor reaching in from the world edge. It has to reach past column 250, because the flood stage
        // samples from column 200 inward, and it has to sit in the band the stage RETRIES into rather than
        // the one it first offers, because almost every offer is a retry; and it has to stay UNDER the
        // counter's ceiling in area, and clear of every other feature's rows - its ceiling ran through the
        // mushroom marker band at first, so the counter refused it on material long before the border rule
        // was consulted - because a
        // cave big enough to trip the ceiling is refused for its size whether or not the border rule fires,
        // which shadows that rule entirely.
        Carve(store, 1, 960, 700, 2, 0);

        // Shimmer, which the flood counter caps on. Nothing else in the world shimmers, so without this that
        // rule is simply unreachable.
        Carve(store, 1000, 660, 40, 10, 0);
        for (int x = 1000; x < 1040; x++)
        for (int y = 664; y < 670; y++)
        {
            WorldTile tile = store.Get(x, y);
            if (tile.IsActive)
                continue;
            tile.LiquidAmount = 255;
            tile.LiquidKind = WorldLiquidKind.Shimmer;
            store.Set(x, y, tile);
        }

        // Half-bricked stone inside a pocket's rim: the counter's solidity test reads the shape, and a world
        // of whole blocks cannot tell that apart from one that ignores it.
        for (int x = 3600; x < 3680; x++)
        for (int y = 470; y < 700; y++)
        {
            WorldTile tile = store.Get(x, y);
            if (!tile.IsActive || y % 4 != 0)
                continue;
            tile.Shape = 1;
            store.Set(x, y, tile);
        }

        // Stone that is itself papered. The moss spread guards its edge paint on the edge having no wall, and
        // an ordinary cave only ever papers the open cells, never the blocks that bound them.
        for (int x = 3750; x < 3830; x++)
        for (int y = 470; y < 700; y++)
        {
            WorldTile tile = store.Get(x, y);
            if (!tile.IsActive)
                continue;
            tile.Wall = 2;
            store.Set(x, y, tile);
        }

        // A pocket cut INTO that papered stone, so the spread has somewhere to start that is bounded by
        // blocks which already carry a wall. Without it the guard on the edge's wall is never consulted.
        Carve(store, 3770, 520, 40, 18, 0);

        // Exposed stone in the stage-six band even where there is no lava, so the pass has cells that reach
        // the lava census and fail it - that is the decrement worth a control.
        for (int col = 0; col < 24; col++)
        {
            int left = 150 + col * 170;
            if (left > Width - 120)
                break;
            Carve(store, left, 900 + col % 4 * 30, 24, 8, 0);
        }

        return store;
    }

    private static void Carve(WorldTileStore store, int left, int top, int width, int height, ushort wall)
    {
        for (int x = left; x < left + width && x < Width - 1; x++)
        for (int y = top; y < top + height && y < Height - 1; y++)
        {
            if (x < 1 || y < 1)
                continue;

            WorldTile tile = store.Get(x, y);
            tile.Flags &= ~WorldTileFlags.Active;
            tile.FrameX = -1;
            tile.FrameY = -1;
            tile.Wall = wall;
            store.Set(x, y, tile);
        }
    }

    private static void Ring(WorldTileStore store, int left, int top, int width, int height, ushort type)
    {
        for (int x = left - 1; x <= left + width && x < Width - 1; x++)
        for (int y = top - 1; y <= top + height && y < Height - 1; y++)
        {
            if (x < 1 || y < 1)
                continue;
            if (x > left - 1 && x < left + width && y > top - 1 && y < top + height)
                continue;

            WorldTile tile = store.Get(x, y);
            if (!tile.IsActive)
                continue;

            tile.Type = type;
            store.Set(x, y, tile);
        }
    }

    private static void Fill(WorldTileStore store, int left, int top, int width, int height)
    {
        for (int x = left; x < left + width && x < Width - 1; x++)
        for (int y = top; y < top + height && y < Height - 1; y++)
        {
            if (x < 1 || y < 1)
                continue;

            WorldTile tile = store.Get(x, y);
            if (tile.IsActive)
                continue;

            tile.LiquidAmount = 255;
            tile.LiquidKind = WorldLiquidKind.Lava;
            store.Set(x, y, tile);
        }
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
