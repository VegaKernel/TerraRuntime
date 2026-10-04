namespace TerraRuntime.Core.Worlds;

/// <summary>
/// Server-owned random source for vanilla world-item spawn values. Calls are made only from the authoritative
/// game thread; the abstraction exists so source-backed random ranges can be tested without sharing extension RNG streams.
/// </summary>
public interface IWorldItemSpawnRandom
{
    int NextInt32(int inclusiveMin, int exclusiveMax);
}

/// <summary>
/// Owned UnifiedRandom adapter for world-item spawns. Application composition shares the source with admitted
/// NPC, loot and projectile paths; standalone callers may supply their own seed.
/// </summary>
public sealed class SystemWorldItemSpawnRandom : IWorldItemSpawnRandom
{
    private readonly VanillaUnifiedRandom1458 _random;

    public SystemWorldItemSpawnRandom()
        : this(new VanillaUnifiedRandom1458(Environment.TickCount))
    {
    }

    public SystemWorldItemSpawnRandom(int seed)
        : this(new VanillaUnifiedRandom1458(seed))
    {
    }

    internal SystemWorldItemSpawnRandom(VanillaUnifiedRandom1458 random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    internal VanillaUnifiedRandom1458 SourceRandom => _random;

    public int NextInt32(int inclusiveMin, int exclusiveMax)
    {
        if (exclusiveMax <= inclusiveMin)
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax));

        return _random.Next(inclusiveMin, exclusiveMax);
    }
}
