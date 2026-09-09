using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Source-pinned TerrariaServer 1.4.5.8 facts used by <c>Liquid.tilesIgnoreWater(true)</c> during
/// <c>Liquid.QuickWater</c>. <c>WorldGen.SetBoulderSolidity(false)</c> covers 138, 484, 664 and 711..716;
/// Liquid additionally makes tile 546 non-solid. Bubble 379 is handled separately because QuickWater
/// explicitly forces it solid.
/// </summary>
internal static class VanillaLiquidQuickWaterFacts1458
{
    // Liquid.worldGenTilesIgnoreWater(true), distinct from the boulder override shared with loading.
    public static bool IgnoresSolidDuringWorldGenerationSettle(TileTypeId type) => type.Value is 10 or 190 or 191 or 192;

    private static ReadOnlySpan<ushort> IgnoredSolidTypes =>
    [
        138,
        484,
        546,
        664,
        711,
        712,
        713,
        714,
        715,
        716
    ];

    public static bool IgnoresSolidDuringSettle(TileTypeId type)
    {
        int value = type.Value;
        foreach (ushort candidate in IgnoredSolidTypes)
        {
            if (candidate == value)
                return true;
        }
        return false;
    }
}
