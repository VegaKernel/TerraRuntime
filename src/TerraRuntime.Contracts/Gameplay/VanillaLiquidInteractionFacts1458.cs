namespace TerraRuntime.Contracts.Gameplay;

/// <summary>
/// Source-pinned TerrariaServer 1.4.5.8 facts consumed by <c>Liquid.LiquidCheck</c> and
/// <c>Liquid.CreateLiquidMergeTile</c>. The obsidian-kill set is the final runtime table after the
/// <c>tileLavaDeath</c> copy, explicit overrides, echo-furniture additions and tile 324 update in
/// <c>Main.Initialize_TileAndNPCData*</c>. The source assembly SHA-256 is
/// d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e.
/// </summary>
public static class VanillaLiquidInteractionFacts1458
{
    private const ushort LihzahrdBrickUnsafeWallType = 350;

    private static readonly ushort[] PreventsTileReplaceIfOnTopOfIt =
    [
        5, 72, 323, 583, 584, 585, 586, 587, 588, 589, 596, 616, 634
    ];

    private static readonly ushort[] ReplaceTileBreakUp =
    [
        3, 20, 24, 27, 61, 71, 73, 74, 82, 83, 84, 110, 113, 185, 186, 187, 201, 227, 233, 236,
        238, 254, 484, 485, 529, 530, 549, 590, 595, 615, 624, 637, 700, 702, 703, 705
    ];

    private static readonly ushort[] ReplaceTileBreakDown =
    [
        52, 62, 115, 205, 382, 444, 528, 636, 638
    ];

    private static readonly ushort[] ObsidianKillTileTypes =
    [
        3, 5, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 24, 27, 28, 29, 32, 33,
        34, 35, 36, 42, 49, 50, 51, 52, 55, 61, 62, 69, 71, 72, 73, 74, 77, 78,
        79, 80, 81, 82, 83, 84, 85, 86, 87, 89, 90, 91, 92, 93, 94, 95, 96, 97,
        98, 100, 101, 102, 103, 104, 105, 106, 110, 113, 115, 125, 126, 128, 129, 132, 133, 134,
        135, 136, 139, 149, 165, 172, 173, 174, 178, 184, 185, 186, 187, 201, 205, 209, 210, 212,
        213, 215, 216, 217, 218, 219, 220, 227, 228, 231, 233, 236, 238, 240, 241, 242, 243, 244,
        245, 246, 247, 254, 269, 270, 271, 275, 276, 277, 278, 279, 280, 281, 282, 283, 285, 286,
        287, 288, 289, 290, 291, 292, 293, 294, 295, 296, 297, 298, 299, 300, 301, 302, 303, 304,
        305, 306, 307, 308, 309, 310, 314, 316, 317, 318, 319, 323, 324, 335, 337, 338, 339, 349,
        352, 353, 354, 355, 382, 413, 425, 453, 456, 463, 464, 465, 469, 484, 485, 486, 487, 488,
        489, 490, 493, 497, 499, 506, 510, 511, 528, 529, 530, 532, 533, 538, 544, 546, 547, 548,
        550, 551, 552, 553, 554, 555, 556, 558, 559, 560, 564, 565, 567, 568, 569, 570, 571, 572,
        573, 579, 580, 581, 582, 591, 599, 600, 601, 602, 603, 604, 605, 606, 607, 608, 609, 610,
        611, 612, 619, 620, 621, 622, 623, 624, 629, 630, 631, 632, 636, 640, 642, 643, 644, 645,
        647, 648, 649, 650, 651, 652, 654, 655, 656, 660, 693, 694, 697, 698, 699, 700, 701, 702,
        703, 704, 705, 706, 707, 710
    ];

    // TileID.Sets.IsAContainer = Factory.CreateBoolSet(21, 467, 88).
    public static bool IsContainer(TileTypeId type) =>
        type == VanillaTileIds.Containers ||
        type == VanillaTileIds.Containers2 ||
        type == VanillaTileIds.Dressers;

    public static bool IsObsidianKill(TileTypeId type)
    {
        if ((uint)type.Value > ushort.MaxValue)
            return false;

        return Array.BinarySearch(ObsidianKillTileTypes, checked((ushort)type.Value)) >= 0;
    }

    public static bool PreventsReplacementWhenOnTop(TileTypeId type) =>
        Contains(PreventsTileReplaceIfOnTopOfIt, type);

    public static bool BreaksWhenSupportIsReplacedAbove(TileTypeId type) =>
        Contains(ReplaceTileBreakUp, type);

    public static bool BreaksWhenSupportIsReplacedBelow(TileTypeId type) =>
        Contains(ReplaceTileBreakDown, type);

    public static bool IsLiquidMergeReplacementBlockedByWall(WallTypeId type) =>
        type.Value == LihzahrdBrickUnsafeWallType;

    private static bool Contains(ushort[] sortedTypes, TileTypeId type)
    {
        if ((uint)type.Value > ushort.MaxValue)
            return false;

        return Array.BinarySearch(sortedTypes, checked((ushort)type.Value)) >= 0;
    }

    public const int ObsidianKillTileTypeCount = 276;
}

/// <summary>
/// Terraria.ID.TileChangeType values written in packet 20 by TerrariaServer 1.4.5.8 liquid material merges.
/// Values are wire identities and therefore remain version-pinned.
/// </summary>
public enum VanillaTileChangeType1458 : byte
{
    None = 0,
    LavaWater = 1,
    HoneyWater = 2,
    HoneyLava = 3,
    ShimmerWater = 4,
    ShimmerLava = 5,
    ShimmerHoney = 6
}
