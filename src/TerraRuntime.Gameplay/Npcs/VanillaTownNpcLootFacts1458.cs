using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Official 1.4.5.8 ItemDropDatabase NPC-specific town registrations (excluding globals).</summary>
public static class VanillaTownNpcLootFacts1458
{
    // ItemDropDatabase.RegisterTownNPCDrops: the remaining town identities have no specific rules.
    // Presence does not mean a conditional reward is eligible; callers still need its owned predicate.
    public static bool HasRegisteredSpecificRules(NpcTypeId type) =>
        type == VanillaNpcIds.Guide || type == VanillaNpcIds.Steampunker ||
        type == VanillaNpcIds.Painter || type == VanillaNpcIds.Stylist ||
        type == VanillaNpcIds.TaxCollector || type == VanillaNpcIds.Tavernkeep ||
        type == VanillaNpcIds.PartyGirl || type == VanillaNpcIds.DyeTrader ||
        type == VanillaNpcIds.Mechanic || type == VanillaNpcIds.Princess ||
        type == VanillaNpcIds.Clothier || type == VanillaNpcIds.TravellingMerchant;
}
