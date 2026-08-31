using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

public enum VanillaTownAffection1458
{
    Hate = 0,
    Dislike = 1,
    Like = 2,
    Love = 3
}

public readonly record struct VanillaTownHappinessBiomeState1458(
    bool Forest = false,
    bool Ocean = false,
    bool Snow = false,
    bool Desert = false,
    bool Jungle = false,
    bool Underground = false,
    bool Hallow = false,
    bool Mushroom = false,
    bool Corruption = false,
    bool Crimson = false,
    bool Dungeon = false);

public readonly record struct VanillaTownHappinessContext1458(
    bool RemixWorld = false,
    bool LoveStruck = false,
    bool Homeless = false,
    float DistanceFromHomeTiles = 0f,
    int NpcsWithinHouse = 0,
    int NpcsWithinVillage = 0,
    VanillaTownHappinessBiomeState1458 Biomes = default);

public readonly record struct VanillaTownHappinessResult1458(
    float PriceAdjustment,
    bool MoodRuined,
    int AppliedBiomePreferences,
    int AppliedNpcPreferences);

/// <summary>
/// Source-pinned TerrariaServer 1.4.5.8 ShopHelper price adjustment.
/// Nearby NPC ids are the source ShopHelper's &lt;25 tile resident set; crowd/village counts are supplied separately.
/// Localized happiness report text is intentionally outside this numeric parity primitive.
/// </summary>
public static class VanillaTownHappiness1458
{
    public const float LowestPossiblePriceMultiplier = 0.75f;
    public const float MaxHappinessAchievementPriceMultiplier = 0.82f;
    public const float HighestPossiblePriceMultiplier = 1.5f;
    public const float LikeMultiplier = 0.94f;
    public const float DislikeMultiplier = 1.06f;
    public const float LoveMultiplier = 0.88f;
    public const float HateMultiplier = 1.12f;
    public const float SpaceMultiplier = 0.95f;
    public const float CrowdingMultiplier = 1.05f;
    public const float LoveStruckMultiplier = 0.9f;

    private static readonly HashSet<int> TownPets =
    [
        637, 638, 656, 670, 678, 679, 680, 681, 682, 683, 684
    ];

    public static VanillaTownHappinessResult1458 Resolve(
        NpcTypeId npcType,
        in VanillaTownHappinessContext1458 context,
        ReadOnlySpan<NpcTypeId> nearbyNpcTypes)
    {
        int type = npcType.Value;
        float price = context.LoveStruck ? LoveStruckMultiplier : 1f;

        if (context.RemixWorld || type is 368 or 453 or 37 || TownPets.Contains(type))
            return new(price, MoodRuined: false, AppliedBiomePreferences: 0, AppliedNpcPreferences: 0);

        bool ruined = context.Homeless || context.DistanceFromHomeTiles > 120f ||
                      context.Biomes.Corruption || context.Biomes.Crimson || context.Biomes.Dungeon;
        if (ruined)
            price = 1000f;

        bool normalSpacing = true;
        float crowdFactor = CrowdingMultiplier;
        if (type == 663)
        {
            normalSpacing = false;
            crowdFactor = 1f;
            if (context.NpcsWithinHouse < 2 && context.NpcsWithinVillage < 2)
            {
                price = 1000f;
                ruined = true;
            }
        }

        if (context.NpcsWithinHouse > 3)
        {
            for (int i = 3; i < context.NpcsWithinHouse; i++)
                price *= crowdFactor;
        }

        if (normalSpacing && context.NpcsWithinHouse <= 2 && context.NpcsWithinVillage < 4)
            price *= SpaceMultiplier;

        VanillaTownHappinessBiomeState1458 biomes = context.Biomes;
        int biomePreferences = ApplyBiomePreference(type, in biomes, ref price);
        int npcPreferences = ApplyNpcPreferences(type, nearbyNpcTypes, ref price);

        return new(
            PriceAdjustment: LimitAndRound(price),
            MoodRuined: ruined,
            AppliedBiomePreferences: biomePreferences,
            AppliedNpcPreferences: npcPreferences);
    }

