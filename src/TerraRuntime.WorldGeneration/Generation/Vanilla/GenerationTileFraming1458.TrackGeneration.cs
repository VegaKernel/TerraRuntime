using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    internal void PlaceSourcePressureTrack(int x, int y)
    {
        // Minecart.Initialize's existing source frame-kind table starts pressure frames at20.
        At(x, y) = new WorldTile { Type = 314, Flags = WorldTileFlags.Active, FrameX = 20, FrameY = -1 };
        SquareTileFrame(x, y);
    }
}
