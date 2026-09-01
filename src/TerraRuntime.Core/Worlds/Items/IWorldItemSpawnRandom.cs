namespace TerraRuntime.Core;

/// <summary>
/// Server-owned random source for vanilla world-item spawn values. Calls are made only from the authoritative
/// game thread; the abstraction exists so source-backed random ranges can be tested without sharing extension RNG streams.
/// </summary>
public interface IWorldItemSpawnRandom
{
    int NextInt32(int inclusiveMin, int exclusiveMax);
}

/// <summary>
/// Default runtime-local random stream for vanilla world-item spawns. Terraria's Main.rand sequence is not treated
/// as a persistence or protocol identity; source contracts pin the requested ranges and ordering instead.
/// </summary>
public sealed class SystemWorldItemSpawnRandom : IWorldItemSpawnRandom
{
    private readonly Random _random;

    public SystemWorldItemSpawnRandom()
        : this(new Random())
    {
    }

    public SystemWorldItemSpawnRandom(int seed)
        : this(new Random(seed))
    {
    }

    private SystemWorldItemSpawnRandom(Random random)
    {
        _random = random;
    }

    public int NextInt32(int inclusiveMin, int exclusiveMax)
    {
        if (exclusiveMax <= inclusiveMin)
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax));

        return _random.Next(inclusiveMin, exclusiveMax);
    }
}