    private static int ApplyBiomePreference(int npcType, in VanillaTownHappinessBiomeState1458 biomes, ref float price)
    {
        ReadOnlySpan<BiomePreference> preferences = GetBiomePreferences(npcType);
        VanillaTownAffection1458? chosen = null;
        for (int i = 0; i < preferences.Length; i++)
        {
            BiomePreference preference = preferences[i];
            if (!IsInBiome(preference.Biome, in biomes))
                continue;
            if (chosen is null || preference.Affection > chosen.Value)
                chosen = preference.Affection;
        }

        if (chosen is null)
            return 0;
        price *= Multiplier(chosen.Value);
        return 1;
    }

    private static int ApplyNpcPreferences(int npcType, ReadOnlySpan<NpcTypeId> nearbyNpcTypes, ref float price)
    {
        int applied = 0;
        if (npcType == 663)
        {
            int loved = Math.Min(3, nearbyNpcTypes.Length);
            for (int i = 0; i < loved; i++)
                price *= LoveMultiplier;
            applied += loved;
        }
        else if (Contains(nearbyNpcTypes, 663))
        {
            price *= LikeMultiplier;
            applied++;
        }

        ReadOnlySpan<NpcPreference> preferences = GetNpcPreferences(npcType);
        for (int i = 0; i < preferences.Length; i++)
        {
            NpcPreference preference = preferences[i];
            if (!Contains(nearbyNpcTypes, preference.OtherNpcType))
                continue;
            price *= Multiplier(preference.Affection);
            applied++;
        }
        return applied;
    }

