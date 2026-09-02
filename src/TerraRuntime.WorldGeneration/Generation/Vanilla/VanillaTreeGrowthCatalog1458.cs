using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration;

/// <summary>
/// Source-backed tile and wall capabilities consumed by TerrariaServer 1.4.5.8 <c>WorldGen.GrowTree</c>.
/// Packed tables keep versioned content identities at the catalog boundary instead of scattering raw IDs through
/// the growth algorithm. The sets mirror <c>IsTileTypeFitForTree</c>, <c>TileID.Sets.CommonSapling</c>,
/// <c>EmptyTileCheck(..., 20)</c>, and <c>WallID.Sets.AllowsPlantsToGrow</c> in the pinned source.
/// </summary>
internal static class VanillaTreeGrowthCatalog1458
{
    private static ReadOnlySpan<ulong> TreeGroundWords =>
    [
        0x1000000000800004UL, 0x0000200000000040UL, 0x0000000000080000UL,
        0x0000000000000080UL, 0x0000000000000000UL, 0x0000000000000000UL,
        0x0000000000000000UL, 0x0000100020000000UL, 0x0000000000000000UL,
        0x0200000000000000UL, 0x0000000000600000UL, 0x0000000000000000UL
    ];

    private static ReadOnlySpan<ulong> CommonSaplingWords =>
    [
        0x0000000000100000UL, 0x0000000000000000UL, 0x0000000000000000UL,
        0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL,
        0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL,
        0x0000008000084000UL, 0x0000000000000000UL, 0x0000000000000000UL
    ];

    private static ReadOnlySpan<ulong> ReplaceableGrowthWords =>
    [
        0x6000000101100008UL, 0x00024000001C06A0UL, 0x0100000000000000UL,
        0x0000020000000200UL, 0x0000000000000000UL, 0x0000000100000000UL,
        0x0000000000000000UL, 0x0000002000000000UL, 0x0000000000060000UL,
        0x2000008000084000UL, 0x0000000000008000UL, 0x0000000000000000UL
    ];

    private static ReadOnlySpan<ulong> PlantGrowthWallWords =>
    [
        0x8000000000000001UL, 0x00000C000003047FUL, 0x0000000001423C00UL,
        0x0020000000000000UL, 0x2800000000001300UL, 0x0000000000000000UL
    ];

    public static bool IsTreeGround(TileTypeId type) => Test(type, TreeGroundWords, VanillaTileIds.Count);

    public static bool IsCommonSapling(TileTypeId type) => Test(type, CommonSaplingWords, VanillaTileIds.Count);

    public static bool IsReplaceableGrowthTile(TileTypeId type) =>
        Test(type, ReplaceableGrowthWords, VanillaTileIds.Count);

    public static bool AllowsPlantGrowth(WallTypeId type) =>
        Test(type.Value, PlantGrowthWallWords, VanillaWallIds.Count);

    private static bool Test(TileTypeId type, ReadOnlySpan<ulong> words, int count) =>
        Test(type.Value, words, count);

    private static bool Test(int value, ReadOnlySpan<ulong> words, int count) =>
        (uint)value < (uint)count && (words[value >> 6] & (1UL << (value & 63))) != 0;
}

internal enum VanillaTreeSegmentFeature1458 : byte
{
    Straight = 0,
    TrunkVariant1 = 1,
    TrunkVariant2 = 2,
    TrunkVariant3 = 3,
    TrunkVariant4 = 4,
    LeftBranch = 5,
    RightBranch = 6,
    BothBranches = 7,
    StraightVariant1 = 8,
    StraightVariant2 = 9
}

internal enum VanillaTreeRootShape1458 : byte
{
    Both = 0,
    Right = 1,
    Left = 2,
    None = 3
}

internal readonly record struct VanillaTreeFrame1458(short X, short Y);

/// <summary>
/// Tree tile atlas coordinates pinned to TerrariaServer 1.4.5.8 <c>WorldGen.GrowTree</c>. Callers select semantic
/// segment roles; raw sprite-sheet rows and columns remain owned by this framing catalog.
/// </summary>
internal static class VanillaTreeFrameCatalog1458
{
    private const int FrameStepPixels = 22;
    private const int TrunkAccentRow = 3;
    private const int RootRow = 6;
    private const int FoliageRow = 9;

    public static VanillaTreeFrame1458 Trunk(VanillaTreeSegmentFeature1458 feature, int variant) => feature switch
    {
        VanillaTreeSegmentFeature1458.TrunkVariant1 => Frame(column: 0, TrunkAccentRow + variant),
        VanillaTreeSegmentFeature1458.TrunkVariant2 => Frame(column: 1, variant),
        VanillaTreeSegmentFeature1458.TrunkVariant3 => Frame(column: 2, TrunkAccentRow + variant),
        VanillaTreeSegmentFeature1458.TrunkVariant4 => Frame(column: 1, TrunkAccentRow + variant),
        VanillaTreeSegmentFeature1458.LeftBranch => Frame(column: 4, variant),
        VanillaTreeSegmentFeature1458.RightBranch => Frame(column: 3, TrunkAccentRow + variant),
        VanillaTreeSegmentFeature1458.BothBranches => Frame(column: 5, TrunkAccentRow + variant),
        _ => Frame(column: 0, variant)
    };

    public static VanillaTreeFrame1458 LeftBranch(bool leafy, int variant) =>
        leafy ? Frame(column: 2, FoliageRow + variant) : Frame(column: 3, variant);

    public static VanillaTreeFrame1458 RightBranch(bool leafy, int variant) =>
        leafy ? Frame(column: 3, FoliageRow + variant) : Frame(column: 4, TrunkAccentRow + variant);

    public static VanillaTreeFrame1458 RightRoot(int variant) => Frame(column: 1, RootRow + variant);

    public static VanillaTreeFrame1458 LeftRoot(int variant) => Frame(column: 2, RootRow + variant);

    public static bool TryGetTrunkBase(
        VanillaTreeRootShape1458 shape,
        int variant,
        out VanillaTreeFrame1458 frame)
    {
        frame = shape switch
        {
            VanillaTreeRootShape1458.Both => Frame(column: 4, RootRow + variant),
            VanillaTreeRootShape1458.Right => Frame(column: 0, RootRow + variant),
            VanillaTreeRootShape1458.Left => Frame(column: 3, RootRow + variant),
            _ => default
        };
        return shape != VanillaTreeRootShape1458.None;
    }

    public static VanillaTreeFrame1458 Top(bool leafy, int variant) =>
        Frame(leafy ? 1 : 0, FoliageRow + variant);

    private static VanillaTreeFrame1458 Frame(int column, int row) =>
        new(checked((short)(column * FrameStepPixels)), checked((short)(row * FrameStepPixels)));
}
