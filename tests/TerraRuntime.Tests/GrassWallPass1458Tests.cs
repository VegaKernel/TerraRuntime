using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for the registered TerrariaServer 1.4.5.8
/// <c>GenPassNameID.SurfaceDirtWallsToGrassWalls</c> pass.
/// </summary>
/// <remarks>
/// Expectations come from calling the unmodified registered delegate through <c>GenPass.Apply</c> on the same
/// synthetic ground inside the pinned dedicated server, and checking the next shared RNG and a SHA-256 over
/// every field of every cell.
///
/// The fixture is a grass surface over dirt with a papered hollow just above it, alternating between a dirt
/// wall and bare air so a grass cell sees both inside one square - which is exactly what the pass looks for
/// before it repapers the pocket green. The hollow is kept well away from the world edge because the pocket
/// measurement gives up the moment it reaches the border, which would refuse every pocket and leave nothing to
/// compare. Each fixture then varies one thing that changes the outcome: the wall the pocket is entered at, a
/// wall the paint cannot replace, a wall the measurement refuses to count past, snow inside the pocket, a
/// pocket larger than the measurement's cap, a pocket larger than the paint's own cap, and an exposed dirt
/// face so the pass's second half actually grows grass.
/// </remarks>
public sealed class GrassWallPass1458Tests
{
    private const int Width = 300;
    private const int Height = 200;
    private const int Surface = 50;
    private const double WorldSurface = 80.0;

    private const int AirColumn = 100;
    private const int BareLeft = 120;
    private const int BareRight = 161;
    private const int ShaftColumn = 200;
    private const int ShaftBottom = 86;
    private const int TwinLeft = 160;
    private const int TwinRight = 201;
    private const int UnwalledLeft = 20;
    private const int UnwalledRight = 41;

