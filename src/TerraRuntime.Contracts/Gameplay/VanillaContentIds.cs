namespace TerraRuntime.Contracts.Gameplay;

/// <summary>
/// Initial source-verified TerrariaServer 1.4.5.8 NPC content ids used by TerraRuntime gameplay.
/// Extend this catalog only from the repository source-of-truth hierarchy; do not guess ids.
/// </summary>
public static class VanillaNpcIds
{
    public static readonly NpcTypeId BlueSlime = new(1);
    public static readonly NpcTypeId DemonEye = new(2);
    public static readonly NpcTypeId Zombie = new(3);
    public static readonly NpcTypeId EyeOfCthulhu = new(4);
    public static readonly NpcTypeId ServantOfCthulhu = new(5);
    public static readonly NpcTypeId EaterOfSouls = new(6);
    public static readonly NpcTypeId MotherSlime = new(16);
    public static readonly NpcTypeId Skeleton = new(21);
    public static readonly NpcTypeId MeteorHead = new(23);
    public static readonly NpcTypeId Hornet = new(42);
    public static readonly NpcTypeId KingSlime = new(50);
    public static readonly NpcTypeId LavaSlime = new(59);
    public static readonly NpcTypeId DungeonSlime = new(71);
    public static readonly NpcTypeId CorruptSlime = new(81);
    public static readonly NpcTypeId Corruptor = new(94);
    public static readonly NpcTypeId TheHungryII = new(116);
    public static readonly NpcTypeId WanderingEye = new(133);
    public static readonly NpcTypeId Probe = new(139);
    public static readonly NpcTypeId IlluminantSlime = new(138);
    public static readonly NpcTypeId ToxicSludge = new(141);
    public static readonly NpcTypeId IceSlime = new(147);
    public static readonly NpcTypeId Crimslime = new(183);
    public static readonly NpcTypeId SpikedIceSlime = new(184);
    public static readonly NpcTypeId CataractEye = new(190);
    public static readonly NpcTypeId SleepyEye = new(191);
    public static readonly NpcTypeId DilatedEye = new(192);
    public static readonly NpcTypeId GreenEye = new(193);
    public static readonly NpcTypeId PurpleEye = new(194);
    public static readonly NpcTypeId SpikedJungleSlime = new(204);
    public static readonly NpcTypeId UmbrellaSlime = new(225);
    public static readonly NpcTypeId RainbowSlime = new(244);
    public static readonly NpcTypeId SlimeMasked = new(302);
    public static readonly NpcTypeId DemonEyeOwl = new(317);
    public static readonly NpcTypeId DemonEyeSpaceship = new(318);
    public static readonly NpcTypeId SlimeRibbonWhite = new(333);
    public static readonly NpcTypeId SlimeRibbonYellow = new(334);
    public static readonly NpcTypeId SlimeRibbonGreen = new(335);
    public static readonly NpcTypeId SlimeRibbonRed = new(336);
    public static readonly NpcTypeId SpikedSlime = new(535);
    public static readonly NpcTypeId SandSlime = new(537);
    public static readonly NpcTypeId QueenSlimeMinionBlue = new(658);
    public static readonly NpcTypeId QueenSlimeMinionPink = new(659);
    public static readonly NpcTypeId GoldenSlime = new(667);
    public static readonly NpcTypeId ShimmerSlime = new(676);
    public static readonly NpcTypeId PigronCorruption = new(170);
    public static readonly NpcTypeId PigronHallow = new(171);
    public static readonly NpcTypeId Crimera = new(173);
    public static readonly NpcTypeId MossHornet = new(176);
    public static readonly NpcTypeId PigronCrimson = new(180);
    public static readonly NpcTypeId Moth = new(205);
    public static readonly NpcTypeId Bee = new(210);
    public static readonly NpcTypeId SmallBee = new(211);
    public static readonly NpcTypeId FattyHornet = new(231);
    public static readonly NpcTypeId HoneyHornet = new(232);
    public static readonly NpcTypeId LeafyHornet = new(233);
    public static readonly NpcTypeId SpikeyHornet = new(234);
    public static readonly NpcTypeId StingyHornet = new(235);
    public static readonly NpcTypeId Parrot = new(252);
    public static readonly NpcTypeId BloodSquid = new(619);
}

/// <summary>
/// Initial source-verified TerrariaServer 1.4.5.8 NPC AI-style ids corresponding to the supported
/// lifecycle/AI slice. These are behavior families, not NPC content ids.
/// </summary>
public static class VanillaNpcAiStyles
{
    public static readonly NpcAiStyleId Slime = new(1);
    public static readonly NpcAiStyleId DemonEye = new(2);
    public static readonly NpcAiStyleId Fighter = new(3);
    public static readonly NpcAiStyleId EyeOfCthulhu = new(4);
    public static readonly NpcAiStyleId Flyer = new(5);
    public static readonly NpcAiStyleId KingSlime = new(15);
}

