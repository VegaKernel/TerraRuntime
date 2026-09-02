namespace TerraRuntime.Gameplay.Npcs;

public readonly record struct VanillaNpcSpawnCadenceResult(int SpawnRate, int MaxSpawns)
{
    public bool IsValid => SpawnRate > 0 && MaxSpawns > 0;
}

/// <summary>
/// Source-backed ordinary spawn cadence slice from TerrariaServer 1.4.5.8 NPC.Spawner.GetSpawnRate.
/// This primitive intentionally owns only the universal defaults and nearby-population pressure that apply
/// after biome/event modifiers. World-mode, equipment, candles and biome multipliers are separate inputs/layers.
/// </summary>
public static class VanillaNpcSpawnCadence
{
    public const int DefaultSpawnRate = 600;
    public const int DefaultMaxSpawns = 5;
    public const int MinimumSpawnRate = DefaultSpawnRate / 10;
    public const int MaximumMaxSpawns = DefaultMaxSpawns * 3;

    public static VanillaNpcSpawnCadenceResult ApplyNearbyPopulationPressure(
        int spawnRate,
        int maxSpawns,
        float nearbyActiveNpcs,
        bool deepOrEvilPopulationBoost = false)
    {
        if (spawnRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(spawnRate));
        if (maxSpawns <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSpawns));
        if (!float.IsFinite(nearbyActiveNpcs) || nearbyActiveNpcs < 0f)
            throw new ArgumentOutOfRangeException(nameof(nearbyActiveNpcs));

        double population = nearbyActiveNpcs;
        if (population < maxSpawns * 0.2)
            spawnRate = (int)(spawnRate * 0.6f);
        else if (population < maxSpawns * 0.4)
            spawnRate = (int)(spawnRate * 0.7f);
        else if (population < maxSpawns * 0.6)
            spawnRate = (int)(spawnRate * 0.8f);
        else if (population < maxSpawns * 0.8)
            spawnRate = (int)(spawnRate * 0.9f);

        // Vanilla applies this second population-pressure pass below the midpoint between worldSurface and
        // rockLayer, or in Corruption/Crimson. It compounds with the common pass above rather than replacing it.
        if (deepOrEvilPopulationBoost)
        {
            if (population < maxSpawns * 0.2)
                spawnRate = (int)(spawnRate * 0.7f);
            else if (population < maxSpawns * 0.4)
                spawnRate = (int)(spawnRate * 0.9f);
        }

        if (spawnRate < MinimumSpawnRate)
            spawnRate = MinimumSpawnRate;
        if (maxSpawns > MaximumMaxSpawns)
            maxSpawns = MaximumMaxSpawns;

        return new VanillaNpcSpawnCadenceResult(spawnRate, maxSpawns);
    }

    public static VanillaNpcSpawnCadenceResult OrdinarySurface(float nearbyActiveNpcs) =>
        ApplyNearbyPopulationPressure(
            DefaultSpawnRate,
            DefaultMaxSpawns,
            nearbyActiveNpcs,
            deepOrEvilPopulationBoost: false);

    public static bool CanAttemptSpawn(float nearbyActiveNpcs, int maxSpawns)
    {
        if (!float.IsFinite(nearbyActiveNpcs) || nearbyActiveNpcs < 0f)
            throw new ArgumentOutOfRangeException(nameof(nearbyActiveNpcs));
        if (maxSpawns <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSpawns));

        return nearbyActiveNpcs < maxSpawns;
    }
}
