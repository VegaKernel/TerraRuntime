using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for the object validators TerrariaServer 1.4.5.8 <c>WorldGen.TileFrame</c> runs
/// during world generation: <c>CheckJunglePlant</c> for the jungle plant family and <c>Check3x2</c> for the two
/// large pile families.
/// </summary>
/// <remarks>
/// Expectations come from calling <c>WorldGen.SquareTileFrame</c> - the call <c>KillTile</c> itself makes - on
/// the same synthetic ground inside the pinned dedicated server, and checking the next shared RNG and a SHA-256
/// over every field of every cell. Each fixture is a flat substrate band with one object on it; the "broken"
/// fixtures clear a single cell first, which is what a flower patch does to an object it plants over.
///
/// The fixtures are chosen to separate the three things that decide an object's fate. The footprint: plant
/// detritus frames from row zero when it is three wide and from row thirty-six when it is two wide, and the
/// validator reads the row to know which. The substrate: a pile cut for snow, mud, ash or sand is destroyed
/// when it no longer stands on one, while a style outside those groups carries no ground requirement at all.
/// And the grass pile's downgrade, which rewrites the object to Large Piles instead of destroying it - so the
/// destruction that follows no longer recognises it, and it survives with a different identity.
/// </remarks>
public sealed class GenerationTileFraming1458Tests
{
    private const int Width = 200;
    private const int Height = 200;
    private const int GroundRow = 100;
    private const int ObjectX = 50;

    [Theory]
    // fixture, substrate, type, style, twoWide, breakColumn, breakRow, worldHash
    [InlineData("det3-intact", 60, 233, 0, false, -1, 0,
        "3c67bb79f7e3c30ad8bc900b14686acc2889097cff1f1a3beea24943b595dcad")]
    [InlineData("det3-broken", 60, 233, 0, false, 1, 0,
        "49d98f2fd5fca33f37cac564517b656daf8c6401d08d1b716a481e687ae0d870")]
    [InlineData("det3-broken-lower", 60, 233, 0, false, 2, 1,
        "49d98f2fd5fca33f37cac564517b656daf8c6401d08d1b716a481e687ae0d870")]
    [InlineData("det3-stone", 1, 233, 0, false, -1, 0,
        "fc4f3339c031ff1d03dfab73e74c0caeb7e7f18b436b80813a59c5b06097409a")]
    [InlineData("det3-style5", 60, 233, 5, false, -1, 0,
        "20a022973734440321528a8a8836056ea5094a09445dd5a2d9572331c97358c4")]
    [InlineData("det2-intact", 60, 233, 0, true, -1, 0,
        "9bd474dd8c8a7e13673eff6f64b0fa4bffd9ec2f8224bcce73c08029983a70ba")]
    [InlineData("det2-broken", 60, 233, 0, true, 1, 0,
        "49d98f2fd5fca33f37cac564517b656daf8c6401d08d1b716a481e687ae0d870")]
    [InlineData("det2-stone", 1, 233, 0, true, -1, 0,
        "fc4f3339c031ff1d03dfab73e74c0caeb7e7f18b436b80813a59c5b06097409a")]
    [InlineData("det2-style7", 60, 233, 7, true, -1, 0,
        "8366416cae918092989f85347cef3ab7baa0df7893f4a1dd347588642b642be3")]
    [InlineData("det2-style7-broken", 60, 233, 7, true, 0, 1,
        "49d98f2fd5fca33f37cac564517b656daf8c6401d08d1b716a481e687ae0d870")]
    [InlineData("pile186-snow-on-snow", 147, 186, 26, false, -1, 0,
        "9bda55f5b3134eb065272523e66c6aa18905f2b500a880866cb27e2f54601554")]
    [InlineData("pile186-snow-on-stone", 1, 186, 26, false, -1, 0,
        "fc4f3339c031ff1d03dfab73e74c0caeb7e7f18b436b80813a59c5b06097409a")]
    [InlineData("pile186-mud-on-mud", 59, 186, 33, false, -1, 0,
        "5bc4ac2d8ca612832e57ac584b1568e6ff11ee7fc4963991f75c8d9a317e051f")]
    [InlineData("pile186-mud-on-stone", 1, 186, 33, false, -1, 0,
        "fc4f3339c031ff1d03dfab73e74c0caeb7e7f18b436b80813a59c5b06097409a")]
    [InlineData("pile186-free-on-stone", 1, 186, 10, false, -1, 0,
        "b397810d8d57f4e0be88b681b2c3d8fbba33670c8d76aea8b3f3d217741962d4")]
    [InlineData("pile187-mud-on-mud", 59, 187, 0, false, -1, 0,
        "f9da8910ccb0d44be3ad047ebd3d86c2a43f8be4fb3d0a860fb3347d3a932ce1")]
    [InlineData("pile187-mud-on-stone", 1, 187, 0, false, -1, 0,
        "fc4f3339c031ff1d03dfab73e74c0caeb7e7f18b436b80813a59c5b06097409a")]
    [InlineData("pile187-ash-on-ash", 57, 187, 7, false, -1, 0,
        "7158bf5b5a1ab8ba95dbe2ae913d41ec4a7ddf67d76399680450f349d4c57679")]
    [InlineData("pile187-ash-on-stone", 1, 187, 7, false, -1, 0,
        "fc4f3339c031ff1d03dfab73e74c0caeb7e7f18b436b80813a59c5b06097409a")]
    [InlineData("pile187-sand-on-sand", 53, 187, 30, false, -1, 0,
        "d03b0797b959606e8f29354e01180da2941f083bb4c37e15e37b170655506660")]
    [InlineData("pile187-sand-on-stone", 1, 187, 30, false, -1, 0,
        "fc4f3339c031ff1d03dfab73e74c0caeb7e7f18b436b80813a59c5b06097409a")]
    [InlineData("pile187-grass-on-grass", 2, 187, 14, false, -1, 0,
        "dbe7024da32ac1765c50af8f76e0464502c66169a23ccb2d16440d45df67b614")]
    [InlineData("pile187-grass-on-stone", 1, 187, 14, false, -1, 0,
        "bc94544c2dd40b253afe76832712f49a2e5c7fe51874da6bcd15a77d2c2cef8f")]
    [InlineData("pile187-grass-on-stone-broken", 1, 187, 14, false, 0, 1,
        "2ad6119316693c4195d37549a922efcc6a48be67e4d4f188285e43593f3ed507")]
    [InlineData("pile187-grass-on-hallowed", 477, 187, 15, false, -1, 0,
        "30930e4a0743a12b13ff2d6c052f9204d252ffd770f49757010f857f3b1f7b5a")]
    // The fallen log is the one identity this validator never destroys while a world is being generated: it
    // puts the object back and forces grass under all three columns, so a log on stone ends up standing on a
    // strip of grass it made itself.
    [InlineData("log488-on-grass", 2, 488, 0, false, -1, 0,
        "ac6dbaeb234a8b109aff756c97b7a9c4c363989cf68af1ac49744f00d13d1d97")]
    [InlineData("log488-on-stone", 1, 488, 0, false, -1, 0,
        "8fd6dfca51ac50b6c31d427b69e7b3f57a715c045ef20724b34dd48d38822a6e")]
    [InlineData("log488-on-snow", 147, 488, 0, false, -1, 0,
        "dcabcb2b54cc3d71a6a791f8f199b59d5a2e73960403011b360c74beddf1afdb")]
    [InlineData("log488-on-stone-broken", 1, 488, 0, false, 1, 0,
        "8fd6dfca51ac50b6c31d427b69e7b3f57a715c045ef20724b34dd48d38822a6e")]
    [InlineData("log488-on-grass-broken", 2, 488, 0, false, 1, 0,
        "ac6dbaeb234a8b109aff756c97b7a9c4c363989cf68af1ac49744f00d13d1d97")]
    public void Validators_match_official(
        string fixture,
        int substrate,
        int type,
        int style,
        bool twoWide,
        int breakColumn,
        int breakRow,
        string worldHash)
    {
        WorldTileStore store = CreateStore((ushort)substrate, (ushort)type, style, twoWide);

        int frameX = ObjectX;
        int frameY = GroundRow - 1;
        if (breakColumn >= 0)
        {
            frameX = ObjectX - 1 + breakColumn;
            frameY = GroundRow - 2 + breakRow;
            WorldTile cleared = store.Get(frameX, frameY);
            cleared.Flags &= ~WorldTileFlags.Active;
            cleared.Type = 0;
            cleared.FrameX = -1;
            cleared.FrameY = -1;
            store.Set(frameX, frameY, cleared);
        }

        var random = new CountingRandom();
        new GenerationTileFraming1458(store, random).SquareTileFrame(frameX, frameY);
        Assert.Equal(0, random.Count);

        Assert.True(worldHash == Hash(store), $"{fixture}: official={worldHash} runtime={Hash(store)} " +
            $"footprint={Footprint(store)}");
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTileStore CreateStore(ushort substrate, ushort type, int style, bool twoWide)
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            bool active = y >= GroundRow;
            store.Set(x, y, new WorldTile
            {
                Type = active ? (y == GroundRow ? substrate : (ushort)1) : (ushort)0,
                FrameX = active ? (short)0 : (short)-1,
                FrameY = active ? (short)0 : (short)-1,
                Flags = active ? WorldTileFlags.Active : WorldTileFlags.None
            });
        }

