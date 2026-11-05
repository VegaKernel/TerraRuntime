namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    // WorldGen.FinishRemixWorld calls CheckLilyPad directly, not TileFrame.
    // Preserve that distinction so the final x/y scan can revisit a pad
    // that moves downward into its next row.
    internal void CheckLilyPadForRemix(int x, int y) => FrameLilyPad(x, y);
}
