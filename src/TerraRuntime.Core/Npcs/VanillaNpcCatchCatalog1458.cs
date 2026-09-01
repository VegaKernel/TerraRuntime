using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

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

    public static bool IsMysticFrog(NpcTypeId type) => type.Value == 687;
}

/// <summary>
/// Item.NewItem state used by NPC.CatchNPC. DefaultToCapturedCritter fixes all captured-critter item hitboxes at
/// 12x12; Item.NewItem is called with a zero-size source rectangle at player center, then ordinary gravity velocity.
/// </summary>
public static class VanillaNpcCatchWorldItem1458
{
    private const float CapturedItemHalfSize = 6f;
    private const float VelocityScale = 0.1f;
    public const int ReservationTicks = 100;

    public static WorldItemDropStateUpdate Create(
        float playerCenterX,
        float playerCenterY,
        ItemTypeId itemType,
        IWorldItemSpawnRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (itemType.IsNone)
            throw new ArgumentException("Catch item type must be assigned.", nameof(itemType));

        return new WorldItemDropStateUpdate(
            PositionX: playerCenterX - CapturedItemHalfSize,
            PositionY: playerCenterY - CapturedItemHalfSize,
            VelocityX: random.NextInt32(-30, 31) * VelocityScale,
            VelocityY: random.NextInt32(-40, -15) * VelocityScale,
            Stack: 1,
            Prefix: VanillaPrefixIds.NoneValue,
            Ownership: WorldItemOwnershipMode.ReserveForLocalPlayer,
            ItemNetId: checked((short)itemType.Value),
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: ReservationTicks);
    }
}
