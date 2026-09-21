using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for the registered TerrariaServer 1.4.5.8
/// <c>GenPassNameID.SpreadingGrassOnSurfaceSunflowersEvilsOnSurfaceAndLavaCleanup</c> pass.
/// </summary>
/// <remarks>
/// Expectations come from calling the unmodified registered delegate through <c>GenPass.Apply</c> on the same
/// synthetic ground inside the pinned dedicated server, and checking the next shared RNG and a SHA-256 over
/// every field of every cell.
///
/// Every fixture is run under two seeds and every pair must agree, because for an ordinary world this pass
/// spends no shared RNG at all - everything the registration's name mentions after the grass is inside its
/// remix branch. The next-draw column is therefore the same value for a given seed in all eleven fixtures, and
/// a port that drew anything would break that first.
///
/// The ground is one bare surface row over dirt over stone, and each fixture changes what that row is made of
/// and what lies just under or just over it: already grassed, plain dirt, exposed stone, and stone with sand,
/// snow, an evil grass or jungle material within reach of it. Two of them are about refusals rather than
/// conversions - stone under papered air or under cover - and the last is about the column walk alone.
/// </remarks>
public sealed class SurfaceGrassSpreadPass1458Tests
{
    private const int Width = 300;
    private const int Height = 200;
    private const int Surface = 50;
    private const double WorldSurface = 80.0;
    private const double WorldSurfaceHigh = 60.0;
    private const int JungleMinX = 200;
    private const int JungleMaxX = 260;

