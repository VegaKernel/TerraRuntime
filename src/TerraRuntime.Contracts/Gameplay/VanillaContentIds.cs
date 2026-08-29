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
}

/// <summary>
/// TerrariaServer 1.4.5.8 item content bounds. The count is pinned to ItemID.Count as independently
/// exercised by packet-5 Item.netDefaults normalization tests.
/// </summary>
public static class VanillaItemIds
{
    public const int Count = 6196;

    public static ItemTypeId None => default;

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
    public static readonly ProjectileTypeId ThrowingKnife = new(48);
    public static readonly ProjectileTypeId PoisonedKnife = new(54);
    public static readonly ProjectileTypeId BoneDagger = new(599);

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