    private static NpcPreference[] GetNpcPreferences(int npcType) => npcType switch
    {
        17 => [new(588, VanillaTownAffection1458.Like), new(18, VanillaTownAffection1458.Like), new(441, VanillaTownAffection1458.Dislike), new(369, VanillaTownAffection1458.Hate)],
        18 => [new(19, VanillaTownAffection1458.Love), new(108, VanillaTownAffection1458.Like), new(208, VanillaTownAffection1458.Dislike), new(20, VanillaTownAffection1458.Dislike), new(633, VanillaTownAffection1458.Hate)],
        227 => [new(20, VanillaTownAffection1458.Love), new(208, VanillaTownAffection1458.Like), new(209, VanillaTownAffection1458.Dislike), new(160, VanillaTownAffection1458.Dislike)],
        207 => [new(19, VanillaTownAffection1458.Like), new(227, VanillaTownAffection1458.Like), new(178, VanillaTownAffection1458.Dislike), new(229, VanillaTownAffection1458.Hate)],
        208 => [new(108, VanillaTownAffection1458.Love), new(353, VanillaTownAffection1458.Like), new(17, VanillaTownAffection1458.Dislike), new(441, VanillaTownAffection1458.Hate), new(633, VanillaTownAffection1458.Love)],
        369 => [new(208, VanillaTownAffection1458.Like), new(38, VanillaTownAffection1458.Like), new(441, VanillaTownAffection1458.Like), new(550, VanillaTownAffection1458.Hate)],
        353 => [new(207, VanillaTownAffection1458.Love), new(229, VanillaTownAffection1458.Like), new(550, VanillaTownAffection1458.Dislike), new(107, VanillaTownAffection1458.Hate)],
        38 => [new(550, VanillaTownAffection1458.Love), new(124, VanillaTownAffection1458.Like), new(107, VanillaTownAffection1458.Dislike), new(19, VanillaTownAffection1458.Dislike)],
        20 => [new(228, VanillaTownAffection1458.Like), new(160, VanillaTownAffection1458.Like), new(369, VanillaTownAffection1458.Dislike), new(588, VanillaTownAffection1458.Hate)],
        550 => [new(38, VanillaTownAffection1458.Love), new(107, VanillaTownAffection1458.Like), new(22, VanillaTownAffection1458.Dislike), new(207, VanillaTownAffection1458.Hate)],
        19 => [new(18, VanillaTownAffection1458.Love), new(178, VanillaTownAffection1458.Like), new(588, VanillaTownAffection1458.Dislike), new(38, VanillaTownAffection1458.Hate)],
        107 => [new(124, VanillaTownAffection1458.Love), new(207, VanillaTownAffection1458.Like), new(54, VanillaTownAffection1458.Dislike), new(353, VanillaTownAffection1458.Hate)],
        228 => [new(20, VanillaTownAffection1458.Like), new(22, VanillaTownAffection1458.Like), new(18, VanillaTownAffection1458.Dislike), new(160, VanillaTownAffection1458.Hate)],
        54 => [new(160, VanillaTownAffection1458.Love), new(441, VanillaTownAffection1458.Like), new(18, VanillaTownAffection1458.Dislike), new(124, VanillaTownAffection1458.Hate)],
        124 => [new(107, VanillaTownAffection1458.Love), new(209, VanillaTownAffection1458.Like), new(19, VanillaTownAffection1458.Dislike), new(54, VanillaTownAffection1458.Hate)],
        441 => [new(17, VanillaTownAffection1458.Love), new(208, VanillaTownAffection1458.Like), new(38, VanillaTownAffection1458.Dislike), new(124, VanillaTownAffection1458.Dislike), new(142, VanillaTownAffection1458.Hate)],
        229 => [new(369, VanillaTownAffection1458.Love), new(550, VanillaTownAffection1458.Like), new(353, VanillaTownAffection1458.Dislike), new(22, VanillaTownAffection1458.Hate)],
        108 => [new(588, VanillaTownAffection1458.Love), new(17, VanillaTownAffection1458.Like), new(228, VanillaTownAffection1458.Dislike), new(209, VanillaTownAffection1458.Hate)],
        178 => [new(209, VanillaTownAffection1458.Love), new(227, VanillaTownAffection1458.Like), new(208, VanillaTownAffection1458.Dislike), new(108, VanillaTownAffection1458.Dislike), new(20, VanillaTownAffection1458.Dislike)],
        209 => [new(353, VanillaTownAffection1458.Like), new(229, VanillaTownAffection1458.Like), new(178, VanillaTownAffection1458.Like), new(108, VanillaTownAffection1458.Hate), new(633, VanillaTownAffection1458.Dislike)],
        142 => [new(441, VanillaTownAffection1458.Hate)],
        588 => [new(227, VanillaTownAffection1458.Like), new(369, VanillaTownAffection1458.Love), new(17, VanillaTownAffection1458.Hate), new(229, VanillaTownAffection1458.Dislike), new(633, VanillaTownAffection1458.Like)],
        22 => [new(54, VanillaTownAffection1458.Like), new(178, VanillaTownAffection1458.Dislike), new(227, VanillaTownAffection1458.Hate), new(633, VanillaTownAffection1458.Like)],
        160 => [new(22, VanillaTownAffection1458.Love), new(20, VanillaTownAffection1458.Like), new(54, VanillaTownAffection1458.Dislike), new(228, VanillaTownAffection1458.Hate)],
        633 => [new(369, VanillaTownAffection1458.Dislike), new(19, VanillaTownAffection1458.Hate), new(228, VanillaTownAffection1458.Love), new(588, VanillaTownAffection1458.Like)],
        _ => []
    };

