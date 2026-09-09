namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Component-local Terraria 1.4.5.8 UnifiedRandom stream. It is deliberately not shared with the pass RNG: the
/// pinned room, hall, and entrance implementations construct a new stream from every graph component seed.
/// </summary>
internal sealed class DungeonUnifiedRandom1458
{
    private readonly int[] seedArray = new int[56];
    private uint inext;

    public DungeonUnifiedRandom1458(int seed)
    {
        int subtraction = seed == int.MinValue ? int.MaxValue : Math.Abs(seed);
        int mj = 161803398 - subtraction;
        seedArray[55] = mj;
        int mk = 1;
        for (int index = 1; index < 55; index++)
        {
            int destination = 21 * index % 55;
            seedArray[destination] = mk;
            mk = mj - mk;
            if (mk < 0)
                mk += int.MaxValue;
            mj = seedArray[destination];
        }
        for (int pass = 1; pass < 5; pass++)
        {
            for (int index = 1; index < 56; index++)
            {
                seedArray[index] -= seedArray[1 + (index + 30) % 55];
                if (seedArray[index] < 0)
                    seedArray[index] += int.MaxValue;
            }
        }
    }

    public int Next(int maximum) => (int)(Sample() * maximum);
    public int Next(int minimum, int maximum) => (int)(Sample() * (maximum - (long)minimum)) + minimum;
    public double NextDouble() => Sample();
    private double Sample() => InternalSample() * 4.656612875245797E-10;
    private int InternalSample()
    {
        uint next = inext + 1;
        if (next > 55)
            next = 1;
        uint second = next + 21;
        if (second > 55)
            second -= 55;
        int value = seedArray[next] - seedArray[second];
        if (value == int.MaxValue)
            value--;
        value = seedArray[next] = value + ((value >> 31) & int.MaxValue);
        inext = next;
        return value;
    }
}
