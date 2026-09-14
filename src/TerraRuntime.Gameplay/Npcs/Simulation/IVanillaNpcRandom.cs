namespace TerraRuntime.Gameplay.Npcs;

/// <summary>
/// Authoritative-thread random stream consumed by source-backed vanilla NPC gameplay algorithms. Runtime
/// composition owns the concrete stream; gameplay rules request integer ranges and unit-interval samples.
/// </summary>
public interface IVanillaNpcRandom
{
    int NextInt32(int inclusiveMin, int exclusiveMax);

    /// <summary>Uniform sample in [0,1). Seed-compatible streams override this to preserve their native draw.</summary>
    double NextDouble() => NextInt32(0, int.MaxValue) * (1d / int.MaxValue);
}
