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
    public static readonly NpcTypeId KingSlime = new(50);
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
    public static readonly TileTypeId Platforms = new(19);
    public static readonly TileTypeId Containers = new(21);
    public static readonly TileTypeId Signs = new(55);
    public static readonly TileTypeId Tombstones = new(85);
    public static readonly TileTypeId Dressers = new(88);
    public static readonly TileTypeId TargetDummy = new(378);
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
