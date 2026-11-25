using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class RuntimeWiring1458Tests
{
    [Theory]
    [InlineData(1, "0101010101010101")]
    [InlineData(2, "0011001100110011")]
    [InlineData(3, "0110011001100110")]
    [InlineData(4, "0000111100001111")]
    [InlineData(5, "0101101001011010")]
    [InlineData(6, "0011110000111100")]
    [InlineData(7, "0110100101101001")]
    [InlineData(8, "0000000011111111")]
    [InlineData(9, "0101010110101010")]
    [InlineData(10, "0011001111001100")]
    [InlineData(11, "0110011010011001")]
    [InlineData(12, "0000111111110000")]
    [InlineData(13, "0101101010100101")]
    [InlineData(14, "0011110011000011")]
    [InlineData(15, "0110100110010110")]
    public void Packet59_torch_frames_match_both_originals_for_every_wire_mask(int sourceMask, string originalFrames)
    {
        for (int targetMask = 0; targetMask < originalFrames.Length; targetMask++)
        {
            using var scene = new Scene();
            scene.Tiles.Set(20, 20, new WorldTile { Type = 136, Flags = TileFlagsForWireMask(sourceMask) });
            scene.Tiles.Set(21, 20, new WorldTile { Type = 4, Flags = TileFlagsForWireMask(targetMask) });

            scene.Press(20, 20);

            int expectedFrame = originalFrames[targetMask] == '1' ? 66 : 0;
            Assert.Equal((sourceMask, targetMask, expectedFrame), (sourceMask, targetMask, (int)scene.Tiles.Get(21, 20).FrameX));
            Assert.Equal(1, scene.State.AppliedClientTileManipulations);
        }
    }

    private static WorldTileFlags TileFlagsForWireMask(int mask) => WorldTileFlags.Active
        | ((mask & 1) != 0 ? WorldTileFlags.WireRed : WorldTileFlags.None)
        | ((mask & 2) != 0 ? WorldTileFlags.WireBlue : WorldTileFlags.None)
        | ((mask & 4) != 0 ? WorldTileFlags.WireGreen : WorldTileFlags.None)
        | ((mask & 8) != 0 ? WorldTileFlags.WireYellow : WorldTileFlags.None);
}