    [Theory]
    // fixture, seed, nextDraw, worldHash
    [InlineData("plain", 42, 102911, "e230cabc874a2ea8d386496efa7fb9f2c9a7416014b5a03c0e665b2221f6e141")]
    [InlineData("plain", 1458, 113895, "b6a6c973410810f42b5737e8d5f62150fdedf959f5b9d9bc7740b7f767ff1e52")]
    // The pocket is entered at a dirt SLAB wall rather than a dirt wall, which is accepted just the same - and
    // which the measurement then counts as nothing at all, so the size cap cannot refuse it.
    [InlineData("wall15", 42, 102911, "e230cabc874a2ea8d386496efa7fb9f2c9a7416014b5a03c0e665b2221f6e141")]
    [InlineData("wall15", 1458, 113895, "b6a6c973410810f42b5737e8d5f62150fdedf959f5b9d9bc7740b7f767ff1e52")]
    // Two exposed bands of dirt in the surface row, and neither costs a draw. The one under the papered
    // hollow grows grass except where standing lava forces the enclosure flag; the one out past the hollow,
    // which never gets a green wall beside it, grows nothing at all.
    [InlineData("bare", 42, 102911, "e37eca732a0067faa0dc483d423e20304c2e84a1bb71223848005f9d9ad8a941")]
    [InlineData("bare", 1458, 113895, "425753b025168c8a9af61c45a4ffba210482b2be7ed616108967ece39042e2e2")]
    // A bare column punched from the hollow down past the surface line: the flood follows it down, the dirt
    // either side of it is no longer walled in, and the grass that grows down those two columns stops at the
    // surface line itself - the one refusal in SpreadGrass this ground otherwise never reaches.
    [InlineData("shaft", 42, 837148, "6af8e33e704e53a1de4f2dc2d39096ec096ad65392ddb4dc4cd286fe20ea4033")]
    [InlineData("shaft", 1458, 132920, "53292922b0c929b5e4d179c91aadd3c68740c4c6eaa330b40deabe1b43e84898")]
    // A second pocket carved under the grass row, cut off from the hollow above by the grass itself: the
    // flood paints solid cells but never spreads through one, so these cells are reachable only by being
    // chosen as the pocket to enter. The hollow above runs to the world border, which the measurement
    // refuses, so the two pockets have opposite verdicts and only the LAST candidate found in the square -
    // this one - is ever painted.
    [InlineData("twin", 42, 694637, "454aa0a1e6487ad84caf0dec338cd697838352f12e958da349ee0463007f61bc")]
    [InlineData("twin", 1458, 19196, "092d32f2ca19c41040dd89610a876abf06b3f62df196cdc35ceb3638f77cdad7")]
    // A column of a wall the paint refuses to replace, which the measurement still counts past: the pocket is
    // painted around the column, and the eleven cells it keeps cost eleven draws in the second half.
    [InlineData("blocked", 42, 943713, "9800511e6be1a35b4630474941dd6e45e3a1b11b9dc5a828b7d2410bb79aa624")]
    [InlineData("blocked", 1458, 751883, "9e397f69d8e45b0f20f45b1db81c0c86a6e5bbc69bc84993685c053e59359346")]
    // Three ways to refuse the whole pocket, which all leave the walls exactly as they were found and so cost
    // the same draws: a wall on the measurement's bail list, snow inside the pocket, and a pocket over its cap.
    [InlineData("wall3", 42, 981391, "f2f775e514f8956d2548892247a6d7c9e81742255127549c87c0aebaa5ba717b")]
    [InlineData("wall3", 1458, 414336, "f2f775e514f8956d2548892247a6d7c9e81742255127549c87c0aebaa5ba717b")]
    [InlineData("snow", 42, 981391, "f8da2fd0fe5e58e3818bc0b1e5e38953a853b39e4c0096cbece21db7e20c4e25")]
    [InlineData("snow", 1458, 414336, "f8da2fd0fe5e58e3818bc0b1e5e38953a853b39e4c0096cbece21db7e20c4e25")]
    [InlineData("big", 42, 981391, "f4d3f212b2ef5f61a0a8eef6f8095a017a140faf0f83b862d806258dc2e8ad3b")]
    [InlineData("big", 1458, 414336, "f4d3f212b2ef5f61a0a8eef6f8095a017a140faf0f83b862d806258dc2e8ad3b")]
    // A hollow that runs to the world's edge: the measurement gives up the moment it reaches the border, and
    // because the whole hollow is one pocket that refuses every cell in it.
    [InlineData("edge", 42, 981391, "2f0be2c1914cf1ff578f26e880bc7d0cfc9d15c253f10d76c4b204209daba52e")]
    [InlineData("edge", 1458, 414336, "2f0be2c1914cf1ff578f26e880bc7d0cfc9d15c253f10d76c4b204209daba52e")]
    // A pocket the measurement accepts and the paint cannot finish: its own cap stops the flood partway, so
    // which cells end up green is decided by the order the flood takes them off its queue.
    [InlineData("cap", 42, 319688, "199b14d976053a2fdc3a08c2d6b4eb65ac92396d6bb306fc614e401abd6ea9c7")]
    [InlineData("cap", 1458, 65451, "6ff029b6452f9d8f21b4349fd17dda096924b60b33d5ab1ee9ef864a490ab7fc")]
    public void Pass_matches_official(string fixture, int seed, int nextDraw, string worldHash)
    {
        WorldTileStore store = CreateStore(fixture);
        var random = new RandomAdapter(seed);

        new GrassWallPass1458(store, random, WorldSurface, TestContext.Current.CancellationToken).Apply();

        string expected = $"{nextDraw}|{worldHash}";
        string actual = $"{random.Next(1000000)}|{Hash(store)}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTileStore CreateStore(string fixture)
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));

        bool tall = fixture is "big" or "cap";
        int hollowTop = tall ? 10 : 40;
        int hollowLeft = fixture is "edge" or "twin" ? 0 : 60;
        const int hollowRight = 240;
        // "cap" papers the whole hollow so the flood has more to paint than its cap allows, and leaves one
        // bare column so a grass cell below can still see air and offer the flood a pocket to enter.
        bool solidPaper = fixture == "cap";
        ushort candidateWall = fixture == "wall15" || solidPaper ? (ushort)15 : (ushort)2;

        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            var tile = new WorldTile();
            if (y < Surface)
            {
                if (y >= hollowTop && x >= hollowLeft && x < hollowRight)
                {
                    bool bare = solidPaper ? x == AirColumn : x % 2 != 0;
                    tile.Wall = bare ? (ushort)0 : candidateWall;
                }

                tile.FrameX = -1;
                tile.FrameY = -1;
            }
            else if (y == Surface)
            {
                // "bare" leaves a band of the surface row as plain dirt, which is the only thing in this
                // fixture SpreadGrass can convert: the dirt under the grass row is walled in on all eight
                // sides, and an enclosed cell is refused.
                bool bareBand = fixture == "bare" &&
                    ((x >= BareLeft && x < BareRight) || (x >= UnwalledLeft && x < UnwalledRight));
                tile.Flags = WorldTileFlags.Active;
                tile.Type = bareBand ? (ushort)0 : (ushort)2;
                tile.Wall = 2;
            }
            else if (y < 80)
            {
                tile.Flags = WorldTileFlags.Active;
                tile.Type = 0;
                tile.Wall = 2;
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
            case "bare":
                // Standing lava anywhere inside the square forces the enclosure flag, which refuses the
                // conversion - so half of the bare band grows grass and half does not.
                for (int x = BareLeft; x < BareLeft + 20; x++)
                for (int y = Surface - 5; y < Surface; y++)
                {
                    WorldTile wet = store.Get(x, y);
                    wet.LiquidAmount = 255;
                    wet.LiquidKind = WorldLiquidKind.Lava;
                    store.Set(x, y, wet);
                }

                break;
            case "twin":
                for (int x = TwinLeft; x < TwinRight; x += 2)
                {
                    WorldTile under = store.Get(x, Surface + 1);
                    under.Flags &= ~WorldTileFlags.Active;
                    under.Type = 0;
                    under.Wall = 2;
                    under.FrameX = -1;
                    under.FrameY = -1;
                    store.Set(x, Surface + 1, under);
                }

                break;
            case "shaft":
                for (int y = hollowTop; y < ShaftBottom; y++)
                {
                    WorldTile shaft = store.Get(ShaftColumn, y);
                    shaft.Flags &= ~WorldTileFlags.Active;
                    shaft.Type = 0;
                    shaft.Wall = 2;
                    shaft.FrameX = -1;
                    shaft.FrameY = -1;
                    store.Set(ShaftColumn, y, shaft);
                }

                for (int y = Surface + 1; y < ShaftBottom; y++)
                foreach (int x in (int[])[ShaftColumn - 1, ShaftColumn + 1])
                {
                    WorldTile side = store.Get(x, y);
                    side.Flags |= WorldTileFlags.Active;
                    side.Type = 0;
                    side.Wall = 2;
                    side.FrameX = 0;
                    side.FrameY = 0;
                    store.Set(x, y, side);
                }

                break;
            case "blocked":
                // Wall 4 is a wall the paint refuses to replace, but not one the measurement bails on, so the
                // count still passes and the paint is confined to one side of the column.
                Paper(store, Width / 2, hollowTop, 80, 4);
                break;
            case "wall3":
                // Wall 3 is on the measurement's bail list, so the count reaches its cap and nothing spreads.
                Paper(store, Width / 2, hollowTop, 80, 3);
                break;
            case "snow":
                for (int x = 150; x < 170; x++)
                for (int y = hollowTop; y < Surface; y++)
                {
                    WorldTile snow = store.Get(x, y);
                    snow.Flags |= WorldTileFlags.Active;
                    snow.Type = 147;
                    store.Set(x, y, snow);
                }

                break;
        }

        return store;
    }

    private static void Paper(WorldTileStore store, int x, int top, int bottom, ushort wall)
    {
        for (int y = top; y < bottom; y++)
        {
            WorldTile tile = store.Get(x, y);
            tile.Wall = wall;
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
