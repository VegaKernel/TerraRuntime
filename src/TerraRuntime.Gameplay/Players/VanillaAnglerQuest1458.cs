namespace TerraRuntime.Gameplay.Players;

public readonly record struct AnglerQuestWorld1458(bool HardMode, bool Crimson, bool AnyEarlyBossDowned, bool HasWorldSurface);

public static class VanillaAnglerQuest1458
{
    public const int QuestCount = 41;
    public static int ItemForQuest(int quest) => quest switch
    {
        >= 0 and < 39 => 2450 + quest,
        39 => 4393,
        40 => 4394,
        _ => throw new ArgumentOutOfRangeException(nameof(quest))
    };

    public static bool IsEligible(int quest, in AnglerQuestWorld1458 world)
    {
        if ((uint)quest >= QuestCount) return false;
        int fish = ItemForQuest(quest);
        if (!world.HardMode && fish is 2454 or 2462 or 2463 or 2465 or 2468 or 2471 or 2473 or 2480 or 2483 or 2484)
            return false;
        if (world.Crimson && fish is 2454 or 2457 or 2485 || !world.Crimson && fish is 2463 or 2477)
            return false;
        if (!world.AnyEarlyBossDowned && !world.HardMode && fish is 2476 or 2453 or 2473)
            return false;
        return world.HasWorldSurface || fish is not (2476 or 2479 or 2480 or 2452 or 2453 or 2481 or 2483 or 2456 or
            2458 or 2459 or 2461 or 2467 or 2468 or 2487 or 2488 or 2470 or 2473 or 2474);
    }

    /// <summary>Source Main.AnglerQuestSwap rejection sampling, with a bounded refusal instead of an invented fallback.</summary>
    public static bool TryChoose(Random random, in AnglerQuestWorld1458 world, out int quest)
    {
        ArgumentNullException.ThrowIfNull(random);
        for (int attempt = 0; attempt < 4_096; attempt++)
        {
            int candidate = random.Next(QuestCount);
            if (!IsEligible(candidate, world)) continue;
            quest = candidate;
            return true;
        }
        quest = 0;
        return false;
    }
}
