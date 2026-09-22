using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for the platform arm of TerrariaServer 1.4.5.8 <c>WorldGen.TileFrameImportant</c>.
/// </summary>
/// <remarks>
/// <para>
/// Expectations come from calling the unmodified <c>WorldGen.TileFrame</c> inside the pinned dedicated server
/// on the same nine-cell neighbourhood, over every combination of the centre platform's own shape, both side
/// neighbours, all four diagonals, and whether the cell above forbids sloping: 51,200 cases per shape,
/// 307,200 in all.
/// </para>
/// <para>
/// The side neighbours cover the cases the table actually distinguishes rather than a sample of materials: a
/// flat platform, a hammered one, one sloped each way, a DIFFERENT platform identity, plain Stone, plain Dirt,
/// a gem, and a closed trapdoor. The last two are the ones that prove the <c>Main.tileStone</c> remap is real -
/// a trapdoor is not solid on its own, so without being read as Stone first it would not merge at all.
/// </para>
/// <para>
/// Shapes four and five - vanilla slopes three and four - hash identically to the flat case, because the source
/// only gives slopes one and two frames of their own and lets the others fall through to the ordinary table.
/// That equality is the evidence the fall-through is right rather than an accident of the fixture.
/// </para>
/// </remarks>
public sealed class GenerationPlatformFraming1458Tests
{
    private const int Width = 80;
    private const int Height = 80;
    private const int X = 40;
    private const int Y = 40;
    private const int SideOptions = 10;
    private const int DiagonalOptions = 4;

    [Theory]
    // centre shape, case count, frame hash
    [InlineData(0, 51200, "e6c8763a5071e6a4ae80044d2a5dee0a8d04d37171e67c0094167728c4910eaa")]
    [InlineData(1, 51200, "7f266171d4866d1a2b42278fb4835796dbc648c584c95bbd1ef80adb7d8ad43c")]
    [InlineData(2, 51200, "f0db8f27eda40566ef79a8ebf38f8240f42865472cdd2684ca70149aaca67d14")]
    [InlineData(3, 51200, "317ce3a3e62d2f24a490862b14f466758aec19b59ae4ad09c2eb5463aad5ef55")]
    [InlineData(4, 51200, "e6c8763a5071e6a4ae80044d2a5dee0a8d04d37171e67c0094167728c4910eaa")]
    [InlineData(5, 51200, "e6c8763a5071e6a4ae80044d2a5dee0a8d04d37171e67c0094167728c4910eaa")]
    public void Platform_frames_match_official(int shape, int caseCount, string frameHash)
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));
        var framing = new GenerationTileFraming1458(store, new RandomAdapter(0));
        var sb = new StringBuilder();
        int cases = 0;

        for (int left = 0; left < SideOptions; left++)
        for (int right = 0; right < SideOptions; right++)
        for (int upperLeft = 0; upperLeft < DiagonalOptions; upperLeft++)
        for (int upperRight = 0; upperRight < DiagonalOptions; upperRight++)
        for (int lowerLeft = 0; lowerLeft < DiagonalOptions; lowerLeft++)
        for (int lowerRight = 0; lowerRight < DiagonalOptions; lowerRight++)
        for (int above = 0; above < 2; above++)
        {
            Clear(store);
            Centre(store, shape);
            Side(store, X - 1, Y, left);
            Side(store, X + 1, Y, right);
            Diagonal(store, X - 1, Y - 1, upperLeft);
            Diagonal(store, X + 1, Y - 1, upperRight);
            Diagonal(store, X - 1, Y + 1, lowerLeft);
            Diagonal(store, X + 1, Y + 1, lowerRight);
            if (above == 1)
                Set(store, X, Y - 1, active: true, type: 21, shape: 0);

            framing.TileFrame(X, Y);
            sb.Append(store.Get(X, Y).FrameX).Append(';');
            cases++;
        }

        string expected = $"{caseCount}|{frameHash}";
        string actual = $"{cases}|{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");
    }

    private static void Clear(WorldTileStore store)
    {
        for (int x = X - 2; x <= X + 2; x++)
        for (int y = Y - 2; y <= Y + 2; y++)
            store.Set(x, y, new WorldTile());
    }

    private static void Centre(WorldTileStore store, int shape) =>
        // WorldTile.Shape holds 0 for a full block, 1 for a half brick and vanilla slopes 1..4 as 2..5.
        Set(store, X, Y, active: true, type: 19, shape: (byte)(shape == 0 ? 0 : shape == 1 ? 1 : shape));

    private static void Side(WorldTileStore store, int x, int y, int option)
    {
        switch (option)
        {
            case 0: return;
            case 1: Set(store, x, y, true, 19, 0); return;
            case 2: Set(store, x, y, true, 19, 1); return;
            case 3: Set(store, x, y, true, 19, 2); return;
            case 4: Set(store, x, y, true, 19, 3); return;
            case 5: Set(store, x, y, true, 1, 0); return;
            case 6: Set(store, x, y, true, 0, 0); return;
            case 7: Set(store, x, y, true, 63, 0); return;
            case 8: Set(store, x, y, true, 435, 0); return;
            default: Set(store, x, y, true, 131, 0); return;
        }
    }

    private static void Diagonal(WorldTileStore store, int x, int y, int option)
    {
        if (option == 0)
            return;
        Set(store, x, y, true, 19, (byte)(option == 2 ? 2 : option == 3 ? 3 : 0));
    }

    private static void Set(WorldTileStore store, int x, int y, bool active, ushort type, byte shape) =>
        store.Set(x, y, new WorldTile
        {
            Flags = active ? WorldTileFlags.Active : 0, Type = type, Shape = shape
        });

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
