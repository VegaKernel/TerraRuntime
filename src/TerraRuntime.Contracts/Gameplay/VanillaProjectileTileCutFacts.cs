namespace TerraRuntime.Contracts.Gameplay;

/// <summary>
/// Version-pinned TerrariaServer 1.4.5.8 tile-cut catalog used by ordinary projectile <c>CutTilesAt</c>.
/// The source assembly SHA-256 is d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e.
/// <c>TileID.Sets.TileCutIgnore.None</c> is all-false in this version, so the server-owned default-player
/// path has no additional ignore mask while <c>dontHurtNature</c> remains at its vanilla default false value.
/// </summary>
public static class VanillaProjectileTileCutFacts
{
    private static readonly ushort[] CuttableTileTypes =
    [
        3, 24, 28, 32, 51, 52, 61, 62, 69, 71, 73, 74, 82, 83, 84, 110, 113, 115, 184, 201,
        205, 231, 236, 254, 352, 382, 444, 454, 484, 485, 518, 519, 528, 529, 549, 636, 637, 638,
        654, 655, 711
    ];

    public const int CuttableTileTypeCount = 41;

    public static bool IsCuttable(TileTypeId type)
    {
        if ((uint)type.Value > ushort.MaxValue)
            return false;

        return Array.BinarySearch(CuttableTileTypes, checked((ushort)type.Value)) >= 0;
    }
}
