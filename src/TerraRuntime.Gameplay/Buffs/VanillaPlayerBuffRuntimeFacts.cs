using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Buffs;

/// <summary>
/// Source-backed player buff runtime facts needed by authoritative combat/status simulation. This is intentionally
/// narrow: only mechanics whose acquisition/lifetime are server-owned may consume these helpers.
/// </summary>
public static class VanillaPlayerBuffRuntimeFacts
{
    public const int LifeRegenCountPerHitPoint = 120;
    public const int MaxBadLifeRegenDamagePerTick = 4;

    /// <summary>Player.AddBuff_DetermineBuffTimeToAdd difficulty scaling from GameDifficultyData.</summary>
    public static int ResolveDuration(BuffTypeId type, int baseDurationTicks, bool expertMode, bool masterMode)
    {
        if (baseDurationTicks <= 0)
            return 0;
        if (!VanillaBuffDefinitionCatalog.TryGet(type, out VanillaBuffDefinition definition) ||
            !definition.TimeIsExtendedWithGameDifficulty ||
            !expertMode)
        {
            return baseDurationTicks;
        }

        float multiplier = masterMode ? 2.5f : 2f;
        return checked((int)(baseDurationTicks * multiplier));
    }

    /// <summary>
    /// Player.UpdateLifeRegen negative lifeRegen contribution for the admitted server-owned DoT subset.
    /// Poisoned contributes -4 while On Fire! and Hellfire each contribute -8 every player update. The two fire
    /// flags are independent in Player.UpdateLifeRegen and therefore stack when both are active. Positive
    /// regeneration is suppressed by any admitted DoT and is not invented by this helper.
    /// </summary>
    public static int GetBadLifeRegenDelta(bool poisoned, bool onFire, bool onFire3 = false)
    {
        int delta = 0;
        if (poisoned)
            delta -= 4;
        if (onFire)
            delta -= 8;
        if (onFire3)
            delta -= 8;
        return delta;
    }

    public static int ConsumeBadLifeRegenDamage(ref int lifeRegenCount)
    {
        if (lifeRegenCount > -LifeRegenCountPerHitPoint)
            return 0;

        int damage = Math.Min(lifeRegenCount / -LifeRegenCountPerHitPoint, MaxBadLifeRegenDamagePerTick);
        lifeRegenCount += LifeRegenCountPerHitPoint * damage;
        return damage;
    }
}