    [Theory]
    // fixture, seed, nextDraw, worldHash
    // An already grassed surface is left exactly as it was found.
    [InlineData("grassed", 42, 668106, "8ae7e7e91dc02c9a9543cf10ad719fe5fa61aabcacd553938a5b99be2a4d7217")]
    [InlineData("grassed", 1458, 422351, "8ae7e7e91dc02c9a9543cf10ad719fe5fa61aabcacd553938a5b99be2a4d7217")]
    // A bare dirt surface: the column walk starts a spread in every column it may reach, which is every
    // column but the ten at each edge that SpreadGrass itself refuses.
    [InlineData("dirt", 42, 668106, "e96e8debf72684750b74772caa973c37740089148a3a8e84077f383dc71a6c11")]
    [InlineData("dirt", 1458, 422351, "e96e8debf72684750b74772caa973c37740089148a3a8e84077f383dc71a6c11")]
    // A bare stone surface with nothing biome-flavoured near it takes plain dirt, and the column walk then
    // grows grass on what the re-skin just made - the two scans in series are what puts a dirt-and-grass skin
    // on a stone hill.
    [InlineData("stone", 42, 668106, "b4ccc059d26a5721e0b750d3946fda4f406406d87b6a01e5fb5f488fb72e7ef1")]
    [InlineData("stone", 1458, 422351, "b4ccc059d26a5721e0b750d3946fda4f406406d87b6a01e5fb5f488fb72e7ef1")]
    // Sand under the stone, with a snow band starting where it ends. Sand wins the scan and cannot be
    // overwritten once seen, so the snow the scan reads afterwards cannot take those cells back; and because
    // the scan runs in ascending columns and reads what earlier columns already wrote, the sand walks right
    // across the map through the snow.
    [InlineData("sand", 42, 668106, "26644e16a56d06f1daa8f9875cd12958f3d5cbbfee37af6764a1c4704bc2ef14")]
    [InlineData("sand", 1458, 422351, "26644e16a56d06f1daa8f9875cd12958f3d5cbbfee37af6764a1c4704bc2ef14")]
    // Snow under the stone, which is taken the same way but without sand's stickiness.
    [InlineData("snow", 42, 668106, "c51d65bde47dc9287b0c2e3922b772803e855e3834d767e9bb7e303723dca518")]
    [InlineData("snow", 1458, 422351, "c51d65bde47dc9287b0c2e3922b772803e855e3834d767e9bb7e303723dca518")]
    // Corrupt grass under the stone, in two bands, the second with a stone roof over part of it: a block
    // directly over the cell turns the answer into plain dirt instead of the evil grass.
    [InlineData("evil", 42, 668106, "76b5424a1c1b0635d7c5b760ec0fd3207a23f1ada600ca71a51d5c2f7153d737")]
    [InlineData("evil", 1458, 422351, "76b5424a1c1b0635d7c5b760ec0fd3207a23f1ada600ca71a51d5c2f7153d737")]
    // Jungle material under the stone, inside and outside the jungle's own columns, part of it roofed. Inside
    // the range the answer is re-chosen from the roof - mud under cover, jungle grass in the open.
    [InlineData("mud", 42, 668106, "a7d6aa6a9a16a038e607ae0d0517c61afc6895aa821267f1f5cbae1a5631d0ff")]
    [InlineData("mud", 1458, 422351, "a7d6aa6a9a16a038e607ae0d0517c61afc6895aa821267f1f5cbae1a5631d0ff")]
    // The same ground with the jungle's columns still unmeasured, which is what an ordinary world actually
    // reaches this pass with: Reset leaves them at -1 and the pass that fills them in is registered
    // twenty-two places later, so the re-decision above is dead code in production. The two hashes differ,
    // which is what pins the range test itself.
    [InlineData("mudflat", 42, 668106, "bf2d8772ff50b88a32017ede7ada163123f1bfa49abdb6c92ddb2b81a0987fce")]
    [InlineData("mudflat", 1458, 422351, "bf2d8772ff50b88a32017ede7ada163123f1bfa49abdb6c92ddb2b81a0987fce")]
    // One cell of jungle grass in a dirt surface. The scan converts the dirt beside it and then scans those
    // cells in turn, so the jungle creeps right to the end of the scan's own range.
    [InlineData("creep", 42, 668106, "e28785444b5751491d53ce83a02337da94a72499da5a0c4f2d7e261b19ac6413")]
    [InlineData("creep", 1458, 422351, "e28785444b5751491d53ce83a02337da94a72499da5a0c4f2d7e261b19ac6413")]
    // Two refusals: stone under papered air, and stone under four rows of stone with a bare cell BELOW it.
    // Neither has a bare unpapered cell ABOVE it inside the scan's reach, which is the only thing the
    // exposure test accepts, so neither is re-skinned at all.
    [InlineData("buried", 42, 668106, "0683b1c63d62ccef430317c5f8ae71bd58840160983f4332c33ddbeff1dd1f0e")]
    [InlineData("buried", 1458, 422351, "0683b1c63d62ccef430317c5f8ae71bd58840160983f4332c33ddbeff1dd1f0e")]
    // The column walk alone. An overhang gives a column a start above the surface as well as on it, a papered
    // column gives it none at all, and a column hollowed out twice under the surface gives it a start at each
    // ledge - except that the walk stops at the first block it passes below the cut-off, so the lower ledge is
    // never offered.
    [InlineData("gaps", 42, 668106, "8745c6da6127a8a511c7dda6da1d0416d96d131e02a88c31f1282e1952ae0f1a")]
    [InlineData("gaps", 1458, 422351, "8745c6da6127a8a511c7dda6da1d0416d96d131e02a88c31f1282e1952ae0f1a")]
    // Two sealed pockets under the surface, walled in grass so nothing the surface spread converts can recurse
    // into them: the only way their ledges grow grass is if the column walk offers them. The left pocket is
    // papered, so the walk never re-arms inside it and its ledge is left alone; the right one is bare, so the
    // walk offers its upper ledge - and then stops, because that ledge is the first block it has passed below
    // the cut-off, which is what leaves the lower ledge alone.
    [InlineData("pocket", 42, 668106, "497586c76bc25020f71c4d028791827afc375c5eb64b07ea1ae0c992045c9aeb")]
    [InlineData("pocket", 1458, 422351, "497586c76bc25020f71c4d028791827afc375c5eb64b07ea1ae0c992045c9aeb")]
    public void Pass_matches_official(string fixture, int seed, int nextDraw, string worldHash)
    {
        WorldTileStore store = CreateStore(fixture);
        var random = new RandomAdapter(seed);

        new SurfaceGrassSpreadPass1458(
                store, random, WorldSurface, WorldSurfaceHigh,
                fixture == "mudflat" ? -1 : JungleMinX,
                fixture == "mudflat" ? -1 : JungleMaxX,
                TestContext.Current.CancellationToken)
            .Apply();

        string expected = $"{nextDraw}|{worldHash}";
        string actual = $"{random.Next(1000000)}|{Hash(store)}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTileStore CreateStore(string fixture)
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));

        ushort surfaceType = fixture switch
        {
            "dirt" or "gaps" or "pocket" => 0,
            "stone" or "sand" or "snow" or "evil" or "mud" or "mudflat" or "buried" => 1,
            _ => 2
        };

        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            var tile = new WorldTile();
            if (y < Surface)
            {
                tile.FrameX = -1;
                tile.FrameY = -1;
            }
            else if (y == Surface)
            {
                tile.Flags = WorldTileFlags.Active;
                tile.Type = surfaceType;
            }
            else if (y < 80)
            {
                tile.Flags = WorldTileFlags.Active;
                tile.Type = 0;
            }
            else
            {
                tile.Flags = WorldTileFlags.Active;
                tile.Type = 1;
            }

            store.Set(x, y, tile);
        }

        switch (fixture)
        {
            case "sand":
                Band(store, 100, 121, Surface + 1, 53);
                Band(store, 121, 142, Surface + 1, 147);
                break;
            case "snow":
                Band(store, 100, 121, Surface + 1, 147);
                break;
            case "evil":
                Band(store, 100, 121, Surface + 1, 23);
                Band(store, 180, 201, Surface + 1, 23);
                Roof(store, 185, 196);
                break;
            case "mud":
            case "mudflat":
                Band(store, 100, 121, Surface + 1, 60);
                Band(store, 210, 231, Surface + 1, 60);
                Roof(store, 215, 226);
                Band(store, 240, 251, Surface + 1, 59);
                break;
            case "creep":
                for (int x = 0; x < Width; x++)
                    Retype(store, x, Surface, 0);
                Retype(store, 100, Surface, 60);
                break;
            case "buried":
                for (int x = 100; x < 121; x++)
                for (int y = Surface - 4; y < Surface; y++)
                {
                    WorldTile papered = store.Get(x, y);
                    papered.Wall = 2;
                    store.Set(x, y, papered);
                }

                for (int x = 180; x < 201; x++)
                for (int y = Surface - 4; y < Surface; y++)
                    Fill(store, x, y, 1);
                for (int x = 180; x < 201; x++)
                    Hollow(store, x, Surface + 2, Surface + 3);
                break;
            case "pocket":
                foreach (int x in (int[])[129, 151, 159, 181])
                for (int y = Surface + 1; y < 80; y++)
                    Fill(store, x, y, 2);
                for (int x = 130; x < 151; x++)
                {
                    Hollow(store, x, Surface + 1, 65);
                    for (int y = Surface + 1; y < 65; y++)
                    {
                        WorldTile papered = store.Get(x, y);
                        papered.Wall = 2;
                        store.Set(x, y, papered);
                    }

                    Fill(store, x, 65, 0);
                    for (int y = 66; y < 80; y++)
                        Fill(store, x, y, 2);
                }

                for (int x = 160; x < 181; x++)
                {
                    Hollow(store, x, Surface + 1, 65);
                    Fill(store, x, 65, 0);
                    Hollow(store, x, 66, 71);
                    Fill(store, x, 71, 0);
                    for (int y = 72; y < 80; y++)
                        Fill(store, x, y, 2);
                }

                break;
            case "gaps":
                for (int x = 100; x < 121; x++)
                    Fill(store, x, 30, 0);
                for (int x = 130; x < 151; x++)
                for (int y = 0; y < Surface; y++)
                {
                    WorldTile papered = store.Get(x, y);
                    papered.Wall = 2;
                    store.Set(x, y, papered);
                }

                for (int x = 160; x < 181; x++)
                {
                    Hollow(store, x, 51, 65);
                    Fill(store, x, 65, 0);
                    Hollow(store, x, 66, 71);
                    Fill(store, x, 71, 0);
                }

                break;
        }

        return store;
    }

    private static void Band(WorldTileStore store, int left, int right, int row, ushort type)
    {
        for (int x = left; x < right; x++)
            Fill(store, x, row, type);
    }

    private static void Roof(WorldTileStore store, int left, int right)
    {
        for (int x = left; x < right; x++)
            Fill(store, x, Surface - 1, 1);
    }

    private static void Hollow(WorldTileStore store, int x, int top, int bottom)
    {
        for (int y = top; y < bottom; y++)
        {
            WorldTile tile = store.Get(x, y);
            tile.Flags &= ~WorldTileFlags.Active;
            store.Set(x, y, tile);
        }
    }

    private static void Fill(WorldTileStore store, int x, int y, ushort type)
    {
        WorldTile tile = store.Get(x, y);
        tile.Flags |= WorldTileFlags.Active;
        tile.Type = type;
        tile.FrameX = 0;
        tile.FrameY = 0;
        store.Set(x, y, tile);
    }

    private static void Retype(WorldTileStore store, int x, int y, ushort type)
    {
        WorldTile tile = store.Get(x, y);
        tile.Type = type;
        store.Set(x, y, tile);
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
