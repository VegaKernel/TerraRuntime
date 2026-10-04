using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Independent source NPC.SetDefaults boss flags and NPCID.Sets.FaceEmote metadata, not archetype roles.</summary>
public static class VanillaTownNpcSocialEmoteCatalog1458
{
    public const int PositiveIdentityCount = 697;
    public static bool TryGet(NpcTypeId type, out bool boss, out byte face)
    {
        boss = false; face = 0;
        if (type.Value <= 0 || type.Value >= PositiveIdentityCount) return false;
        boss = type == VanillaNpcIds.EyeOfCthulhu ||
            type == VanillaNpcIds.SkeletronHead ||
            type == VanillaNpcIds.KingSlime ||
            type == VanillaNpcIds.WallOfFlesh ||
            type == VanillaNpcIds.Retinazer ||
            type == VanillaNpcIds.Spazmatism ||
            type == VanillaNpcIds.SkeletronPrime ||
            type == VanillaNpcIds.Destroyer ||
            type == VanillaNpcIds.QueenBee ||
            type == VanillaNpcIds.Golem ||
            type == VanillaNpcIds.Plantera ||
            type == VanillaNpcIds.BrainOfCthulhu ||
            type == VanillaNpcIds.DukeFishron ||
            type == VanillaNpcIds.MartianSaucerCore ||
            type == VanillaNpcIds.MoonLordHead ||
            type == VanillaNpcIds.MoonLordHand ||
            type == VanillaNpcIds.MoonLordCore ||
            type == VanillaNpcIds.LunaticCultist ||
            type == VanillaNpcIds.EmpressOfLight ||
            type == VanillaNpcIds.QueenSlime ||
            type == VanillaNpcIds.TorchGod ||
            type == VanillaNpcIds.Deerclops;
        if (type == VanillaNpcIds.Merchant) face = 101;
        else if (type == VanillaNpcIds.Nurse) face = 102;
        else if (type == VanillaNpcIds.ArmsDealer) face = 103;
        else if (type == VanillaNpcIds.Dryad) face = 104;
        else if (type == VanillaNpcIds.Guide) face = 105;
        else if (type == VanillaNpcIds.OldMan) face = 106;
        else if (type == VanillaNpcIds.Demolitionist) face = 107;
        else if (type == VanillaNpcIds.Clothier) face = 108;
        else if (type == VanillaNpcIds.GoblinTinkerer) face = 109;
        else if (type == VanillaNpcIds.Wizard) face = 110;
        else if (type == VanillaNpcIds.Mechanic) face = 111;
        else if (type == VanillaNpcIds.SantaClaus) face = 112;
        else if (type == VanillaNpcIds.Truffle) face = 113;
        else if (type == VanillaNpcIds.Steampunker) face = 114;
        else if (type == VanillaNpcIds.DyeTrader) face = 115;
        else if (type == VanillaNpcIds.PartyGirl) face = 116;
        else if (type == VanillaNpcIds.Cyborg) face = 117;
        else if (type == VanillaNpcIds.Painter) face = 118;
        else if (type == VanillaNpcIds.WitchDoctor) face = 119;
        else if (type == VanillaNpcIds.Pirate) face = 120;
        else if (type == VanillaNpcIds.Stylist) face = 121;
        else if (type == VanillaNpcIds.TravellingMerchant) face = 122;
        else if (type == VanillaNpcIds.Angler) face = 123;
        else if (type == VanillaNpcIds.TaxCollector) face = 125;
        else if (type == VanillaNpcIds.SkeletonMerchant) face = 124;
        else if (type == VanillaNpcIds.Golfer) face = 140;
        else if (type == VanillaNpcIds.Zoologist) face = 141;
        else if (type == VanillaNpcIds.Princess) face = 145;
        return true;
    }
}