        int width = twoWide ? 2 : 3;
        int stride = twoWide ? 36 : 54;
        int topRow = twoWide && type == 233 ? 36 : 0;
        int left = ObjectX - 1;
        int top = GroundRow - 2;
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            store.Set(left + dx, top + dy, new WorldTile
            {
                Type = type,
                FrameX = (short)(style * stride + dx * 18),
                FrameY = (short)(topRow + dy * 18),
                Flags = WorldTileFlags.Active
            });
        }

        return store;
    }

    /// <summary>
    /// The validators themselves draw nothing; the kills they trigger spend only what the identity's dust
    /// costs, and every identity in these fixtures costs nothing. The probe confirms it: the next shared draw
    /// is the same value in all twenty-five cases.
    /// </summary>
    private sealed class CountingRandom : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(1458);
        public int Count { get; private set; }
        public int Next() { Count++; return random.Next(); }
        public int Next(int max) { Count++; return random.Next(max); }
        public int Next(int min, int max) { Count++; return random.Next(min, max); }
        public double NextDouble() { Count++; return random.NextDouble(); }
        public void NextBytes(byte[] bytes) { Count++; random.NextBytes(bytes); }
    }

    private static string Footprint(WorldTileStore store)
    {
        var sb = new StringBuilder();
        for (int y = GroundRow - 2; y <= GroundRow; y++)
        for (int x = ObjectX - 1; x <= ObjectX + 1; x++)
        {
            WorldTile tile = store.Get(x, y);
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(tile.IsActive ? "1" : "0").Append(':')
              .Append(tile.Type).Append(':')
              .Append(tile.FrameX).Append(':')
              .Append(tile.FrameY);
        }

        return sb.ToString();
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
              .Append(slope).Append(',')
              .Append(tile.Shape == 1 ? '1' : '0').Append(';');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
