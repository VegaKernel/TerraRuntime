using System.Text;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for the object death TerrariaServer 1.4.5.8 <c>WorldGen.WaterCheck</c> performs
/// during load on a two-by-two pot standing on platforms, with lava in one, the other, both or neither.
/// </summary>
/// <remarks>
/// A seed-1458 world contains this arrangement, and both halves of it are ordinary vanilla:
/// <c>WorldGen.PlacePot</c> accepts a support on plain <c>Main.tileSolid</c>, which platforms satisfy, and
/// <c>WorldGen.CheckPot</c> keeps the pot for the same reason because <c>SolidTile2</c> has no platform
/// exclusion either.
///
/// Expectations come from driving the unmodified <c>WorldGen.WaterCheck</c> inside the pinned dedicated server
/// on the same synthetic ground. The measured cascade is that the lava-bearing platform cell dies, and the
/// <c>SquareTileFrame</c> inside <c>KillTile</c> then runs <c>CheckPot</c>, which finds one of the pot's two
/// supports gone and destroys all four of the pot's cells - while the OTHER platform cell, if it carries no
/// lava, survives. That footprint is the pot's two-by-two plus exactly one of the two cells beneath it, which
/// is why a single rectangle cannot describe it.
///
/// The probe also measured that the surviving platform is re-framed from 0 to 90, which the loading model does
/// NOT reproduce: it kills cells and frames nothing. That is a general gap in loading-time framing rather than
/// anything about pots, so these comparisons check the death set and the liquid, and deliberately do not check
/// the surviving platform's frame.
/// </remarks>
public sealed class LoadingLiquidPotOnPlatform1458Tests
{
    private const int Width = 400;
    private const int Height = 800;
    private const int Px = 200;
    private const int Py = 300;

    [Theory]
    // Lava under the pot's LEFT support: the pot and that platform die, the right platform survives.
    [InlineData("left", "0,-2=-/0w 1,-2=-/0w 0,-1=-/0w 1,-1=-/0w 0,0=-/255L 1,0=19/0w")]
    // Lava under the RIGHT support: the same, mirrored. The pot dies either way, because CheckPot tests both
    // of its supports and either one going is enough.
    [InlineData("right", "0,-2=-/0w 1,-2=-/0w 0,-1=-/0w 1,-1=-/0w 0,0=19/0w 1,0=-/255L")]
    // Lava under both: the first platform the scan reaches takes the pot with it, and the second dies on its
    // own pass with nothing left above it.
    [InlineData("both", "0,-2=-/0w 1,-2=-/0w 0,-1=-/0w 1,-1=-/0w 0,0=-/255L 1,0=-/255L")]
    // Water, not lava: nothing dies at all.
    [InlineData("dry", "0,-2=28/0w 1,-2=28/0w 0,-1=28/0w 1,-1=28/0w 0,0=19/255w 1,0=19/0w")]
    // A stone platform does not burn, so the lava changes nothing.
    [InlineData("stonePlatform", "0,-2=28/0w 1,-2=28/0w 0,-1=28/0w 1,-1=28/0w 0,0=19/255L 1,0=19/0w")]
    // The same lava with no pot above it: only the platform goes.
    [InlineData("noPot", "0,-2=-/0w 1,-2=-/0w 0,-1=-/0w 1,-1=-/0w 0,0=-/255L 1,0=19/0w")]
    public void Loading_lava_under_a_potted_platform_matches_official(string fixture, string expected)
    {
        WorldTileStore tiles = CreateStore(fixture);

        Assert.True(new VanillaWorldLiquidSimulator1458(tiles).WaterCheckLoading().IsApplied,
            $"{fixture}: loading water check refused the world");
        Assert.Equal(expected, Dump(tiles));
    }

    // The cells the probe dumps: the pot's footprint and the two platform cells under it, in reading order.
    // The frame is left out because the loading model frames nothing; see the remarks.
    private static string Dump(WorldTileStore tiles)
    {
        var sb = new StringBuilder();
        for (int y = Py - 2; y <= Py; y++)
        for (int x = Px; x <= Px + 1; x++)
        {
            WorldTile t = tiles.Get(x, y);
            sb.Append(x - Px).Append(',').Append(y - Py).Append('=')
              .Append(t.IsActive ? t.Type.ToString() : "-")
              .Append('/').Append(t.LiquidAmount)
              .Append(t.LiquidKind == WorldLiquidKind.Lava ? "L" : "w")
              .Append(' ');
        }

        return sb.ToString().TrimEnd();
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTileStore CreateStore(string fixture)
    {
        var tiles = new WorldTileStore(new WorldDimensions(Width, Height));

        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            // A sealed stone box with an open chamber around the test cells, so nothing drains away.
            bool open = x > Px - 6 && x < Px + 7 && y > Py - 8 && y < Py + 1;
            var tile = new WorldTile();
            if (open)
            {
                tile.FrameX = -1;
                tile.FrameY = -1;
            }
            else
            {
                tile.Flags = WorldTileFlags.Active;
                tile.Type = 1;
            }

            tiles.SetInitialPopulationTile(x, y, in tile);
        }

        // Two platform cells side by side. Frame row 13 is a stone platform, which does not burn.
        short platformFrameY = (short)(fixture == "stonePlatform" ? 18 * 13 : 0);
        for (int k = 0; k < 2; k++)
        {
            var platform = new WorldTile
            {
                Flags = WorldTileFlags.Active, Type = 19, FrameX = 0, FrameY = platformFrameY
            };
            tiles.SetInitialPopulationTile(Px + k, Py, in platform);
        }

        // The two-by-two pot standing on them, framed the way WorldGen.PlacePot writes it.
        if (fixture != "noPot")
        {
            for (int k = 0; k < 2; k++)
            for (int l = 0; l < 2; l++)
            {
                var pot = new WorldTile
                {
                    Flags = WorldTileFlags.Active, Type = 28,
                    FrameX = (short)(k * 18), FrameY = (short)(l * 18)
                };
                tiles.SetInitialPopulationTile(Px + k, Py - 2 + l, in pot);
            }
        }

        if (fixture is "left" or "both" or "stonePlatform" or "noPot")
            Wet(tiles, Px, Py, WorldLiquidKind.Lava);
        if (fixture is "right" or "both")
            Wet(tiles, Px + 1, Py, WorldLiquidKind.Lava);
        if (fixture == "dry")
            Wet(tiles, Px, Py, WorldLiquidKind.Water);

        return tiles;
    }

    private static void Wet(WorldTileStore tiles, int x, int y, WorldLiquidKind kind)
    {
        WorldTile tile = tiles.Get(x, y);
        tile.LiquidAmount = 255;
        tile.LiquidKind = kind;
        tiles.SetInitialPopulationTile(x, y, in tile);
    }
}
