namespace TerraRuntime.Core;

/// <summary>
/// Source-pinned implementation of Terraria 1.4.5.8 <c>Terraria.Utilities.UnifiedRandom</c>.
/// This type exists for vanilla world-generation parity only; custom generators continue to use the independent
/// runtime RNG exposed through the public world-generation contracts.
/// </summary>
internal sealed class VanillaUnifiedRandom1458
{
    public const string SourceSha256 = "09cb3db449e6df6db20884c6b9e3d74a27112728fe34d5383510f926991e721e";
    public const string SetSeedSha256 = "75f31504dd46f8d3eea41116bd3799a2157aa9472c990c33e68bf800ff43aa08";
    public const string InternalSampleSha256 = "7526cd925d47dcbe2b2547a98b0d753ff3af7ea5d01474d49aa1bde31d123b29";
    public const string SampleSha256 = "9b1e4ee8cc31335f697001234da47ff500294505986bd9222b671627ddffa58c";
    public const string LargeRangeSha256 = "9cb17a7babf272616f050fd28596919c4b022bad3c9f6f1546f7e7ba79cda72b";

    private readonly int[] seedArray = new int[56];
    private uint inext;

    public VanillaUnifiedRandom1458(int seed) => SetSeed(seed);

    public void SetSeed(int seed)
    {
        Array.Clear(seedArray);
        int subtraction = seed == int.MinValue ? int.MaxValue : Math.Abs(seed);
        int mj = 161803398 - subtraction;
        seedArray[55] = mj;
        int mk = 1;

        for (int i = 1; i < 55; i++)
        {
            int ii = 21 * i % 55;
            seedArray[ii] = mk;
            mk = mj - mk;
            if (mk < 0)
                mk += int.MaxValue;
            mj = seedArray[ii];
        }

        for (int k = 1; k < 5; k++)
        {
            for (int i = 1; i < 56; i++)
            {
                seedArray[i] -= seedArray[1 + (i + 30) % 55];
                if (seedArray[i] < 0)
                    seedArray[i] += int.MaxValue;
            }
        }

        inext = 0;
    }

    public int Next() => InternalSample();

    public int Next(int maxValue)
    {
        if (maxValue < 0)
            throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be positive.");
        return (int)(Sample() * maxValue);
    }

    public int Next(int minValue, int maxValue)
    {
        if (minValue > maxValue)
            throw new ArgumentOutOfRangeException(nameof(minValue), "minValue must be less than maxValue");

        long range = (long)maxValue - minValue;
        if (range <= int.MaxValue)
            return (int)(Sample() * range) + minValue;
        return (int)((long)(GetSampleForLargeRange() * range) + minValue);
    }

    public double NextDouble() => Sample();

    public void NextBytes(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = (byte)(InternalSample() % 256);
    }

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

    private double GetSampleForLargeRange()
    {
        int value = InternalSample();
        if (InternalSample() % 2 == 0)
            value = -value;
        return (value + 2147483646.0) / 4294967293.0;
    }
}
