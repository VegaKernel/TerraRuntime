using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration;

/// <summary>
/// Version-pinned TerrariaServer 1.4.5.8 tile capabilities used by the ordinary WorldGen.TileRunner path.
/// These facts are kept separate from the runner so source-derived content identities do not leak into geometry code.
/// </summary>
internal static class VanillaTileRunnerGenerationCatalog1458
{
    private static ReadOnlySpan<ushort> CuttableTiles =>
    [
        3, 24, 28, 32, 51, 52, 61, 62, 69, 71, 73, 74, 82, 83, 84, 110, 113, 115, 184, 201, 205,
        231, 236, 254, 352, 382, 444, 454, 484, 485, 518, 519, 528, 529, 549, 636, 637, 638, 654, 655, 711
    ];

    private static ReadOnlySpan<ushort> StoneTargets => [63, 64, 65, 66, 67, 68, 130, 131, 566];

    private static ReadOnlySpan<ushort> NonSolidSlopeSavingTiles => [131, 351, 336, 340, 342, 341, 343, 344];

    private static ReadOnlySpan<ushort> OreTiles =>
    [7, 166, 6, 167, 9, 168, 8, 169, 22, 204, 37, 58, 107, 221, 108, 222, 111, 223, 211];

    public static bool IsProtectedFrameImportant(ushort type) =>
        VanillaWorldFrameImportance326.IsFrameImportant(type) && !CuttableTiles.Contains(type);

    public static bool IsStoneTarget(int type) =>
        type >= 0 && type <= ushort.MaxValue && StoneTargets.Contains((ushort)type);

    public static bool IsOreTarget(int type) =>
        type >= 0 && type <= ushort.MaxValue && OreTiles.Contains((ushort)type);

    public static bool CanBeClearedDuringGeneration(ushort type) =>
        VanillaWorldSmoothingCatalog1458.CanBeClearedDuringGeneration(new TileTypeId(type));

    public static bool SavesSlopes(int type)
    {
        if (type < 0 || type >= VanillaTileIds.Count)
            return false;

        var id = new TileTypeId(type);
        return VanillaTileCollisionCatalog.IsSolid(id) || NonSolidSlopeSavingTiles.Contains((ushort)type);
    }
}
