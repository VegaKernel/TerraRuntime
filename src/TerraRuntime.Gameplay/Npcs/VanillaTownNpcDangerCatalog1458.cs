namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Original NPCID.Sets danger radius, PrettySafe and turning-critter identities.</summary>
public static class VanillaTownNpcDangerCatalog1458
{
    private const int Nurse = 18;
    private const int ArmsDealer = 19;
    private const int Dryad = 20;
    private const int Guide = 22;
    private const int Demolitionist = 38;
    private const int GoblinTinkerer = 107;
    private const int Mechanic = 124;
    private const int Steampunker = 178;
    private const int DyeTrader = 207;
    private const int Cyborg = 209;
    private const int Painter = 227;
    private const int WitchDoctor = 228;
    private const int Pirate = 229;
    private const int Squirrel = 299;
    private const int Stylist = 353;
    private const int TravellingMerchant = 368;
    private const int Angler = 369;
    private const int SkeletonMerchant = 453;
    private const int SquirrelRed = 538;
    private const int SquirrelGold = 539;
    private const int DD2Bartender = 550;
    private const int Golfer = 588;
    private const int TownCat = 637;
    private const int TownDog = 638;
    private const int GemSquirrelAmethyst = 639;
    private const int GemSquirrelTopaz = 640;
    private const int GemSquirrelSapphire = 641;
    private const int GemSquirrelEmerald = 642;
    private const int GemSquirrelRuby = 643;
    private const int GemSquirrelDiamond = 644;
    private const int GemSquirrelAmber = 645;
    private const int TownBunny = 656;
    private const int TownSlimeBlue = 670;
    private const int TownSlimeGreen = 678;
    private const int TownSlimeOld = 679;
    private const int TownSlimePurple = 680;
    private const int TownSlimeRainbow = 681;
    private const int TownSlimeRed = 682;
    private const int TownSlimeYellow = 683;
    private const int TownSlimeCopper = 684;
    private const int Truffle = 160;
    private const int Wizard = 108;
    private const int SantaClaus = 142;
    private const int PartyGirl = 208;
    private const int Merchant = 17;
    private const int BestiaryGirl = 633;
    private const int Clothier = 54;
    private const int Princess = 663;
    private const int TaxCollector = 441;
    // Version-pinned NPCID.Sets catalogs; defaults 200 / -1.
    public static int DangerRange(int type) => type switch
    {
        Demolitionist or GoblinTinkerer or Nurse or Angler or SkeletonMerchant => 300, Merchant => 320,
        ArmsDealer or Steampunker or TravellingMerchant => 900, Guide or Clothier or Wizard or Truffle or Princess => 700,
        Mechanic or WitchDoctor or Painter => 800, Pirate or Cyborg => 1000, Dryad => 1200,
        DyeTrader or Stylist => 60, PartyGirl => 400, SantaClaus => 500, TaxCollector => 50, BestiaryGirl => 100,
        DD2Bartender or Golfer => 120,
        TownDog or TownCat or TownBunny or TownSlimeBlue or TownSlimeGreen or TownSlimeOld or TownSlimePurple or TownSlimeRainbow or TownSlimeRed or TownSlimeYellow or TownSlimeCopper => 250,
        _ => 200
    };
    public static int PrettySafeRange(int type) => type switch
    {
        ArmsDealer or WitchDoctor or Steampunker or Pirate or Cyborg or Painter => 300,
        Guide or Mechanic or Dryad or TravellingMerchant => 200, Clothier or Wizard or Truffle => 100, _ => -1
    };
    public static bool IsTurningCritter(int type) =>
        type is GemSquirrelAmber or GemSquirrelAmethyst or GemSquirrelDiamond or GemSquirrelEmerald or GemSquirrelRuby or GemSquirrelSapphire or GemSquirrelTopaz or Squirrel or SquirrelGold or SquirrelRed;
    public static bool IsPartyAttackType(int type) => type is Demolitionist or Merchant or GoblinTinkerer or
        Mechanic or Nurse or Angler or SkeletonMerchant or PartyGirl or SantaClaus or BestiaryGirl or DD2Bartender or Golfer;
    public static bool IsNonPersistentSocialPeerType(int type) => type is TravellingMerchant or SkeletonMerchant;
}