    private static BiomePreference[] GetBiomePreferences(int npcType) => npcType switch
    {
        22 => [new(VanillaTownAffection1458.Like, Biome.Forest), new(VanillaTownAffection1458.Dislike, Biome.Ocean)],
        17 => [new(VanillaTownAffection1458.Like, Biome.Forest), new(VanillaTownAffection1458.Dislike, Biome.Desert)],
        588 => [new(VanillaTownAffection1458.Like, Biome.Forest), new(VanillaTownAffection1458.Dislike, Biome.Underground)],
        633 => [new(VanillaTownAffection1458.Like, Biome.Forest), new(VanillaTownAffection1458.Dislike, Biome.Desert)],
        441 => [new(VanillaTownAffection1458.Like, Biome.Snow), new(VanillaTownAffection1458.Dislike, Biome.Hallow)],
        124 => [new(VanillaTownAffection1458.Like, Biome.Snow), new(VanillaTownAffection1458.Dislike, Biome.Underground)],
        209 => [new(VanillaTownAffection1458.Like, Biome.Snow), new(VanillaTownAffection1458.Dislike, Biome.Jungle)],
        142 => [new(VanillaTownAffection1458.Love, Biome.Snow), new(VanillaTownAffection1458.Hate, Biome.Desert)],
        207 => [new(VanillaTownAffection1458.Like, Biome.Desert), new(VanillaTownAffection1458.Dislike, Biome.Forest)],
        19 => [new(VanillaTownAffection1458.Like, Biome.Desert), new(VanillaTownAffection1458.Dislike, Biome.Snow)],
        178 => [new(VanillaTownAffection1458.Like, Biome.Desert), new(VanillaTownAffection1458.Dislike, Biome.Jungle)],
        20 => [new(VanillaTownAffection1458.Like, Biome.Jungle), new(VanillaTownAffection1458.Dislike, Biome.Desert)],
        228 => [new(VanillaTownAffection1458.Like, Biome.Jungle), new(VanillaTownAffection1458.Dislike, Biome.Hallow)],
        227 => [new(VanillaTownAffection1458.Like, Biome.Jungle), new(VanillaTownAffection1458.Dislike, Biome.Forest)],
        369 => [new(VanillaTownAffection1458.Like, Biome.Ocean), new(VanillaTownAffection1458.Dislike, Biome.Desert)],
        229 => [new(VanillaTownAffection1458.Like, Biome.Ocean), new(VanillaTownAffection1458.Dislike, Biome.Underground)],
        353 => [new(VanillaTownAffection1458.Like, Biome.Ocean), new(VanillaTownAffection1458.Dislike, Biome.Snow)],
        38 => [new(VanillaTownAffection1458.Like, Biome.Underground), new(VanillaTownAffection1458.Dislike, Biome.Ocean)],
        107 => [new(VanillaTownAffection1458.Like, Biome.Underground), new(VanillaTownAffection1458.Dislike, Biome.Jungle)],
        54 => [new(VanillaTownAffection1458.Like, Biome.Underground), new(VanillaTownAffection1458.Dislike, Biome.Hallow)],
        108 => [new(VanillaTownAffection1458.Like, Biome.Hallow), new(VanillaTownAffection1458.Dislike, Biome.Ocean)],
        18 => [new(VanillaTownAffection1458.Like, Biome.Hallow), new(VanillaTownAffection1458.Dislike, Biome.Snow)],
        208 => [new(VanillaTownAffection1458.Like, Biome.Hallow), new(VanillaTownAffection1458.Dislike, Biome.Underground)],
        550 => [new(VanillaTownAffection1458.Like, Biome.Hallow), new(VanillaTownAffection1458.Dislike, Biome.Snow)],
        160 => [new(VanillaTownAffection1458.Like, Biome.Mushroom)],
        _ => []
    };

    private static bool IsInBiome(Biome biome, in VanillaTownHappinessBiomeState1458 state) => biome switch
    {
        Biome.Forest => state.Forest,
        Biome.Ocean => state.Ocean,
        Biome.Snow => state.Snow,
        Biome.Desert => state.Desert,
        Biome.Jungle => state.Jungle,
        Biome.Underground => state.Underground,
        Biome.Hallow => state.Hallow,
        Biome.Mushroom => state.Mushroom,
        _ => false
    };

    private static float Multiplier(VanillaTownAffection1458 affection) => affection switch
    {
        VanillaTownAffection1458.Love => LoveMultiplier,
        VanillaTownAffection1458.Like => LikeMultiplier,
        VanillaTownAffection1458.Dislike => DislikeMultiplier,
        VanillaTownAffection1458.Hate => HateMultiplier,
        _ => 1f
    };

    private static float LimitAndRound(float value) =>
        MathF.Round(Math.Clamp(value, LowestPossiblePriceMultiplier, HighestPossiblePriceMultiplier) * 100f) / 100f;

    private static bool Contains(ReadOnlySpan<NpcTypeId> nearby, int type)
    {
        for (int i = 0; i < nearby.Length; i++)
            if (nearby[i].Value == type) return true;
        return false;
    }

    private readonly record struct NpcPreference(int OtherNpcType, VanillaTownAffection1458 Affection);
    private readonly record struct BiomePreference(VanillaTownAffection1458 Affection, Biome Biome);
    private enum Biome { Forest, Ocean, Snow, Desert, Jungle, Underground, Hallow, Mushroom }
}
