using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    // Source1.4.5.8 CheckVines runs before the generation cosmetic-frame barrier.
    private void FrameVine(int i, int j)
    {
        WorldTile above = At(i, j - 1);
        int support = above.IsActive && !above.IsActuated && above.SourceSlope is not (3 or 4)
            ? above.Type : -1;
        ushort type = At(i, j).Type;
        if (support == type) return;
        ushort replacement = support switch
        {
            60 or 226 or 62 => 62,
            109 or 115 or 492 => 115,
            23 or 636 or 661 => 636,
            199 or 205 or 662 => 205,
            2 or 52 or 477 when type != 382 => 52,
            382 => 382,
            70 or 528 => 528,
            633 or 638 => 638,
            _ => 0
        };
        if (replacement != 0 && replacement != type)
        {
            At(i, j).Type = replacement;
            SquareTileFrame(i, j);
            return;
        }
        // Leaf384 and living leaf192 are direct supports without a conversion family.
        bool supported = type switch
        {
            52 or 382 => support is 2 or 477 or 192,
            62 => support is 60 or 384 or 226,
            115 => support is 109 or 492,
            528 => support == 70,
            636 => support is 23 or 661,
            205 => support is 199 or 662,
            638 => support == 633,
            _ => false
        };
        if (!supported) KillTile(i, j);
    }
}
