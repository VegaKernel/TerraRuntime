using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

/// <summary>
/// Source-pinned TerrariaServer 1.4.5.8 facts for persistent town residents and assignable town pets/slimes.
/// The catalog deliberately excludes Old Man, Traveling Merchant and Skeleton Merchant because their lifecycle
/// is not a persistent TownRoomManager household. AI style 7 is admitted for wire/default materialization here;
/// full schedule/interaction parity remains a separate behavior implementation gate.
/// </summary>
public static class VanillaTownNpcFacts1458
{
    public const int OrdinaryHousingCategory = 0;
    public const int PetHousingCategory = 1;

    public static bool IsHousingEligible(NpcTypeId type) => TryGetHousingCategory(type, out _);

    public static bool TryGetHousingCategory(NpcTypeId type, out int category)
    {
        if (IsPetOrTownSlime(type))
        {
            category = PetHousingCategory;
            return true;
        }

        if (IsOrdinaryResident(type))
        {
            category = OrdinaryHousingCategory;
            return true;
        }

        category = default;
        return false;
    }

    public static bool CanShareRoom(NpcTypeId first, NpcTypeId second) =>
        TryGetHousingCategory(first, out int firstCategory) &&
        TryGetHousingCategory(second, out int secondCategory) &&
        firstCategory != secondCategory;

    /// <summary>Returns exact SetDefaults fields required by runtime spawn/life/hitbox/wire projection.</summary>
    public static bool TryGetDefinition(NpcTypeId type, out VanillaNpcDefinition definition)
    {
        if (!TryGetHousingCategory(type, out _))
        {
            definition = default;
            return false;
        }

        int height = 40;
        int defense = 15;
        int lifeMax = 250;
        float knockback = 0.5f;

        if (type == VanillaNpcIds.TownCat || type == VanillaNpcIds.TownBunny || IsTownSlime(type))
            height = 20;
        else if (type == VanillaNpcIds.TownDog)
            height = 28;

        if (type == VanillaNpcIds.Guide)
            defense = 30;
        else if (type == VanillaNpcIds.Cyborg)
        {
            defense = 30;
            lifeMax = 500;
            knockback = 0.25f;
        }

        definition = new VanillaNpcDefinition(
            Type: type,
            AiStyle: VanillaNpcAiStyles.Town,
            BehaviorFamily: VanillaNpcBehaviorFamily.None,
            PhysicsFamily: VanillaNpcPhysicsFamily.GroundFighter,
            Role: NpcArchetypeRole.Town,
            BaseWidth: 18,
            BaseHeight: height,
            Damage: 10,
            Defense: defense,
            LifeMax: lifeMax,
            KnockBackResist: knockback,
            Scale: 1f,
            NoGravityAtSpawn: false,
            NoTileCollideAtSpawn: false,
            SyncAnchor: VanillaNpcSyncAnchor.TopLeft);
        return true;
    }

    private static bool IsOrdinaryResident(NpcTypeId type) =>
        type == VanillaNpcIds.Merchant ||
        type == VanillaNpcIds.Nurse ||
        type == VanillaNpcIds.ArmsDealer ||
        type == VanillaNpcIds.Dryad ||
        type == VanillaNpcIds.Guide ||
        type == VanillaNpcIds.Demolitionist ||
        type == VanillaNpcIds.Clothier ||
        type == VanillaNpcIds.GoblinTinkerer ||
        type == VanillaNpcIds.Wizard ||
        type == VanillaNpcIds.Mechanic ||
        type == VanillaNpcIds.SantaClaus ||
        type == VanillaNpcIds.Truffle ||
        type == VanillaNpcIds.Steampunker ||
        type == VanillaNpcIds.DyeTrader ||
        type == VanillaNpcIds.PartyGirl ||
        type == VanillaNpcIds.Cyborg ||
        type == VanillaNpcIds.Painter ||
        type == VanillaNpcIds.WitchDoctor ||
        type == VanillaNpcIds.Pirate ||
        type == VanillaNpcIds.Stylist ||
        type == VanillaNpcIds.Angler ||
        type == VanillaNpcIds.TaxCollector ||
        type == VanillaNpcIds.Tavernkeep ||
        type == VanillaNpcIds.Golfer ||
        type == VanillaNpcIds.Zoologist ||
        type == VanillaNpcIds.Princess;

    private static bool IsPetOrTownSlime(NpcTypeId type) =>
        type == VanillaNpcIds.TownCat ||
        type == VanillaNpcIds.TownDog ||
        type == VanillaNpcIds.TownBunny ||
        IsTownSlime(type);

    private static bool IsTownSlime(NpcTypeId type) =>
        type == VanillaNpcIds.TownSlimeBlue ||
        type == VanillaNpcIds.TownSlimeGreen ||
        type == VanillaNpcIds.TownSlimeOld ||
        type == VanillaNpcIds.TownSlimePurple ||
        type == VanillaNpcIds.TownSlimeRainbow ||
        type == VanillaNpcIds.TownSlimeRed ||
        type == VanillaNpcIds.TownSlimeYellow ||
        type == VanillaNpcIds.TownSlimeCopper;
}