/// <summary>
/// TerrariaServer 1.4.5.8 item content bounds and named identities currently consumed by gameplay.
/// The count is pinned to ItemID.Count as independently exercised by packet-5 Item.netDefaults normalization tests.
/// Named ids are added only after source verification against the pinned TerrariaServer binary.
/// </summary>
public static class VanillaItemIds
{
    public const int Count = 6196;

    public static ItemTypeId None => default;
    public static readonly ItemTypeId DirtBlock = new(2);
    public static readonly ItemTypeId Gel = new(23);
    public static readonly ItemTypeId Chest = new(48);
    public static readonly ItemTypeId SlimeStaff = new(1309);
    public static readonly ItemTypeId CopperPickaxe = new(3509);

    public static bool TryCreate(int rawType, out ItemTypeId type)
    {
        if ((uint)rawType >= (uint)Count)
        {
            type = default;
            return false;
        }

        type = new ItemTypeId(rawType);
        return true;
    }
}

/// <summary>
/// TerrariaServer 1.4.5.8 projectile content bounds and the initial named identities used by runtime tests.
/// IDs 1111..1135 were the final additions in the 1.4.5.7 content update, leaving ProjectileID.Count at
/// 1136 for 1.4.5.8. Zero is ProjectileID.None. A handful of in-range legacy gaps also fall through
/// Projectile.SetDefaults and therefore are not live authoritative projectile types.
/// </summary>
public static class VanillaProjectileIds
{
    public const int Count = 1136;

    public static ProjectileTypeId None => default;
    public static readonly ProjectileTypeId WoodenArrowFriendly = new(1);
    public static readonly ProjectileTypeId FireArrow = new(2);
    public static readonly ProjectileTypeId Shuriken = new(3);
    public static readonly ProjectileTypeId UnholyArrow = new(4);
    public static readonly ProjectileTypeId JestersArrow = new(5);
    public static readonly ProjectileTypeId EnchantedBoomerang = new(6);
    public static readonly ProjectileTypeId Bullet = new(14);
    public static readonly ProjectileTypeId GreenLaser = new(20);
    public static readonly ProjectileTypeId Bone = new(21);
    public static readonly ProjectileTypeId ThrowingKnife = new(48);
    public static readonly ProjectileTypeId Seed = new(51);
    public static readonly ProjectileTypeId PoisonedKnife = new(54);
    public static readonly ProjectileTypeId ConfettiGun = new(178);
    public static readonly ProjectileTypeId ConfettiMelee = new(289);
    public static readonly ProjectileTypeId RottenEgg = new(318);
    public static readonly ProjectileTypeId StarAnise = new(330);
    public static readonly ProjectileTypeId BoneArrowFromMerchant = new(474);
    public static readonly ProjectileTypeId NurseSyringeHurt = new(583);
    public static readonly ProjectileTypeId SantaBombs = new(589);
    public static readonly ProjectileTypeId BoneDagger = new(599);
    public static readonly ProjectileTypeId Waffle = new(1012);
    public static readonly ProjectileTypeId SoundGun = new(1099);
    public static readonly ProjectileTypeId MeleeBone = new(1111);
    public static readonly ProjectileTypeId BoneShard = new(1124);

    public static bool TryCreate(int rawType, out ProjectileTypeId type)
    {
        if ((uint)rawType >= (uint)Count)
        {
            type = default;
            return false;
        }

        type = new ProjectileTypeId(rawType);
        return true;
    }

    public static bool IsLiveWireType(ProjectileTypeId type) =>
        VanillaProjectileLifecycleFacts.IsDefinedLiveType(type);
}

/// <summary>
/// Source-verified TerrariaServer 1.4.5.8 tile identities currently consumed by world/gameplay code.
/// The catalog intentionally grows with implemented behavior instead of copying an unverified giant ID table.
/// Behavior-family predicates live here so consumers do not duplicate raw-id membership sets.
/// </summary>
public static class VanillaTileIds
{
    public const int Count = 754;

