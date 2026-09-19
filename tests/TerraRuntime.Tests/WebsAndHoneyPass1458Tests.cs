using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for the registered TerrariaServer 1.4.5.8
/// <c>GenPassNameID.WebsInSpiderCavesAndHoneyPlusSpeleothemsInBeehives</c> pass.
/// </summary>
/// <remarks>
/// Expectations come from calling the unmodified registered delegate through <c>GenPass.Apply</c> on the same
/// synthetic ground inside the pinned dedicated server, and checking the next shared RNG and a SHA-256 over
/// every field of every cell - the liquid's KIND included, without which the honey half of this pass would be
/// invisible to the comparison, since turning water into honey changes no amount and no identity.
///
/// The fixture is a papered cavern with a stone pillar every ninth column, so speleothems have something to
/// hang from and cobwebs have something to anchor to. The wall is varied one fixture at a time because the two
/// halves of the pass are independent and spend different draws: a hive cell spends one deciding whether to
/// offer a speleothem and two more inside <c>PlaceTight</c> if it does, a spider cell one deciding whether to
/// offer a web and a second on its radius. The wet pair adds standing liquid, which a hive turns to honey and a
/// spider cave empties; the mixed fixture papers each half of the world with a different wall so both run in
/// one scan.
/// </remarks>
public sealed class WebsAndHoneyPass1458Tests
{
    private const int Width = 700;
    private const int Height = 700;
    private const int GroundRow = 200;
    private const double WorldSurface = 200.0;
    private const double RockLayer = 250.0;
    private const int BeachDistance = 380;

    [Theory]
    // fixture, seed, nextDraw, worldHash
    [InlineData("hive", 42, 64911, "48d05959f712d76a803d44d4fcf3c389995aba0a0dec5c77257e9f891fd2d41a")]
    [InlineData("hive", 1458, 29982, "30ea963125d19946ea8326a413628d06f2fc32a29efd5e4d9f639b1d519340e4")]
    [InlineData("spider", 42, 463676, "d9f40342142d3b7868c3e7175552d1debb06aa3ed0400b101758c2e148b1eeb6")]
    [InlineData("spider", 1458, 311941, "358050438b298572395eb6cd980b8c9721e35338e232d9adaa78b8f5f852b2ee")]
    // Standing liquid: a hive turns it to honey without spending a draw, a spider cave empties it.
    [InlineData("hive-wet", 42, 64911, "e5e064f0d0ad7ffe2902ab2dd70eff0ae055e1250922c8b57febc9c12d7457c5")]
    [InlineData("hive-wet", 1458, 29982, "b27266875b64c96f2dea9990ba3b92d86e8759413c985df9eb38bb63b5080e6e")]
    [InlineData("spider-wet", 42, 463676, "d9f40342142d3b7868c3e7175552d1debb06aa3ed0400b101758c2e148b1eeb6")]
    [InlineData("spider-wet", 1458, 311941, "358050438b298572395eb6cd980b8c9721e35338e232d9adaa78b8f5f852b2ee")]
    [InlineData("mixed", 42, 792697, "3a547a671e8850e2582f18381304277625e7f4ee2751d101dc66bb4921f8b854")]
    [InlineData("mixed", 1458, 515311, "7d40d47b6ab72daef0fa9ce8db15dda35173d147272d09ec5ffea9ba8f43ec74")]
    public void Pass_matches_official(string fixture, int seed, int nextDraw, string worldHash)
    {
        WorldTileStore store = CreateStore(fixture);
        var random = new RandomAdapter(seed);

        new WebsAndHoneyPass1458(
                store, random, WorldSurface, RockLayer, BeachDistance, TestContext.Current.CancellationToken)
            .Apply();

        string expected = $"{nextDraw}|{worldHash}";
        string actual = $"{random.Next(1000000)}|{Hash(store)}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTileStore CreateStore(string fixture)
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            bool active = y >= GroundRow;
            store.Set(x, y, new WorldTile
            {
                Type = active ? (ushort)1 : (ushort)0,
                FrameX = active ? (short)0 : (short)-1,
                FrameY = active ? (short)0 : (short)-1,
                Flags = active ? WorldTileFlags.Active : WorldTileFlags.None
            });
        }

        ushort wall = fixture.StartsWith("spider", StringComparison.Ordinal) ? (ushort)62 : (ushort)86;
        bool both = fixture == "mixed";
        bool wet = fixture.EndsWith("-wet", StringComparison.Ordinal) || both;
        for (int x = 110; x < Width - 110; x++)
        {
            ushort here = both && x > Width / 2 ? (ushort)62 : wall;
            for (int y = GroundRow + 5; y < Height - 110; y++)
            {
                bool pillar = x % 9 == 0 && y % 11 < 3;
                store.Set(x, y, new WorldTile
                {
                    Wall = here,
                    Type = pillar ? (ushort)1 : (ushort)0,
                    FrameX = pillar ? (short)0 : (short)-1,
                    FrameY = pillar ? (short)0 : (short)-1,
                    Flags = pillar ? WorldTileFlags.Active : WorldTileFlags.None,
                    LiquidAmount = wet && !pillar && y % 7 == 0 ? (byte)160 : (byte)0
                });
            }
        }

        return store;
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
