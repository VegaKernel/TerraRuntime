namespace TerraRuntime.WorldGeneration.Vanilla;

// Dedicated FastRandom fields used by DesertHive materials and Granite decorations,
// not UnifiedRandom. Preserve signed32 -> float NextDouble conversion and coordinate mixing.
internal struct GenerationFieldRandom1458
{
    private ulong state;
    public GenerationFieldRandom1458(int seed) => state = unchecked((ulong)seed);
    private GenerationFieldRandom1458(ulong seed) => state = seed;
    private static ulong Advance(ulong value) => unchecked(value * 25214903917UL + 11) & 0xFFFFFFFFFFFFUL;
    public readonly GenerationFieldRandom1458 WithModifier(ulong modifier) => new(Advance(modifier) ^ state);
    public readonly GenerationFieldRandom1458 WithCoordinates(int x, int y) =>
        WithModifier(unchecked((ulong)(x + 2654435769U + ((long)y << 6)) + ((ulong)y >> 2)));
    private int Bits(int count) { state = Advance(state); return unchecked((int)(state >> (48 - count))); }
    public double NextDouble() => (float)Bits(32) * 4.656613E-10f;
    public int Next(int maximum)
    {
        if ((maximum & -maximum) == maximum) return (int)((long)maximum * Bits(31) >> 31);
        int bits, value;
        do { bits = Bits(31); value = bits % maximum; } while (unchecked(bits - value + maximum - 1) < 0);
        return value;
    }
}
