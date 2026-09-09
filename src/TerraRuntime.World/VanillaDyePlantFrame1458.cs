namespace TerraRuntime.World;

/// <summary>WorldGen.PlaceDye / TileObjectData.StyleDye, TerrariaServer 1.4.5.8.
/// One occupied cell uses a 32+2 pixel horizontal atlas stride, not the ordinary 16+2 tile stride.</summary>
public static class VanillaDyePlantFrame1458
{
    public const int Stride = 34;
    public const int SupportedStyleCount = 12;

    public static bool IsSupported(short frameX, short frameY) =>
        frameY == 0 && frameX >= 0 && frameX < Stride * SupportedStyleCount && frameX % Stride == 0;

    public static short ForStyle(int style)
    {
        if ((uint)style >= SupportedStyleCount) throw new ArgumentOutOfRangeException(nameof(style));
        return checked((short)(style * Stride));
    }
}
