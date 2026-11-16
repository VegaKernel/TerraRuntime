using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>
/// Source-pinned TerrariaServer 1.4.5.8 NPCID.Sets.CountsAsCritter plus NPC.SetDefaults catchItem mappings.
/// CountsAsCritter and catchItem are intentionally separate: vanilla has critters with no catch item and catchable
/// entities such as Truffle Worm that are not in CountsAsCritter.
/// </summary>
public static class VanillaNpcCatchCatalog1458
{
    private static readonly HashSet<int> Critters =
    [
        46, 303, 337, 540, 443, 74, 297, 298, 442, 611, 689, 377, 446, 612, 613, 356, 444,
        595, 596, 597, 598, 599, 600, 601, 604, 605, 357, 448, 374, 484, 355, 358, 606, 359, 360,
        485, 486, 487, 148, 149, 55, 230, 592, 593, 299, 538, 539, 300, 447, 361, 445, 362, 363,
        364, 365, 367, 366, 583, 584, 585, 602, 603, 607, 608, 609, 610, 616, 617, 625, 626, 627,
        615, 639, 640, 641, 642, 643, 644, 645, 646, 647, 648, 649, 650, 651, 652, 653, 654, 655,
        661, 669, 671, 672, 673, 674, 675, 677, 687, 688
    ];

    // Main.Initialize_TileAndNPCData1; independent of CountsAsCritter and catchItem.
    public static bool HasCatchProtection(NpcTypeId type) => type.Value is
        46 or 55 or 74 or 148 or 149 or 297 or 298 or 299 or 300 or 355 or 356 or 357 or 358 or 359 or
        360 or 361 or 362 or 363 or 364 or 365 or 366 or 367 or 374 or 377 or 442 or 443 or 444 or 445 or
        446 or 447 or 448 or 484 or 485 or 486 or 487 or 538 or 539 or 583 or 584 or 585 or 592 or 593 or
        595 or 596 or 597 or 598 or 599 or 600 or 601 or 602 or 603 or 604 or 605 or 606 or 607 or 608 or
        609 or 610 or 611 or 612 or 613 or 614 or 616 or 617 or 626 or 627 or 639 or 640 or 641 or 642 or
        643 or 644 or 645 or 646 or 647 or 648 or 649 or 650 or 651 or 652 or 653 or 654 or 655 or 661 or
        669 or 671 or 672 or 673 or 674 or 675 or 677 or 688;

    public static bool CountsAsCritter(NpcTypeId type) => type.IsAssigned && Critters.Contains(type.Value);

    public static bool TryGetCatchItem(NpcTypeId type, out ItemTypeId itemType)
    {
        int item = type.Value switch
        {
            46 or 303 or 337 or 540 => 2019,
            55 or 230 => 261,
            74 => 2015,
            297 => 2016,
            298 => 2017,
            148 or 149 => 2205,
            299 => 2018,
            300 => 2003,
            355 => 1992,
            356 => 1994,
            357 => 2002,
            358 => 2004,
            359 => 2006,
            360 => 2007,
            361 => 2121,
            362 or 363 => 2122,
            364 or 365 => 2123,
            366 => 2156,
            367 => 2157,
            374 or 375 => 2673,
            377 => 2740,
            442 => 2889,
            443 => 2890,
            444 => 2891,
            445 => 2892,
            446 => 2893,
            447 => 2894,
            448 => 2895,
            >= 484 and <= 487 => 3191 + type.Value - 484,
            538 => 3563,
            539 => 3564,
            583 => 4068,
            584 => 4069,
            585 => 4070,
            592 or 593 => 4274,
            >= 595 and <= 601 => 4334 + type.Value - 595,
            602 or 603 => 4359,
            604 => 4361,
            605 => 4362,
            606 => 4363,
            607 => 4373,
            608 or 609 => 4374,
            610 => 4375,
            611 => 4395,
            612 => 4418,
            613 => 4419,
            614 => 1338,
            616 => 4464,
            617 => 4465,
            626 => 4480,
            627 => 4482,
            >= 639 and <= 645 => 4831 + type.Value - 639,
            >= 646 and <= 652 => 4838 + type.Value - 646,
            653 => 4845,
            654 => 4847,
            655 => 4849,
            661 => 4961,
            669 => 5132,
            671 => 5212,
            672 => 5300,
            673 => 5311,
            674 => 5312,
            675 => 5313,
            677 => 5350,
            687 => 2121,
            688 => 5511,
            _ => 0
        };

        if (item <= 0 || !VanillaItemIds.TryCreate(item, out itemType) || itemType.IsNone)
        {
            itemType = default;
            return false;
        }
        return true;
    }

    public static bool IsMysticFrog(NpcTypeId type) => type == VanillaNpcIds.MysticFrog;
}
