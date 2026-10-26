using TerraRuntime.Core;

namespace TerraRuntime.Application;

// Typed adapter over an existing detached source cursor. It never owns a seed,
// forks a stream or commits RNG independently of the surrounding transaction.
internal sealed class FishingRandom1458(VanillaUnifiedRandom1458 source) : Random
{
    public override int Next() => source.Next();
    public override int Next(int maxValue) => source.Next(maxValue);
    public override int Next(int minValue, int maxValue) => source.Next(minValue, maxValue);
    public override double NextDouble() => source.NextDouble();
    public override float NextSingle() => (float)source.NextDouble();
    protected override double Sample() => source.NextDouble();
}
