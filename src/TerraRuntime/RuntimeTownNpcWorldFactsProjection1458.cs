using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

internal static class RuntimeTownNpcWorldFactsProjection1458
{
    public static VanillaTownSpawnWorldFacts1458 FromMetadata(WorldFileRuntimeMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new VanillaTownSpawnWorldFacts1458(
            InfectedSeed: metadata.InfectedSeed,
            VampireSeed: metadata.VampireSeed,
            TenthAnniversaryWorld: metadata.TenthAnniversaryWorld,
            GetGoodWorld: metadata.GetGoodWorld,
            Christmas: metadata.ForceXMasForToday || metadata.ForceXMasForever,
            GenuineParty: metadata.PartyGenuine,
            HardMode: metadata.HardMode,
            DownedBoss1: metadata.DownedBoss1,
            DownedBoss2: metadata.DownedBoss2,
            DownedBoss3: metadata.DownedBoss3,
            DownedQueenBee: metadata.DownedQueenBee,
            DownedMechBossAny: metadata.DownedMechBossAny,
            DownedPlantBoss: metadata.DownedPlantBoss,
            DownedFrost: metadata.DownedFrost,
            DownedPirates: metadata.DownedPirates,
            SavedGoblin: metadata.SavedGoblin,
            SavedWizard: metadata.SavedWizard,
            SavedMechanic: metadata.SavedMechanic,
            SavedAngler: metadata.SavedAngler,
            SavedStylist: metadata.SavedStylist,
            SavedTaxCollector: metadata.SavedTaxCollector,
            SavedGolfer: metadata.SavedGolfer,
            SavedBartender: metadata.SavedBartender,
            BoughtCat: metadata.BoughtCat,
            BoughtDog: metadata.BoughtDog,
            BoughtBunny: metadata.BoughtBunny,
            UnlockedMerchantSpawn: metadata.UnlockedMerchantSpawn,
            UnlockedDemolitionistSpawn: metadata.UnlockedDemolitionistSpawn,
            UnlockedPartyGirlSpawn: metadata.UnlockedPartyGirlSpawn,
            UnlockedDyeTraderSpawn: metadata.UnlockedDyeTraderSpawn,
            UnlockedArmsDealerSpawn: metadata.UnlockedArmsDealerSpawn,
            UnlockedNurseSpawn: metadata.UnlockedNurseSpawn,
            UnlockedPrincessSpawn: metadata.UnlockedPrincessSpawn,
            UnlockedSlimeBlueSpawn: metadata.UnlockedSlimeBlueSpawn,
            UnlockedSlimeGreenSpawn: metadata.UnlockedSlimeGreenSpawn,
            UnlockedSlimeOldSpawn: metadata.UnlockedSlimeOldSpawn,
            UnlockedSlimePurpleSpawn: metadata.UnlockedSlimePurpleSpawn,
            UnlockedSlimeRainbowSpawn: metadata.UnlockedSlimeRainbowSpawn,
            UnlockedSlimeRedSpawn: metadata.UnlockedSlimeRedSpawn,
            UnlockedSlimeYellowSpawn: metadata.UnlockedSlimeYellowSpawn,
            UnlockedSlimeCopperSpawn: metadata.UnlockedSlimeCopperSpawn,
            BestiaryCompletionPercent: 0f,
            PartyGirlRollSucceeded: false);
    }
}
