namespace TerraRuntime.Gameplay.Npcs;

/// <summary>
/// Authoritative-thread random stream consumed by source-backed vanilla NPC gameplay algorithms. Runtime
/// composition owns the concrete stream; gameplay rules depend only on the requested integer ranges.
/// </summary>
public interface IVanillaNpcRandom
{
    int NextInt32(int inclusiveMin, int exclusiveMax);
}
