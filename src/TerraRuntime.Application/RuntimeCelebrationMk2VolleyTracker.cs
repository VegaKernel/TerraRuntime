using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal readonly record struct RuntimeCelebrationMk2VolleyAdmission(
    bool StartsVolley,
    int Pattern,
    int ProjectileOrdinal,
    long VolleyTick);

/// <summary>
/// Generation-aware validation for the child sequence emitted by TerrariaServer 1.4.5.8 projectile 714.
/// The first observed volley may begin at any pattern because the holder starts with a randomized ai[0]; every
/// subsequent volley advances modulo seven and patterns 4/5 admit exactly two/three child projectiles.
/// </summary>
internal sealed class RuntimeCelebrationMk2VolleyTracker
{
    private const int VolleyIntervalTicks = 8;
    private readonly PlayerSessionGeneration[] generations = new PlayerSessionGeneration[byte.MaxValue + 1];
    private readonly long[] volleyTicks = new long[byte.MaxValue + 1];
    private readonly sbyte[] patterns = new sbyte[byte.MaxValue + 1];
    private readonly byte[] projectileCounts = new byte[byte.MaxValue + 1];

    internal RuntimeCelebrationMk2VolleyTracker()
    {
        Array.Fill(volleyTicks, long.MinValue);
        Array.Fill(patterns, (sbyte)-1);
    }

    internal bool TryInspect(
        PlayerHandle player,
        long tick,
        int pattern,
        float ai1,
        out RuntimeCelebrationMk2VolleyAdmission admission)
    {
        admission = default;
        int capacity = TerraRuntime.Gameplay.Projectiles.VanillaExplosiveProjectileFacts1458.GetCelebrationVolleyCapacity(pattern);
        if (capacity == 0 || !float.IsFinite(ai1))
            return false;

        int slot = player.Slot.Value;
        bool currentGeneration = generations[slot] == player.Generation;
        long previousTick = currentGeneration ? volleyTicks[slot] : long.MinValue;
        int previousPattern = currentGeneration ? patterns[slot] : -1;
        int previousCount = currentGeneration ? projectileCounts[slot] : 0;

        if (previousTick != long.MinValue && tick == previousTick && previousPattern == pattern)
        {
            if (previousCount >= capacity || !HasExpectedAi1(pattern, previousCount, ai1))
                return false;
            admission = new RuntimeCelebrationMk2VolleyAdmission(false, pattern, previousCount, previousTick);
            return true;
        }

        if (previousTick != long.MinValue)
        {
            if (tick - previousTick < VolleyIntervalTicks ||
                previousCount != TerraRuntime.Gameplay.Projectiles.VanillaExplosiveProjectileFacts1458.GetCelebrationVolleyCapacity(previousPattern) ||
                pattern != (previousPattern + 1) % 7)
            {
                return false;
            }
        }

        if (!HasExpectedAi1(pattern, projectileOrdinal: 0, ai1))
            return false;

        admission = new RuntimeCelebrationMk2VolleyAdmission(true, pattern, 0, tick);
        return true;
    }

    internal void Commit(PlayerHandle player, in RuntimeCelebrationMk2VolleyAdmission admission)
    {
        int slot = player.Slot.Value;
        generations[slot] = player.Generation;
        volleyTicks[slot] = admission.VolleyTick;
        patterns[slot] = checked((sbyte)admission.Pattern);
        projectileCounts[slot] = checked((byte)(admission.ProjectileOrdinal + 1));
    }

    private static bool HasExpectedAi1(int pattern, int projectileOrdinal, float ai1) =>
        pattern == 4
            ? ai1 == (projectileOrdinal == 0 ? 0f : 1f)
            : ai1 == 0f;
}
