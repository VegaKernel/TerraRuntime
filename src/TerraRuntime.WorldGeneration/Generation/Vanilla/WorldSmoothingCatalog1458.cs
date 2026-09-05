using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Version-pinned capability owner for the TerrariaServer 1.4.5.8 Smooth World pass. Raw identities stay in this
/// catalog instead of leaking into topology decisions in <see cref="WorldSmoother1458"/>.
/// </summary>
internal static class WorldSmoothingCatalog1458
{
    private const short TreeLeftDetachedFrameX = 66;
    private const short TreeRightDetachedFrameX = 88;
    private const short TreeLeftDetachedFrameYMaximum = 44;
    private const short TreeRightDetachedFrameYMinimum = 66;
    private const short TreeRightDetachedFrameYMaximum = 110;
    private const short TreeSupportFrameYExclusive = 198;
    private const int CactusFrameWidth = 18;

    private static ReadOnlySpan<ushort> CannotClearDuringGeneration =>
    [396, 400, 401, 397, 398, 399, 404, 368, 367, 41, 43, 44, 481, 482, 483, 226, 237];

    private static ReadOnlySpan<ushort> PreventsGenerationSlopes => [48, 137, 232, 191, 151, 274, 135, 442, 428];

    private static ReadOnlySpan<ushort> CannotBePounded =>
    [10, 30, 48, 137, 190, 232, 380, 387, 388, 476, 484, 138, 664, 665, 711, 712, 713, 714, 715, 716];

    private static ReadOnlySpan<ushort> ForbidsSlopeBelow =>
    [21, 26, 77, 88, 235, 237, 441, 467, 468, 470, 475, 488, 597];

    private static ReadOnlySpan<ushort> SandConversionFamily => [53, 112, 116, 234];

    private static ReadOnlySpan<ushort> SecondPhaseExclusions => [137, 48, 232, 191, 151, 274, 75, 76];

    private static ReadOnlySpan<ushort> UnsupportedGapNeighbors => [190, 48, 232];

    private static ReadOnlySpan<ushort> TreeTrunks => [5, 72, 583, 584, 585, 586, 587, 588, 589, 596, 616, 634];

    private static ReadOnlySpan<ushort> ProtectsDifferentSupport => [21, 26, 72, 77, 88, 467, 488];

    public static bool CanBeClearedDuringGeneration(TileTypeId type) =>
        IsDefined(type) && !Contains(CannotClearDuringGeneration, type);

    public static bool PreventsSlopesDuringGeneration(TileTypeId type) =>
        Contains(PreventsGenerationSlopes, type);

    public static bool CanBePounded(TileTypeId type) => IsDefined(type) && !Contains(CannotBePounded, type);

    public static bool ForbidsSlopingBelow(TileTypeId type) => Contains(ForbidsSlopeBelow, type);

    public static bool IsSandConversion(TileTypeId type) => Contains(SandConversionFamily, type);

    public static bool IsSecondPhaseCandidate(TileTypeId type) => !Contains(SecondPhaseExclusions, type);

    public static bool SupportsGapFillNeighbor(TileTypeId type) => !Contains(UnsupportedGapNeighbors, type);

    public static bool SupportsGapFillBase(TileTypeId type) => type.Value is not (151 or 274);

    public static bool UsesNeighborIdentityForGapFill(TileTypeId type) => type.Value == 495;

    public static bool IsPressurePlateWire(TileTypeId type) => type.Value == 136;

    public static bool IsTrap(TileTypeId type) => type.Value == 137;

    public static bool IsTemporarilySolidCrackedBrick(TileTypeId type) => type.Value is 481 or 482 or 483;

    public static bool CanRemoveTileBelow(in WorldTile above, TileTypeId belowType)
    {
        if (!above.IsActive)
            return true;

        TileTypeId aboveType = above.TileType;
        if (Contains(TreeTrunks, aboveType) && aboveType != belowType)
        {
            bool detachedLeftRoot =
                above.FrameX == TreeLeftDetachedFrameX && above.FrameY is >= 0 and <= TreeLeftDetachedFrameYMaximum;
            bool detachedRightRoot =
                above.FrameX == TreeRightDetachedFrameX &&
                above.FrameY is >= TreeRightDetachedFrameYMinimum and <= TreeRightDetachedFrameYMaximum;
            if (!detachedLeftRoot && !detachedRightRoot && above.FrameY < TreeSupportFrameYExclusive)
                return false;
        }

        if (aboveType.Value == 323 && aboveType != belowType && above.FrameX is 66 or 220)
            return false;
        if (Contains(ProtectsDifferentSupport, aboveType) && aboveType != belowType)
            return false;
        if (aboveType.Value == 80 && aboveType != belowType)
        {
            int cactusFrame = above.FrameX / CactusFrameWidth;
            if ((uint)cactusFrame <= 1u || (uint)(cactusFrame - 4) <= 1u)
                return false;
        }

        return true;
    }

    private static bool IsDefined(TileTypeId type) => (uint)type.Value < (uint)VanillaTileIds.Count;

    private static bool Contains(ReadOnlySpan<ushort> values, TileTypeId type) =>
        values.Contains(checked((ushort)type.Value));
}