    public static readonly TileTypeId Dirt = new(0);
    public static readonly TileTypeId Stone = new(1);
    public static readonly TileTypeId Grass = new(2);
    public static readonly TileTypeId ClosedDoor = new(10);
    public static readonly TileTypeId OpenDoor = new(11);
    public static readonly TileTypeId Platforms = new(19);
    public static readonly TileTypeId Containers = new(21);
    public static readonly TileTypeId CorruptGrass = new(23);
    public static readonly TileTypeId Ebonstone = new(25);
    public static readonly TileTypeId DemonAltar = new(26);
    public static readonly TileTypeId Sunflower = new(27);
    public static readonly TileTypeId Cobweb = new(51);
    public static readonly TileTypeId Sand = new(53);
    public static readonly TileTypeId Signs = new(55);
    public static readonly TileTypeId Mud = new(59);
    public static readonly TileTypeId JungleGrass = new(60);
    public static readonly TileTypeId MushroomGrass = new(70);
    public static readonly TileTypeId Hellforge = new(77);
    public static readonly TileTypeId Tombstones = new(85);
    public static readonly TileTypeId Dressers = new(88);
    public static readonly TileTypeId SnowBlock = new(147);
    public static readonly TileTypeId IceBlock = new(161);
    public static readonly TileTypeId CrimsonGrass = new(199);
    public static readonly TileTypeId Crimstone = new(203);
    public static readonly TileTypeId Hive = new(225);
    public static readonly TileTypeId LihzahrdBrick = new(226);
    public static readonly TileTypeId LihzahrdAltar = new(237);
    public static readonly TileTypeId Marble = new(367);
    public static readonly TileTypeId Granite = new(368);
    public static readonly TileTypeId TargetDummy = new(378);
    public static readonly TileTypeId TallGateClosed = new(388);
    public static readonly TileTypeId TallGateOpen = new(389);
    public static readonly TileTypeId ItemFrame = new(395);
    public static readonly TileTypeId AnnouncementBox = new(425);
    public static readonly TileTypeId TeamBlockRedPlatform = new(427);
    public static readonly TileTypeId TeamBlockGreenPlatform = new(435);
    public static readonly TileTypeId TeamBlockBluePlatform = new(436);
    public static readonly TileTypeId TeamBlockYellowPlatform = new(437);
    public static readonly TileTypeId TeamBlockPinkPlatform = new(438);
    public static readonly TileTypeId TeamBlockWhitePlatform = new(439);
    public static readonly TileTypeId Containers2 = new(467);
    public static readonly TileTypeId DisplayDoll = new(470);
    public static readonly TileTypeId WeaponsRack2 = new(471);
    public static readonly TileTypeId HatRack = new(475);
    public static readonly TileTypeId FoodPlatter = new(520);
    public static readonly TileTypeId TatteredWoodSign = new(573);
    public static readonly TileTypeId TeleportationPylon = new(597);
    public static readonly TileTypeId DeadCellsDisplayJar = new(698);

    public static bool TryCreate(int rawType, out TileTypeId type)
    {
        if ((uint)rawType >= (uint)Count)
        {
            type = default;
            return false;
        }

        type = new TileTypeId(rawType);
        return true;
    }

    public static bool IsPlatform(TileTypeId type) =>
        type == Platforms ||
        type == TeamBlockRedPlatform ||
        type == TeamBlockGreenPlatform ||
        type == TeamBlockBluePlatform ||
        type == TeamBlockYellowPlatform ||
        type == TeamBlockPinkPlatform ||
        type == TeamBlockWhitePlatform;

    public static bool IsChestAnchor(TileTypeId type) =>
        type == Containers ||
        type == Containers2 ||
        type == Dressers;

    public static bool IsClosedDoor(TileTypeId type) =>
        type == ClosedDoor ||
        type == TallGateClosed;

    public static bool CarriesSignText(TileTypeId type) =>
        type == Signs ||
        type == Tombstones ||
        type == AnnouncementBox ||
        type == TatteredWoodSign;
}

/// <summary>
/// Source-verified TerrariaServer 1.4.5.8 wall identities currently consumed by world/gameplay code.
/// Zero is the vanilla no-wall identity and remains a catalogued value at storage/protocol boundaries.
/// </summary>
public static class VanillaWallIds
{
    public const int Count = 367;

    public static readonly WallTypeId None = new(0);
    public static readonly WallTypeId Stone = new(1);
    public static readonly WallTypeId DirtUnsafe = new(2);
    public static readonly WallTypeId BlueDungeonUnsafe = new(7);
    public static readonly WallTypeId Dirt = new(16);
    public static readonly WallTypeId BlueDungeon = new(17);
    public static readonly WallTypeId Glass = new(21);
    public static readonly WallTypeId SpiderUnsafe = new(62);
    public static readonly WallTypeId HiveUnsafe = new(86);
    public static readonly WallTypeId LihzahrdBrickUnsafe = new(87);

    public static bool TryCreate(int rawType, out WallTypeId type)
    {
        if ((uint)rawType >= (uint)Count)
        {
            type = default;
            return false;
        }

        type = new WallTypeId(rawType);
        return true;
    }
}
