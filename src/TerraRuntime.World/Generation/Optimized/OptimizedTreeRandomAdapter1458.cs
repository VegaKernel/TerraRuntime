using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Adapts one isolated optimized pass RNG to the narrow UnifiedRandom-shaped surface consumed by the clean-room
/// TerrariaServer 1.4.5.8 ordinary-tree grower. This intentionally does not claim vanilla RNG parity: optimized
/// placement remains custom and isolated, while growth gates, branch/root choices and atlas framing reuse the
/// source-backed tree implementation.
/// </summary>
internal sealed class OptimizedTreeRandomAdapter1458(IWorldGenerationRandom inner) : IWorldGenerationVanillaRandom
{
    private const double UInt53Scale = 1d / (1UL << 53);

    public int Next() => (int)(inner.NextUInt32() & 0x7FFFFFFFu);

    public int Next(int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return maxValue == 0 ? 0 : inner.NextInt32(maxValue);
    }

    public int Next(int minValue, int maxValue)
    {
        if (minValue > maxValue)
            throw new ArgumentOutOfRangeException(nameof(minValue), "minValue cannot exceed maxValue.");
        if (minValue == maxValue)
            return minValue;

        long span = (long)maxValue - minValue;
        if (span <= int.MaxValue)
            return checked(minValue + inner.NextInt32((int)span));

        ulong bound = (ulong)span;
        ulong threshold = unchecked(0UL - bound) % bound;
        ulong sample;
        do
        {
            sample = inner.NextUInt64();
        }
        while (sample < threshold);

        return checked((int)(minValue + (long)(sample % bound)));
    }

    public double NextDouble() => (inner.NextUInt64() >> 11) * UInt53Scale;

    public void NextBytes(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        int offset = 0;
        while (offset < buffer.Length)
        {
            uint value = inner.NextUInt32();
            for (int byteIndex = 0; byteIndex < sizeof(uint) && offset < buffer.Length; byteIndex++, offset++)
            {
                buffer[offset] = (byte)value;
                value >>= 8;
            }
        }
    }
}
