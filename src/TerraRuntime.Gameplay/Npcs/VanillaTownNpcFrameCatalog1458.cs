using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Source Main.npcFrameCount and NPCID.Sets frame partitions for ordinary town residents.</summary>
public static class VanillaTownNpcFrameCatalog1458
{
    public static bool TryGet(NpcTypeId type, out int total, out int extra, out int attack)
    {
        total = extra = attack = 0;
        if (!VanillaTownNpcFacts1458.TryGetHousingCategory(type, out int category) ||
            category != VanillaTownNpcFacts1458.OrdinaryHousingCategory || type == VanillaNpcIds.OldMan) return false;
        if (type == VanillaNpcIds.Dryad) { total = 21; extra = 7; attack = 2; }
        else if (type == VanillaNpcIds.Clothier || type == VanillaNpcIds.Wizard ||
                 type == VanillaNpcIds.Truffle || type == VanillaNpcIds.Princess)
        { total = 23; extra = 7; attack = 2; }
        else if (type == VanillaNpcIds.Guide || type == VanillaNpcIds.Cyborg ||
                 type == VanillaNpcIds.WitchDoctor || type == VanillaNpcIds.Pirate)
        { total = 26; extra = 10; attack = 5; }
        else if (type == VanillaNpcIds.Nurse || type == VanillaNpcIds.Mechanic ||
                 type == VanillaNpcIds.Steampunker || type == VanillaNpcIds.PartyGirl ||
                 type == VanillaNpcIds.Stylist || type == VanillaNpcIds.Angler || type == VanillaNpcIds.Zoologist)
        { total = 23; extra = 9; attack = 4; }
        else if (type == VanillaNpcIds.Merchant || type == VanillaNpcIds.ArmsDealer ||
                 type == VanillaNpcIds.Demolitionist || type == VanillaNpcIds.GoblinTinkerer ||
                 type == VanillaNpcIds.SantaClaus || type == VanillaNpcIds.DyeTrader || type == VanillaNpcIds.Painter ||
                 type == VanillaNpcIds.TaxCollector || type == VanillaNpcIds.Tavernkeep || type == VanillaNpcIds.Golfer)
        { total = 25; extra = 9; attack = 4; }
        else return false;
        return true;
    }
}
