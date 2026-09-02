using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Extensions;

/// <summary>
/// Deterministic RNG stream reserved for host/runtime gameplay extensions. This generator is deliberately
/// independent from every vanilla RNG stream: adding, removing or reordering an unrelated extension cannot
/// advance vanilla randomness. Seed derivation is stable across processes and uses ordinal UTF-16 extension IDs.
/// </summary>
public struct GameplayExtensionRandom
{
    private ulong state;

    public GameplayExtensionRandom(ulong seed)
    {
        state = seed;
    }

    /// <summary>
    /// Creates an entity-scoped stream. Callers should use a stable world seed/identity, extension ID, entity slot,
    /// entity generation and an optional extension-defined stream discriminator. Different logical streams should
    /// use different discriminators rather than sharing mutable RNG state accidentally.
    /// </summary>
    public static GameplayExtensionRandom ForEntity(
        ulong worldSeed,
        GameplayExtensionId extensionId,
        uint entitySlot,
        ulong entityGeneration,
        ulong stream = 0)
    {
        if (!extensionId.IsAssigned)
            throw new ArgumentException("A deterministic extension RNG requires an assigned extension ID.", nameof(extensionId));
        if (entityGeneration == 0)
            throw new ArgumentOutOfRangeException(nameof(entityGeneration));

        ulong seed = Mix(worldSeed ^ 0xA0761D6478BD642FUL);
        seed = Mix(seed ^ StableIdHash(extensionId));
        seed = Mix(seed ^ ((ulong)entitySlot << 32) ^ entityGeneration);
        seed = Mix(seed ^ stream ^ 0xE7037ED1A0B428DBUL);
        return new GameplayExtensionRandom(seed);
    }

    public ulong NextUInt64()
    {
        state = unchecked(state + 0x9E3779B97F4A7C15UL);
        return Mix(state);
    }

    public uint NextUInt32() => (uint)(NextUInt64() >> 32);

    public int NextInt32(int exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMax);
        return (int)(((ulong)NextUInt32() * (uint)exclusiveMax) >> 32);
    }

    public int NextInt32(int inclusiveMin, int exclusiveMax)
    {
        if (exclusiveMax <= inclusiveMin)
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax));

        uint range = checked((uint)((long)exclusiveMax - inclusiveMin));
        uint offset = (uint)(((ulong)NextUInt32() * range) >> 32);
        return checked((int)(inclusiveMin + (long)offset));
    }

    public float NextSingle() => (NextUInt32() >> 8) * (1f / 16_777_216f);

    private static ulong StableIdHash(GameplayExtensionId id)
    {
        ulong hash = 0xCBF29CE484222325UL;
        foreach (char character in id.Value)
        {
            hash ^= (byte)character;
            hash = unchecked(hash * 0x100000001B3UL);
            hash ^= (byte)(character >> 8);
            hash = unchecked(hash * 0x100000001B3UL);
        }

        return hash;
    }

    private static ulong Mix(ulong value)
    {
        value ^= value >> 30;
        value = unchecked(value * 0xBF58476D1CE4E5B9UL);
        value ^= value >> 27;
        value = unchecked(value * 0x94D049BB133111EBUL);
        return value ^ (value >> 31);
    }
}
